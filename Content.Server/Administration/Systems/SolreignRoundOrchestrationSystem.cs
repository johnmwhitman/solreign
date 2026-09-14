using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Content.Server._Solreign.Director;
using Content.Server._Solreign.SeasonLedger;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Popups;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Log;
using Robust.Shared.Network;

namespace Content.Server.Administration.Systems
{
    /// <summary>
    ///     v11 backbone #4: tells the Director daemon when a shift begins and ends, so its
    ///     round-scoped generation (bounties, market inventory, narrative beats, haunts, relic
    ///     injection, faction summaries) runs each round instead of never.
    ///     <c>POST /api/director/round-start</c> on the PreRoundLobby&#8594;InRound transition;
    ///     <c>POST /api/director/round-end {round_number}</c> on the round-end summary.
    ///
    ///     SR-W-082: the round-end body also OPTIONALLY carries a <c>roster</c> array (career-rank
    ///     salary), gated by <see cref="CCVars.SolreignSalaryEnabled"/> on top of everything else
    ///     here — see <see cref="SalaryRosterPayload"/> for the payload shape/mapping and
    ///     <see cref="BuildRoundEndBodyAsync"/> for how it's assembled.
    ///
    ///     Gated on the MASTER <see cref="CCVars.SolreignDirectorEnabled"/> (this is delivery
    ///     backbone, not a player-facing feature — everything the daemon generates still has to
    ///     come back through the signed, master-gated poll loop, and each consuming feature keeps
    ///     its own kill-switch CVar). Fail-closed via <see cref="DirectorChannel"/>: default-off,
    ///     token floor, signed outbound, ack-verified, rate-limited.
    /// </summary>
    public sealed partial class SolreignRoundOrchestrationSystem : EntitySystem
    {
        [Dependency] private IConfigurationManager _config = default!;
        [Dependency] private SeasonLedgerSystem _seasonLedger = default!;
        [Dependency] private IPlayerManager _playerManager = default!;
        [Dependency] private SharedPopupSystem _popupSystem = default!;
        [Dependency] private IChatManager _chatManager = default!;

        private const string Channel = "round";

        private static readonly HttpClient WebhookClient = new HttpClient();

        /// <summary>
        ///     ALIVENESS P1 #5: accounts whose roster entry was actually DELIVERED (2xx) to the
        ///     daemon this round-end, queued for a visible stipend notice. The async POST
        ///     continuation never touches sessions/entities/popups itself — scalar account ids
        ///     only, drained on the game thread in <see cref="Update"/> (the SolreignOracleSystem
        ///     threading idiom). Bounded: at most one batch per round end.
        /// </summary>
        private readonly ConcurrentQueue<List<NetUserId>> _pendingStipendNotices = new();

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
            SubscribeLocalEvent<RoundEndMessageEvent>(OnRoundEnd);
        }

        private void OnRunLevelChanged(GameRunLevelChangedEvent ev)
        {
            // Same shift-start definition Providence uses: lobby -> live round.
            if (ev.Old != GameRunLevel.PreRoundLobby || ev.New != GameRunLevel.InRound)
                return;

            // Single-read snapshot instead of separate IsReady()+GetToken() calls (Codex MEDIUM #4).
            if (!DirectorChannel.TryGetReadyToken(_config, CCVars.SolreignDirectorEnabled, out var token))
                return;

            if (!DirectorChannel.TryEnterRateLimit(Channel))
                return;

            // The daemon's round-start route takes no payload; it stamps its own clock and kicks
            // off the round-scoped generation task.
            Post(token, "/api/director/round-start", string.Empty);
        }

        private void OnRoundEnd(RoundEndMessageEvent ev)
        {
            if (!DirectorChannel.TryGetReadyToken(_config, CCVars.SolreignDirectorEnabled, out var token))
                return;

            if (!DirectorChannel.TryEnterRateLimit(Channel))
                return;

            // Single-read CVar snapshot on the main thread, same discipline as the token/rate-limit
            // checks above — the async body build below must not touch config/ECS/session state.
            var salaryEnabled = _config.GetCVar(CCVars.SolreignSalaryEnabled);
            var roundId = ev.RoundId;
            var players = ev.AllPlayersEndInfo;

            Task.Run(async () =>
            {
                var (body, rosterAccounts) = await BuildRoundEndBodyAsync(roundId, players, salaryEnabled);
                var delivered = await SendSignedPostAsync(token, "/api/director/round-end", body);

                // ALIVENESS P1 #5: the award moment used to be completely invisible in-game.
                // Only notify accounts whose roster entry the daemon actually ACCEPTED (2xx) —
                // a failed POST means no salary was granted, so no notice; conservative, never
                // announces a grant that didn't happen.
                if (delivered && rosterAccounts.Count > 0)
                    _pendingStipendNotices.Enqueue(rosterAccounts);
            });
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            while (_pendingStipendNotices.TryDequeue(out var accounts))
            {
                // Kill-switch discipline (SolreignOracleSystem idiom): a runtime flip of the
                // master or the salary CVar must stop queued notices, not just new POSTs.
                if (!DirectorChannel.IsReady(_config, CCVars.SolreignDirectorEnabled)
                    || !_config.GetCVar(CCVars.SolreignSalaryEnabled))
                {
                    _pendingStipendNotices.Clear();
                    return;
                }

                foreach (var account in accounts)
                {
                    // Disconnected before the ack came back: drop quietly — the Standing itself
                    // is safe on the daemon/ledger side; only this round's notice is missed.
                    if (!_playerManager.TryGetSessionById(account, out var session))
                        continue;

                    // Chat first — it persists into the lobby even after the round-end summary
                    // covers the world view (the LowPopLobbyReminderSystem delivery idiom).
                    _chatManager.DispatchServerMessage(session, Loc.GetString("solreign-salary-stipend-chat"));

                    if (session.AttachedEntity is { } entity && Exists(entity))
                        _popupSystem.PopupEntity(Loc.GetString("solreign-salary-stipend-popup"), entity, entity, PopupType.Medium);
                }
            }
        }

