using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Content.Server._Solreign.Director;
using Content.Server._Solreign.SeasonLedger;
using Content.Server.GameTicking.Events;
using Content.Shared._Solreign.Noticeboards;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Random;

namespace Content.Server._Solreign.Noticeboards;

/// <summary>
///     v14 wave-1 #2: the crew Noticeboards (spec docs/council/2026-07-17-player-text-safety.md —
///     the C2 posture memo is LAW for this system's behavior). A player-reachable wallmount
///     (<see cref="SolreignNoticeboardComponent"/>) where a player pins a short, classifier-gated,
///     auto-expiring note, and PROVIDENCE seeds each freshly-spawned board so it never looks
///     empty.
///
///     Two distinct I/O disciplines, each mirroring its closest existing precedent:
///     <list type="bullet">
///     <item>PURE LEDGER reads/writes that never touch the daemon (board-open fetch, report/hide)
///     use the <see cref="SolreignMarkGardenSystem"/>-style direct <c>async void</c> handler with
///     a post-await <see cref="Deleted(EntityUid)"/> re-check — SQLite I/O doesn't block the game
///     thread, so no queue is needed.</item>
///     <item>The DAEMON round trip (posting — the classifier call) uses the
///     <see cref="SolreignBountySystem"/>/<c>SolreignOracleSystem</c>-style
///     <see cref="Task.Run(Func{Task})"/> + <see cref="ConcurrentQueue{T}"/> drained in
///     <see cref="Update"/>, exactly per this lane's design brief: "the crypt wire's signed
///     fire-and-forget is NOT right here — the write must be SYNCHRONOUS-outcome: note stays
///     PENDING-invisible until an approve comes back." A submitted note is never written to the
///     Season Ledger before that approval arrives — there is no on-disk "pending" row at all
///     (see <see cref="FirePost"/>'s doc comment for why that satisfies "pending forever" on a
///     dead daemon without ever needing to garbage-collect abandoned pending rows).</item>
///     </list>
///
///     Every quota/capacity/cooldown NUMBER lives in <see cref="NoticeboardRules"/> and is
///     enforced ATOMICALLY at the moment of ledger write (<see cref="SeasonLedgerStore.TryPostNoteAsync"/>)
///     — a classifier approval is advisory only until that call returns <c>Posted: true</c>.
///
///     Everything gates on <c>solreign.noticeboards.enabled</c> (ships FALSE — dormant; zero
///     behavior, zero reads, zero writes, integration-test-pinned). That flag is a
///     design/staging switch only — production activation additionally requires the Moderation
///     Constitution's Section 8 human gate (see the CVar's own doc comment); this system has no
///     opinion on and does not check that gate, by design — it is not a thing code can verify.
/// </summary>
public sealed partial class SolreignNoticeboardSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private UserInterfaceSystem _uiSystem = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SeasonLedgerSystem _ledger = default!;

    private static readonly HttpClient WebhookClient = new();

    private const string Channel = "noticeboard";

    private bool _enabled;

    private readonly record struct PendingPostResult(
        EntityUid Board,
        EntityUid? Actor,
        string PopupKey,
        List<SolreignNoticeboardNoteView>? Refreshed);

    /// <summary>
    ///     ALIVENESS P0 discipline (the Bounty/Oracle precedent): every item here reports the
    ///     outcome of a specific already-sent submission (a popup) and, only when it changed board
    ///     content, a refreshed BUI push. The popup half is drained unconditionally in
    ///     <see cref="Update"/> regardless of the current kill-switch state — it is feedback about
    ///     the player's OWN prior action, not new unsolicited daemon content — while the refresh
    ///     half is gated on the channel still being ready at drain time.
    /// </summary>
    private readonly ConcurrentQueue<PendingPostResult> _pendingPostResults = new();

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_config, CCVars.SolreignNoticeboardsEnabled, v => _enabled = v, invokeImmediately: true);

        SubscribeLocalEvent<SolreignNoticeboardComponent, BoundUIOpenedEvent>(OnBoardUiOpened);
        SubscribeLocalEvent<SolreignNoticeboardComponent, SolreignNoticeboardPostMessage>(OnPostMessage);
        SubscribeLocalEvent<SolreignNoticeboardComponent, SolreignNoticeboardReportMessage>(OnReportMessage);
        SubscribeLocalEvent<SolreignNoticeboardComponent, MapInitEvent>(OnBoardMapInit);

        SubscribeLocalEvent<RoundStartingEvent>(_ => OnRoundStarting());
    }

    // ---------------------------------------------------------------- board-open projection (pure ledger read)

    private async void OnBoardUiOpened(EntityUid uid, SolreignNoticeboardComponent component, BoundUIOpenedEvent args)
    {
        if (!_enabled)
            return;

        var boardId = component.BoardId;
        var nowUtc = DateTime.UtcNow.ToString("o");

        List<NoticeboardNoteRecord> records;
        try
        {
            records = await _ledger.GetActiveNoticeboardNotesAsync(boardId, nowUtc);
        }
        catch (Exception e)
        {
            Log.Error($"Noticeboard read failed for board '{boardId}': {e.Message}");
            if (!Deleted(uid))
            {
                _uiSystem.SetUiState(uid, SolreignNoticeboardUiKey.Key,
                    new SolreignNoticeboardUiState(new List<SolreignNoticeboardNoteView>(), offline: true));
            }
            return;
        }

        // Post-await discipline (Mark Garden / ProvidenceWelcomeSystem.LoadWelcome shape): the
        // board may have been deleted while this read was in flight.
        if (Deleted(uid))
            return;

        _uiSystem.SetUiState(uid, SolreignNoticeboardUiKey.Key, new SolreignNoticeboardUiState(ToViews(records)));
    }

    // ---------------------------------------------------------------- posting (daemon round trip)

    private void OnPostMessage(EntityUid uid, SolreignNoticeboardComponent component, SolreignNoticeboardPostMessage args)
    {
        var actor = args.Actor;

        // NOT-OP-BUG (v15.0.1 hotfix): the feature CVar gate must surface a popup, not a silent
        // return — matches the Bounty/Oracle idiom (the daemon-unreachable branch below uses the
        // same "not accepting" copy). The kill switch is intact; only the feedback shape changed.
        if (!_enabled)
        {
            _popup.PopupEntity(Loc.GetString(NoticeboardCopy.NotAcceptingKey), actor, actor, PopupType.MediumCaution);
            return;
        }

        if (!NoticeboardRules.TrySanitizePostText(args.Text, out var text))
            return;

        if (!DeathAttribution.TryGetPlayerGuid(EntityManager, actor, out var playerGuidStr)
            || !Guid.TryParse(playerGuidStr, out var user))
        {
            return;
        }

        // Re-gate per message (the Oracle/Bounty idiom): a runtime kill switch flip must stop the
        // NEXT submission immediately, not just new BoundUIOpenedEvents.
        if (!DirectorChannel.TryGetReadyToken(_config, CCVars.SolreignNoticeboardsEnabled, out var token))
        {
            // The honest, non-lore "daemon unreachable" UX the spec asks for — distinct from a
            // classifier REJECTION, which gets its own (also generic) copy below.
            _popup.PopupEntity(Loc.GetString(NoticeboardCopy.NotAcceptingKey), actor, actor, PopupType.MediumCaution);
            return;
        }

        if (!DirectorChannel.TryEnterRateLimit(Channel))
        {
            _popup.PopupEntity(Loc.GetString(NoticeboardCopy.SubmitBusyKey), actor, actor, PopupType.MediumCaution);
            return;
        }

        // Main-thread reads captured before Task.Run — never touch EntityManager off-thread.
        var authorDisplay = MetaData(actor).EntityName;

        FirePost(actor, uid, component.BoardId, user, authorDisplay, text, isProvidence: false, token);
    }

    /// <summary>
    ///     Fires the signed classifier round trip and, only on approval, the atomic ledger write.
    ///     Nothing is EVER written to <c>noticeboard_notes</c> before the daemon approves — so a
    ///     daemon that never answers leaves the submission "pending forever" in exactly the sense
    ///     the spec means it: the board never shows the note, indefinitely, with no garbage row to
    ///     clean up and no way for an unreviewed note to leak into view later. <paramref name="actor"/>
    ///     is null for a PROVIDENCE seed post (spec rule 5) — no player submitted it, so no outcome
    ///     popup is delivered for it, only the (still classifier-gated) ledger write.
    /// </summary>
    private void FirePost(
        EntityUid? actor,
        EntityUid board,
        string boardId,
        Guid user,
        string authorDisplay,
        string text,
        bool isProvidence,
        string token)
    {
        Task.Run(async () =>
        {
            try
            {
                var payload = new
                {
                    player_guid = user == Guid.Empty ? string.Empty : user.ToString(),
                    board_id = boardId,
                    text
                };
                var json = JsonSerializer.Serialize(payload);

                var (request, ctx) = DirectorChannel.BuildSignedRequest(
                    HttpMethod.Post, DirectorChannel.GetBaseUrl(_config) + "/api/noticeboard/post", token, json);
                using (request)
                {
                    using var response = await WebhookClient.SendAsync(request);
                    if (!response.IsSuccessStatusCode)
                    {
                        Log.Warning($"Noticeboard classify returned HTTP {(int) response.StatusCode}.");
                        _pendingPostResults.Enqueue(new PendingPostResult(board, actor, NoticeboardCopy.NotAcceptingKey, null));
                        return;
                    }

                    var body = await response.Content.ReadAsStringAsync();
                    response.Headers.TryGetValues(DirectorChannel.SignatureHeader, out var sigVals);
                    response.Headers.TryGetValues(DirectorChannel.TimestampHeader, out var tsVals);
                    response.Headers.TryGetValues(DirectorChannel.NonceHeader, out var nonceVals);
                    if (!DirectorChannel.VerifyResponse(token, ctx, body, sigVals?.FirstOrDefault(), tsVals?.FirstOrDefault(), nonceVals?.FirstOrDefault()))
                    {
                        Log.Warning("Noticeboard classify ack unsigned/unverified/unbound to request; discarding.");
                        _pendingPostResults.Enqueue(new PendingPostResult(board, actor, NoticeboardCopy.NotAcceptingKey, null));
                        return;
                    }

                    var ack = JsonSerializer.Deserialize<NoticeboardPostAckDto>(body);
                    if (ack == null)
                    {
                        Log.Warning("Noticeboard classify ack was a verified but null/unparseable body; discarding.");
                        _pendingPostResults.Enqueue(new PendingPostResult(board, actor, NoticeboardCopy.NotAcceptingKey, null));
                        return;
                    }

                    if (!ack.allowed)
                    {
                        // Spec rule 2: private, generic, never echoes the text, never states why.
                        // Nothing reaches the ledger.
                        _pendingPostResults.Enqueue(new PendingPostResult(board, actor, NoticeboardCopy.WithheldKey, null));
                        return;
                    }

                    // Approved by the classifier — advisory only until the ledger's own atomic
                    // write-time check (capacity/quota/cooldown) passes.
                    var approvedText = string.IsNullOrEmpty(ack.text) ? text : ack.text;
                    var nowUtc = DateTime.UtcNow.ToString("o");
                    var expiryHours = _config.GetCVar(CCVars.SolreignNoticeboardExpiryHours);

                    var (posted, _, rejection) = await _ledger.TryPostNoticeboardNoteAsync(
                        boardId,
                        user,
                        isProvidence,
                        authorDisplay,
                        approvedText,
                        nowUtc,
                        expiryHours,
                        NoticeboardRules.BoardCapacity,
                        NoticeboardRules.ProvidenceCapacity,
                        NoticeboardRules.CooldownHours);

                    if (!posted)
                    {
                        var key = rejection switch
                        {
                            NoticeboardPostRejection.BoardFull => NoticeboardCopy.BoardFullKey,
                            NoticeboardPostRejection.ProvidenceQuotaFull => NoticeboardCopy.BoardFullKey,
                            NoticeboardPostRejection.AuthorHasActiveNote => NoticeboardCopy.QuotaKey,
                            NoticeboardPostRejection.AuthorOnCooldown => NoticeboardCopy.CooldownKey,
                            _ => NoticeboardCopy.NotAcceptingKey,
                        };
                        _pendingPostResults.Enqueue(new PendingPostResult(board, actor, key, null));
                        return;
                    }

                    var refreshed = await _ledger.GetActiveNoticeboardNotesAsync(boardId, nowUtc);
                    _pendingPostResults.Enqueue(new PendingPostResult(board, actor, NoticeboardCopy.SuccessKey, ToViews(refreshed)));
                }
            }
            catch (Exception ex)
            {
                // Covers timeouts, DNS/connection/TLS failures, and a malformed (throwing) body —
                // the daemon never answered cleanly, so this is the same honest "not accepting"
                // outcome as a non-2xx response.
                Log.Error($"Noticeboard post failed: {ex.Message}");
                _pendingPostResults.Enqueue(new PendingPostResult(board, actor, NoticeboardCopy.NotAcceptingKey, null));
            }
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var channelReady = _enabled && DirectorChannel.IsReady(_config, CCVars.SolreignNoticeboardsEnabled);

        while (_pendingPostResults.TryDequeue(out var result))
        {
            if (result.Actor is { } actorUid && Exists(actorUid))
                _popup.PopupEntity(Loc.GetString(result.PopupKey), actorUid, actorUid, PopupType.Medium);

            if (channelReady && result.Refreshed != null && Exists(result.Board))
                _uiSystem.SetUiState(result.Board, SolreignNoticeboardUiKey.Key, new SolreignNoticeboardUiState(result.Refreshed));
        }
    }

    // ---------------------------------------------------------------- report/hide (pure ledger write, no daemon)

    private async void OnReportMessage(EntityUid uid, SolreignNoticeboardComponent component, SolreignNoticeboardReportMessage args)
    {
        if (!_enabled)
            return;

        // A live player session only — a modified/detached client cannot puppet the report tool.
        if (!_players.TryGetSessionByEntity(args.Actor, out _))
            return;

        var boardId = component.BoardId;
        var nowUtc = DateTime.UtcNow.ToString("o");
        var actor = args.Actor;

        bool hidden;
        try
        {
            // Spec rule 3: this alone IS the containment action — immediate, reversible, no
            // independent review required, same as a moderator's own hide. Idempotent: reporting
            // an already-hidden note is a quiet no-op.
            hidden = await _ledger.HideNoticeboardNoteAsync(args.NoteId, "reported", nowUtc);
        }
        catch (Exception e)
        {
            Log.Error($"Noticeboard report failed for note {args.NoteId}: {e.Message}");
            return;
        }

        if (hidden && Exists(actor))
            _popup.PopupEntity(Loc.GetString(NoticeboardCopy.ReportedKey), actor, actor, PopupType.Medium);

        if (Deleted(uid))
            return;

        List<NoticeboardNoteRecord> refreshed;
        try
        {
            refreshed = await _ledger.GetActiveNoticeboardNotesAsync(boardId, nowUtc);
        }
        catch (Exception e)
        {
            Log.Error($"Noticeboard refresh-after-report failed for board '{boardId}': {e.Message}");
            return;
        }

        if (!Deleted(uid))
            _uiSystem.SetUiState(uid, SolreignNoticeboardUiKey.Key, new SolreignNoticeboardUiState(ToViews(refreshed)));
    }

    // ---------------------------------------------------------------- PROVIDENCE seeding (C1 council ask)

    private void OnBoardMapInit(EntityUid uid, SolreignNoticeboardComponent component, MapInitEvent args)
    {
        if (!_enabled)
            return;

        // Boards ship admin-spawnable only this wave (no map placement — a future wave adds
        // that). MapInitEvent fires exactly once per spawn either way, so this is the correct,
        // forward-compatible "when a board comes into existence" hook — it will keep working
        // unchanged once a future wave map-places boards (MapInitEvent fires for those too).
        SeedProvidenceIfEmpty(uid, component.BoardId);
    }

    private async void SeedProvidenceIfEmpty(EntityUid board, string boardId)
    {
        try
        {
            var nowUtc = DateTime.UtcNow.ToString("o");
            var current = await _ledger.GetActiveNoticeboardProvidenceCountAsync(boardId, nowUtc);
            if (current >= NoticeboardRules.ProvidenceCapacity)
                return;

            if (Deleted(board))
                return;

            if (!DirectorChannel.TryGetReadyToken(_config, CCVars.SolreignNoticeboardsEnabled, out var token))
                return; // daemon channel not configured — the board simply stays unseeded until it is

            var authorDisplay = Loc.GetString(NoticeboardCopy.ProvidenceAuthorKey);
            var seedsNeeded = NoticeboardRules.ProvidenceCapacity - current;

            for (var i = 0; i < seedsNeeded; i++)
            {
                if (!DirectorChannel.TryEnterRateLimit(Channel))
                    break; // don't spin — a later board-open/spawn will pick up the remainder

                var seedKey = NoticeboardCopy.PickProvidenceSeedKey(_random.Next());
                var text = Loc.GetString(seedKey);

                // actor: null — no player submitted this, so no outcome popup is queued for it.
                // The atomic ledger write-time check (SeasonLedgerStore.TryPostNoteAsync) is the
                // real guard against over-seeding even if this fires more than once concurrently.
                FirePost(null, board, boardId, Guid.Empty, authorDisplay, text, isProvidence: true, token);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Noticeboard Providence seed check failed for board '{boardId}': {e.Message}");
        }
    }

    // ---------------------------------------------------------------- expiry sweep (storage hygiene only)

    private void OnRoundStarting()
    {
        if (!_enabled)
            return;

        SweepExpired();
    }

    private async void SweepExpired()
    {
        try
        {
            var removed = await _ledger.SweepExpiredNoticeboardNotesAsync(DateTime.UtcNow.ToString("o"));
            if (removed > 0)
                Log.Info($"Noticeboard expiry sweep removed {removed} expired note(s).");
        }
        catch (Exception e)
        {
            Log.Error($"Noticeboard expiry sweep failed: {e.Message}");
        }
    }

    // ---------------------------------------------------------------- shaping helpers

    private static List<SolreignNoticeboardNoteView> ToViews(List<NoticeboardNoteRecord> records)
    {
        return records.Select(r => new SolreignNoticeboardNoteView
        {
            Id = r.Id,
            AuthorDisplay = r.AuthorDisplay,
            Text = r.Body,
            IsProvidence = r.IsProvidence,
        }).ToList();
    }

    private sealed class NoticeboardPostAckDto
    {
        public bool allowed { get; set; }
        public string? text { get; set; }
    }

    // ---------------------------------------------------------------- test seams (house ForTests idiom)

    /// <summary>Runs the full board-open read path (CVar gate included) against one board.</summary>
    internal void OnBoardUiOpenedForTests(EntityUid uid, SolreignNoticeboardComponent component) =>
        OnBoardUiOpened(uid, component, new BoundUIOpenedEvent(SolreignNoticeboardUiKey.Key, uid, default));

    /// <summary>Runs the expiry sweep on demand, bypassing the RoundStartingEvent hook.</summary>
    internal void SweepExpiredForTests() => SweepExpired();
}
