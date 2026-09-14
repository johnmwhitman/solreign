using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using System.Linq;
using Content.Server._Solreign.PlayerDelight.Mark;
using Content.Server.Roles.Jobs;
using Content.Server.Station.Systems;
using Content.Shared._Solreign.PlayerDelight.FirstShift;
using Content.Shared._Solreign.PlayerDelight.Mark;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Pinpointer;
using Content.Shared.Popups;
using Content.Shared.Roles;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.PlayerDelight.FirstShift;

/// <summary>Server-authoritative, round-local First Shift adapter on the existing arrival beacon.</summary>
public sealed partial class FirstShiftSystem : EntitySystem
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private SharedMindSystem _minds = default!;
    [Dependency] private JobSystem _jobs = default!;
    [Dependency] private StationSystem _stations = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IChatManager _chatManager = default!;

    private readonly FirstShiftRoundState _state = new();
    private IReadOnlyList<FirstShiftCard> _catalog = Array.Empty<FirstShiftCard>();
    private Dictionary<string, FirstShiftAssignmentPrototype> _cards = new(StringComparer.Ordinal);
    private FirstShiftSnapshotAdapter<ICommonSession> _snapshots = default!;
    private bool _enabled;
    private bool _catalogValid;

    /// <summary>
    ///     MG-W3 addition: cached mirror of <c>CCVars.SolreignMarkEnabled</c> — the kishōtenketsu
    ///     restructure's own master gate (docs/specs/MARK-SPEC-2026-07-17-DRAFT.md §3.6). Drives
    ///     the appended Mark stage's reachability, the Task 4/5 panel blocks, and the garden anchor
    ///     branch. Independent of <see cref="_enabled"/> (the base First Shift feature's own CVar).
    /// </summary>
    private bool _markEnabled;

    private static readonly string[] Task4RowKeys =
    {
        "first-shift-task4-row-1",
        "first-shift-task4-row-2",
        "first-shift-task4-row-3",
    };

    private static readonly string[] Task4DeflectionKeys =
    {
        "first-shift-task4-deflection-1",
        "first-shift-task4-deflection-2",
        "first-shift-task4-deflection-3",
    };

    internal FirstShiftCounters Counters => _state.Counters;
    internal bool Enabled => _enabled;
    private bool Available => _enabled && _catalogValid;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FirstShiftBeaconComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<FirstShiftBeaconComponent, BoundUIClosedEvent>(OnClosed);
        SubscribeLocalEvent<FirstShiftBeaconComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<FirstShiftBeaconComponent, EntityTerminatingEvent>(OnTerminating);
        SubscribeLocalEvent<FirstShiftBeaconComponent, FirstShiftStartMessage>(OnStart);
        SubscribeLocalEvent<FirstShiftBeaconComponent, FirstShiftAdvanceMessage>(OnAdvance);
        SubscribeLocalEvent<FirstShiftBeaconComponent, FirstShiftRerollMessage>(OnReroll);
        SubscribeLocalEvent<FirstShiftBeaconComponent, FirstShiftCompleteMessage>(OnComplete);
        SubscribeLocalEvent<FirstShiftBeaconComponent, FirstShiftEndMessage>(OnEnd);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => ClearRound());

        BuildCatalog();
        _snapshots = new(TryResolveSession, IsBeaconAvailable, uid => GetNetEntity(uid), BuildState,
            (snapshot, session) => RaiseNetworkEvent(snapshot, session));
        _players.PlayerStatusChanged += OnPlayerStatusChanged;
        _cfg.OnValueChanged(CCVars.SolreignFirstShiftAssignmentsEnabled, OnEnabledChanged, true);
        Subs.CVar(_cfg, CCVars.SolreignMarkEnabled, v => _markEnabled = v, invokeImmediately: true);
    }

    public override void Shutdown()
    {
        _cfg.UnsubValueChanged(CCVars.SolreignFirstShiftAssignmentsEnabled, OnEnabledChanged);
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
        _snapshots?.Clear();
        base.Shutdown();
    }

    private void BuildCatalog()
    {
        var prototypes = _prototypes.EnumeratePrototypes<FirstShiftAssignmentPrototype>().ToArray();
        var result = FirstShiftCatalogBuilder.Build(prototypes);
        _catalogValid = result.Errors.Count == 0;
        _catalog = _catalogValid ? result.Cards : Array.Empty<FirstShiftCard>();
        _cards = _catalogValid
            ? prototypes.ToDictionary(card => card.ID, StringComparer.Ordinal)
            : new(StringComparer.Ordinal);
    }

    private void OnEnabledChanged(bool enabled)
    {
        var viewers = _snapshots?.OpenUsers.ToArray() ?? Array.Empty<NetUserId>();
        _enabled = enabled;
        if (!enabled)
        {
            _state.ClearActive();
            _snapshots?.Publish(viewers);
            return;
        }
        _snapshots?.Publish(viewers);
    }

    private void OnOpened(EntityUid uid, FirstShiftBeaconComponent _, BoundUIOpenedEvent args)
    { if (TryActor(args.Actor, out var actor)) { _snapshots.RememberOpen(actor, uid); _snapshots.Publish([actor]); } }
    private void OnClosed(EntityUid uid, FirstShiftBeaconComponent _, BoundUIClosedEvent args)
    { if (TryActor(args.Actor, out var actor)) _snapshots.Close(actor, uid); }
    private void OnShutdown(EntityUid uid, FirstShiftBeaconComponent _, ComponentShutdown args) => _snapshots.RemoveBeacon(uid);
    private void OnTerminating(EntityUid uid, FirstShiftBeaconComponent _, ref EntityTerminatingEvent args) => _snapshots.RemoveBeacon(uid);

    private void OnStart(EntityUid uid, FirstShiftBeaconComponent _, FirstShiftStartMessage args)
    {
        // v15.0.1 hotfix (wingmate Start assignment no-op): the original authorization routed the
        // snapshot-generation check (IsStartAuthorized's third arg) into the same silent drop as
        // the other conditions. That guard is correct for Advance/Reroll/Complete/End (those
        // mutate an existing assignment and a stale click would re-fire a finished stage), but
        // for Start it is a no-op trap: the UI already gates the button on state.Active == false,
        // the !active check below covers the "user already has an assignment" case, and the server
        // is the authority on state — if the client's snapshot drifted, a stale generation must
        // not silently eat a legitimate first-assignment press. The other three conditions are
        // sufficient to keep invalid state transitions out of the reducer.
        if (!TryActor(args.Actor, out var actor)) return;
        if (!Available)
        {
            Log.Debug($"FirstShiftSystem.OnStart: rejecting — feature unavailable. Actor={actor}, beacon={uid}.");
            return;
        }
        if (_state.GetAssignment(actor) is not null)
        {
            Log.Debug($"FirstShiftSystem.OnStart: rejecting — actor already has an assignment. Actor={actor}, beacon={uid}.");
            return;
        }
        if (args.Department is not (FirstShiftDepartment.Engineering or FirstShiftDepartment.Medical
                or FirstShiftDepartment.Science or FirstShiftDepartment.Cargo
                or FirstShiftDepartment.Service))
        {
            Log.Debug($"FirstShiftSystem.OnStart: rejecting — invalid department {args.Department}. Actor={actor}, beacon={uid}.");
            return;
        }

        _state.Request(actor, args.Department, _catalog, (ulong) Math.Max(0, _ticker.RoundId), _timing.CurTime);
        _snapshots.Publish([actor]);
    }

    private void OnAdvance(EntityUid uid, FirstShiftBeaconComponent _, FirstShiftAdvanceMessage args)
    { ApplyGenerationIntent(uid, args.Actor, args.SnapshotGeneration, args.AssignmentGeneration, actor => _state.Advance(actor)); }
    private void OnReroll(EntityUid uid, FirstShiftBeaconComponent _, FirstShiftRerollMessage args)
    {
        // P3.2 OTHER SILENT DROPS (audit fix 1 of 4) — the only one of the four generation-gated
        // paths that surfaces feedback on a stale-snapshot drop. Reroll is the only "unlucky
        // department" recourse a player has, and a click that races a snapshot Publish is
        // indistinguishable from a broken button. The other three (Advance/Complete/End) keep
        // their silent drop per the audit's reasoning — the UI re-renders the same stage so the
        // player has SOME signal, and a popup on every press would be spam.
        //
        // Inline the generation check here (instead of going through the shared
        // ApplyGenerationIntent helper) so we can distinguish "stale snapshot" from "no actor /
        // !Available" — only the former gets a toast.
        if (!TryActor(args.Actor, out var actor) || !Available ||
            !_snapshots.IsCurrent(actor, uid, args.SnapshotGeneration) ||
            !_state.IsCurrentGeneration(actor, args.AssignmentGeneration))
        {
            // Only fire the popup when the guard actually ran (actor is present + feature
            // available) but the click lost the generation race — the OTHER "no actor /
            // !Available" cases are upstream gates the player can't reach in normal play, and
            // adding a popup there would just be noise on a misbehaving client.
            if (TryActor(args.Actor, out actor) && Available &&
                (!_snapshots.IsCurrent(actor, uid, args.SnapshotGeneration) ||
                 !_state.IsCurrentGeneration(actor, args.AssignmentGeneration)))
            {
                _popup.PopupEntity(Loc.GetString("first-shift-reroll-stale-snapshot-popup"), args.Actor, args.Actor, PopupType.MediumCaution);
            }
            return;
        }

        _state.Reroll(actor, _catalog, (ulong) Math.Max(0, _ticker.RoundId), _timing.CurTime);
        _snapshots.Publish([actor]);
    }
    private void OnComplete(EntityUid uid, FirstShiftBeaconComponent _, FirstShiftCompleteMessage args)
    {
        var result = ApplyGenerationIntent(uid, args.Actor, args.SnapshotGeneration, args.AssignmentGeneration,
            actor =>
            {
                // Dormant-Mark ship posture (spec §3.6): the client presents Debrief as terminal
                // while the feature is off (FirstShiftPresentation.For collapses "next-mark" to
                // "complete" there), so a Complete arriving at Debrief with the CVar off is really
                // this player's honest "I'm done" — walk the pure reducer through the unseen Mark
                // stage in this same call so Complete()'s unconditional Stage==Mark law is
                // satisfied without the intermediate stage ever reaching a snapshot Publish.
                if (!_markEnabled && _state.GetAssignment(actor) is { Stage: FirstShiftAssignmentStage.Debrief })
                    _state.Advance(actor);
                return _state.Complete(actor);
            });

        // ALIVENESS P0 #4: the one onboarding loop a newcomer is funneled into must not end in
        // silence. A genuinely-applied Complete (debrief stage, current generations — everything
        // the reducer already enforces) gets a visible acknowledgment: a toast at the player,
        // plus a private chat handoff pointing at the Contracts Board (chat persists after the
        // popup fades — the LowPopLobbyReminderSystem delivery idiom). Card/safety text is
        // untouched: this fires strictly AFTER the observation-only assignment is over.
        if (result is not { Changed: true, Reason: "completed" })
            return;

        _popup.PopupEntity(Loc.GetString("first-shift-complete-popup"), args.Actor, args.Actor, PopupType.Medium);

        if (_players.TryGetSessionByEntity(args.Actor, out var session))
            _chatManager.DispatchServerMessage(session, Loc.GetString("first-shift-complete-handoff"));
    }
    private void OnEnd(EntityUid uid, FirstShiftBeaconComponent _, FirstShiftEndMessage args)
    { ApplyGenerationIntent(uid, args.Actor, args.SnapshotGeneration, args.AssignmentGeneration, actor => _state.Clear(actor)); }

    private FirstShiftTransitionResult? ApplyGenerationIntent(EntityUid beacon, EntityUid actorEntity,
        ulong snapshotGeneration,
        uint assignmentGeneration,
        Func<NetUserId, FirstShiftTransitionResult> apply)
    {
        if (!TryActor(actorEntity, out var actor) || !Available ||
            !_snapshots.IsCurrent(actor, beacon, snapshotGeneration) ||
            !_state.IsCurrentGeneration(actor, assignmentGeneration)) return null;
        var result = apply(actor);
        _snapshots.Publish([actor]);
        return result;
    }

    private FirstShiftUiState BuildState(NetUserId user)
    {
        var suggested = SuggestedDepartment(user);
        var assignment = _state.GetAssignment(user);
        if (!Available)
            return FirstShiftUiState.Disabled(suggested);
        if (assignment is null)
            return FirstShiftUiState.Idle(suggested);
        if (!_cards.TryGetValue(assignment.Value.CardId, out var card))
            return FirstShiftUiState.Disabled(suggested);

        // Task 5 (spec §3.6 edit #4): once parked at Mark, the SAME marker plumbing repoints from
        // the card's own anchor chain to the Continuity Garden — no new widgets, just a different
        // resolution source. Mark is only ever reached while _markEnabled is true (the OnComplete
        // dormant shim keeps it that way), so no extra CVar check is needed here.
        NetCoordinates? anchor;
        string anchorLabel;
        bool fallback;
        if (assignment.Value.Stage == FirstShiftAssignmentStage.Mark)
        {
            anchor = ResolveGardenAnchor(user, out anchorLabel);
            fallback = false;
        }
        else
        {
            anchor = ResolveAnchor(user, card, out anchorLabel, out fallback);
        }

        var remaining = assignment.Value.NextRerollAt - _timing.CurTime;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

        var roundSeed = (ulong) Math.Max(0, _ticker.RoundId);
        var task4RowKey = _markEnabled ? SelectTask4RowKey(roundSeed) : string.Empty;
        var task4DeflectionKey = _markEnabled ? SelectTask4DeflectionKey(roundSeed) : string.Empty;

        return new(true, true, suggested, assignment.Value.Department, card.ID, card.Title.Id, card.Why.Id,
            card.Orient.Id, card.Try.Id, card.IfStuck.Id, card.SafetyStop.Id, card.GuideEntry.Id,
            SelectFlavor(card, roundSeed), assignment.Value.Stage, anchor, anchorLabel, fallback,
            anchor is null, assignment.Value.Generation, remaining == TimeSpan.Zero, remaining,
            _markEnabled, task4RowKey, task4DeflectionKey);
    }

    /// <summary>
    ///     Resolves the Continuity Garden's coordinates for the Task 5 marker (spec §3.6 edit #4):
    ///     the lowest-uid <see cref="SolreignMarkGardenComponent"/> entity on the player's station —
    ///     the exact deterministic pick <c>SolreignMarkGardenSystem.ResolveGarden</c> uses for the
    ///     round-start projection, so the task-list marker and the actual materialized garden can
    ///     never disagree about which fixture is "the" garden on a foreign multi-garden map.
    /// </summary>
    private NetCoordinates? ResolveGardenAnchor(NetUserId user, out string anchorLabel)
    {
        anchorLabel = string.Empty;
        if (!_players.TryGetSessionById(user, out var session) || session.AttachedEntity is not { } actor ||
            _stations.GetOwningStation(actor) is not { } station)
            return null;

        EntityUid? best = null;
        var query = EntityQueryEnumerator<SolreignMarkGardenComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (_stations.GetOwningStation(uid, xform) != station)
                continue;
            if (best is null || uid.Id < best.Value.Id)
                best = uid;
        }

        if (best is not { } garden)
            return null;

        anchorLabel = MarkCopy.GardenNameKey;
        return GetNetCoordinates(Transform(garden).Coordinates);
    }

    /// <summary>Deterministic per-round pick (the SelectFlavor idiom) for the Task 4 row line — spec
    /// §8B's three "reassigned" variants, identical for every player that round.</summary>
    private static string SelectTask4RowKey(ulong roundSeed) => SelectSeeded(roundSeed, "task4-row", Task4RowKeys);

    /// <summary>Same picking law as <see cref="SelectTask4RowKey"/>, the matching deflection line.</summary>
    private static string SelectTask4DeflectionKey(ulong roundSeed) => SelectSeeded(roundSeed, "task4-deflection", Task4DeflectionKeys);

    private static string SelectSeeded(ulong roundSeed, string salt, IReadOnlyList<string> keys)
    {
        var hash = (uint) (roundSeed ^ (roundSeed >> 32));
        foreach (var ch in salt) hash = (hash ^ ch) * 16777619;
        return keys[(int) (hash % (uint) keys.Count)];
    }

    private FirstShiftDepartment SuggestedDepartment(NetUserId user)
    {
        if (!_players.TryGetSessionById(user, out var session) ||
            !_minds.TryGetMind(session, out var mind, out _) || !_jobs.MindTryGetJob(mind, out var job) ||
            !_jobs.TryGetAllDepartments(job.ID, out var departments)) return FirstShiftDepartment.Universal;
        foreach (var department in departments.OrderByDescending(d => d.Primary).ThenByDescending(d => d.Weight))
            if (MapDepartment(department.ID) is { } mapped && mapped != FirstShiftDepartment.Universal)
                return mapped;
        return FirstShiftDepartment.Universal;
    }

    private NetCoordinates? ResolveAnchor(NetUserId user, FirstShiftAssignmentPrototype card, out string anchorLabel, out bool usedFallback)
    {
        anchorLabel = string.Empty;
        usedFallback = false;
        if (!_players.TryGetSessionById(user, out var session) || session.AttachedEntity is not { } actor ||
            _stations.GetOwningStation(actor) is not { } station) return null;
        foreach (var wanted in card.Anchors)
        {
            EntityUid? best = null;
            var query = EntityQueryEnumerator<NavMapBeaconComponent, TransformComponent, MetaDataComponent>();
            while (query.MoveNext(out var uid, out var beacon, out var xform, out var meta))
            {
                if (!beacon.Enabled || !xform.Anchored || meta.EntityPrototype?.ID != wanted.Id ||
                    _stations.GetOwningStation(uid, xform) != station)
                    continue;
                if (best is null || uid.Id < best.Value.Id) best = uid;
            }
            if (best is not { } selected) continue;
            usedFallback = wanted.Id == FirstShiftAssignmentValidator.ArrivalsFallback;
            anchorLabel = $"ent-{wanted.Id}";
            return GetNetCoordinates(Transform(selected).Coordinates);
        }
        return null;
    }

    private static string SelectFlavor(FirstShiftAssignmentPrototype card, ulong roundSeed)
    {
        if (card.Flavor.Count == 0) return string.Empty;
        var hash = (uint) (roundSeed ^ (roundSeed >> 32));
        foreach (var ch in $"{card.Department}:{card.ID}") hash = (hash ^ ch) * 16777619;
        return card.Flavor[(int) (hash % (uint) card.Flavor.Count)].Id;
    }

    private static FirstShiftDepartment MapDepartment(string departmentId) => departmentId switch
    {
        "Engineering" => FirstShiftDepartment.Engineering,
        "Medical" => FirstShiftDepartment.Medical,
        "Science" => FirstShiftDepartment.Science,
        "Cargo" => FirstShiftDepartment.Cargo,
        "Service" or "Civilian" => FirstShiftDepartment.Service,
        _ => FirstShiftDepartment.Universal,
    };

    internal static FirstShiftDepartment MapDepartmentForTests(string departmentId) => MapDepartment(departmentId);
    internal static bool IsStartAuthorizedForTests(bool available, bool active, bool currentSnapshot,
        FirstShiftDepartment department) => IsStartAuthorized(available, active, currentSnapshot, department);

    private static bool IsStartAuthorized(bool available, bool active, bool currentSnapshot,
        FirstShiftDepartment department) => available && !active && currentSnapshot && department is
            FirstShiftDepartment.Engineering or FirstShiftDepartment.Medical or FirstShiftDepartment.Science or
            FirstShiftDepartment.Cargo or FirstShiftDepartment.Service;

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus is not (SessionStatus.Disconnected or SessionStatus.Zombie)) return;
        SessionUnavailable(args.Session.UserId);
    }

    private void SessionUnavailable(NetUserId user)
    {
        _state.Disconnect(user);
        _snapshots.CloseUser(user);
    }

    private void ClearRound() { _state.ClearRound(); _snapshots.Clear(); }
    internal void ClearAllForModerator() { var users = _state.ActiveUsers.ToArray(); _state.ClearActive(); _snapshots.Publish(users); }
    internal bool ClearForModerator(NetUserId user) { var result = _state.Clear(user); _snapshots?.Publish([user]); return result.Changed; }
    private bool TryActor(EntityUid entity, out NetUserId user)
    { user = default; if (!_players.TryGetSessionByEntity(entity, out var session)) return false; user = session.UserId; return true; }
    private bool TryResolveSession(NetUserId user, out ICommonSession session) => _players.TryGetSessionById(user, out session!);
    private bool IsBeaconAvailable(EntityUid uid) => Exists(uid) && HasComp<FirstShiftBeaconComponent>(uid) && MetaData(uid).EntityLifeStage < EntityLifeStage.Terminating;

    /// <summary>
    ///     True while <paramref name="user"/> has an active First Shift assignment this round (any
    ///     stage, not yet completed/ended/cleared) AND the feature is currently available. This is the
    ///     one sanctioned read other systems get of the round-local assignment state — the vista beat
    ///     (<see cref="Vista.SolreignVistaBeatSystem"/>) keys "is this player mid-first-shift" off it
    ///     rather than reaching into <see cref="FirstShiftRoundState"/> directly.
    /// </summary>
    internal bool HasActiveAssignment(NetUserId user) => Available && _state.GetAssignment(user) is not null;

    /// <summary>
    ///     Called by <see cref="SolreignMarkGardenSystem"/> immediately after a successful once-ever
    ///     mark claim (spec §3.6 edit #5): the planting itself completes Task 5 when this account's
    ///     assignment is active AND parked at the Mark stage — the SAME ALIVENESS ack (popup +
    ///     private-chat handoff) the button-path <see cref="OnComplete"/> fires, so a planter and a
    ///     player who skips planting and just presses "Complete assignment" both land on the
    ///     identical "orientation logged" handoff. Silent no-op otherwise (no assignment, wrong
    ///     stage, First Shift itself unavailable, or already completed) — planting always succeeds
    ///     regardless of this call's outcome; this is purely the task-list side effect, and the
    ///     self-paced button-path Complete stays reachable for anyone who never plants at all.
    /// </summary>
    internal void NotifyMarkPlanted(NetUserId user)
    {
        if (!Available || _state.GetAssignment(user) is not { Stage: FirstShiftAssignmentStage.Mark })
            return;

        var result = _state.Complete(user);
        _snapshots?.Publish([user]);

        if (result is not { Changed: true, Reason: "completed" })
            return;

        if (!_players.TryGetSessionById(user, out var session) || session.AttachedEntity is not { } actor)
            return;

        _popup.PopupEntity(Loc.GetString("first-shift-complete-popup"), actor, actor, PopupType.Medium);
        _chatManager.DispatchServerMessage(session, Loc.GetString("first-shift-complete-handoff"));
    }

    internal FirstShiftTransitionResult AdvanceToMarkForTests(NetUserId user) => _state.Advance(user);
    internal void NotifyMarkPlantedForTests(NetUserId user) => NotifyMarkPlanted(user);

    internal FirstShiftAssignment? GetAssignmentForTests(NetUserId user) => _state.GetAssignment(user);
    internal FirstShiftUiState BuildStateForTests(NetUserId user) => BuildState(user);
    internal void OpenForTests(NetUserId user, EntityUid beacon)
    { _snapshots.RememberOpen(user, beacon); _snapshots.Publish([user]); }
    internal void CloseForTests(NetUserId user, EntityUid beacon) => _snapshots.Close(user, beacon);
    internal ulong GetTransportGenerationForTests(NetUserId user) => _snapshots.CurrentGeneration(user);
    internal bool HasOpenSnapshotMappingForTests(NetUserId user) => _snapshots.HasOpenMapping(user);
    internal EntityUid? GetOpenBeaconForTests(NetUserId user) => _snapshots.GetOpenBeacon(user);
    internal void SessionUnavailableForTests(NetUserId user) => SessionUnavailable(user);
    internal void RoundRestartForTests() => ClearRound();
    internal FirstShiftTransitionResult AssignForTests(NetUserId user, FirstShiftDepartment department) =>
        _state.Request(user, department, _catalog, (ulong) Math.Max(0, _ticker.RoundId), _timing.CurTime);
    internal FirstShiftTransitionResult AdvanceForTests(NetUserId user, uint assignmentGeneration) =>
        _state.IsCurrentGeneration(user, assignmentGeneration)
            ? _state.Advance(user)
            : new(false, "stale-generation", _state.GetAssignment(user));
    internal FirstShiftSnapshotAdapter<TSession> CreatePrivateSnapshotAdapterForTests<TSession>(
        TryResolveFirstShiftSession<TSession> resolve,
        Action<FirstShiftPrivateSnapshotEvent, TSession> send) =>
        new(resolve, IsBeaconAvailable, uid => GetNetEntity(uid), BuildState, send);
}
