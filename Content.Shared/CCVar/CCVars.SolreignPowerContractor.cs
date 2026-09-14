using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
///     PROVIDENCE power contractor CVars (SPEC-ai-npc-phase1-v2.md §3-§9): the deterministic "keep
///     the lights on" NPC that leases a capped, temporary <c>PowerSupplierComponent</c> onto a
///     failing HV network when no qualified human is covering it. Own partial file per this
///     codebase's D0 collision-control convention (see <c>CCVars.SolreignLowPop.cs</c>'s doc
///     comment for the same reasoning).
///
///     <see cref="SolreignPowerContractorEnabled"/> is the master kill switch, default OFF per the
///     build directive — this is a fresh, unreviewed-in-production feature and must not silently
///     start dispatching units on any live server until explicitly turned on. Every other CVar in
///     this file is inert while the master switch is off (the monitor system does not even
///     enumerate candidate networks).
///
///     No CVar in this file gates anything MiniMax/LLM-related — per the build directive, §11 (the
///     MiniMax stub) is entirely out of scope for this cut and ships as dead code behind its own
///     future CVar, not this one.
/// </summary>
public sealed partial class CCVars
{
    /// <summary>
    ///     Master kill switch for the whole PROVIDENCE power contractor feature
    ///     (<c>Content.Server._Solreign.PowerContractor.ProvidencePowerContractorSystem</c>).
    ///     Default OFF. While off, the monitor performs no network discovery, spawns nothing, and
    ///     leaves zero footprint.
    /// </summary>
    public static readonly CVarDef<bool> SolreignPowerContractorEnabled =
        CVarDef.Create("solreign.power_contractor.enabled", false, CVar.SERVERONLY);

    /// <summary>
    ///     How often (in seconds) the monitor samples every discovered zone's deficit/reserve
    ///     (§4.3) and human-coverage (§4.4) predicates. Matches the "fixed interval, not per-frame"
    ///     requirement of §4.5.
    /// </summary>
    public static readonly CVarDef<float> SolreignPowerContractorMonitorIntervalSeconds =
        CVarDef.Create("solreign.power_contractor.monitor_interval_seconds", 5f, CVar.SERVERONLY);

    /// <summary>
    ///     Number of consecutive monitor samples a gap (failing AND uncovered, §4.3+§4.4) or a
    ///     clearing condition must hold before the FSM (§5) acts on it. This is the anti-thrash
    ///     hysteresis window from §4.5 — a value of 2 means "this tick's sample plus at least one
    ///     more consecutive sample," i.e. never fires on a single-frame blip.
    /// </summary>
    public static readonly CVarDef<int> SolreignPowerContractorHysteresisTicks =
        CVarDef.Create("solreign.power_contractor.hysteresis_ticks", 2, CVar.SERVERONLY);

    /// <summary>
    ///     §4.3 condition 1: a network is a dispatch candidate only when consumption exceeds
    ///     current supply by more than this fraction of consumption.
    /// </summary>
    public static readonly CVarDef<float> SolreignPowerContractorDeficitFraction =
        CVarDef.Create("solreign.power_contractor.deficit_fraction", 0.15f, CVar.SERVERONLY);

    /// <summary>
    ///     Review finding #7: an ABSOLUTE floor (in watts) on top of
    ///     <see cref="SolreignPowerContractorDeficitFraction"/>'s fractional check -- a network with a
    ///     trivial (near-zero) load and zero supply reads as a 100% fractional deficit, which without
    ///     this floor would summon a full-sized contractor over a load not worth dispatching for. Both
    ///     the fractional AND this absolute check must hold for condition 1 to be satisfied.
    /// </summary>
    public static readonly CVarDef<float> SolreignPowerContractorMinUnmetWatts =
        CVarDef.Create("solreign.power_contractor.min_unmet_watts", 500f, CVar.SERVERONLY);

    /// <summary>
    ///     §4.3 condition 2: the network's aggregate battery reserve ratio
    ///     (InStorageCurrent / InStorageMax) must be below this floor, AND declining since the
    ///     previous sample, to count as "failing" rather than "small and stable."
    /// </summary>
    public static readonly CVarDef<float> SolreignPowerContractorReserveFloorFraction =
        CVarDef.Create("solreign.power_contractor.reserve_floor_fraction", 0.20f, CVar.SERVERONLY);

    /// <summary>
    ///     §3.4: the capped wattage a leased contractor supplies once attached — sized to keep
    ///     critical APCs alive, not to run the station at full load.
    /// </summary>
    public static readonly CVarDef<float> SolreignPowerContractorLeaseWatts =
        CVarDef.Create("solreign.power_contractor.lease_watts", 6000f, CVar.SERVERONLY);

    /// <summary>
    ///     §3.4: hard maximum lease duration in seconds. On expiry the unit recalls regardless of
    ///     whether the trigger condition still holds — the primary defense against the lease being
    ///     exploited as free, permanent power upkeep.
    /// </summary>
    public static readonly CVarDef<float> SolreignPowerContractorLeaseTtlSeconds =
        CVarDef.Create("solreign.power_contractor.lease_ttl_seconds", 300f, CVar.SERVERONLY);

    /// <summary>
    ///     §3.4: anti-thrash backstop. After a lease ends (TTL expiry, human takeover, admin/
    ///     round-end recall, or destruction), the same zone will not redispatch until this many
    ///     seconds have elapsed, even if the gap condition re-confirms immediately.
    /// </summary>
    public static readonly CVarDef<float> SolreignPowerContractorRedispatchCooldownSeconds =
        CVarDef.Create("solreign.power_contractor.redispatch_cooldown_seconds", 180f, CVar.SERVERONLY);

    /// <summary>
    ///     §7.5: maximum number of contractor units live at once, station-wide. Enforced at
    ///     Dormant -&gt; Dispatching.
    /// </summary>
    public static readonly CVarDef<int> SolreignPowerContractorConcurrencyCap =
        CVarDef.Create("solreign.power_contractor.concurrency_cap", 1, CVar.SERVERONLY);

    /// <summary>
    ///     §7.2's bounded ring-search radius (in tiles) around the zone's anchor entity for a valid
    ///     DeployTile, used when the anchor's own tile fails spawn validation.
    /// </summary>
    public static readonly CVarDef<int> SolreignPowerContractorSpawnRingRadius =
        CVarDef.Create("solreign.power_contractor.spawn_ring_radius", 4, CVar.SERVERONLY);
}
