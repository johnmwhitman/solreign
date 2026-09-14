using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Content.Server._Solreign.Director;
using Content.Shared._Solreign.Bounties;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._Solreign.Bounties;

/// <summary>
///     v11 front-door #2: the Liability Board — a player-reachable wallmount (see
///     <see cref="SolreignBountyBoardComponent"/>) that lists the Director daemon's active
///     bounties and lets a player submit a short free-text claim the daemon's LLM judges against
///     the whole pool (the claim API takes no bounty id). Routes through
///     <see cref="DirectorChannel"/> exactly like <c>SolreignCryptSystem</c>: off by default
///     (<see cref="CCVars.SolreignBountiesEnabled"/> plus the master
///     <see cref="CCVars.SolreignDirectorEnabled"/>), every outbound call signed, rate-limited
///     independent of UI-open/claim cadence, async HTTP results marshaled back onto the game
///     thread via a <see cref="ConcurrentQueue{T}"/> drained in <see cref="Update"/> — never
///     touching entities/UI/popups from the <see cref="Task.Run"/> continuation itself (the
///     <c>SolreignOracleSystem</c> idiom).
///
///     <c>GET /api/public/bounties</c> is DELIBERATELY treated as untrusted, unsigned display
///     data: the request is still signed for consistency with every other Director call, but the
///     daemon's public endpoint replies unsigned, so the response is never passed to
///     <see cref="DirectorChannel.VerifyResponse"/> — doing so would just fail closed on every
///     legitimate reply. Instead every field is bounded on the way in
///     (<see cref="BountyClaimRules.SanitizeListings"/>: capped row count, capped title/description
///     length, malformed rows dropped) before it ever reaches a client. The claim POST is the
///     opposite: signed both ways, ack IS verified, because it carries a player's identity and a
///     verdict a player will act on trusting.
/// </summary>
public sealed partial class SolreignBountySystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private UserInterfaceSystem _uiSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private IRobustRandom _random = default!;

    private static readonly HttpClient WebhookClient = new();

    private const string Channel = "bounty";

    /// <summary>Null Listings = the fetch FAILED (ALIVENESS P0 #2): the drain renders the
    /// distinct offline state instead of the true-empty copy, and never touches the cache.</summary>
    private readonly ConcurrentQueue<(EntityUid Board, List<SolreignBountyListing>? Listings)> _pendingListings = new();
    private readonly ConcurrentQueue<(EntityUid Actor, EntityUid Board, string Msg)> _pendingClaimResults = new();

    /// <summary>
    ///     ALIVENESS P0 #1: one queued in-fiction failure popup per failed claim submission —
    ///     the exact <c>SolreignOracleSystem._pendingOracleFailures</c> idiom. FireClaim never
    ///     retries on its own (one HTTP attempt per submitted claim), so this queue is bounded by
    ///     "one entry per failed claim this Update() hasn't drained yet" — never a retry storm.
    ///     Drained (and the entity-existence check applied) on the game thread in
    ///     <see cref="Update"/>; the async Task.Run continuation never touches entities/UI/popups.
    /// </summary>
    private readonly ConcurrentQueue<EntityUid> _pendingClaimFailures = new();

    /// <summary>Last successfully fetched listing per board, so a claim verdict can refresh the window without a second round trip.</summary>
    private readonly Dictionary<EntityUid, List<SolreignBountyListing>> _lastKnownListings = new();

    /// <summary>
    ///     v14 wave-1 #3 (Station Audits): count of claim VERDICTS delivered this round — not
    ///     "claims won", since this codebase never established what the daemon's <c>ClaimAckDto.status</c>
    ///     string means (only <c>msg</c> is read/shown; <c>status</c> is deserialized but otherwise
    ///     unused). Counting adjudications rather than guessing at an undocumented accept/reject
    ///     contract keeps this honest. Incremented in <see cref="Update"/>'s existing
    ///     <c>_pendingClaimResults</c> drain — no new behavior, just a read-only tally alongside it.
    ///     Reset on <see cref="RoundRestartCleanupEvent"/>.
    /// </summary>
    private int _claimVerdictsThisRound;

    /// <summary>Read-only accessor for Station Audits — see <see cref="_claimVerdictsThisRound"/>.</summary>
    internal int ClaimVerdictsThisRoundForAudit => _claimVerdictsThisRound;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SolreignBountyBoardComponent, BoundUIOpenedEvent>(OnBoardUiOpened);
        SubscribeLocalEvent<SolreignBountyBoardComponent, SolreignBountyClaimMessage>(OnClaimMessage);
        SubscribeLocalEvent<SolreignBountyBoardComponent, ComponentRemove>(OnBoardRemoved);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _claimVerdictsThisRound = 0;
    }

    private void OnBoardRemoved(EntityUid uid, SolreignBountyBoardComponent component, ComponentRemove args)
    {
        _lastKnownListings.Remove(uid);
    }

    private void OnBoardUiOpened(EntityUid uid, SolreignBountyBoardComponent component, BoundUIOpenedEvent args)
    {
        if (!DirectorChannel.TryGetReadyToken(_config, CCVars.SolreignBountiesEnabled, out var token))
            return;

        if (!DirectorChannel.TryEnterRateLimit(Channel))
            return;

        FetchBounties(uid, token);
    }

    private void OnClaimMessage(EntityUid uid, SolreignBountyBoardComponent component, SolreignBountyClaimMessage args)
    {
        // Re-gate per message, same as the Oracle petition handler — a runtime kill switch flip
        // must stop the NEXT claim immediately, not just new BoundUIOpenedEvents.
        if (!DirectorChannel.TryGetReadyToken(_config, CCVars.SolreignBountiesEnabled, out var token))
        {
            // P3.2 OTHER SILENT DROPS (audit fix 4 of 4) — daemon-down on the press path. The
            // OnBoardUiOpened branch is read-only and the audit accepts its silent return; the
            // press path needed the same "not answering" line the market/noticeboard/library open
            // paths already show, so a player who mashes Submit on a half-down board sees the
            // failure rather than eating the click. Distinct copy from the busy-desk popup
            // (P0 #1, line 142 below) and the claim-failure trio so a downed daemon never reads
            // as rate-limited or as a daemon-side verdict.
            _popupSystem.PopupEntity(Loc.GetString("solreign-bounties-claim-not-answering-popup"), args.Actor, args.Actor, PopupType.MediumCaution);
            return;
        }

        var actor = args.Actor;

        if (!BountyClaimRules.TrySanitizeClaimText(args.ClaimText, out var claimText))
            return;

        if (!DeathAttribution.TryGetPlayerGuid(EntityManager, actor, out var playerGuid))
            return;

        if (!DirectorChannel.TryEnterRateLimit(Channel))
        {
            // ALIVENESS P0 #1 (rate-limit branch): the player pressed Submit and their claim is
            // NOT going anywhere — dead air here is the exact confusion this fix removes. This
            // handler runs on the game thread, so popup directly (no queue needed). Distinct
            // "desk is busy" wording, never the daemon-didn't-answer trio: the claim was refused
            // locally, not lost in transit.
            _popupSystem.PopupEntity(Loc.GetString("solreign-bounties-claim-busy-popup"), actor, actor, PopupType.MediumCaution);
            return;
        }

        // Capture everything the continuation needs on the game thread before Task.Run — never
        // touch EntityManager/entities from the async continuation itself.
        FireClaim(actor, uid, playerGuid, claimText, token);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // ALIVENESS P0 #1: drained unconditionally, ahead of the kill-switch gate below — the
        // exact SolreignOracleSystem discipline: a failure popup only reports that OUR OWN
        // already-sent claim POST didn't come back clean; it is not itself daemon-driven CONTENT,
        // so it must not be silently wiped by the same gate that clears queued daemon effects.
        while (_pendingClaimFailures.TryDequeue(out var failedActor))
        {
            if (!Exists(failedActor))
                continue;

            var key = BountyClaimRules.PickClaimFailureLocKey(_random.Next(BountyClaimRules.ClaimFailurePopupLocKeys.Length));
            _popupSystem.PopupEntity(Loc.GetString(key), failedActor, failedActor, PopupType.MediumCaution);
        }

        // Codex-style kill-switch discipline (SolreignOracleSystem idiom): a runtime flip must
        // stop queued effects from trickling through after disablement, not just new calls.
        if (!DirectorChannel.IsReady(_config, CCVars.SolreignBountiesEnabled))
        {
            _pendingListings.Clear();
            _pendingClaimResults.Clear();
            return;
        }

        while (_pendingListings.TryDequeue(out var listingResult))
        {
            if (!DirectorChannel.IsReady(_config, CCVars.SolreignBountiesEnabled))
            {
                _pendingListings.Clear();
                break;
            }

            var (board, fetched) = listingResult;
            if (!Exists(board))
                continue;

            // ALIVENESS P0 #2: a null fetch result means the daemon never answered cleanly —
            // render the distinct OFFLINE state (never the true-empty copy) and leave the cache
            // alone so a later claim verdict still refreshes with the last honest listing.
            var (listings, offline) = BountyClaimRules.ShapeBoardListings(fetched);
            if (!offline)
                _lastKnownListings[board] = listings;

            _uiSystem.SetUiState(board, SolreignBountyUiKey.Key, new SolreignBountyUiState(listings, offline: offline));
        }

        while (_pendingClaimResults.TryDequeue(out var claimResult))
        {
            if (!DirectorChannel.IsReady(_config, CCVars.SolreignBountiesEnabled))
            {
                _pendingClaimResults.Clear();
                break;
            }

            var (actor, board, msg) = claimResult;

            // Tally the adjudication itself (v14 wave-1 #3), independent of whether the actor/board
            // entities still exist — the daemon answered, which is the fact Station Audits cares
            // about, not whether the claimant is still around to see the popup.
            _claimVerdictsThisRound++;

            if (Exists(actor))
                _popupSystem.PopupEntity(Loc.GetString("solreign-bounties-verdict-popup", ("msg", msg)), actor, actor, PopupType.Large);

            if (Exists(board))
            {
                var listings = _lastKnownListings.TryGetValue(board, out var cached) ? cached : new List<SolreignBountyListing>();
                _uiSystem.SetUiState(board, SolreignBountyUiKey.Key, new SolreignBountyUiState(listings, msg));
            }
        }
    }

    private void FetchBounties(EntityUid board, string token)
    {
        Task.Run(async () =>
        {
            try
            {
                var (request, _) = DirectorChannel.BuildSignedRequest(HttpMethod.Get, DirectorChannel.GetBaseUrl(_config) + "/api/public/bounties", token, string.Empty);
                using (request)
                {
                    using var response = await WebhookClient.SendAsync(request);
                    if (!response.IsSuccessStatusCode)
                    {
                        // ALIVENESS P0 #2: a failed fetch must never leave the default (empty-
                        // looking) UI standing — queue the explicit offline state instead.
                        Log.Warning($"Bounty list fetch returned HTTP {(int) response.StatusCode}; pushing offline board state.");
                        _pendingListings.Enqueue((board, null));
                        return;
                    }

                    var body = await response.Content.ReadAsStringAsync();

                    // Deliberately UNVERIFIED — /api/public/bounties is an unsigned public
                    // endpoint (see this system's doc comment). Every field is bounded below
                    // instead of trusted via signature.
                    var raw = JsonSerializer.Deserialize<BountyDto[]>(body);
                    if (raw == null)
                    {
                        Log.Warning("Bounty list fetch returned a null/unparseable body; pushing offline board state.");
                        _pendingListings.Enqueue((board, null));
                        return;
                    }

                    var ids = raw.Select(r => r.id).ToList();
                    var titles = raw.Select(r => r.title).ToList();
                    var descriptions = raw.Select(r => r.description).ToList();

                    var sanitized = BountyClaimRules.SanitizeListings(ids, titles, descriptions);
                    var listings = sanitized
                        .Select(s => new SolreignBountyListing { Id = s.Id, Title = s.Title, Description = s.Description })
                        .ToList();

                    _pendingListings.Enqueue((board, listings));
                }
            }
            catch (Exception ex)
            {
                // Covers timeouts, DNS/connection/TLS failures, and a malformed (throwing) JSON
                // body — the daemon never answered cleanly, so the board must say so (P0 #2).
                Log.Error($"Bounty list fetch failed: {ex.Message}");
                _pendingListings.Enqueue((board, null));
            }
        });
    }

    private void FireClaim(EntityUid actor, EntityUid board, string playerGuid, string claimText, string token)
    {
        Task.Run(async () =>
        {
            try
            {
                var payload = new
                {
                    player_guid = playerGuid,
                    claim_text = claimText
                };
                var json = JsonSerializer.Serialize(payload);

                var (request, ctx) = DirectorChannel.BuildSignedRequest(HttpMethod.Post, DirectorChannel.GetBaseUrl(_config) + "/api/bounty/claim", token, json);
                using (request)
                {
                    using var response = await WebhookClient.SendAsync(request);

                    // ALIVENESS P0 #1: a non-2xx response used to just `return` here — the player
                    // pressed Submit Claim and got dead air forever. Every player-path failure
                    // branch below now logs server-side AND queues exactly one in-fiction failure
                    // popup — never a retry, this claim POST is already done.
                    if (!response.IsSuccessStatusCode)
                    {
                        Log.Warning($"Bounty claim returned HTTP {(int) response.StatusCode}.");
                        _pendingClaimFailures.Enqueue(actor);
                        return;
                    }

                    var body = await response.Content.ReadAsStringAsync();
                    response.Headers.TryGetValues(DirectorChannel.SignatureHeader, out var sigVals);
                    response.Headers.TryGetValues(DirectorChannel.TimestampHeader, out var tsVals);
                    response.Headers.TryGetValues(DirectorChannel.NonceHeader, out var nonceVals);
                    if (!DirectorChannel.VerifyResponse(token, ctx, body, sigVals?.FirstOrDefault(), tsVals?.FirstOrDefault(), nonceVals?.FirstOrDefault()))
                    {
                        Log.Warning("Bounty claim ack unsigned/unverified/unbound to request; discarding.");
                        _pendingClaimFailures.Enqueue(actor);
                        return;
                    }

                    var ack = JsonSerializer.Deserialize<ClaimAckDto>(body);
                    if (ack == null)
                    {
                        Log.Warning("Bounty claim ack was a verified but null/unparseable body; discarding.");
                        _pendingClaimFailures.Enqueue(actor);
                        return;
                    }

                    var msg = BountyClaimRules.ClampStatusMessage(ack.msg ?? string.Empty);
                    _pendingClaimResults.Enqueue((actor, board, msg));
                }
            }
            catch (Exception ex)
            {
                // Covers timeouts and every other transport-level failure — the daemon never
                // even answered the claim.
                Log.Error($"Bounty claim failed: {ex.Message}");
                _pendingClaimFailures.Enqueue(actor);
            }
        });
    }

    private sealed class BountyDto
    {
        public int id { get; set; }
        public string? title { get; set; }
        public string? description { get; set; }
    }

    private sealed class ClaimAckDto
    {
        public string status { get; set; } = string.Empty;
        public string msg { get; set; } = string.Empty;
    }
}
