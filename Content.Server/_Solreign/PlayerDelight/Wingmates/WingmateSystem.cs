using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._Solreign.SeasonLedger;
using Content.Shared._Solreign.PlayerDelight.Wingmates;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Ghost.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.PlayerDelight.Wingmates;

/// <summary>
/// Authenticated adapter for round-local Wingmates state. The shared BUI component contains only a
/// public feature shell; every viewer-derived snapshot is networked to one authenticated session.
/// </summary>
public sealed partial class WingmateSystem : EntitySystem
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IResourceManager _res = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    private static readonly HashSet<string> AllowedDepartments =
    [
        "Engineering", "Medical", "Service", "Science", "Security", "Cargo", "Command",
    ];

    private static readonly TimeSpan ExpiryCheckInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(1);
    private const int RequestAttemptsPerWindow = 3;
    private const int OfferAttemptsPerWindow = 6;

    private readonly WingmateRoundState _state =
        new(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    private readonly WingmateFixedWindowLimiter _requestLimiter =
        new(RequestAttemptsPerWindow, AttemptWindow);
    private readonly WingmateFixedWindowLimiter _offerLimiter =
        new(OfferAttemptsPerWindow, AttemptWindow);
    private readonly HashSet<NetUserId> _approvedGuides = new();
    private readonly HashSet<NetUserId> _volunteeringGuides = new();
    private readonly HashSet<(Guid Blocker, Guid Blocked)> _persistentBlocks = new();
    private readonly ConcurrentQueue<IReadOnlyCollection<(Guid Blocker, Guid Blocked)>> _loadedBlocks = new();

    // Durable-write bookkeeping for persistent (cross-round) blocks. A block is added to
    // _persistentBlocks — and therefore enforced — only after DrainCompletedBlockWrites() confirms the
    // SQLite write succeeded. Nothing here is "fire and forget": every kicked-off write's outcome is
    // captured in _completedBlockWrites and applied on the next drain (every Update(), or on demand for
    // tests/integration), and a failed write notifies the blocker instead of silently no-op'ing.
    private readonly HashSet<(Guid Blocker, Guid Blocked)> _pendingPersistentBlocks = new();
    private readonly ConcurrentQueue<PersistentBlockWriteResult> _completedBlockWrites = new();
    private readonly Dictionary<(Guid Blocker, Guid Blocked), Task> _pendingBlockWriteTasksForTests = new();

    // A failed durable-block write's popup notice would otherwise be silently dropped if the blocker is
    // disconnected/unattached at drain time (Update() runs on its own schedule, independent of the
    // player's session). Queue it here and deliver on the seam this system already observes for the
    // blocker reopening a beacon (OnUiOpened) rather than inventing a new PlayerAttached subscription.
    //
    // Coalesced to a HARD-BOUNDED tally per user — exactly two counters (this-shift failures,
    // previous-shift failures) plus the epoch the "this-shift" counter is relative to — never one
    // record per failure, so a ledger outage cannot grow per-user state without bound (O(1) per user,
    // and the notice only ever renders at most two lines anyway). Counts — not a bare presence flag —
    // because two failed block writes against two DIFFERENT partners must not collapse into one generic
    // "something failed" popup: the player needs to know both safety actions failed. Counts are surfaced
    // in the notice text; the blocked party's identity never is (that would leak who they tried to block
    // to a shoulder-surfer).
    //
    // Deliberately NOT round-scoped: a failed durable block is a failed cross-round safety action, and
    // the fact that it failed must never be silently lost just because the round rolled over before the
    // player could see it. ClearRound() leaves these tallies alone; a round boundary only changes their
    // WORDING at delivery time (NormalizePendingBlockFailureTally folds the this-shift counter into the
    // previous-shift counter once the epoch has moved on). Tallies are removed only on actual delivery —
    // one beacon-open popup is the acknowledgment — or by an operator feature-disable (see
    // OnEnabledChanged).
    private readonly Dictionary<NetUserId, WingmateBlockFailureTally> _pendingBlockFailures = new();

    // Bumped every time the feature is operator-disabled. Durable-block writes are stamped with the
    // service epoch active when they started; a FAILED write whose service epoch is stale by drain time
    // straddled a disable, and its notice is discarded entirely (see DrainCompletedBlockWrites) — the
    // operator reset the service, and a maintenance-window failure must not surface as a popup during
    // or after the maintenance, nor mislabel itself "previous shift" off the disable's epoch bump.
    private int _serviceEpoch;

    // P1.4 SILENT-DROP BATCH FIX: per-actor last-transition failure (loc key) plus a monotonic seq
    // the client uses to render a one-shot popup. Populated by ExpireApplyAndPublish whenever a BUI
    // action's transition result is non-success, cleared on the next success. Bounded by open-actor
    // count (one entry per attached player), not by failure count — the seq is what disambiguates
    // distinct failures for the same actor. Cleared on disconnect (HandleUnavailableSession) and on
    // round restart (ClearRound).
    private readonly Dictionary<NetUserId, (string Status, uint Seq)> _lastTransition = new();
    private uint _nextTransitionSeq = 1;

    private SeasonLedgerStore _ledger = default!;
    private Func<Guid, Guid, Task<bool>>? _writePersistentBlock;
    private WingmateSnapshotAdapter<ICommonSession> _snapshots = default!;
    private TimeSpan _nextExpiryCheck;
    private bool _persistentBlocksReady;
    private bool _enabled;

    // FIX 1 (UX-SIMPLE, auto guide eligibility): cached per-account CAREER tours-played count, used
    // only to evaluate CCVars.SolreignWingmatesMinimumShifts under the auto-eligibility posture.
    // Loaded lazily (kicked off the first time a not-yet-approved account opens a beacon while auto
    // eligibility is on) and cached for the process lifetime — career tours only ever grow, so a
    // stale cached value can only under-count, never wrongly grant eligibility early. Same
    // async-load-then-drain idiom as _persistentBlocks/_loadedBlocks above: the ledger read never
    // blocks the game thread, and an unresolved lookup fails CLOSED (not yet eligible) rather than
    // optimistically allowing volunteering before the tenure check has actually run.
    private readonly Dictionary<NetUserId, int> _careerToursCache = new();
    private readonly HashSet<NetUserId> _careerToursLoading = new();
    // Nullable Tours: null means "this fetch failed/errored" — the sentinel that lets
    // DrainLoadedCareerTours (main thread only) be the ONE place that mutates _careerToursLoading,
    // instead of the async fetch continuation touching it directly (grk review finding: a plain
    // HashSet mutated from both the ECS thread and an async continuation is a data race).
    private readonly ConcurrentQueue<(NetUserId User, int? Tours)> _loadedCareerTours = new();

    // grk design-sanity pass, finding 1 (HIGH): a moderator's wingmaterevoke must be a HARD deny
    // that overrides the auto-eligibility path too, not just a removal from _approvedGuides — the
    // CVar's own doc comment promises "wingmateapprove/wingmaterevoke keep working ... either way,"
    // and without this denylist a revoked-but-still-tenured account would simply re-qualify via
    // MeetsAutoEligibilityCriteria on its very next check. Cleared only by RoundRestart (ClearRound)
    // or an explicit re-ApproveGuide — mirrors _approvedGuides' own round-local lifetime exactly
    // (see ClearRound/OnEnabledChanged, neither of which clears _approvedGuides either).
    private readonly HashSet<NetUserId> _deniedGuides = new();

    // Test-only overrides mirroring the existing _writePersistentBlock test-seam idiom: rules-test
    // harnesses construct WingmateSystem directly (bypassing Initialize()), so _cfg/_ledger are
    // never wired up there. These let auto-eligibility posture and the tenure lookup be driven
    // deterministically without touching IoC or SQLite.
    private bool? _autoGuideEligibilityOverrideForTests;
    private Func<NetUserId, Task<int>>? _fetchCareerToursOverrideForTests;

    /// <summary>
    /// Coarse moderator-visible state. Deliberately excludes partner identity, offers, nonces,
    /// round blocks, and transition reasons.
    /// </summary>
    internal WingmateModeratorSnapshot GetModeratorSnapshot(NetUserId user) =>
        new(_state.GetStatus(user), _approvedGuides.Contains(user), _volunteeringGuides.Contains(user));

    /// <summary>
    /// Whether this account is currently volunteering as a guide (round-local state). Read-only
    /// surface for the third-visit wingmate prompt (<c>SolreignWingmatePromptSystem</c>) — a player
    /// already volunteering needs no nudge. Exposes strictly less than
    /// <see cref="GetModeratorSnapshot"/> already does.
    /// </summary>
    public bool IsVolunteeringGuide(NetUserId user) => _volunteeringGuides.Contains(user);

    /// <summary>Approves a connected account to opt in as a guide for this round. An explicit
    /// approval always overrides a prior <see cref="RevokeGuide"/> denial, in either eligibility
    /// posture.</summary>
    internal WingmateTransitionResult ApproveGuide(NetUserId user)
    {
        var changed = _approvedGuides.Add(user);
        changed |= _deniedGuides.Remove(user);
        var result = new WingmateTransitionResult(changed, "moderator-action", affectedUsers: new[] { user });
        PublishModeratorResult(result);
        return result;
    }

    /// <summary>
    /// Revokes guide eligibility and removes the account from any request, offer, or pair. This is
    /// intentionally safe to use while the player-facing feature CVar is disabled.
    ///
    /// Also records a hard DENY (<see cref="_deniedGuides"/>) so this account cannot simply
    /// re-qualify on its very next eligibility check via the auto-eligibility path (grk
    /// design-sanity review finding: a bare removal from <see cref="_approvedGuides"/> is not a
    /// real revoke once auto-eligibility is on — an account that still meets the basic/tenure
    /// criteria would otherwise become eligible again immediately). This is the actual mechanism
    /// behind the CVar's "wingmateapprove/wingmaterevoke keep working ... either way" guarantee.
    /// </summary>
    internal WingmateTransitionResult RevokeGuide(NetUserId user)
    {
        var changed = _approvedGuides.Remove(user);
        changed |= _volunteeringGuides.Remove(user);
        changed |= _deniedGuides.Add(user);
        var hadState = _state.GetStatus(user) != WingmateStatus.Idle;
        var affected = _state.RemoveUser(user);
        var result = new WingmateTransitionResult(changed || hadState, "moderator-action",
            affectedUsers: affected.ToArray());
        PublishModeratorResult(result);
        return result;
    }

    /// <summary>Ends an active pair without creating or exposing a reason.</summary>
    internal WingmateTransitionResult DissolveForModerator(NetUserId user)
    {
        var transition = _state.Dissolve(user);
        var result = new WingmateTransitionResult(transition.Changed, "moderator-action",
            affectedUsers: transition.AffectedUsers.ToArray());
        PublishModeratorResult(result);
        return result;
    }

    // ---------------------------------------------------------------------------------------
    // FIX 1 (UX-SIMPLE, 2026-07-16): guide eligibility gate.
    //
    // Before this, the ONLY path that ever added an account to _approvedGuides was the
    // moderator-only `wingmateapprove` console command — with zero in-game explanation when that
    // hadn't happened, an ordinary player's Volunteer button was permanently, silently greyed out.
    // CCVars.SolreignWingmatesAutoGuideEligibility (default TRUE) makes basic-criteria accounts
    // automatically guide-eligible; `wingmateapprove`/`wingmaterevoke` keep working as an
    // override/revoke path in EITHER posture — see IsGuideEligible below, which OR's the
    // moderator grant with the auto path rather than replacing it.
    // ---------------------------------------------------------------------------------------

    private bool AutoGuideEligibilityEnabled =>
        _autoGuideEligibilityOverrideForTests ?? (_cfg?.GetCVar(CCVars.SolreignWingmatesAutoGuideEligibility) ?? false);

    /// <summary>
    /// True if this account may act as a guide right now. A moderator's <c>wingmateapprove</c>
    /// grant always works, in either posture — see the CVar's doc comment. A moderator's
    /// <c>wingmaterevoke</c> is a HARD deny that overrides the auto path too (see
    /// <see cref="_deniedGuides"/>/<see cref="RevokeGuide"/>) — checked FIRST, before either the
    /// explicit-approval or the auto path get a say. When the CVar is on and the account is not
    /// denied, one never explicitly approved may still qualify automatically via
    /// <see cref="MeetsAutoEligibilityCriteria"/>.
    /// </summary>
    private bool IsGuideEligible(NetUserId actor) =>
        !_deniedGuides.Contains(actor) &&
        (_approvedGuides.Contains(actor) || (AutoGuideEligibilityEnabled && MeetsAutoEligibilityCriteria(actor)));

    /// <summary>
    /// The actual automatic-posture gate: alive, not a ghost/observer, and at or above the
    /// configurable career-tenure floor (<see cref="CCVars.SolreignWingmatesMinimumShifts"/>, an
    /// existing CVar that had never been wired to anything until this fix). Fails CLOSED (not
    /// eligible) on any unresolved precondition — no session/entity, still loading tenure, etc —
    /// same discipline as every other fail-closed gate in this file.
    /// </summary>
    private bool MeetsAutoEligibilityCriteria(NetUserId actor)
    {
        if (_players == null || !_players.TryGetSessionById(actor, out var session))
            return false;
        if (session.AttachedEntity is not { } entity || !Exists(entity))
            return false;

        var isGhost = HasComp<GhostComponent>(entity);
        var isDead = TryComp<MobStateComponent>(entity, out var mobState) && _mobState.IsDead(entity, mobState);
        if (!IsBasicGuideCriteriaMet(isGhost, isDead))
            return false;

        return HasEnoughCareerTours(actor);
    }

    /// <summary>Pure decision seam, unit-testable without IoC/EntityManager — mirrors the existing
    /// <see cref="ResolveDisplayNameForTests"/> idiom in this file.</summary>
    internal static bool IsBasicGuideCriteriaMet(bool isGhost, bool isDead) => !isGhost && !isDead;

    private bool HasEnoughCareerTours(NetUserId actor)
    {
        var minimum = _cfg?.GetCVar(CCVars.SolreignWingmatesMinimumShifts) ?? 0;
        if (minimum <= 0)
            return true; // operator set the floor to 0 (or the CVar is unreachable): no tenure gate at all.

        if (!_careerToursCache.TryGetValue(actor, out var tours))
        {
            RequestCareerToursLoad(actor);
            return false; // fail closed while unresolved — same idiom as OffersFailClosedUntilPersistentBlocksAreLoaded
        }

        return tours >= minimum;
    }

    private void RequestCareerToursLoad(NetUserId actor)
    {
        if (!_careerToursLoading.Add(actor))
            return; // a fetch for this account is already in flight

        FetchCareerToursAsync(actor, _fetchCareerToursOverrideForTests ?? DefaultFetchCareerTours);
    }

    private async void FetchCareerToursAsync(NetUserId actor, Func<NetUserId, Task<int>> fetch)
    {
        try
        {
            var tours = await fetch(actor);
            _loadedCareerTours.Enqueue((actor, tours));
        }
        catch (Exception e)
        {
            // Mirrors LoadPersistentBlocks: never let a transient ledger-read failure crash the
            // process. grk review finding (thread safety): this async continuation may resume on
            // ANY thread-pool thread, not necessarily the main ECS thread — it must NEVER mutate
            // _careerToursLoading (a plain, non-concurrent HashSet also touched from
            // RequestCareerToursLoad/DrainLoadedCareerTours on the main thread) directly. Enqueue a
            // null-Tours sentinel instead and let DrainLoadedCareerTours (main-thread only, same
            // discipline as DrainCompletedBlockWrites for the persistent-block queue) be the ONE
            // place that clears the in-flight marker. A later beacon-open can then retry the fetch;
            // until then the account simply stays fail-closed (not auto-eligible via tenure).
            Log?.Error($"Error while loading career tours for Wingmates eligibility ({actor}):\n{e}");
            _loadedCareerTours.Enqueue((actor, null));
        }
    }

    private async Task<int> DefaultFetchCareerTours(NetUserId actor)
    {
        if (_ledger == null)
            return 0;
        var stats = await _ledger.GetCareerStatsAsync(actor.UserId);
        return stats.Tours;
    }

    private void DrainLoadedCareerTours()
    {
        while (_loadedCareerTours.TryDequeue(out var entry))
        {
            if (entry.Tours is { } tours)
                _careerToursCache[entry.User] = tours;
            _careerToursLoading.Remove(entry.User);
        }
    }

    /// <summary>
    /// FIX 1: the reason a not-yet-eligible viewer cannot volunteer as a guide right now, or null
    /// once they ARE eligible (or already volunteering, or the feature itself is disabled — the
    /// existing Disabled panel already explains that case on its own). Computed alongside
    /// <see cref="WingmateUiState.CanVolunteer"/> so the client never has to silently grey a
    /// button with no explanation (per the diagnostic report: "no silent greyed buttons anywhere
    /// in the wingmate flow").
    /// </summary>
    private string? ComputeVolunteerIneligibleReason(NetUserId viewer, bool canVolunteer)
    {
        if (!_enabled || canVolunteer || _volunteeringGuides.Contains(viewer))
            return null;

        if (!AutoGuideEligibilityEnabled)
            return "wingmates-guide-ineligible-manual";

        if (_players == null || !_players.TryGetSessionById(viewer, out var session) ||
            session.AttachedEntity is not { } entity || !Exists(entity))
            return "wingmates-guide-ineligible-generic";

        if (HasComp<GhostComponent>(entity))
            return "wingmates-guide-ineligible-observer";

        if (TryComp<MobStateComponent>(entity, out var mobState) && _mobState.IsDead(entity, mobState))
            return "wingmates-guide-ineligible-not-alive";

        if (!_careerToursCache.ContainsKey(viewer))
            return "wingmates-guide-ineligible-loading";

        return "wingmates-guide-ineligible-tenure";
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WingmateBeaconComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<WingmateBeaconComponent, BoundUIClosedEvent>(OnUiClosed);
        SubscribeLocalEvent<WingmateBeaconComponent, ComponentShutdown>(OnBeaconShutdown);
        SubscribeLocalEvent<WingmateBeaconComponent, EntityTerminatingEvent>(OnBeaconTerminating);
        SubscribeLocalEvent<WingmateBeaconComponent, WingmateRequestMessage>(OnRequest);
        SubscribeLocalEvent<WingmateBeaconComponent, WingmateCancelRequestMessage>(OnCancelRequest);
        SubscribeLocalEvent<WingmateBeaconComponent, WingmateOfferMessage>(OnOffer);
        SubscribeLocalEvent<WingmateBeaconComponent, WingmateAcceptMessage>(OnAccept);
        SubscribeLocalEvent<WingmateBeaconComponent, WingmateDeclineMessage>(OnDecline);
        SubscribeLocalEvent<WingmateBeaconComponent, WingmateDissolveMessage>(OnDissolve);
        SubscribeLocalEvent<WingmateBeaconComponent, WingmatePauseMessage>(OnPause);
        SubscribeLocalEvent<WingmateBeaconComponent, WingmateResumeMessage>(OnResume);
        SubscribeLocalEvent<WingmateBeaconComponent, WingmateBlockCurrentPartnerMessage>(OnBlock);
        SubscribeLocalEvent<WingmateBeaconComponent, WingmateVolunteerMessage>(OnVolunteer);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);

        _ledger = new SeasonLedgerStore(SeasonLedgerDbPath.Resolve(_cfg, _res));
        _writePersistentBlock = _ledger.AddWingmateBlockAsync;
        LoadPersistentBlocks();
        _snapshots = new WingmateSnapshotAdapter<ICommonSession>(
            TryResolveSession,
            IsBeaconAvailable,
            beacon => GetNetEntity(beacon),
            BuildPrivateState,
            (snapshot, session) => RaiseNetworkEvent(snapshot, session),
            () => _state.Expire(CurrentTime));
        _players.PlayerStatusChanged += OnPlayerStatusChanged;
        _cfg.OnValueChanged(CCVars.SolreignWingmatesEnabled, OnEnabledChanged, true);
    }

    public override void Shutdown()
    {
        _cfg.UnsubValueChanged(CCVars.SolreignWingmatesEnabled, OnEnabledChanged);
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        while (_loadedBlocks.TryDequeue(out var blocks))
        {
            _persistentBlocks.UnionWith(blocks);
            _persistentBlocksReady = true;
        }

        DrainLoadedCareerTours();

        // NOTHING drains while the feature is operator-disabled — a disable must suppress every
        // player-visible consequence of in-flight work, including failure popups delivered mid-
        // maintenance to attached players. Results simply sit in the (thread-safe) queue; the first
        // enabled Update() drains them, where the service-epoch stamp discards maintenance-window
        // failure notices while still applying confirmed durable blocks (see DrainCompletedBlockWrites).
        // If the feature never re-enables this process lifetime, the queue is bounded by the writes
        // in flight at disable time and confirmed blocks are re-loaded from SQLite on next boot; the
        // pending-gate entries left behind keep the affected pairs fail-closed in the meantime.
        if (!_enabled)
            return;

        // Async continuations enqueue scalar write results only; all state mutation and ECS
        // publication happens here, on the main thread.
        DrainCompletedBlockWrites();

        if (_timing.CurTime < _nextExpiryCheck)
            return;
        _nextExpiryCheck = _timing.CurTime + ExpiryCheckInterval;
        PublishAfterExpiry(Array.Empty<NetUserId>());
    }

    private void OnEnabledChanged(bool enabled)
    {
        var affected = _snapshots.OpenUsers.ToArray();
        _enabled = enabled;
        _nextExpiryCheck = _timing.CurTime;
        if (!enabled)
        {
            _state.Clear();
            _volunteeringGuides.Clear();
            _requestLimiter.Clear();
            _offerLimiter.Clear();
            // Undelivered failure notices are dropped here — and ONLY here. A round rollover must never
            // lose the fact that a durable safety action failed (ClearRound leaves the tallies alone),
            // but a feature disable is a deliberate operator action taking the whole feature out of
            // service: a notice delivered after a disable/re-enable cycle would describe an attempt made
            // under a service state the operator has explicitly reset, so dropping is the intended
            // trade-off rather than an accidental loss.
            _pendingBlockFailures.Clear();
            // Bumping the service epoch extends that same suppression to writes still in flight (or
            // completed-but-undrained): their FAILURE results are discarded at the next enabled drain
            // instead of becoming popups — see DrainCompletedBlockWrites. Update() also stops draining
            // entirely while disabled, so nothing surfaces mid-maintenance either.
            _serviceEpoch++;
        }
        RefreshPublicShells();
        PublishAfterExpiry(affected);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args) => ClearRound();

    private void ClearRound()
    {
        _state.Clear();
        _approvedGuides.Clear();
        _volunteeringGuides.Clear();
        // Mirrors _approvedGuides' own round-local lifetime exactly (see RevokeGuide's doc comment):
        // a moderator's deny is a THIS-ROUND decision, same as an approval.
        _deniedGuides.Clear();
        // Null-safe like the other _snapshots call sites in this file (PublishAfterExpiry,
        // PublishModeratorResult, HandleUnavailableSession): rules-unit tests construct WingmateSystem
        // directly, bypassing Initialize(), so _snapshots is never wired up there.
        _snapshots?.Clear();
        _requestLimiter.Clear();
        _offerLimiter.Clear();
        // _pendingBlockFailures deliberately NOT cleared: an undelivered "your durable block failed"
        // notice must survive round rollover until the player actually sees it (it will be delivered
        // with previous-shift wording — see NormalizePendingBlockFailureTally). Only delivery, or an
        // operator feature-disable (OnEnabledChanged), removes the tallies.
        // _lastTransition IS cleared: per-action failure popups are tied to the current round's
        // action attempt — a pre-round failure would surface as a stale popup against a freshly
        // rebuilt state, which is misleading. Per-actor entries are also dropped on disconnect
        // (HandleUnavailableSession) so a disconnected player cannot inherit a stale popup.
        _lastTransition.Clear();
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus is not (SessionStatus.Disconnected or SessionStatus.Zombie))
            return;
        HandleUnavailableSession(args.Session.UserId);
    }

    private void HandleUnavailableSession(NetUserId user)
    {
        var affected = _state.RemoveUser(user);
        _volunteeringGuides.Remove(user);
        // P1.4 SILENT-DROP BATCH FIX: the failing popup is per-actor; a disconnected actor's
        // pending failure is moot (no session to render it) and the entry only re-grows if they
        // reconnect and press something that fails again — no need to carry the bytes across.
        _lastTransition.Remove(user);
        // Rate-limit windows are deliberately NOT cleared here. They are keyed by the account-stable
        // NetUserId and must persist across a reconnect within the same round — otherwise a player
        // could bypass the Request/Offer rate limit at will by disconnecting and reconnecting. Only a
        // round boundary (OnEnabledChanged(false) / ClearRound) resets them.
        _snapshots?.CloseUser(user, clearGeneration: true);
        PublishAfterExpiry(affected);
    }

    private void OnUiOpened(EntityUid uid, WingmateBeaconComponent component, BoundUIOpenedEvent args)
    {
        if (!TryGetActor(args.Actor, out var actor, out _))
            return;
        _snapshots.RememberOpen(actor, uid);
        DeliverPendingBlockFailureNotice(actor);
        // FIX 1: prefetch this account's career tenure as soon as they open a beacon (rather than
        // waiting for the first Volunteer press) so the auto-eligibility gate is usually already
        // resolved by the time they act — an already-approved account never needs this lookup.
        if (!_approvedGuides.Contains(actor) && AutoGuideEligibilityEnabled)
            RequestCareerToursLoad(actor);
        PublishAfterExpiry(new[] { actor });
    }

    private void OnUiClosed(EntityUid uid, WingmateBeaconComponent component, BoundUIClosedEvent args)
    {
        if (TryGetActor(args.Actor, out var actor, out _))
            _snapshots.Close(actor, uid);
    }

    private void OnBeaconShutdown(EntityUid uid, WingmateBeaconComponent component, ComponentShutdown args) =>
        _snapshots.RemoveBeacon(uid);

    private void OnBeaconTerminating(EntityUid uid, WingmateBeaconComponent component, ref EntityTerminatingEvent args) =>
        _snapshots.RemoveBeacon(uid);

    private void OnRequest(EntityUid uid, WingmateBeaconComponent component, WingmateRequestMessage args)
    {
        if (!TryGetActor(args.Actor, out var actor, out _)) return;
        RememberBeacon(actor, uid);
        ExpireApplyAndPublish(() => Request(actor, args.Department, args.TeachingMode), actor);
    }

    private void OnCancelRequest(EntityUid uid, WingmateBeaconComponent component, WingmateCancelRequestMessage args)
    {
        if (!TryGetActor(args.Actor, out var actor, out _) || !_enabled) return;
        RememberBeacon(actor, uid);
        ExpireApplyAndPublish(() => new WingmateTransitionResult(true, "cancelled",
            affectedUsers: _state.RemoveUser(actor).ToArray()), actor);
    }

    private void OnOffer(EntityUid uid, WingmateBeaconComponent component, WingmateOfferMessage args)
    {
        if (!TryGetActor(args.Actor, out var actor, out _)) return;
        RememberBeacon(actor, uid);
        ExpireApplyAndPublish(() => Offer(actor, args.RequesterToken), actor);
    }

    private void OnAccept(EntityUid uid, WingmateBeaconComponent component, WingmateAcceptMessage args)
    {
        if (!TryGetActor(args.Actor, out var actor, out _) || !_enabled) return;
        RememberBeacon(actor, uid);
        ExpireApplyAndPublish(() => _state.Accept(actor, args.OfferNonce, CurrentTime), actor);
    }

    private void OnDecline(EntityUid uid, WingmateBeaconComponent component, WingmateDeclineMessage args)
    {
        if (!TryGetActor(args.Actor, out var actor, out _) || !_enabled) return;
        RememberBeacon(actor, uid);
        ExpireApplyAndPublish(() => Decline(actor, args.OfferNonce, args.BlockGuide), actor);
    }

    private void OnDissolve(EntityUid uid, WingmateBeaconComponent component, WingmateDissolveMessage args)
    {
        if (!TryGetActor(args.Actor, out var actor, out _) || !_enabled) return;
        RememberBeacon(actor, uid);
        ExpireApplyAndPublish(() => _state.Dissolve(actor), actor);
    }

    private void OnPause(EntityUid uid, WingmateBeaconComponent component, WingmatePauseMessage args)
    {
        if (!TryGetActor(args.Actor, out var actor, out _) || !_enabled) return;
        RememberBeacon(actor, uid);
        ExpireApplyAndPublish(() => _state.Pause(actor), actor);
    }

    private void OnResume(EntityUid uid, WingmateBeaconComponent component, WingmateResumeMessage args)
    {
        if (!TryGetActor(args.Actor, out var actor, out _) || !_enabled) return;
        RememberBeacon(actor, uid);
        ExpireApplyAndPublish(() => _state.Resume(actor), actor);
    }

    private void OnBlock(EntityUid uid, WingmateBeaconComponent component, WingmateBlockCurrentPartnerMessage args)
    {
        if (!TryGetActor(args.Actor, out var actor, out _) || !_enabled) return;
        RememberBeacon(actor, uid);
        ExpireApplyAndPublish(() => BlockCurrentPartner(actor), actor);
    }

    private void OnVolunteer(EntityUid uid, WingmateBeaconComponent component, WingmateVolunteerMessage args)
    {
        if (!TryGetActor(args.Actor, out var actor, out _) || !_enabled) return;
        RememberBeacon(actor, uid);
        ExpireApplyAndPublish(() => SetVolunteering(actor, args.Volunteering, args.CharterAccepted), actor);
    }

    private bool TryGetActor(EntityUid actorEntity, out NetUserId actor, out ICommonSession session)
    {
        actor = default;
        session = default!;
        if (!_players.TryGetSessionByEntity(actorEntity, out var found))
            return false;
        session = found;
        actor = found.UserId;
        return true;
    }

    private WingmateTransitionResult Request(NetUserId actor, string department,
        Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode mode)
    {
        if (!_enabled) return new(false, "disabled");
        if (!_requestLimiter.TryConsume(actor, CurrentTime)) return new(false, "rate-limited");
        if (!AllowedDepartments.Contains(department)) return new(false, "invalid-department");
        if (!TryMapTeachingMode(mode, out var mapped)) return new(false, "invalid-teaching-mode");
        return _state.Request(actor, department, mapped, CurrentTime);
    }

    private WingmateTransitionResult Offer(NetUserId actor, Guid requesterToken)
    {
        if (!_enabled) return new(false, "disabled");
        if (!_offerLimiter.TryConsume(actor, CurrentTime)) return new(false, "rate-limited");
        if (!_persistentBlocksReady) return new(false, "blocks-loading");
        if (!IsGuideEligible(actor)) return new(false, "not-approved");
        if (!_volunteeringGuides.Contains(actor)) return new(false, "not-volunteering");
        // Tokens are viewer-bound: only a token this system minted for `actor` specifically can ever
        // resolve here, so a token intercepted from — or forged for — a different viewer is rejected
        // identically to an unknown token.
        if (!_state.TryResolveRequesterToken(actor, requesterToken, out var requester))
            return new(false, "invalid-requester-token");
        return OfferResolved(actor, requester);
    }

    private WingmateTransitionResult OfferResolved(NetUserId actor, NetUserId requester)
    {
        if (IsBlockedOrPending(actor, requester))
            return new(false, "blocked");
        return _state.Offer(actor, requester, CurrentTime);
    }

    private bool IsPersistentlyBlocked(NetUserId first, NetUserId second) =>
        _persistentBlocks.Contains((first.UserId, second.UserId)) ||
        _persistentBlocks.Contains((second.UserId, first.UserId));

    /// <summary>
    /// True if a durable block between the two accounts is either confirmed
    /// (<see cref="_persistentBlocks"/>) or has a write currently in flight
    /// (<see cref="_pendingPersistentBlocks"/>). Gates new offers and seeker visibility so a guide
    /// cannot slip an offer through the window between starting a decline-time block and its SQLite
    /// write being confirmed — including after the ordinary decline cooldown lapses, or across a round
    /// boundary/feature toggle (<see cref="WingmateRoundState.Clear"/> resets decline cooldowns, but
    /// pending durable-block bookkeeping is deliberately round-independent). Deliberately NOT used by
    /// <see cref="BeginPersistentBlock"/>'s already-confirmed fast path: that path must only fire for a
    /// block that is durably confirmed, never merely pending, or a second concurrent block attempt would
    /// apply the round-local dissolve before its own write is confirmed.
    /// </summary>
    private bool IsBlockedOrPending(NetUserId first, NetUserId second) =>
        IsPersistentlyBlocked(first, second) ||
        _pendingPersistentBlocks.Contains((first.UserId, second.UserId)) ||
        _pendingPersistentBlocks.Contains((second.UserId, first.UserId));

    private WingmateTransitionResult SetVolunteering(NetUserId actor, bool volunteering, bool charterAccepted)
    {
        if (!_enabled) return new(false, "disabled");
        if (volunteering && !IsGuideEligible(actor)) return new(false, "not-approved");
        if (volunteering && !charterAccepted) return new(false, "charter-not-accepted");
        var changed = volunteering ? _volunteeringGuides.Add(actor) : _volunteeringGuides.Remove(actor);
        if (volunteering)
            return new(changed, "volunteering", affectedUsers: new[] { actor });
        var withdrawn = _state.WithdrawOutgoingOffer(actor);
        var affected = withdrawn.AffectedUsers.Append(actor).Distinct().ToArray();
        return new(changed || withdrawn.Changed, "not-volunteering", affectedUsers: affected);
    }

    private WingmateTransitionResult BlockCurrentPartner(NetUserId actor)
    {
        var partner = _state.GetPartner(actor);
        if (partner is null)
            return new(false, "not-paired");

        return BeginPersistentBlock(actor, partner.Value);
    }

    /// <summary>
    /// Starts (or reports) a durable, cross-round block between two accounts. Nothing observable
    /// happens synchronously: the round-local pair is left untouched and the block is not enforced
    /// until <see cref="DrainCompletedBlockWrites"/> confirms the SQLite write succeeded. This is the
    /// only path that may add to <see cref="_persistentBlocks"/> — optimistically mutating that set
    /// before the write is confirmed would let a failed write silently "succeed" in memory.
    /// </summary>
    private WingmateTransitionResult BeginPersistentBlock(NetUserId blocker, NetUserId blocked)
    {
        if (blocker == blocked)
            return new(false, "self-block");

        // A block already loaded from the ledger or confirmed by a previous write is safe to enforce
        // immediately — no new write is needed.
        if (IsPersistentlyBlocked(blocker, blocked))
            return _state.BlockForRound(blocker, blocked);

        var key = (blocker.UserId, blocked.UserId);
        if (!_pendingPersistentBlocks.Add(key))
            return new(false, "block-pending", affectedUsers: new[] { blocker });

        // Stamp the write with the round epoch AND service epoch active right now, at write-start. A
        // write that straddles a round restart (ClearRound bumps WingmateRoundState.RoundEpoch) is
        // recognized at drain time: a successful write still confirms unconditionally (see
        // DrainCompletedBlockWrites for why that's safe), while a FAILED write's notice is retained but
        // re-worded — delivered with previous-shift phrasing instead of being presented as a
        // current-round event, and never silently dropped. A write that straddles an operator DISABLE
        // (stale service epoch) instead has its failure notice discarded entirely — see
        // DrainCompletedBlockWrites.
        var epoch = _state.RoundEpoch;
        var serviceEpoch = _serviceEpoch;

        if (_writePersistentBlock == null)
        {
            // No ledger wired up (e.g. a unit-test harness that never ran Initialize()). Fail closed
            // through the same drain path real writes use, rather than silently no-op'ing.
            _completedBlockWrites.Enqueue(new PersistentBlockWriteResult(blocker, blocked, false, epoch, serviceEpoch));
            return new(false, "block-pending", affectedUsers: new[] { blocker });
        }

        var task = PersistBlockAsync(blocker, blocked, epoch, serviceEpoch);
        _pendingBlockWriteTasksForTests[key] = task;
        return new(false, "block-pending", affectedUsers: new[] { blocker });
    }

    private async Task PersistBlockAsync(NetUserId blocker, NetUserId blocked, int epoch, int serviceEpoch)
    {
        var success = false;

        try
        {
            success = await _writePersistentBlock!(blocker.UserId, blocked.UserId);
        }
        catch (Exception e)
        {
            // Defensive, mirroring LoadPersistentBlocks: SetPersistentBlockWriterForTests lets a unit
            // test harness supply a writer without ever running Initialize(), so Log (wired via IoC
            // PostInject, not our Initialize()) may still be unset. Guard so a throwing test double
            // reports through the same fail-closed path instead of NRE'ing on a null Log.
            Log?.Error($"Error while persisting Wingmates block for {blocker}:\n{e}");
        }

        // The exception path above is caught here, not left to become an unobserved-task-exception:
        // the outcome is always recorded so the pending block is never stranded.
        _completedBlockWrites.Enqueue(new PersistentBlockWriteResult(blocker, blocked, success, epoch, serviceEpoch));
    }

    /// <summary>
    /// Applies every completed durable-block write since the last drain. This is the only place that
    /// mutates <see cref="_persistentBlocks"/> for a newly-written block, applies the round-local
    /// dissolve/block, and publishes the result — i.e. the point at which a pending block "takes
    /// effect". A failed write instead notifies the blocker and leaves state exactly as it was.
    ///
    /// A successful write is applied unconditionally, regardless of which round it resolves in: the
    /// durable block it confirms is deliberately round-independent (see BeginPersistentBlock /
    /// _pendingPersistentBlocks, and <see cref="PendingDurableBlockRejectsReofferAcrossARoundBoundaryUntilDrained"/>-
    /// style coverage), and BlockForRound only dissolves a pairing if the blocker is CURRENTLY paired
    /// with exactly the blocked account — the pending-inclusive gate (<see cref="IsBlockedOrPending"/>)
    /// already prevents those two specific accounts from re-pairing with each other while the write is
    /// in flight, so applying it late can never touch an unrelated new-round pairing.
    ///
    /// A FAILED write is never dropped across a ROUND boundary: it records a failed cross-round SAFETY
    /// action ("prevent this guide from offering to me in future rounds"), and silently losing it would
    /// leave the player believing a durable block exists when it does not — the guide could offer again
    /// in a later round with no indication anything went wrong. The write's stamped round epoch (<see
    /// cref="PersistentBlockWriteResult.Epoch"/>, captured at write start) changes only the WORDING at
    /// delivery time: a failure from the current round keeps the current-shift phrasing, while one that
    /// straddled a round boundary is delivered with previous-shift phrasing (see
    /// <see cref="NormalizePendingBlockFailureTally"/>), so the notice is both never lost and never
    /// misleading about when the attempt happened.
    ///
    /// The ONE exception is an operator DISABLE: a failed write whose stamped service epoch (<see
    /// cref="PersistentBlockWriteResult.ServiceEpoch"/>) is stale straddled OnEnabledChanged(false), and
    /// its notice is discarded outright — consistent with the disable-time clear of the queued tallies,
    /// and it also prevents the disable's round-epoch bump from mislabeling a maintenance-window failure
    /// as "previous shift" when no round actually ended.
    ///
    /// Failure notification is BATCHED per user within one drain: every failed result for a user in
    /// this pass is accumulated into their tally first, then exactly one delivery attempt is made per
    /// affected user — two writes failing in the same Update produce ONE aggregated popup, never two.
    /// </summary>
    private void DrainCompletedBlockWrites(Action<NetUserId>? onFailure = null)
    {
        HashSet<NetUserId>? notifiedUsers = null;

        while (_completedBlockWrites.TryDequeue(out var write))
        {
            var key = (write.Blocker.UserId, write.Blocked.UserId);
            _pendingPersistentBlocks.Remove(key);
            _pendingBlockWriteTasksForTests.Remove(key);

            if (!write.Success)
            {
                if (write.ServiceEpoch != _serviceEpoch)
                    continue;

                if (onFailure != null)
                {
                    onFailure(write.Blocker);
                    continue;
                }

                AccumulatePendingBlockFailure(write.Blocker, write.Epoch);
                (notifiedUsers ??= new HashSet<NetUserId>()).Add(write.Blocker);
                continue;
            }

            _persistentBlocks.Add(key);
            var result = _state.BlockForRound(write.Blocker, write.Blocked);
            PublishAfterExpiry(result.AffectedUsers.Count == 0
                ? new[] { write.Blocker, write.Blocked }
                : result.AffectedUsers);
        }

        if (notifiedUsers == null)
            return;

        foreach (var user in notifiedUsers)
            DeliverPendingBlockFailureNotice(user);
    }

    /// <summary>
    /// Folds one more failed durable-block write into this blocker's bounded tally (so two failures
    /// against two different partners are never collapsed into a single generic popup, and however many
    /// failures accumulate the per-user state stays O(1) — see <see cref="_pendingBlockFailures"/>).
    /// Does NOT deliver: the drain batches all of a user's failures in one pass and makes exactly one
    /// delivery attempt afterwards.
    /// </summary>
    private void AccumulatePendingBlockFailure(NetUserId blocker, int writeEpoch)
    {
        var tally = NormalizePendingBlockFailureTally(blocker);
        _pendingBlockFailures[blocker] = writeEpoch == _state.RoundEpoch
            ? tally with { CurrentCount = tally.CurrentCount + 1 }
            : tally with { StaleCount = tally.StaleCount + 1 };
    }

    /// <summary>
    /// This account's failure tally expressed relative to the CURRENT round epoch: if the round has
    /// moved on since the tally's this-shift counter was last touched, that counter folds into the
    /// previous-shift counter — which is exactly how round rollover re-words (rather than loses) an
    /// undelivered notice. Pure read; callers persist the result if they mutate it.
    /// </summary>
    private WingmateBlockFailureTally NormalizePendingBlockFailureTally(NetUserId user)
    {
        var currentEpoch = _state.RoundEpoch;
        if (!_pendingBlockFailures.TryGetValue(user, out var tally))
            return new WingmateBlockFailureTally(currentEpoch, 0, 0);
        if (tally.Epoch == currentEpoch)
            return tally;
        return new WingmateBlockFailureTally(currentEpoch, 0, tally.StaleCount + tally.CurrentCount);
    }

    /// <summary>
    /// Pure aggregation seam for the failure notice, unit-testable without IoC/Loc: renders the two
    /// bounded counters into at most two (locKey, count) parts — one aggregated popup per user, never
    /// one popup per failure. The parts carry ONLY counts: no blocked-party identity ever reaches the
    /// notice (that would leak who the player tried to block to a shoulder-surfer).
    /// </summary>
    internal static IReadOnlyList<(string LocKey, int Count)> BuildBlockFailureNoticeParts(
        int currentShiftCount, int previousShiftCount)
    {
        var parts = new List<(string LocKey, int Count)>(2);
        if (currentShiftCount > 0)
            parts.Add(("wingmates-block-persistence-failed-count", currentShiftCount));
        if (previousShiftCount > 0)
            parts.Add(("wingmates-block-persistence-failed-previous-shift", previousShiftCount));
        return parts;
    }

    /// <summary>
    /// Renders this account's queued failure tally into one popup body (current-shift line,
    /// previous-shift line, or both), or null when nothing is queued. Localization only — the
    /// aggregation/wording-selection logic lives in <see cref="BuildBlockFailureNoticeParts"/> and
    /// <see cref="NormalizePendingBlockFailureTally"/>.
    /// </summary>
    private string? BuildPendingBlockFailureNoticeText(NetUserId user)
    {
        if (!_pendingBlockFailures.ContainsKey(user))
            return null;

        var tally = NormalizePendingBlockFailureTally(user);
        if (tally.CurrentCount + tally.StaleCount == 0)
            return null;

        var lines = BuildBlockFailureNoticeParts(tally.CurrentCount, tally.StaleCount)
            .Select(part => Loc.GetString(part.LocKey, ("count", part.Count)));
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Attempts an immediate popup delivery of every queued failure record for this account as ONE
    /// aggregated popup; returns false (without throwing or logging) if the blocker has no resolvable,
    /// attached, existing entity right now. Callers are responsible for retrying — this seam never
    /// drops a record on its own, it only reports whether *this* attempt landed.
    /// </summary>
    private bool TryDeliverBlockFailureNotice(NetUserId blocker)
    {
        // Session/entity availability is checked BEFORE any text is built: rules-test harnesses
        // construct this system without IoC (no ILocalizationManager), and they must reach the ordinary
        // "cannot deliver right now" outcome rather than throwing inside Loc.GetString.
        if (_players == null ||
            !_players.TryGetSessionById(blocker, out var session) ||
            session.AttachedEntity is not { } entity ||
            !Exists(entity))
            return false;

        if (BuildPendingBlockFailureNoticeText(blocker) is not { } text)
            return true;

        _popup.PopupEntity(text, entity, entity, PopupType.MediumCaution);
        return true;
    }

    /// <summary>
    /// Delivers any queued block-persistence-failure tally for this account, called once per user per
    /// drain pass and from the same beacon-UI-open seam <see cref="OnUiOpened"/> already observes for
    /// every other private-state refresh. The tally is removed only when a delivery actually lands (one
    /// beacon-open popup is the acknowledgment); if delivery can't land (e.g. attached entity not yet
    /// ready this same tick), it is kept rather than lost.
    /// </summary>
    private void DeliverPendingBlockFailureNotice(NetUserId user)
    {
        if (!_pendingBlockFailures.ContainsKey(user))
            return;
        if (TryDeliverBlockFailureNotice(user))
            _pendingBlockFailures.Remove(user);
    }

    private WingmateTransitionResult Decline(NetUserId requester, Guid nonce, bool blockGuide)
    {
        var incoming = _state.GetIncomingOffer(requester);
        if (incoming is not { } offer || offer.Nonce != nonce)
            return new(false, "stale-offer");

        // Declining is immediate and unconditional — it never depends on the durable ledger. The
        // stronger cross-round block is layered on top, but only takes effect once SQLite confirms it,
        // so a write failure can never silently turn a decline into an acceptance or vice versa.
        var declined = _state.Decline(requester, nonce, CurrentTime);
        if (declined.Changed && blockGuide)
            BeginPersistentBlock(requester, offer.Guide);

        return declined;
    }

    private async void LoadPersistentBlocks()
    {
        // Defensive, mirroring the existing _players != null convention in this file: test harnesses
        // that construct WingmateSystem directly (bypassing EntitySystemManager) never run Initialize(),
        // so _ledger/Log are never wired up. Without this guard, the async-void failure path below would
        // itself NRE on a null Log and crash the process instead of merely no-op'ing.
        if (_ledger == null)
            return;

        try
        {
            _loadedBlocks.Enqueue(await _ledger.GetWingmateBlocksAsync());
        }
        catch (Exception e)
        {
            Log.Error($"Error while loading persistent Wingmates blocks; offers remain fail-closed:\n{e}");
        }
    }

    private static bool TryMapTeachingMode(
        Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode mode,
        out Content.Server._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode mapped)
    {
        switch (mode)
        {
            case Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour:
                mapped = Content.Server._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour;
                return true;
            case Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.LearnByDoing:
                mapped = Content.Server._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.LearnByDoing;
                return true;
            case Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.ShadowMe:
                mapped = Content.Server._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.ShadowMe;
                return true;
            default:
                mapped = default;
                return false;
        }
    }

    private TimeSpan CurrentTime => _timing?.CurTime ?? TimeSpan.Zero;

    private WingmateTransitionResult ExpireApplyAndPublish(Func<WingmateTransitionResult> apply, NetUserId actor)
    {
        var affected = _state.Expire(CurrentTime).ToHashSet();
        var result = apply();
        if (result.AffectedUsers.Count == 0)
            affected.Add(actor);
        else
            affected.UnionWith(result.AffectedUsers);

        // P1.4 SILENT-DROP BATCH FIX: every BUI action whose transition result is non-success now
        // leaves a one-shot failure trace on the actor's private snapshot, so the client can render
        // a popup (see WingmateBoundUserInterface.ApplyPrivateSnapshot) instead of swallowing the
        // click. A success clears any prior failure so a stale reason never bleeds onto an unrelated
        // later snapshot. The seq is bumped monotonically (it never wraps in practice — UI lifetime
        // is bounded by session), so the client can use a strict > comparison to dedupe replays.
        // The Reason is an opaque .ftl key by convention; if a caller returns an empty/null reason
        // on a non-success result we still stamp the seq (so the seq cannot silently stall) and fall
        // back to a generic failure key so the client never gets an untranslatable code.
        if (!result.Changed)
        {
            var key = string.IsNullOrEmpty(result.Reason) ? "wingmates-action-failed" : result.Reason;
            _lastTransition[actor] = (key, _nextTransitionSeq++);
        }
        else
        {
            _lastTransition.Remove(actor);
        }

        PublishAfterExpiry(affected);
        return result;
    }

    /// <summary>
    /// The only system-level path to private publication. The adapter performs one expiry pass before
    /// constructing viewer state and merges affected users without recursing.
    /// </summary>
    private void PublishAfterExpiry(IEnumerable<NetUserId> affected)
    {
        // Rules tests instantiate the system without IoC/Initialize; live ECS execution always owns
        // the targeted adapter. Keep transition seams usable without inventing a transport fixture.
        if (_snapshots == null)
            return;
        _snapshots.Publish(affected);
    }

    private void PublishModeratorResult(WingmateTransitionResult result)
    {
        // Unit rules tests instantiate the system without IoC/Initialize. Live command execution
        // always has the adapter; keeping the seam null-safe prevents test-only transport setup.
        if (_snapshots == null || result.AffectedUsers.Count == 0)
            return;
        _snapshots.Publish(result.AffectedUsers);
    }

    private WingmateUiState BuildPrivateState(NetUserId viewer)
    {
        var status = _state.GetStatus(viewer);
        var incoming = _state.GetIncomingOffer(viewer);
        var ownRequest = _state.GetRequestDetails(viewer);
        var seeker = IsGuideEligible(viewer) && _volunteeringGuides.Contains(viewer)
            ? _state.GetSeekingRequests(viewer)
                .Where(request => !IsBlockedOrPending(viewer, request.Requester))
                .Select(request => (WingmateRequestSnapshot?) request).FirstOrDefault()
            : null;
        var mode = !_enabled ? WingmateUiMode.Disabled : status switch
        {
            WingmateStatus.Seeking => WingmateUiMode.Seeking,
            WingmateStatus.OfferPending when incoming != null => WingmateUiMode.OfferReceived,
            WingmateStatus.Paired => WingmateUiMode.Paired,
            WingmateStatus.Paused => WingmateUiMode.Paused,
            _ when seeker != null => WingmateUiMode.OfferAvailable,
            _ => WingmateUiMode.Idle,
        };
        var requesterName = seeker is { } visibleSeeker ? GetDisplayName(visibleSeeker.Requester) : null;
        var partnerName = incoming is { } proposal
            ? GetDisplayName(proposal.Guide)
            : _state.GetPartner(viewer) is { } partner ? GetDisplayName(partner) : null;
        var canVolunteer = _enabled && IsGuideEligible(viewer);

        // ALIVENESS P0 #3: only the Seeking view pays for the peer scan — every other mode keeps
        // the default (true), which renders the normal copy. Sent as a bare presence bit; the
        // snapshot never says who, how many, or where (privacy-reviewed view, see the state's
        // own doc comment).
        var peerAvailable = mode != WingmateUiMode.Seeking || HasEligiblePeer(viewer);

        // P1.4 SILENT-DROP BATCH FIX: forward the actor's last-transition failure (if any) so the
        // client can pop it. Null/0 when no failure is queued (a successful transition cleared the
        // entry in ExpireApplyAndPublish, or the actor never triggered a rejection yet).
        var transition = _lastTransition.GetValueOrDefault(viewer);

        return new WingmateUiState(mode,
            ownRequest?.Department ?? seeker?.Department ?? string.Empty,
            ToSharedMode(ownRequest?.TeachingMode ?? seeker?.TeachingMode ?? WingmateTeachingMode.Tour),
            seeker?.RequesterToken, requesterName, partnerName, incoming?.Nonce, _enabled,
            canVolunteer,
            _enabled ? $"wingmates-status-{status.ToString().ToLowerInvariant()}" : "wingmates-status-disabled",
            _volunteeringGuides.Contains(viewer),
            ComputeVolunteerIneligibleReason(viewer, canVolunteer),
            peerAvailable,
            transition.Status,
            transition.Seq);
    }

    /// <summary>
    ///     ALIVENESS P0 #3: true when at least one OTHER connected player currently has an
    ///     attached, alive, non-ghost entity — i.e. someone who could plausibly become this
    ///     seeker's wingmate this shift (the same basic-criteria floor guides are held to; the
    ///     tenure/approval gates are deliberately NOT applied here, because a basic-eligible
    ///     player can still be moderator-approved mid-round). Fails CLOSED (no peers) when the
    ///     player manager is absent — rules-test harnesses construct this system without IoC,
    ///     same null-guard idiom as <see cref="GetDisplayName"/>.
    /// </summary>
    private bool HasEligiblePeer(NetUserId viewer)
    {
        if (_players == null)
            return false;

        foreach (var session in _players.Sessions)
        {
            var isSelf = session.UserId == viewer;
            var hasAttached = session.AttachedEntity is { } entity && Exists(entity);
            var isGhost = hasAttached && HasComp<GhostComponent>(session.AttachedEntity!.Value);
            var isDead = hasAttached
                         && TryComp<MobStateComponent>(session.AttachedEntity!.Value, out var mobState)
                         && _mobState.IsDead(session.AttachedEntity!.Value, mobState);

            if (IsEligiblePeer(isSelf, hasAttached, isGhost, isDead))
                return true;
        }

        return false;
    }

    /// <summary>Pure decision seam for <see cref="HasEligiblePeer"/> — unit-testable without
    /// IoC/EntityManager, mirroring <see cref="IsBasicGuideCriteriaMet"/>.</summary>
    internal static bool IsEligiblePeer(bool isSelf, bool hasAttachedEntity, bool isGhost, bool isDead) =>
        !isSelf && hasAttachedEntity && IsBasicGuideCriteriaMet(isGhost, isDead);

    private string? GetDisplayName(NetUserId user)
    {
        if (_players == null || !_players.TryGetSessionById(user, out var session))
            return null;

        var hasAttachedEntity = session.AttachedEntity is { } entity && Exists(entity);
        var entityName = hasAttachedEntity ? MetaData(session.AttachedEntity!.Value).EntityName : null;

        // Seam kept separate from the IoC-resolved session/entity lookups above so the "IC name, not
        // account name" decision is unit-testable without a running EntityManager.
        return ResolveDisplayNameForTests(session.Name, entityName, hasAttachedEntity);
    }

    /// <summary>
    /// Pure decision seam for <see cref="GetDisplayName"/>: display names must come from the attached
    /// IC entity's <c>MetaData.EntityName</c>, never from the account-identifying <c>session.Name</c>.
    /// Exposed so the "which field wins" contract is unit-testable without a live EntityManager.
    /// </summary>
    internal static string? ResolveDisplayNameForTests(string? sessionAccountName, string? attachedEntityName,
        bool hasAttachedEntity) => hasAttachedEntity ? attachedEntityName : null;

    private static Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode ToSharedMode(
        WingmateTeachingMode mode) => mode switch
    {
        WingmateTeachingMode.Tour => Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour,
        WingmateTeachingMode.LearnByDoing => Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.LearnByDoing,
        WingmateTeachingMode.ShadowMe => Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.ShadowMe,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    private void RefreshPublicShells()
    {
        var query = EntityQueryEnumerator<WingmateBeaconComponent>();
        while (query.MoveNext(out var uid, out _))
            _ui.SetUiState(uid, WingmateUiKey.Beacon, new WingmatePublicShellState(_enabled));
    }

    private void RememberBeacon(NetUserId actor, EntityUid beacon) => _snapshots.RememberOpen(actor, beacon);

    private bool TryResolveSession(NetUserId user, out ICommonSession session) =>
        _players.TryGetSessionById(user, out session!);

    private bool IsBeaconAvailable(EntityUid beacon) =>
        Exists(beacon) &&
        TryComp<WingmateBeaconComponent>(beacon, out _) &&
        MetaData(beacon).EntityLifeStage < EntityLifeStage.Terminating;

    internal static bool IsAllowedDepartmentForTests(string department) => AllowedDepartments.Contains(department);
    internal static WingmateTeachingMode MapTeachingModeForTests(
        Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode mode)
    {
        if (!TryMapTeachingMode(mode, out var mapped)) throw new ArgumentOutOfRangeException(nameof(mode));
        return mapped;
    }
    internal WingmateStatus GetStatusForTests(NetUserId user) => _state.GetStatus(user);
    internal NetUserId? GetPartnerForTests(NetUserId user) => _state.GetPartner(user);
    internal WingmateOffer? GetIncomingOfferForTests(NetUserId user) => _state.GetIncomingOffer(user);
    internal void SeedApprovedGuideForTests(NetUserId user) => _approvedGuides.Add(user);
    /// <summary>Bypasses SetVolunteering's eligibility gate entirely — for exercising the "already
    /// volunteering, eligibility since revoked/lost, opt-out must stay reachable" edge case
    /// directly, since the eligibility gate itself makes this state otherwise hard to reach via the
    /// normal test entry points in a rules harness (see ComputeVolunteerIneligibleReasonForTests).</summary>
    internal void SeedVolunteeringForTests(NetUserId user) => _volunteeringGuides.Add(user);
    internal void ClearForTests()
    {
        _state.Clear(); _approvedGuides.Clear(); _volunteeringGuides.Clear();
        _requestLimiter.Clear(); _offerLimiter.Clear();
        _careerToursCache.Clear(); _careerToursLoading.Clear();
        _deniedGuides.Clear();
        _lastTransition.Clear();
    }
    internal bool IsDeniedGuideForTests(NetUserId user) => _deniedGuides.Contains(user);
    internal void SetEnabledForTests(bool enabled)
    {
        _enabled = enabled;
        _persistentBlocksReady = true;
        if (!enabled)
        {
            _state.Clear(); _volunteeringGuides.Clear();
            _requestLimiter.Clear(); _offerLimiter.Clear();
            // Mirrors OnEnabledChanged(false): an operator disable is the ONE path allowed to drop
            // undelivered failure notices (round rollover is not), and its service-epoch bump extends
            // that suppression to writes still in flight.
            _pendingBlockFailures.Clear();
            _serviceEpoch++;
        }
    }
    /// <summary>Returns this account's currently-queued transition failure (loc key + seq), so tests
    /// can assert the silent-drop fix populates the per-actor trace on a rejected BUI action and
    /// clears it on the next success. Null when nothing is queued.</summary>
    internal (string Status, uint Seq)? GetLastTransitionForTests(NetUserId user) =>
        _lastTransition.TryGetValue(user, out var entry) ? entry : null;
    internal WingmateTransitionResult RequestForTests(NetUserId actor, string department,
        Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode mode)
    {
        if (!_enabled) return new(false, "disabled");
        if (!_requestLimiter.TryConsume(actor, CurrentTime)) return new(false, "rate-limited");
        if (!AllowedDepartments.Contains(department)) return new(false, "invalid-department");
        return !TryMapTeachingMode(mode, out var mapped)
            ? new(false, "invalid-teaching-mode")
            : _state.Request(actor, department, mapped, TimeSpan.Zero);
    }
    internal WingmateTransitionResult OfferForTests(NetUserId actor, NetUserId requester) =>
        ExpireApplyAndPublish(() =>
        {
            if (!_enabled) return new(false, "disabled");
            if (!_offerLimiter.TryConsume(actor, CurrentTime)) return new(false, "rate-limited");
            if (!_persistentBlocksReady) return new(false, "blocks-loading");
            if (!IsGuideEligible(actor)) return new(false, "not-approved");
            if (!_volunteeringGuides.Contains(actor)) return new(false, "not-volunteering");
            return OfferResolved(actor, requester);
        }, actor);
    /// <summary>FIX 1 test seam: drives auto-guide-eligibility posture without wiring IoC/_cfg —
    /// mirrors <see cref="SetPersistentBlockWriterForTests"/>'s override idiom.</summary>
    internal void SetAutoGuideEligibilityForTests(bool enabled) => _autoGuideEligibilityOverrideForTests = enabled;
    /// <summary>FIX 1 test seam: swaps the real ledger-backed career-tours fetch for a test double,
    /// so the tenure gate's async load-then-cache path can be exercised deterministically.</summary>
    internal void SetCareerToursFetcherForTests(Func<NetUserId, Task<int>> fetch) =>
        _fetchCareerToursOverrideForTests = fetch;
    /// <summary>Seeds a cached tenure value directly, bypassing the async fetch entirely.</summary>
    internal void SeedCareerToursForTests(NetUserId user, int tours) => _careerToursCache[user] = tours;
    internal bool IsGuideEligibleForTests(NetUserId user) => IsGuideEligible(user);
    internal void DrainLoadedCareerToursForTests() => DrainLoadedCareerTours();
    internal string? ComputeVolunteerIneligibleReasonForTests(NetUserId user) =>
        ComputeVolunteerIneligibleReason(user, _enabled && IsGuideEligible(user));
    /// <summary>Returns the opaque token that would be minted for <paramref name="viewer"/> to address
    /// <paramref name="requester"/>'s request, or <c>null</c> if that requester is not currently visible
    /// to that viewer as a seeker (mirrors <see cref="WingmateRoundState.GetSeekingRequests"/> directly,
    /// bypassing the guide-eligibility gate in <see cref="BuildPrivateState"/> so token shape/binding can
    /// be tested independently of authorization).</summary>
    internal Guid? GetRequesterTokenForTests(NetUserId viewer, NetUserId requester) =>
        _state.GetSeekingRequests(viewer)
            .Where(request => request.Requester == requester)
            .Select(request => (Guid?) request.RequesterToken)
            .FirstOrDefault();
    internal void SeedPersistentBlockForTests(NetUserId blocker, NetUserId blocked) =>
        _persistentBlocks.Add((blocker.UserId, blocked.UserId));
    internal void SetPersistentBlocksReadyForTests(bool ready) => _persistentBlocksReady = ready;
    /// <summary>Exercises the real token-addressed Offer path (rate limit, resolution, blocks) that
    /// production code reaches via <see cref="OnOffer"/>, including with forged/unknown tokens.</summary>
    internal WingmateTransitionResult OfferByTokenForTests(NetUserId actor, Guid requesterToken) =>
        ExpireApplyAndPublish(() => Offer(actor, requesterToken), actor);
    internal WingmateTransitionResult AcceptForTests(NetUserId actor, Guid nonce) =>
        ExpireApplyAndPublish(
            () => !_enabled ? new(false, "disabled") : _state.Accept(actor, nonce, CurrentTime), actor);
    internal WingmateTransitionResult DeclineForTests(NetUserId actor, Guid nonce, bool blockGuide = false) =>
        ExpireApplyAndPublish(
            () => !_enabled ? new(false, "disabled") : Decline(actor, nonce, blockGuide), actor);
    internal void SessionUnavailableForTests(NetUserId user) => HandleUnavailableSession(user);
    internal void RoundRestartForTests() => ClearRound();
    internal WingmateTransitionResult SetVolunteeringForTests(NetUserId user, bool volunteering, bool charterAccepted) =>
        SetVolunteering(user, volunteering, charterAccepted);
    internal bool IsVolunteeringForTests(NetUserId user) => _volunteeringGuides.Contains(user);
    internal WingmateTransitionResult BlockCurrentPartnerForTests(NetUserId user) => BlockCurrentPartner(user);
    /// <summary>Starts a durable block between two arbitrary accounts through the real
    /// <see cref="BeginPersistentBlock"/> path (write kickoff, pending gate, drain bookkeeping) without
    /// needing the pair to be round-locally paired first — for tests that accumulate many write
    /// outcomes against different partners.</summary>
    internal WingmateTransitionResult BeginPersistentBlockForTests(NetUserId blocker, NetUserId blocked) =>
        BeginPersistentBlock(blocker, blocked);
    internal WingmateTransitionResult BlockUsersForTests(NetUserId user, NetUserId other) => _state.BlockForRound(user, other);
    internal WingmateTransitionResult PauseForTests(NetUserId user) => _state.Pause(user);
    internal WingmateTransitionResult ResumeForTests(NetUserId user) => _state.Resume(user);
    internal WingmateUiState BuildPrivateStateForTests(NetUserId user) => BuildPrivateState(user);
    internal bool HasOpenSnapshotMappingForTests(NetUserId user) => _snapshots.HasOpenMapping(user);
    internal bool IsPersistentlyBlockedForTests(NetUserId first, NetUserId second) =>
        IsPersistentlyBlocked(first, second);
    /// <summary>Exposes the pending-inclusive gate used by offers/seeker-visibility, distinct from
    /// <see cref="IsPersistentlyBlockedForTests"/> (confirmed-only) so tests can assert the in-flight
    /// window is closed without disturbing the confirmed-only fast path used elsewhere.</summary>
    internal bool IsBlockedOrPendingForTests(NetUserId first, NetUserId second) =>
        IsBlockedOrPending(first, second);
    internal bool HasPendingBlockFailureNoticeForTests(NetUserId user) => _pendingBlockFailures.ContainsKey(user);
    /// <summary>The total number of undelivered block-persistence failures tallied for this account, so
    /// tests can assert two failures against two different partners are counted distinctly rather than
    /// collapsing into a single generic notice.</summary>
    internal int GetPendingBlockFailureCountForTests(NetUserId user)
    {
        var tally = NormalizePendingBlockFailureTally(user);
        return tally.CurrentCount + tally.StaleCount;
    }
    /// <summary>This account's undelivered failure tally split into (this-shift, previous-shift) counts
    /// relative to the CURRENT round epoch — the exact two numbers delivery would render — so tests can
    /// assert both the coalesced bound and the rollover re-classification.</summary>
    internal (int CurrentShift, int PreviousShift) GetPendingBlockFailureTallyForTests(NetUserId user)
    {
        var tally = NormalizePendingBlockFailureTally(user);
        return (tally.CurrentCount, tally.StaleCount);
    }
    /// <summary>The rendered one-popup notice body for this account's queued failure records (or null if
    /// none), so integration tests — where Loc is live — can assert singular/plural/previous-shift
    /// wording and the absence of any blocked-party identity in the exact text a player would see.</summary>
    internal string? BuildPendingBlockFailureNoticeTextForTests(NetUserId user) =>
        BuildPendingBlockFailureNoticeText(user);
    /// <summary>The round epoch <see cref="BeginPersistentBlock"/> would currently stamp a new write
    /// with, so tests can assert a write's outcome is dropped once a round boundary has moved past it.</summary>
    internal int GetRoundEpochForTests() => _state.RoundEpoch;
    /// <summary>Swaps the real ledger-backed writer for a test double so success/failure/timing of the
    /// durable block write can be controlled deterministically without touching SQLite.
    /// 🔴 The override is PROCESS-LIFETIME state on a pooled server: a test that stubs the writer and
    /// does not restore it silently rewires every LATER test's "durable" write to the stub. That is
    /// exactly how PersistentBlockSurvivesRoundRestartAndPreventsOffer failed for a full day on
    /// 2026-08-02 — an earlier fixture sibling installed an always-fail stub, so the "real SQLite
    /// write" the later test believed it was awaiting always returned false, the drain never
    /// dissolved the pair, and the assertion failed with (thanks to a VSTest quirk) no visible
    /// message. Pair stubbing with <see cref="RestorePersistentBlockWriterForTests"/>.</summary>
    internal void SetPersistentBlockWriterForTests(Func<Guid, Guid, Task<bool>> writer) =>
        _writePersistentBlock = writer;

    /// <summary>Re-wires the REAL ledger-backed writer after a test stubbed it. Safe on any
    /// initialized system (<c>_ledger</c> is created in <c>Initialize()</c>, which every pooled
    /// integration server has run).</summary>
    internal void RestorePersistentBlockWriterForTests() =>
        _writePersistentBlock = _ledger.AddWingmateBlockAsync;
    /// <summary>Applies every completed durable-block write queued so far. Production calls this every
    /// <see cref="Update"/> tick; tests call it directly for deterministic control over when a pending
    /// block "lands".</summary>
    internal void DrainCompletedBlockWritesForTests(Action<NetUserId>? onFailure = null) =>
        DrainCompletedBlockWrites(onFailure);
    /// <summary>The real <see cref="Task"/> backing a still-in-flight durable block write, so
    /// integration tests against the real SQLite-backed ledger can <c>await</c> genuine completion
    /// instead of polling or sleeping.</summary>
    internal Task? GetPendingBlockWriteTaskForTests(NetUserId blocker, NetUserId blocked) =>
        _pendingBlockWriteTasksForTests.TryGetValue((blocker.UserId, blocked.UserId), out var task) ? task : null;

    /// <summary>
    /// Integration-only final-send boundary. State construction, expiry, beacon validation, network
    /// identity and generation are the same production functions used by the live adapter; only the
    /// opaque destination resolver and send capture are supplied by the one-client test harness.
    /// </summary>
    internal WingmateSnapshotAdapter<TSession> CreatePrivateSnapshotAdapterForTests<TSession>(
        TryResolveWingmateSession<TSession> resolveSession,
        Action<WingmatePrivateSnapshotEvent, TSession> send) =>
        new(resolveSession, IsBeaconAvailable, beacon => GetNetEntity(beacon), BuildPrivateState, send,
            () => _state.Expire(CurrentTime));
}

internal readonly record struct WingmateModeratorSnapshot(
    WingmateStatus Status,
    bool Approved,
    bool Volunteering);

/// <summary>Outcome of one durable-block SQLite write, captured for the next drain to apply. Epoch is
/// the <see cref="WingmateRoundState.RoundEpoch"/> that was active when the write started; a FAILURE
/// result that has straddled a round boundary keeps its notice but is delivered with previous-shift
/// wording instead of being presented as a current-round event. ServiceEpoch is the operator-disable
/// generation at write start; a FAILURE result with a stale ServiceEpoch straddled a feature disable
/// and its notice is discarded outright. A successful result is applied regardless of either epoch —
/// the durable block it confirms is deliberately round- and service-independent.</summary>
internal readonly record struct PersistentBlockWriteResult(
    NetUserId Blocker,
    NetUserId Blocked,
    bool Success,
    int Epoch,
    int ServiceEpoch);

/// <summary>Bounded per-user tally of undelivered durable-block failure notices: the count of failures
/// stamped with <see cref="Epoch"/> (rendered with this-shift wording while that epoch is current) plus
/// the count of failures from any earlier epoch (rendered with previous-shift wording). Exactly two
/// counters however many failures accumulate — the resource ceiling that replaces per-failure records.</summary>
internal readonly record struct WingmateBlockFailureTally(
    int Epoch,
    int CurrentCount,
    int StaleCount);

internal delegate bool TryResolveWingmateSession<TSession>(NetUserId user, out TSession session);

/// <summary>
/// Testable targeted-transport boundary. It validates the open mapping, live beacon component, and
/// authenticated destination before converting the entity or constructing private state.
/// </summary>
internal sealed class WingmateSnapshotAdapter<TSession>
{
    private readonly TryResolveWingmateSession<TSession> _resolveSession;
    private readonly Func<EntityUid, bool> _beaconAvailable;
    private readonly Func<EntityUid, NetEntity> _getNetEntity;
    private readonly Func<NetUserId, WingmateUiState> _buildState;
    private readonly Action<WingmatePrivateSnapshotEvent, TSession> _send;
    private readonly Func<IEnumerable<NetUserId>> _expire;
    private readonly Dictionary<NetUserId, EntityUid> _openBeaconByUser = new();
    private readonly Dictionary<NetUserId, ulong> _generations = new();

    public IEnumerable<NetUserId> OpenUsers => _openBeaconByUser.Keys;

    public WingmateSnapshotAdapter(TryResolveWingmateSession<TSession> resolveSession,
        Func<EntityUid, bool> beaconAvailable,
        Func<EntityUid, NetEntity> getNetEntity,
        Func<NetUserId, WingmateUiState> buildState,
        Action<WingmatePrivateSnapshotEvent, TSession> send,
        Func<IEnumerable<NetUserId>>? expire = null)
    {
        _resolveSession = resolveSession;
        _beaconAvailable = beaconAvailable;
        _getNetEntity = getNetEntity;
        _buildState = buildState;
        _send = send;
        _expire = expire ?? (() => Array.Empty<NetUserId>());
    }

    public void RememberOpen(NetUserId user, EntityUid beacon) => _openBeaconByUser[user] = beacon;

    public void Close(NetUserId user, EntityUid beacon)
    {
        if (_openBeaconByUser.GetValueOrDefault(user) == beacon)
            CloseUser(user);
    }

    public void CloseUser(NetUserId user, bool clearGeneration = false)
    {
        _openBeaconByUser.Remove(user);
        if (clearGeneration)
            _generations.Remove(user);
    }

    public void RemoveBeacon(EntityUid beacon)
    {
        foreach (var user in _openBeaconByUser
                     .Where(entry => entry.Value == beacon)
                     .Select(entry => entry.Key)
                     .ToArray())
            CloseUser(user);
    }

    public void Clear()
    {
        _openBeaconByUser.Clear();
        _generations.Clear();
    }

    public bool HasOpenMapping(NetUserId user) => _openBeaconByUser.ContainsKey(user);

    public void Publish(IEnumerable<NetUserId> users)
    {
        var publish = users.ToHashSet();
        publish.UnionWith(_expire());
        foreach (var user in publish)
        {
            if (!_openBeaconByUser.TryGetValue(user, out var beacon))
                continue;
            if (!_beaconAvailable(beacon))
            {
                CloseUser(user);
                continue;
            }
            if (!_resolveSession(user, out var session))
                continue;

            var generation = _generations.GetValueOrDefault(user) + 1;
            var snapshot = new WingmatePrivateSnapshotEvent(_getNetEntity(beacon), generation,
                _buildState(user));
            _send(snapshot, session);
            _generations[user] = generation;
        }
    }
}
