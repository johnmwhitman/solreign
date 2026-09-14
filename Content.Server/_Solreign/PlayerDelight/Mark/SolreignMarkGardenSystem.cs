using System;
using System.Collections.Generic;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server.Maps;
using Content.Server.Station.Events;
using Content.Server.Station.Systems;
using Content.Server._Solreign.PlayerDelight.FirstShift;
using Content.Server._Solreign.SeasonLedger;
using Content.Shared.CCVar;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Ghost.Components;
using Content.Shared.Maps;
using Content.Shared.Mind;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Content.Shared._Solreign.PlayerDelight.Mark;
using Content.Shared._Solreign.PlayerDelight.Wingmates;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;

namespace Content.Server._Solreign.PlayerDelight.Mark;

/// <summary>
///     "The Mark" garden wave (docs/specs/MARK-SPEC-2026-07-17-DRAFT.md §3.2/§3.3/§4, MG-W2): thin
///     ECS glue over the pure W1 helpers (<see cref="MarkAgeRules"/>, <see cref="MarkCopy"/>,
///     <see cref="MarkProjectionRules"/>, <see cref="MarkGardenLayout"/>). Owns three jobs:
///
///     1. ROUND-START PROJECTION (§3.3): on <see cref="StationPostInitEvent"/>, resolve the
///        station's Continuity Garden (map-placed fixture — MG-W5's wave — else the §4.3 runtime
///        fallback: spawn one beside the station's <c>SolreignWingmateBeacon</c>), then async-read
///        every <c>mark</c> row and spawn the stage-appropriate prototype at its deterministic
///        slot. Entities are round-local projections; the DB row is the truth — a wipe, an admin
///        delete, or an explosion costs at most one round.
///     2. PLANTING (§2/§3.2): three alt-click verbs on the garden, one per closed kind. The claim
///        is the atomic once-per-account-EVER <see cref="SeasonLedgerSystem.TryClaimMarkAsync"/>;
///        the winner gets the C1 kind-true confirmation (private popup + chat, the ALIVENESS ack
///        delivery shape), every later attempt gets the quiet already-claimed line. InRound-gated
///        (the FIRST-DEATH review-F1 rule: no PostRound planting).
///     3. EXAMINE (§4.2): stage line (public, kind+stage only) + one suffix — the C3 owner line
///        (the ONLY surface that ever renders the planted character name, rail 5) or the C4
///        stranger line. Composed entirely from data cached on <see cref="SolreignMarkComponent"/>
///        at projection time; no store round-trip during examine.
///
///     Everything gates on <c>solreign.mark.enabled</c> (ships FALSE — dormant; zero behavior,
///     zero reads, integration-test-pinned). The §3.5 return/nudge/overflow spawn beats are MG-W4;
///     the first-shift Task-5 hookup (<c>NotifyMarkPlanted</c>) is MG-W3.
///
///     Threading contract (the <c>ProvidenceWelcomeSystem.LoadWelcome</c> shape, verbatim): all
///     ledger I/O runs in async-void handlers with try/catch + <c>Log.Error</c>; every main-thread
///     read happens BEFORE the first await; every entity touch after an await re-checks
///     <c>Deleted()</c>. In-flight guards are marked before the await (gate-before-await) so a
///     racing re-entry can never double-run.
/// </summary>
public sealed partial class SolreignMarkGardenSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IGameMapManager _gameMapManager = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private SeasonLedgerSystem _ledger = default!;
    [Dependency] private FirstShiftSystem _firstShift = default!;

    /// <summary>The garden fixture's prototype id (mark_garden.yml) — what the §4.3 fallback spawns.</summary>
    public const string GardenPrototypeId = "SolreignMarkGarden";

    /// <summary>
    ///     Fallback-placement candidate ring around the wingmate beacon (§4.3), nearest-first: the
    ///     four cardinal neighbors, then the diagonals, then distance-2 cardinals. Fixed and ordered
    ///     so placement is deterministic per map.
    /// </summary>
    private static readonly Vector2i[] FallbackCandidateRing =
    {
        new(0, -1), new(0, 1), new(1, 0), new(-1, 0),
        new(1, -1), new(-1, -1), new(1, 1), new(-1, 1),
        new(0, -2), new(0, 2), new(2, 0), new(-2, 0),
    };

    private bool _enabled;

    // Stations already materialized this round (gate-before-await): StationPostInit fires once per
    // station per round in production, but the guard makes a double-fire (or a test re-entry) safe.
    private readonly HashSet<EntityUid> _projectedStations = new();

    // Accounts with a plant claim currently in flight — a double-click races the atomic claim
    // harmlessly, but the guard keeps one player's spam from stacking N identical confirmations.
    private readonly HashSet<Guid> _plantsInFlight = new();

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.SolreignMarkEnabled, v => _enabled = v, invokeImmediately: true);
        // Echoes of the Departed (v14, EOD-spec §10): independent master switch — a server may
        // run Echoes without Marks or vice versa (see MaterializeAndProject's gate split below).
        // OnEchoEnabledChanged also deletes live projections when the CVar flips off mid-round.
        Subs.CVar(_cfg, CCVars.SolreignEchoEnabled, OnEchoEnabledChanged, invokeImmediately: true);

        SubscribeLocalEvent<StationPostInitEvent>(OnStationPostInit);
        SubscribeLocalEvent<RoundStartingEvent>(_ => ResetRoundState());
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => ResetRoundState());

        SubscribeLocalEvent<SolreignMarkGardenComponent, GetVerbsEvent<AlternativeVerb>>(OnGardenGetVerbs);
        SubscribeLocalEvent<SolreignMarkComponent, ExaminedEvent>(OnMarkExamined);
        SubscribeLocalEvent<SolreignEchoComponent, ExaminedEvent>(OnEchoExamined);
    }

    private void ResetRoundState()
    {
        _projectedStations.Clear();
        _plantsInFlight.Clear();
        _echoProjectionGeneration++; // cancel any in-flight ProjectEchoes from the prior round
    }

    // ---------------------------------------------------------------- round-start projection (§3.3)

    private void OnStationPostInit(ref StationPostInitEvent ev)
    {
        MaterializeAndProject(ev.Station);
    }

    private void MaterializeAndProject(EntityUid station)
    {
        // Echoes of the Departed (v14, EOD-spec §9): the garden must resolve even when Marks are
        // off — a server owner may want ambient Echo haunting without the player-planted Mark
        // feature. Gate split: garden resolution always runs whenever EITHER feature is live
        // (cheap — an unpopulated garden with both features off already ships today with zero
        // visible difference); each projection job then branches on its own independent CVar.
        if (!_enabled && !_echoEnabled)
            return;

        if (!_projectedStations.Add(station))
            return;

        var garden = ResolveGarden(station) ?? SpawnFallbackGarden(station);
        if (garden is not { } resolved)
        {
            // Feature silently absent this round (foreign map with neither fixture nor beacon,
            // or no safe tile) — nothing breaks, rows persist untouched, next round retries.
            Log.Info($"No Continuity Garden resolvable for station {ToPrettyString(station)}; mark/echo projection skipped this round.");
            return;
        }

        if (_enabled)
            ProjectMarks(station, resolved);
        if (_echoEnabled)
            ProjectEchoes(station, resolved);
    }

    /// <summary>The station's map-placed garden (MG-W5), lowest-uid-deterministic — or null.</summary>
    private EntityUid? ResolveGarden(EntityUid station)
    {
        EntityUid? best = null;
        var query = EntityQueryEnumerator<SolreignMarkGardenComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (_station.GetOwningStation(uid, xform) != station)
                continue;

            if (best is null || uid.Id < best.Value.Id)
                best = uid;
        }

        return best;
    }

    /// <summary>
    ///     §4.3 runtime fallback: no map-placed garden → spawn one on the first safe tile from the
    ///     fixed candidate ring around the station's wingmate beacon (exactly one per rotation map,
    ///     integration-asserted; lowest-uid pick keeps foreign multi-beacon maps deterministic).
    ///     Beacon missing too, or no safe tile → null (skip round). Spawn-then-place failure deletes
    ///     the entity (the SolreignGhostCabinetsRule no-orphans discipline).
    /// </summary>
    private EntityUid? SpawnFallbackGarden(EntityUid station)
    {
        EntityUid? beacon = null;
        var query = EntityQueryEnumerator<WingmateBeaconComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (_station.GetOwningStation(uid, xform) != station)
                continue;

            if (beacon is null || uid.Id < beacon.Value.Id)
                beacon = uid;
        }

        if (beacon is not { } anchor)
            return null;

        var beaconXform = Transform(anchor);
        if (beaconXform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return null;

        var origin = _map.TileIndicesFor(gridUid, grid, beaconXform.Coordinates);
        foreach (var offset in FallbackCandidateRing)
        {
            var indices = origin + offset;
            if (!_map.TryGetTileRef(gridUid, grid, indices, out var tileRef)
                || _turf.IsSpace(tileRef)
                || _turf.IsTileBlocked(tileRef, CollisionGroup.Impassable))
            {
                continue;
            }

            var garden = Spawn(GardenPrototypeId, _map.GridTileToLocal(gridUid, grid, indices));
            if (!Transform(garden).Anchored)
            {
                Del(garden);
                continue;
            }

            return garden;
        }

        return null;
    }

    private async void ProjectMarks(EntityUid station, EntityUid garden)
    {
        try
        {
            var slots = _cfg.GetCVar(CCVars.SolreignMarkSlots);
            var records = await _ledger.GetAllMarksAsync(slots);

            // Post-await discipline (LoadWelcome shape): the round may have moved on underneath us.
            if (Deleted(station) || Deleted(garden) || !TryComp<SolreignMarkGardenComponent>(garden, out var gardenComp))
                return;

            var now = DateTime.UtcNow;
            foreach (var record in records)
            {
                // Corrupt rows (unknown kind, garbage timestamp) skip — never throw inside a
                // round-start projection; the closed table only ever spawns its own twelve ids.
                if (!MarkKinds.TryParse(record.Kind, out var kind))
                {
                    Log.Warning($"Mark row for {record.User} has unparseable kind '{record.Kind}'; skipped.");
                    continue;
                }

                if (!MarkAgeRules.TryParseLedgerUtc(record.PlantedUtc, out var planted))
                {
                    Log.Warning($"Mark row for {record.User} has unparseable planted_utc; skipped.");
                    continue;
                }

                var stage = MarkAgeRules.StageAt(planted, now);
                TrySpawnMark(garden, gardenComp, kind, stage, record.SlotIndex, record.User, record.CharacterName);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Error while projecting marks onto station {station}:\n{e}");
        }
    }

    /// <summary>
    ///     Spawns one mark projection at its deterministic garden slot. Space/missing tiles skip;
    ///     an entity that fails to anchor is deleted (no-orphans). Returns the spawned entity, or
    ///     null when the slot was unusable this round (the row is untouched either way).
    /// </summary>
    private EntityUid? TrySpawnMark(
        EntityUid garden,
        SolreignMarkGardenComponent gardenComp,
        MarkKind kind,
        int stage,
        int slotIndex,
        Guid account,
        string characterName)
    {
        var gardenXform = Transform(garden);
        if (gardenXform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return null;

        if (gardenComp.SlotOffsets.Count == 0)
            return null;

        var indices = _map.TileIndicesFor(gridUid, grid, gardenXform.Coordinates)
                      + MarkGardenLayout.SlotOffset(slotIndex, gardenComp.SlotOffsets);

        if (!_map.TryGetTileRef(gridUid, grid, indices, out var tileRef) || _turf.IsSpace(tileRef))
        {
            Log.Warning($"Mark slot {slotIndex} resolves to a missing/space tile beside {ToPrettyString(garden)}; projection skipped.");
            return null;
        }

        var uid = Spawn(MarkProjectionRules.PrototypeFor(kind, stage), _map.GridTileToLocal(gridUid, grid, indices));
        if (!Transform(uid).Anchored)
        {
            Del(uid);
            Log.Warning($"Mark projection for slot {slotIndex} failed to anchor; deleted (no orphans).");
            return null;
        }

        var mark = EnsureComp<SolreignMarkComponent>(uid);
        mark.Account = account;
        mark.Kind = kind;
        mark.Stage = stage;
        mark.CharacterName = characterName;
        return uid;
    }

    // ---------------------------------------------------------------- planting (§2, §3.2)

    private void OnGardenGetVerbs(EntityUid uid, SolreignMarkGardenComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!_enabled || !args.CanAccess || !args.CanInteract)
            return;

        // Actor must resolve to a live player session and must not be a ghost (spec §3.2).
        if (!_players.TryGetSessionByEntity(args.User, out _) || HasComp<GhostComponent>(args.User))
            return;

        var user = args.User;
        var priority = 10;
        foreach (var kind in MarkKinds.All)
        {
            var pick = kind;
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString(MarkCopy.VerbKeyFor(pick)),
                Priority = priority--,
                Act = () => TryPlant(uid, component, user, pick),
            });
        }
    }

    private void TryPlant(EntityUid garden, SolreignMarkGardenComponent component, EntityUid actor, MarkKind kind)
    {
        // Every failure path is a quiet no-op (the FireFirstShiftPersonal silent-failure law) —
        // never an exception, never a message on the wrong entity.
        if (!_enabled)
            return;

        // Review-F1 gate (ProvidenceFirstDeathSystem precedent): no PostRound/lobby planting — the
        // once-ever claim must never burn outside a live round.
        if (_ticker.RunLevel != GameRunLevel.InRound)
            return;

        if (!_mind.TryGetMind(actor, out _, out var mind) || mind.UserId is not { } userId)
            return;

        var guid = userId.UserId;
        if (!_plantsInFlight.Add(guid))
            return;

        // Main-thread reads, all before the first await.
        var characterName = MetaData(actor).EntityName;
        var roundId = _ticker.RoundId;
        var mapId = _gameMapManager.GetSelectedMap()?.ID ?? "unknown";

        PlantAsync(garden, actor, guid, characterName, kind, roundId, mapId);
    }

    private async void PlantAsync(
        EntityUid garden,
        EntityUid actor,
        Guid guid,
        string characterName,
        MarkKind kind,
        int roundId,
        string mapId)
    {
        try
        {
            var career = await _ledger.GetCareerStatsAsync(guid);
            var (claimed, slot) = await _ledger.TryClaimMarkAsync(
                guid, MarkKinds.ToLedgerString(kind), roundId, mapId, characterName, career.Tours);

            if (Deleted(actor))
                return;

            if (!claimed)
            {
                // Already holds their one mark (or lost a same-tick race to themselves): the quiet
                // refusal line. The row — whatever kind it holds — is untouched.
                DeliverPrivate(actor, guid, Loc.GetString(MarkCopy.AlreadyClaimedKey));
                return;
            }

            // Physically seat the fresh stage-0 projection at the claimed slot. A failed spawn
            // (garden died mid-claim, bad tile) costs only this round's visual — the row is safe
            // and next round's projection retries.
            if (!Deleted(garden) && TryComp<SolreignMarkGardenComponent>(garden, out var gardenComp))
                TrySpawnMark(garden, gardenComp, kind, 0, slot, guid, characterName);

            // C1, kind-true, private popup + private chat (chat persists after the popup fades —
            // the ALIVENESS P0 #4 delivery shape).
            DeliverPrivate(actor, guid, Loc.GetString(MarkCopy.PlantConfirmKeyFor(kind)));

            // MG-W3 wiring (spec §3.6 edit #5): the planting IS Task 5's completion when the
            // player's assignment is active and parked at Mark — silent no-op otherwise (no active
            // assignment, an earlier stage, or First Shift itself off), so a player who plants
            // before ever opening the first-shift panel costs nothing extra.
            _firstShift.NotifyMarkPlanted(new NetUserId(guid));
        }
        catch (Exception e)
        {
            Log.Error($"Error while planting mark for {guid}:\n{e}");
        }
        finally
        {
            _plantsInFlight.Remove(guid);
        }
    }

    private void DeliverPrivate(EntityUid mob, Guid guid, string text)
    {
        _popup.PopupEntity(text, mob, mob, PopupType.Medium);

        if (_players.TryGetSessionById(new NetUserId(guid), out var session))
            _chatManager.DispatchServerMessage(session, text);
    }

    // ---------------------------------------------------------------- examine (§4.2)

    private void OnMarkExamined(EntityUid uid, SolreignMarkComponent component, ExaminedEvent args)
    {
        if (!_enabled)
            return;

        // Public surface: kind + stage only (spec §8A — no variables, no names).
        args.PushMarkup(Loc.GetString(MarkCopy.StageKeyFor(component.Kind, component.Stage)));

        // One suffix: C3 for the owner (the ONLY surface that renders the planted name — rail 5),
        // C4 for everyone else, sessionless examiners included. Deterministic variant per
        // account+round (the round-seed Pick idiom).
        var isOwner = _players.TryGetSessionByEntity(args.Examiner, out var session)
                      && session.UserId.UserId == component.Account;
        var seed = _ticker.RoundId ^ component.Account.GetHashCode();

        args.PushMarkup(isOwner
            ? Loc.GetString(MarkCopy.Pick(MarkCopy.OwnerSuffixKeys, seed), ("name", component.CharacterName))
            : Loc.GetString(MarkCopy.Pick(MarkCopy.StrangerSuffixKeys, seed)));
    }

    // ---------------------------------------------------------------- test seams (house ForTests idiom)

    /// <summary>Runs the full StationPostInit path (CVar gate included) against one station.</summary>
    internal void MaterializeAndProjectForTests(EntityUid station) => MaterializeAndProject(station);

    /// <summary>Runs the verb-Act plant path (CVar + InRound + session gates included).</summary>
    internal void TryPlantForTests(EntityUid garden, EntityUid actor, MarkKind kind) =>
        TryPlant(garden, Comp<SolreignMarkGardenComponent>(garden), actor, kind);

    /// <summary>Clears the per-round guards — call after flipping CVars, the welcome-test rationale.</summary>
    internal void ResetRoundStateForTests() => ResetRoundState();
}
