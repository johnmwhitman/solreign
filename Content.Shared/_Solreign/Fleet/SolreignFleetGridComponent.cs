using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;
using Robust.Shared.ViewVariables;

namespace Content.Shared._Solreign.Fleet;

/// <summary>
///     Identifies the node role of a grid within a Solreign Multi-Station Fleet Operation (SR-W-044).
/// </summary>
public enum FleetNodeType : byte
{
    StationAlpha = 0,
    StationBeta = 1,
    ExpeditionCraft = 2
}

/// <summary>
///     Current transit/docking posture of a fleet grid or expedition vessel.
/// </summary>
public enum FleetTransitState : byte
{
    Stationary = 0,
    InTransit = 1,
    DockingApproach = 2,
    Docked = 3
}

/// <summary>
///     Attached to grid entities representing stations or expedition craft in a fleet operation round.
/// </summary>

[RegisterComponent]
public sealed partial class SolreignFleetGridComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite)]
    public string NodeName { get; set; } = "Fleet Node";

    [ViewVariables(VVAccess.ReadWrite)]
    public FleetNodeType NodeType { get; set; } = FleetNodeType.StationAlpha;

    [ViewVariables(VVAccess.ReadWrite)]
    public FleetTransitState TransitState { get; set; } = FleetTransitState.Stationary;

    /// <summary>
    ///     Guarantees power isolation (separate substation & APC networks).
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public bool PowerIsolated { get; set; } = true;

    /// <summary>
    ///     Guarantees atmospheric pressure and gas isolation.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public bool AtmosIsolated { get; set; } = true;

    /// <summary>
    ///     Guarantees separate cargo bank accounts and requisition pools.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public bool CargoAccountIsolated { get; set; } = true;

    /// <summary>
    ///     Max allowed entity budget for this grid entity to preserve performance budgets.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public int MaxEntityBudget { get; set; } = 500;

    /// <summary>
    ///     Current calculated entity count on this grid.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public int CurrentEntityCount { get; set; } = 0;
}
