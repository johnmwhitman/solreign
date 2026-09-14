using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Content.Server._Solreign.Director;
using Content.Server._Solreign.SeasonLedger;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server.Station.Events;
using Content.Server.Station.Systems;
using Content.Shared._Solreign.Library;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Ghost.Components;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;

namespace Content.Server._Solreign.Library;

/// <summary>
///     STATION-LIBRARY (wave-2 item, Tarn Adams' dissenting C1 council pick): the Station Archive
///     — a bookshelf-class structure ("PROVIDENCE Records Annex") where a player submits a written
///     book/paper that enters the Season Ledger and re-materializes as a readable book every round.
///     Builds on upstream's existing Paper writing/reading infrastructure and the house
///     Bookshelf/Storage furniture rather than reinventing either. Owns four jobs:
///
///     1. SUBMISSION (an alt-click verb opens a plain title+body form BUI —
///     <see cref="OnAnnexGetVerbs"/>/<see cref="OnSubmitMessage"/>): the DAEMON round trip
///     (classifier screening) uses the <c>SolreignNoticeboardSystem</c>-style
///     <see cref="Task.Run(Func{Task})"/> + <see cref="ConcurrentQueue{T}"/> drained in
///     <see cref="Update"/> — nothing is EVER written to <c>library_works</c> before the daemon
///     approves. A rejected submission gets a private, generic popup only; it never echoes the
///     submitted title/body and never states the reason.
///
///     2. ROUND-START PROJECTION (the Continuity Garden pattern, <see cref="OnStationPostInit"/>):
///     for every Annex on a freshly-initialized station, read the N most-recent non-hidden works
///     from the ledger and spawn one book ITEM entity per work (content stamped via the existing
///     <see cref="PaperSystem"/>, inserted into the Annex's own <c>Storage</c> container) — reading
///     one is the existing Paper BUI, unchanged. Round-local projections; the ledger row is truth.
///
///     3. PROVIDENCE SEEDING (<see cref="OnAnnexMapInit"/>): a freshly-spawned Annex whose archive
///     has fewer than <see cref="LibraryCopy.SeedWorks"/>.Length PROVIDENCE rows submits the
///     missing ones through the SAME classifier-gated write path as a player work — once EVER per
///     archive (not per round, unlike Noticeboard: a library seed is a permanent row, indistinguishable
///     from a player submission once written).
///
///     4. REPORT (<see cref="OnBookGetVerbs"/>): a one-tap alt-click verb on any projected book
///     hides its ledger row (immediate, reversible containment — no daemon round trip, the
///     Noticeboard idiom) and removes this round's physical copy. Reversal is a moderator-only
///     console command (<c>LibraryUnhideCommand</c>). Deliberate scope boundary carried over from
///     the Noticeboard precedent: an early, affirmative PERMANENT purge ahead of a moderator's own
///     discretion is NOT built this wave — hide already fully removes the exposure, and this
///     codebase has no reviewer-queue primitive to hang a harder action on yet.
///
///     Everything gates on <c>solreign.library.enabled</c> (ships FALSE — dormant; zero behavior,
///     zero reads, zero writes, integration-test-pinned). Retention deliberately has NO expiry —
///     see <c>SeasonLedgerStore.LibraryWorks.cs</c>'s divergence notes from the Noticeboard C2
///     posture memo.
/// </summary>
public sealed partial class SolreignLibrarySystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private UserInterfaceSystem _uiSystem = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private SharedStorageSystem _storage = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private SeasonLedgerSystem _ledger = default!;

    private static readonly HttpClient WebhookClient = new();

    private const string Channel = "library";

    /// <summary>The book prototype every projected/report-tested work spawns as.</summary>
    public const string BookPrototypeId = "SolreignLibraryBook";

    private bool _enabled;

    // Stations already projected this round (gate-before-await, the Mark Garden idiom):
    // StationPostInit fires once per station per round in production; the guard makes a double-
    // fire (or a test re-entry) safe.
    private readonly HashSet<EntityUid> _projectedStations = new();

    private readonly record struct PendingSubmitResult(EntityUid? Actor, string PopupKey);

    private readonly ConcurrentQueue<PendingSubmitResult> _pendingSubmitResults = new();

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_config, CCVars.SolreignLibraryEnabled, v => _enabled = v, invokeImmediately: true);

        SubscribeLocalEvent<SolreignLibraryAnnexComponent, GetVerbsEvent<AlternativeVerb>>(OnAnnexGetVerbs);
        SubscribeLocalEvent<SolreignLibraryAnnexComponent, SolreignLibrarySubmitMessage>(OnSubmitMessage);
        SubscribeLocalEvent<SolreignLibraryAnnexComponent, MapInitEvent>(OnAnnexMapInit);
        SubscribeLocalEvent<SolreignLibraryBookComponent, GetVerbsEvent<AlternativeVerb>>(OnBookGetVerbs);

        SubscribeLocalEvent<StationPostInitEvent>(OnStationPostInit);
        SubscribeLocalEvent<RoundStartingEvent>(_ => ResetRoundState());
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => ResetRoundState());
    }

    private void ResetRoundState()
    {
        _projectedStations.Clear();
    }

    // ---------------------------------------------------------------- submission (verb + BUI)

    private void OnAnnexGetVerbs(EntityUid uid, SolreignLibraryAnnexComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!_enabled || !args.CanAccess || !args.CanInteract)
            return;

        // Actor must resolve to a live player session and must not be a ghost (the Mark Garden gate).
        if (!_players.TryGetSessionByEntity(args.User, out _) || HasComp<GhostComponent>(args.User))
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(LibraryCopy.VerbSubmitKey),
            Priority = 5,
            Act = () => _uiSystem.TryOpenUi(uid, SolreignLibrarySubmitUiKey.Key, user),
        });
    }

    private void OnSubmitMessage(EntityUid uid, SolreignLibraryAnnexComponent component, SolreignLibrarySubmitMessage args)
    {
        if (!_enabled)
            return;

        var actor = args.Actor;

        if (!LibraryRules.TrySanitizeTitle(args.Title, out var title))
            return;
        if (!LibraryRules.TrySanitizeBody(args.Body, out var body))
            return;

        if (!DeathAttribution.TryGetPlayerGuid(EntityManager, actor, out var playerGuidStr)
            || !Guid.TryParse(playerGuidStr, out var user))
        {
            return;
        }

        // Re-gate per message (the Oracle/Bounty/Noticeboard idiom): a runtime kill switch flip
        // must stop the NEXT submission immediately.
        if (!DirectorChannel.TryGetReadyToken(_config, CCVars.SolreignLibraryEnabled, out var token))
        {
            _popup.PopupEntity(Loc.GetString(LibraryCopy.NotAcceptingKey), actor, actor, PopupType.MediumCaution);
            return;
        }

        if (!DirectorChannel.TryEnterRateLimit(Channel))
        {
            _popup.PopupEntity(Loc.GetString(LibraryCopy.SubmitBusyKey), actor, actor, PopupType.MediumCaution);
            return;
        }

        // Main-thread reads captured before Task.Run — never touch EntityManager off-thread.
        var authorDisplay = MetaData(actor).EntityName;
        var roundId = _ticker.RoundId;
        var archiveId = component.ArchiveId;

        FireSubmit(actor, archiveId, user, authorDisplay, title, body, isProvidence: false, roundId, token);
    }

    /// <summary>
    ///     Fires the signed classifier round trip and, only on approval, the atomic ledger write.
    ///     <paramref name="actor"/> is null for a PROVIDENCE seed (no player submitted it, so no
    ///     outcome popup is queued for it — only the still-gated ledger write).
    /// </summary>
    private void FireSubmit(
        EntityUid? actor,
        string archiveId,
        Guid user,
        string authorDisplay,
        string title,
        string body,
        bool isProvidence,
        int roundId,
        string token)
    {
        Task.Run(async () =>
        {
            try
            {
                var payload = new
                {
                    player_guid = user == Guid.Empty ? string.Empty : user.ToString(),
                    archive_id = archiveId,
                    title,
                    text = body,
                };
                var json = JsonSerializer.Serialize(payload);

                var (request, ctx) = DirectorChannel.BuildSignedRequest(
                    HttpMethod.Post, DirectorChannel.GetBaseUrl(_config) + "/api/library/post", token, json);
                using (request)
                {
                    using var response = await WebhookClient.SendAsync(request);
                    if (!response.IsSuccessStatusCode)
                    {
                        Log.Warning($"Library classify returned HTTP {(int) response.StatusCode}.");
                        _pendingSubmitResults.Enqueue(new PendingSubmitResult(actor, LibraryCopy.NotAcceptingKey));
                        return;
                    }

                    var responseBody = await response.Content.ReadAsStringAsync();
                    response.Headers.TryGetValues(DirectorChannel.SignatureHeader, out var sigVals);
                    response.Headers.TryGetValues(DirectorChannel.TimestampHeader, out var tsVals);
                    response.Headers.TryGetValues(DirectorChannel.NonceHeader, out var nonceVals);
                    if (!DirectorChannel.VerifyResponse(token, ctx, responseBody, sigVals?.FirstOrDefault(), tsVals?.FirstOrDefault(), nonceVals?.FirstOrDefault()))
                    {
                        Log.Warning("Library classify ack unsigned/unverified/unbound to request; discarding.");
                        _pendingSubmitResults.Enqueue(new PendingSubmitResult(actor, LibraryCopy.NotAcceptingKey));
                        return;
                    }

                    var ack = JsonSerializer.Deserialize<LibraryPostAckDto>(responseBody);
                    if (ack == null)
                    {
                        Log.Warning("Library classify ack was a verified but null/unparseable body; discarding.");
                        _pendingSubmitResults.Enqueue(new PendingSubmitResult(actor, LibraryCopy.NotAcceptingKey));
                        return;
                    }

                    if (!ack.allowed)
                    {
                        // Generic, private, never echoes the submitted title/body. Nothing reaches the ledger.
                        _pendingSubmitResults.Enqueue(new PendingSubmitResult(actor, LibraryCopy.WithheldKey));
                        return;
                    }

                    // Approved by the classifier — advisory only until the ledger's own atomic
                    // write-time check (quota, or the Providence seed target) passes.
                    var approvedTitle = string.IsNullOrEmpty(ack.title) ? title : ack.title;
                    var approvedBody = string.IsNullOrEmpty(ack.text) ? body : ack.text;
                    var nowUtc = DateTime.UtcNow.ToString("o");

                    var (posted, _, rejection) = isProvidence
                        ? await _ledger.TryPostLibraryWorkAsync(
                            archiveId, user, true, authorDisplay, approvedTitle, approvedBody, roundId, nowUtc,
                            LibraryCopy.SeedWorks.Length)
                        : await _ledger.TryPostLibraryWorkAsync(
                            archiveId, user, false, authorDisplay, approvedTitle, approvedBody, roundId, nowUtc);

                    if (!posted)
                    {
                        // A Providence seed losing its race is a silent, expected no-op — nobody is
                        // waiting on a popup for it. A player losing the quota race gets told why.
                        if (!isProvidence)
                        {
                            var key = rejection switch
                            {
                                LibraryPostRejection.AuthorAlreadySubmittedThisRound => LibraryCopy.QuotaKey,
                                _ => LibraryCopy.NotAcceptingKey,
                            };
                            _pendingSubmitResults.Enqueue(new PendingSubmitResult(actor, key));
                        }

                        return;
                    }

                    if (!isProvidence)
                        _pendingSubmitResults.Enqueue(new PendingSubmitResult(actor, LibraryCopy.SuccessKey));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Library submit failed: {ex.Message}");
                if (!isProvidence)
                    _pendingSubmitResults.Enqueue(new PendingSubmitResult(actor, LibraryCopy.NotAcceptingKey));
            }
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        while (_pendingSubmitResults.TryDequeue(out var result))
        {
            if (result.Actor is { } actorUid && Exists(actorUid))
                _popup.PopupEntity(Loc.GetString(result.PopupKey), actorUid, actorUid, PopupType.Medium);
        }
    }

    private sealed class LibraryPostAckDto
    {
        public bool allowed { get; set; }
        public string? title { get; set; }
        public string? text { get; set; }
    }

    // ---------------------------------------------------------------- round-start projection (Continuity Garden pattern)

    private void OnStationPostInit(ref StationPostInitEvent ev)
    {
        MaterializeAndProject(ev.Station);
    }

    private void MaterializeAndProject(EntityUid station)
    {
        if (!_enabled)
            return;

        if (!_projectedStations.Add(station))
            return;

        var query = EntityQueryEnumerator<SolreignLibraryAnnexComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var annexComp, out var xform))
        {
            if (_station.GetOwningStation(uid, xform) != station)
                continue;

            ProjectWorks(uid, annexComp.ArchiveId);
        }
    }

    private async void ProjectWorks(EntityUid annex, string archiveId)
    {
        try
        {
            var slots = _config.GetCVar(CCVars.SolreignLibrarySlots);
            var records = await _ledger.GetRecentLibraryWorksAsync(archiveId, slots);

            // Post-await discipline (Mark Garden / LoadWelcome shape): the annex may have been
            // deleted while this read was in flight.
            if (Deleted(annex))
                return;

            foreach (var record in records)
                SpawnBook(annex, record);
        }
        catch (Exception e)
        {
            Log.Error($"Error while projecting library works onto archive '{archiveId}':\n{e}");
        }
    }

    /// <summary>
    ///     Spawns one book item for a ledger row and inserts it into the Annex's own Storage
    ///     container. A failed insert (shelf full/foreign-blocked) deletes the spawned entity — the
    ///     no-orphans discipline — and costs only this round's physical copy; the row is untouched
    ///     and the next round's projection retries it.
    /// </summary>
    private void SpawnBook(EntityUid annex, LibraryWorkRecord record)
    {
        var uid = Spawn(BookPrototypeId, Transform(annex).Coordinates);

        var bookComp = EnsureComp<SolreignLibraryBookComponent>(uid);
        bookComp.WorkId = record.Id;

        if (TryComp<PaperComponent>(uid, out var paperComp))
            _paper.SetContent((uid, paperComp), record.Body);

        _metaData.SetEntityName(uid, record.Title);
        _metaData.SetEntityDescription(uid, Loc.GetString(
            "solreign-library-book-description", ("author", record.AuthorDisplay)));

        if (!_storage.Insert(annex, uid, out _, playSound: false))
        {
            Del(uid);
            Log.Warning($"Library work {record.Id} failed to insert into Annex {ToPrettyString(annex)}; projection skipped this round.");
        }
    }

    // ---------------------------------------------------------------- PROVIDENCE seeding (once EVER per archive)

    private void OnAnnexMapInit(EntityUid uid, SolreignLibraryAnnexComponent component, MapInitEvent args)
    {
        if (!_enabled)
            return;

        SeedIfNeeded(uid, component.ArchiveId);
    }

    private async void SeedIfNeeded(EntityUid annex, string archiveId)
    {
        try
        {
            var current = await _ledger.GetProvidenceLibraryWorkCountAsync(archiveId);
            var target = LibraryCopy.SeedWorks.Length;
            if (current >= target)
                return;

            if (Deleted(annex))
                return;

            if (!DirectorChannel.TryGetReadyToken(_config, CCVars.SolreignLibraryEnabled, out var token))
                return; // daemon channel not configured — the archive simply stays unseeded until it is

            var roundId = _ticker.RoundId;

            for (var i = current; i < target; i++)
            {
                if (!DirectorChannel.TryEnterRateLimit(Channel))
                    break; // don't spin — a later MapInit/StationPostInit will pick up the remainder

                var seed = LibraryCopy.SeedWorks[i];
                var title = Loc.GetString(seed.TitleKey);
                var body = Loc.GetString(seed.BodyKey);
                var authorDisplay = Loc.GetString(seed.AuthorKey);

                // actor: null — no player submitted this, so no outcome popup is queued for it. The
                // atomic ledger write-time check (providenceSeedTarget) is the real guard against
                // over-seeding even if this races another Annex sharing the same archive_id.
                FireSubmit(null, archiveId, Guid.Empty, authorDisplay, title, body, isProvidence: true, roundId, token);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Library Providence seed check failed for archive '{archiveId}':\n{e}");
        }
    }

    // ---------------------------------------------------------------- report (player one-tap containment)

    private void OnBookGetVerbs(EntityUid uid, SolreignLibraryBookComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!_enabled || !args.CanAccess || !args.CanInteract)
            return;

        if (!_players.TryGetSessionByEntity(args.User, out _))
            return;

        var actor = args.User;
        var workId = component.WorkId;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(LibraryCopy.VerbReportKey),
            Priority = -10, // low priority — a rare containment action, never the default click
            Act = () => TryReport(uid, actor, workId),
        });
    }

    private async void TryReport(EntityUid book, EntityUid actor, int workId)
    {
        bool hidden;
        try
        {
            // This alone IS the containment action — immediate, reversible, no independent review
            // required, same as a moderator's own hide (the Noticeboard idiom). Idempotent.
            hidden = await _ledger.HideLibraryWorkAsync(workId, "reported", DateTime.UtcNow.ToString("o"));
        }
        catch (Exception e)
        {
            Log.Error($"Library report failed for work {workId}:\n{e}");
            return;
        }

        if (!hidden)
            return;

        if (Exists(actor))
            _popup.PopupEntity(Loc.GetString(LibraryCopy.ReportedKey), actor, actor, PopupType.Medium);

        // Immediate physical effect: this round's copy comes off the shelf too, not just the row.
        if (!Deleted(book))
            Del(book);
    }

    // ---------------------------------------------------------------- test seams (house ForTests idiom)

    /// <summary>Runs the verb-Act submit path (CVar + session/ghost gates included).</summary>
    internal void TrySubmitForTests(EntityUid annex, EntityUid actor, string title, string body) =>
        OnSubmitMessage(annex, Comp<SolreignLibraryAnnexComponent>(annex),
            new SolreignLibrarySubmitMessage(title, body) { Actor = actor });

    /// <summary>Runs the full StationPostInit projection path (CVar gate included) against one station.</summary>
    internal void MaterializeAndProjectForTests(EntityUid station) => MaterializeAndProject(station);

    /// <summary>Runs the MapInit Providence-seed check (CVar gate included) against one annex.</summary>
    internal void SeedIfNeededForTests(EntityUid annex, string archiveId) => SeedIfNeeded(annex, archiveId);

    /// <summary>Runs the report verb's Act path (CVar/session gates included).</summary>
    internal void TryReportForTests(EntityUid book, EntityUid actor, int workId) => TryReport(book, actor, workId);

    /// <summary>Clears the per-round guards — the Mark Garden/Noticeboard ResetRoundStateForTests idiom.</summary>
    internal void ResetRoundStateForTests() => ResetRoundState();
}
