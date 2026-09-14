using System;
using System.Collections.Generic;
using Content.Server.Afk;
using Content.Server.Antag;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Power.Pow3r;
using Content.Server.Station.Systems;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Content.Shared.Power;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.PowerContractor;

/// <summary>
///     PROVIDENCE power contractor -- the deterministic "keep the lights on" NPC
///     (SPEC-ai-npc-phase1-v2.md §3-§9). Builds §3-§9 only: the leased-supplier power mechanism
///     (§3), zone detection (§4), the lifecycle FSM (§5-§7), and the safety contract (§7-§8). §11
///     (MiniMax/LLM) is out of scope for this cut and is not referenced anywhere in this file.
///
///     Master-gated by <see cref="CCVars.SolreignPowerContractorEnabled"/>, default OFF. While off,
///     <see cref="Update"/> performs no network discovery and leaves zero footprint -- see
///     <see cref="HardResetAllZones"/>, also used for the round-restart zero-surviving-state
///     guarantee (§7's persistence/reconciliation requirements).
///
///     Split across four partial files, mirroring <c>SeasonLedgerSystem</c>'s own multi-file
///     convention for a feature this size:
///       - this file: CVar wiring, the monitor tick loop, zone discovery/reconciliation, round
///         start/end/restart hooks.
///       - <c>.Detection.cs</c>: §4.3 deficit+reserve and §4.4 human-coverage predicates.
///       - <c>.Lifecycle.cs</c>: §5-§7 dispatch/spawn-validation/ring-search and recall sequencing.
///       - <c>.Safety.cs</c>: §7.4-§7.5 TerminatingOrDeleted reconciliation, concurrency-cap
///         accounting, and the implicit-recall paths (destruction, grid removal, round cleanup).
/// </summary>
public sealed partial class ProvidencePowerContractorSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private PowerNetSystem _powerNet = default!;
    [Dependency] private AntagSelectionSystem _antag = default!;
    [Dependency] private IAfkManager _afk = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedJobSystem _job = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private StationSystem _station = default!;

    /// <summary>The contractor's own entity prototype (§7.3: save:false, §3.1-3.4: capped
    /// PowerSupplierComponent). Defined in
    /// Resources/Prototypes/Entities/Mobs/NPCs/_Solreign/providence_power_contractor.yml.</summary>
    public const string ContractorPrototypeId = "ProvidencePowerContractor";

    // Review finding #5: sane upper bounds every numeric CVar is clamped against at subscribe-time,
    // on top of the finite/non-negative check every one of them gets -- see ValidateFinite/
    // ValidateNonNegativeInt below. Values are generous (this is a safety ceiling against a
    // fat-fingered or malicious config, not a balance tuning knob).
    private const float MaxMonitorIntervalSeconds = 3600f;
    private const int MaxHysteresisTicks = 1000;
    private const float MaxFraction = 1f;
    private const float MaxLeaseWatts = 1_000_000f;
    private const float MaxLeaseTtlSeconds = 86_400f;
    private const float MaxRedispatchCooldownSeconds = 86_400f;
    private const int MaxConcurrencyCap = 64;
    private const int MaxSpawnRingRadius = 64;
    private const float MaxMinUnmetWatts = 1_000_000f;

    private bool _enabled;
    private float _monitorIntervalSeconds;
    private int _hysteresisTicks;
    private float _deficitFraction;
    private float _reserveFloorFraction;
    private float _leaseWatts;
    private float _leaseTtlSeconds;
    private float _redispatchCooldownSeconds;
    private int _concurrencyCap;
    private int _spawnRingRadius;
    private float _minUnmetWatts;

    private TimeSpan _nextMonitorTime;

    /// <summary>One entry per discovered HV network (§4.2's runtime replacement for a map-authored
    /// ProvidencePowerAnchor), keyed by the SMES-equivalent anchor entity it was discovered from.</summary>
    private readonly Dictionary<EntityUid, ProvidencePowerContractorZone> _zones = new();

    /// <summary>
    ///     Review finding #4's cooldown-continuity fix: keyed by the physical GRID (stable across a
    ///     PowerState.Network remake, unlike the network object itself or an anchor's own EntityUid) a
    ///     zone was last known to be on. Populated when a still-cooling-down zone's anchor becomes
    ///     unresolvable (replaced or destroyed) before its redispatch cooldown elapsed; consumed by
    ///     <see cref="DiscoverZones"/> so a freshly-discovered anchor on the SAME grid inherits the
    ///     remaining cooldown instead of silently starting a brand-new, cooldown-free lease cycle.
    ///     Pruned of expired entries once per monitor tick and fully cleared on round
    ///     restart/feature-disable (see <see cref="HardResetAllZones"/>) -- never round-persistent.
    /// </summary>
    private readonly Dictionary<EntityUid, TimeSpan> _gridCooldownMemory = new();

    /// <summary>§7.5 concurrency cap accounting -- number of zones currently in Dispatching,
    /// Supplying, RecallPending, or Recalling (i.e. holding a cap slot).</summary>
    private int _liveContractorCount;

    /// <summary>Read-only test/observability accessor -- same idiom as
    /// <c>ProvidenceVoiceSystem.Enabled</c>. Not used by any production code path.</summary>
    public int LiveContractorCount => _liveContractorCount;

    /// <summary>Read-only test/observability accessor for the number of currently-tracked zones.</summary>
    public int ZoneCount => _zones.Count;

    /// <summary>Clamps a float CVar to finite, non-negative, and no more than <paramref name="max"/>
    /// -- review finding #5. Non-finite (NaN/Infinity) or negative input falls back to
    /// <paramref name="fallback"/> entirely rather than being clamped, since there is no sane "nearest
    /// valid value" for a NaN.</summary>
    private static float ValidateFinite(float value, float fallback, float max)
    {
        if (!float.IsFinite(value) || value < 0f)
            return fallback;

        return MathF.Min(value, max);
    }

    /// <summary>Clamps an int CVar to non-negative and no more than <paramref name="max"/> -- review
    /// finding #5.</summary>
    private static int ValidateNonNegativeInt(int value, int fallback, int max)
    {
        if (value < 0)
            return fallback;

        return Math.Min(value, max);
    }

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.SolreignPowerContractorEnabled, v => _enabled = v, invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignPowerContractorMonitorIntervalSeconds,
            v => _monitorIntervalSeconds = ValidateFinite(v, CCVars.SolreignPowerContractorMonitorIntervalSeconds.DefaultValue, MaxMonitorIntervalSeconds),
            invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignPowerContractorHysteresisTicks,
            v => _hysteresisTicks = Math.Max(1, ValidateNonNegativeInt(v, CCVars.SolreignPowerContractorHysteresisTicks.DefaultValue, MaxHysteresisTicks)),
            invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignPowerContractorDeficitFraction,
            v => _deficitFraction = ValidateFinite(v, CCVars.SolreignPowerContractorDeficitFraction.DefaultValue, MaxFraction),
            invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignPowerContractorReserveFloorFraction,
            v => _reserveFloorFraction = ValidateFinite(v, CCVars.SolreignPowerContractorReserveFloorFraction.DefaultValue, MaxFraction),
            invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignPowerContractorLeaseWatts,
            v => _leaseWatts = ValidateFinite(v, CCVars.SolreignPowerContractorLeaseWatts.DefaultValue, MaxLeaseWatts),
            invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignPowerContractorLeaseTtlSeconds,
            v => _leaseTtlSeconds = ValidateFinite(v, CCVars.SolreignPowerContractorLeaseTtlSeconds.DefaultValue, MaxLeaseTtlSeconds),
            invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignPowerContractorRedispatchCooldownSeconds,
            v => _redispatchCooldownSeconds = ValidateFinite(v, CCVars.SolreignPowerContractorRedispatchCooldownSeconds.DefaultValue, MaxRedispatchCooldownSeconds),
            invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignPowerContractorConcurrencyCap,
            v => _concurrencyCap = ValidateNonNegativeInt(v, CCVars.SolreignPowerContractorConcurrencyCap.DefaultValue, MaxConcurrencyCap),
            invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignPowerContractorSpawnRingRadius,
            v => _spawnRingRadius = ValidateNonNegativeInt(v, CCVars.SolreignPowerContractorSpawnRingRadius.DefaultValue, MaxSpawnRingRadius),
            invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignPowerContractorMinUnmetWatts,
            v => _minUnmetWatts = ValidateFinite(v, CCVars.SolreignPowerContractorMinUnmetWatts.DefaultValue, MaxMinUnmetWatts),
            invokeImmediately: true);

        // §7.5: a RoundEndMessageEvent subscriber forces every live contractor into RecallPending --
        // do not rely on individual zones noticing the round ended on their own monitor cadence.
        SubscribeLocalEvent<RoundEndMessageEvent>(OnRoundEnd);

        // Belt-and-suspenders on top of RoundEndMessageEvent: RoundRestartCleanupEvent is the actual
        // round-boundary reset hook (same idiom as LowPopLobbyReminderSystem's _gate.Reset()) --
        // guarantees zero surviving zone/gate state even if a round restarts before a prior round's
        // RecallPending->Recalling->Deleted sequence finished playing out.
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);

        // §5's "destroyed mid-lease" implicit-recall path: entity deletion alone already frees the
        // solver-side supply slot (PowerSupplierShutdown) -- this subscriber's job is only to release
        // the concurrency-cap slot and start the redispatch cooldown, exactly as a normal recall would.
        SubscribeLocalEvent<ProvidencePowerContractorComponent, EntityTerminatingEvent>(OnContractorTerminating);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_enabled)
        {
            // Feature turned off mid-round (or never turned on): leave zero footprint. Cheap no-op
            // once _zones is already empty, which is the steady state whenever the CVar has never
            // been flipped on -- matches the "never flip the enable CVar on" build directive.
            if (_zones.Count > 0)
                HardResetAllZones();

            return;
        }

        var now = _timing.CurTime;

        if (now < _nextMonitorTime)
            return;

        _nextMonitorTime = now + TimeSpan.FromSeconds(MathF.Max(_monitorIntervalSeconds, 0.01f));

        MonitorTick(now);
    }

    /// <summary>One fixed-interval monitor sample (§4.5): discover/refresh zones, reconcile any
    /// topology change (network merge/split, review finding #4), reconcile dead contractors, prune
    /// expired grid-cooldown memory, then advance every zone's FSM by exactly one sample.</summary>
    private void MonitorTick(TimeSpan now)
    {
        DiscoverZones();
        ReconcileZoneTopology(now);
        ReconcileTerminatedContractors();
        PruneExpiredGridCooldowns(now);
        AdvanceZones(now);
    }

    /// <summary>
    ///     §4.2's runtime anchor discovery: every entity with both <c>PowerNetworkBatteryComponent</c>
    ///     and a High-voltage <c>BatteryDischargerComponent</c> whose net is currently resolved (i.e.
    ///     every live SMES-equivalent already wired onto an HV network) becomes -- or continues to be
    ///     -- one zone's anchor. A newly-discovered anchor whose resolved network is already tracked
    ///     by an existing zone (two SMES on the same network) is skipped, so one network is never
    ///     double-tracked under two anchors.
    ///
    ///     Review finding #3: an anchor candidate is only eligible when its grid genuinely belongs to
    ///     SOME station (<see cref="TryGetAnchorStation"/>) -- this excludes unmanned off-station grids
    ///     (an undocked enemy/ruin/derelict wreck, or an admin-spawned shuttle never added to any
    ///     station) that would otherwise be able to spawn a contractor and consume the global
    ///     concurrency cap ahead of the real, crewed public station. A grid with zero SMES anchors, or
    ///     a round with zero stations at all, is a clean no-op here -- the query simply yields nothing
    ///     to track.
    /// </summary>
    private void DiscoverZones()
    {
        var query = EntityQueryEnumerator<PowerNetworkBatteryComponent, BatteryDischargerComponent>();

        while (query.MoveNext(out var uid, out _, out var discharger))
        {
            if (discharger.Voltage != Voltage.High)
                continue;

            if (discharger.Net is not { } net)
                continue;

            if (TerminatingOrDeleted(uid))
                continue;

            if (_zones.ContainsKey(uid))
                continue;

            if (!TryGetAnchorStation(uid, out _))
                continue;

            var network = net.NetworkNode;
            var alreadyTracked = false;

            foreach (var existing in _zones.Values)
            {
                if (!TryResolveZoneNetwork(existing, out var existingNetwork))
                    continue;

                if (ReferenceEquals(existingNetwork, network))
                {
                    alreadyTracked = true;
                    break;
                }
            }

            if (alreadyTracked)
                continue;

            var zone = new ProvidencePowerContractorZone(uid);

            if (TryGetAnchorGrid(uid, out var gridUid))
            {
                zone.LastKnownGridUid = gridUid;

                // Review finding #4: this grid has an unexpired cooldown left over from a
                // just-orphaned zone (its previous anchor was replaced/destroyed before the
                // redispatch backoff elapsed) -- inherit it onto this fresh anchor's gate rather than
                // silently starting a brand-new, cooldown-free lease cycle on the same physical spot.
                if (_gridCooldownMemory.TryGetValue(gridUid, out var preservedCooldownUntil) && preservedCooldownUntil > _timing.CurTime)
                {
                    zone.Gate.SeedCooldown(preservedCooldownUntil);
                    _gridCooldownMemory.Remove(gridUid);
                }
            }

            _zones[uid] = zone;
        }
    }

    /// <summary>
    ///     Review finding #4 (merge half): a topology remake (<c>PowerNet.AfterRemake</c>) can splice
    ///     two previously-separate HV networks -- each already tracked under its own zone -- into ONE
    ///     combined <c>PowerState.Network</c> object mid-round. Left unhandled, both zones would keep
    ///     sampling and could each independently dispatch, leaving two contractors racing to supply
    ///     the very same network. This pass canonicalizes by CURRENT network identity every tick: the
    ///     first zone seen for a given network survives, any other zone that now resolves to the SAME
    ///     network is retired (its live contractor, if any, is unconditionally recalled -- never just
    ///     abandoned) and dropped, so at most one zone ever tracks one real network.
    /// </summary>
    private void ReconcileZoneTopology(TimeSpan now)
    {
        var byNetwork = new Dictionary<PowerState.Network, EntityUid>();
        var survivors = new List<EntityUid>(_zones.Keys);

        foreach (var anchorUid in survivors)
        {
            if (!_zones.TryGetValue(anchorUid, out var zone))
                continue;

            if (!TryResolveZoneNetwork(zone, out var network))
                continue; // unresolvable this tick -- handled separately by AdvanceZones' grid-removal path

            if (!byNetwork.TryGetValue(network, out var survivorAnchor))
            {
                byNetwork[network] = anchorUid;
                continue;
            }

            if (!_zones.TryGetValue(survivorAnchor, out var survivorZone))
            {
                byNetwork[network] = anchorUid;
                continue;
            }

            var loserAnchor = PickMergeLoser(survivorAnchor, survivorZone, anchorUid, zone);
            var winnerAnchor = loserAnchor == survivorAnchor ? anchorUid : survivorAnchor;
            var loserZone = loserAnchor == survivorAnchor ? survivorZone : zone;

            byNetwork[network] = winnerAnchor;

            Log.Info($"providence.power_contractor: network merge detected -- retiring duplicate zone anchor {ToPrettyString(loserAnchor)}, canonical anchor for this network is now {ToPrettyString(winnerAnchor)}");

            if (loserZone.ContractorUid is not null || loserZone.Gate.Phase != ProvidencePowerContractorPhase.Dormant)
            {
                loserZone.Gate.TryForceRecall();
                RecallZone(loserAnchor, loserZone, now);
            }

            _zones.Remove(loserAnchor);
        }
    }

    /// <summary>Picks which of two zones now sharing one merged network gets retired (review finding
    /// #4): prefer keeping whichever already holds a live lease (retiring a live contractor just to
    /// immediately redispatch a new one on the same network is pure churn); if that's a tie, prefer
    /// whichever is furthest from Dormant; if still a tie, prefer whichever has the LONGER remaining
    /// redispatch cooldown (preserves more anti-thrash protection); final tie-break is the lower
    /// EntityUid, purely for determinism/testability.</summary>
    private static EntityUid PickMergeLoser(
        EntityUid anchorA, ProvidencePowerContractorZone zoneA,
        EntityUid anchorB, ProvidencePowerContractorZone zoneB)
    {
        var aLive = zoneA.ContractorUid is not null;
        var bLive = zoneB.ContractorUid is not null;

        if (aLive != bLive)
            return aLive ? anchorB : anchorA;

        var aRank = PhaseSeniority(zoneA.Gate.Phase);
        var bRank = PhaseSeniority(zoneB.Gate.Phase);

        if (aRank != bRank)
            return aRank > bRank ? anchorB : anchorA;

        if (zoneA.Gate.CooldownDeadline != zoneB.Gate.CooldownDeadline)
            return zoneA.Gate.CooldownDeadline > zoneB.Gate.CooldownDeadline ? anchorB : anchorA;

        return anchorA.CompareTo(anchorB) <= 0 ? anchorB : anchorA;
    }

    private static int PhaseSeniority(ProvidencePowerContractorPhase phase) => phase switch
    {
        ProvidencePowerContractorPhase.Supplying => 3,
        ProvidencePowerContractorPhase.Dispatching => 2,
        ProvidencePowerContractorPhase.RecallPending or ProvidencePowerContractorPhase.Recalling => 1,
        _ => 0,
    };

    /// <summary>Review finding #4's cooldown-continuity memory only needs to outlive an orphaned
    /// zone's own cooldown window -- prune anything past its deadline once per monitor tick so the
    /// dictionary never grows unbounded across a long round with heavy grid/anchor churn.</summary>
    private void PruneExpiredGridCooldowns(TimeSpan now)
    {
        if (_gridCooldownMemory.Count == 0)
            return;

        List<EntityUid>? expired = null;

        foreach (var (gridUid, cooldownUntil) in _gridCooldownMemory)
        {
            if (now >= cooldownUntil)
                (expired ??= new List<EntityUid>()).Add(gridUid);
        }

        if (expired is null)
            return;

        foreach (var gridUid in expired)
            _gridCooldownMemory.Remove(gridUid);
    }

    /// <summary>Review finding #3: resolves the owning station for an anchor (or any entity), scoping
    /// both anchor discovery eligibility and cap-priority ordering to real station membership. Returns
    /// false for a grid never added to any station's <c>StationDataComponent.Grids</c> -- an
    /// undocked/enemy/ruin/derelict grid, or an admin-spawned shuttle that was never formally
    /// docked.</summary>
    private bool TryGetAnchorStation(EntityUid entity, out EntityUid stationUid)
    {
        stationUid = default;

        if (_station.GetOwningStation(entity) is not { } station)
            return false;

        stationUid = station;
        return true;
    }

    /// <summary>Review finding #3's cap-priority rule: "the public station" is defined as whichever
    /// currently-known station has the most tiles across its member grids -- in ordinary play there is
    /// exactly one crewed station and every admin-spawned or event-created secondary station is
    /// dramatically smaller, so this is a simple, deterministic, and testable proxy for "the real
    /// station" without inventing a new map-authored flag. Returns null when there are zero stations
    /// at all (nothing to prioritize).</summary>
    private EntityUid? ResolvePublicStation()
    {
        EntityUid? best = null;
        var bestTiles = -1;

        foreach (var station in _station.GetStationsSet())
        {
            var tiles = _station.GetTileCount(station);

            if (tiles <= bestTiles)
                continue;

            bestTiles = tiles;
            best = station;
        }

        return best;
    }

    private void OnRoundEnd(RoundEndMessageEvent ev)
    {
        ForceRecallAllZones();
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        HardResetAllZones();
    }
}
