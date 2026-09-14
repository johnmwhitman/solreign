using Robust.Shared.GameObjects;

namespace Content.Server._Solreign.PowerContractor;

/// <summary>
///     Runtime-discovered replacement for SPEC-ai-npc-phase1-v2.md §4.2's map-authored
///     <c>ProvidencePowerAnchor</c> record (§10 Open Question 2, resolved in favor of the
///     dispatch-spawned / materialize-at-problem path -- no mapper dependency).
///
///     One zone tracks one HV network, discovered by walking every entity that carries both
///     <c>PowerNetworkBatteryComponent</c> and a High-voltage <c>BatteryDischargerComponent</c> --
///     i.e. every SMES already placed on the map as standard per-network reserve capacity
///     (<c>Resources/Prototypes/Entities/Structures/Power/smes.yml:44-52,60-70</c>). SMES already
///     ships on every HV subnet by ordinary mapper convention with zero new content authoring, and
///     is also the literal source of the §4.3 reserve statistic being monitored (SMES batteries feed
///     <c>NetworkPowerStatistics.InStorageCurrent</c>/<c>InStorageMax</c> via
///     <c>PowerNetSystem.GetNetworkStatistics</c>'s <c>network.BatterySupplies</c> accumulation) --
///     so the anchor entity and the thing being measured are the same real infrastructure.
///
///     The anchor's resolved network (via its discharger's <c>.Net.NetworkNode</c>) is re-read fresh
///     every monitor tick rather than cached long-term, because node-group remakes
///     (<c>PowerNet.AfterRemake</c>) recreate the underlying <c>PowerState.Network</c> object on
///     topology changes -- a stale cached reference would silently stop tracking the real network.
/// </summary>
internal sealed class ProvidencePowerContractorZone
{
    public ProvidencePowerContractorZone(EntityUid anchorUid)
    {
        AnchorUid = anchorUid;
    }

    /// <summary>The SMES (or equivalent HV-discharging battery) entity this zone was discovered from.</summary>
    public EntityUid AnchorUid { get; }

    /// <summary>Pure FSM/hysteresis/TTL/cooldown decision state for this zone's lease (§5).</summary>
    public ProvidencePowerContractorGate Gate { get; } = new();

    /// <summary>The live contractor entity, set while Dispatching/Supplying/RecallPending/Recalling.</summary>
    public EntityUid? ContractorUid { get; set; }

    /// <summary>Previous monitor tick's reserve ratio (InStorageCurrent / InStorageMax), used to detect
    /// a *declining* reserve rather than just a currently-low one (§4.3 condition 2).</summary>
    public float? PreviousReserveRatio { get; set; }

    /// <summary>
    ///     Review finding #4: the grid this zone's anchor last successfully resolved onto, refreshed
    ///     every tick <c>TryGetAnchorGrid</c> succeeds. Deliberately NOT re-derived at the moment the
    ///     anchor becomes unresolvable (destroyed/replaced) -- by then the anchor itself may already be
    ///     gone, so this cached value is the only way to know which physical grid a still-cooling-down
    ///     zone's redispatch backoff should be transplanted onto (<c>_gridCooldownMemory</c>).
    /// </summary>
    public EntityUid? LastKnownGridUid { get; set; }
}