        /// <summary>
        ///     Match the daemon's canonical API: POST /api/director/round-end { round_number }.
        ///     SR-W-082: when <paramref name="salaryEnabled"/> is false this is byte-identical to
        ///     the pre-SR-W-082 body (no <c>roster</c> key). When true, resolves each eligible
        ///     player's career rank (<see cref="RankProgression.ComputeCareerRank"/> — the same
        ///     career-ladder call site <c>SeasonLedgerSystem.LoadTitle</c> uses to stamp a
        ///     player's badge) via <see cref="SeasonLedgerSystem.GetCareerStatsAsync"/> and folds
        ///     the results through <see cref="SalaryRosterPayload"/>.
        /// </summary>
        private async Task<(string Body, List<NetUserId> RosterAccounts)> BuildRoundEndBodyAsync(
            int roundId,
            RoundEndMessageEvent.RoundEndPlayerInfo[] players,
            bool salaryEnabled)
        {
            if (!salaryEnabled)
                return (SalaryRosterPayload.Build(roundId, null), new List<NetUserId>());

            var withRanks = new List<(RoundEndMessageEvent.RoundEndPlayerInfo Info, CorporateRank Rank)>();

            foreach (var info in players)
            {
                // Skip the DB round-trip entirely for anyone who can never end up in the roster
                // anyway (no account GUID, or observer/ghost-only — SalaryRosterPayload.
                // BuildEligibleEntries re-checks this itself, so this is purely an optimization,
                // not the source of truth for the exclusion).
                if (!SalaryRosterPayload.IsSalaryEligible(info) || info.PlayerGuid is not { } netUserId)
                    continue;

                try
                {
                    var career = await _seasonLedger.GetCareerStatsAsync(netUserId.UserId);
                    var (rank, _, _) = RankProgression.ComputeCareerRank(career);
                    withRanks.Add((info, rank));
                }
                catch (Exception ex)
                {
                    // One account's ledger read failing must not drop the whole roster/POST.
                    Log.Error($"Salary roster: career-stats lookup failed for an account; omitting from roster: {ex.Message}");
                }
            }

            var roster = SalaryRosterPayload.BuildEligibleEntries(withRanks);

            // ALIVENESS P1 #5: the accounts to notify are exactly the roster entries emitted —
            // BuildEligibleEntries already applied the conservative eligibility gate.
            var rosterAccounts = withRanks
                .Where(pair => SalaryRosterPayload.IsSalaryEligible(pair.Info) && pair.Info.PlayerGuid is not null)
                .Select(pair => pair.Info.PlayerGuid!.Value)
                .ToList();

            return (SalaryRosterPayload.Build(roundId, roster), rosterAccounts);
        }

        private void Post(string token, string path, string body)
        {
            Task.Run(() => SendSignedPostAsync(token, path, body));
        }

        /// <summary>Returns true when the daemon answered 2xx (delivery, not ack-verification —
        /// the ack signature check below only affects whether the response BODY is trusted,
        /// which nothing here consumes; same semantics as before this method returned anything).</summary>
        private async Task<bool> SendSignedPostAsync(string token, string path, string body)
        {
            try
            {
                var (request, ctx) = DirectorChannel.BuildSignedRequest(HttpMethod.Post, DirectorChannel.GetBaseUrl(_config) + path, token, body);
                using (request)
                {
                    using var response = await WebhookClient.SendAsync(request);

                    // Fire-and-forget orchestration signal: no response body is consumed or
                    // acted on. Still verify the signature (request-bound via ctx/nonce) if
                    // the daemon signs its ack, to keep the log honest about whether the
                    // round-trip was authenticated.
                    if (response.IsSuccessStatusCode)
                    {
                        var respBody = await response.Content.ReadAsStringAsync();
                        response.Headers.TryGetValues(DirectorChannel.SignatureHeader, out var sigVals);
                        response.Headers.TryGetValues(DirectorChannel.TimestampHeader, out var tsVals);
                        response.Headers.TryGetValues(DirectorChannel.NonceHeader, out var nonceVals);
                        var verified = DirectorChannel.VerifyResponse(
                            token,
                            ctx,
                            respBody,
                            sigVals is { } sv ? sv.FirstOrDefault() : null,
                            tsVals is { } tv ? tv.FirstOrDefault() : null,
                            nonceVals is { } nv ? nv.FirstOrDefault() : null);
                        if (!verified)
                            Log.Warning($"Round orchestration ack for {path} unsigned/unverified/unbound to request; ignoring response body.");

                        return true;
                    }

                    Log.Warning($"Round orchestration POST {path} returned {(int) response.StatusCode}.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Round orchestration POST {path} failed: {ex.Message}");
                return false;
            }
        }
    }
}
