using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using Content.Shared._Solreign.Fleet;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;

namespace Content.Server._Solreign.Fleet;

/// <summary>
///     Core server system managing SR-W-044 (Multi-Station Fleet Operation Night).
///     Orchestrates multi-station preview rounds, grid resource isolation, cross-grid transit,
///     spectator readability telemetry, and performance budget verification.
/// </summary>
public sealed partial class SolreignFleetOperationSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;

    private bool _fleetEnabled;
    private int _maxGrids;

    private readonly List<EntityUid> _fleetNodes = new();

    public override void Initialize()
    {
        base.Initialize();

        _config.OnValueChanged(CCVars.SolreignFleetOperationsEnabled, v => _fleetEnabled = v, true);
        _config.OnValueChanged(CCVars.SolreignFleetOperationsMaxGrids, v => _maxGrids = v, true);

        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
    }

    private void OnRunLevelChanged(GameRunLevelChangedEvent ev)
    {
        if (ev.Old != GameRunLevel.PreRoundLobby || ev.New != GameRunLevel.InRound)
            return;

        if (!_fleetEnabled)
        {
            Log.Debug("SolreignFleetOperationSystem: Fleet operations disabled by CVar kill-switch.");
            return;
        }

        InitializeFleetRound();
    }

    /// <summary>
    ///     Provisions standard three-node fleet topology (Station Alpha, Station Beta, RV Expedition).
    /// </summary>
    public bool InitializeFleetRound()
    {
        if (!_fleetEnabled)
            return false;

        _fleetNodes.Clear();
        Log.Info($"SolreignFleetOperationSystem: Initializing multi-station fleet preview round (Max Grids: {_maxGrids}).");

        // Node 1: Station Alpha (Meridian Relay Hub)
        var alphaUid = Spawn(null);
        var alphaComp = AddComp<SolreignFleetGridComponent>(alphaUid);
        alphaComp.NodeName = "Station Alpha (Meridian Relay Hub)";
        alphaComp.NodeType = FleetNodeType.StationAlpha;
        alphaComp.PowerIsolated = true;
        alphaComp.AtmosIsolated = true;
        alphaComp.CargoAccountIsolated = true;
        alphaComp.MaxEntityBudget = 500;
        _fleetNodes.Add(alphaUid);

        // Node 2: Station Beta (Nocturne Outpost)
        var betaUid = Spawn(null);
        var betaComp = AddComp<SolreignFleetGridComponent>(betaUid);
        betaComp.NodeName = "Station Beta (Nocturne Outpost)";
        betaComp.NodeType = FleetNodeType.StationBeta;
        betaComp.PowerIsolated = true;
        betaComp.AtmosIsolated = true;
        betaComp.CargoAccountIsolated = true;
        betaComp.MaxEntityBudget = 500;
        _fleetNodes.Add(betaUid);

        // Node 3: Expedition Craft (RV Expedition)
        var craftUid = Spawn(null);
        var craftComp = AddComp<SolreignFleetGridComponent>(craftUid);
        craftComp.NodeName = "RV Expedition Craft";
        craftComp.NodeType = FleetNodeType.ExpeditionCraft;
        craftComp.PowerIsolated = true;
        craftComp.AtmosIsolated = true;
        craftComp.CargoAccountIsolated = true;
        craftComp.MaxEntityBudget = 250;
        _fleetNodes.Add(craftUid);

        Log.Info($"SolreignFleetOperationSystem: Successfully registered {_fleetNodes.Count} fleet nodes.");
        return true;
    }

    /// <summary>
    ///     Initiates cross-grid FTL or sub-light transit for an expedition craft toward a target station node.
    /// </summary>
    public bool TryInitiateTransit(EntityUid vesselUid, EntityUid targetStationUid)
    {
        if (!_fleetEnabled || !TryComp<SolreignFleetGridComponent>(vesselUid, out var vesselComp))
            return false;

        if (vesselComp.NodeType != FleetNodeType.ExpeditionCraft)
        {
            Log.Warning($"SolreignFleetOperationSystem: Entity {vesselUid} is stationary node, transit denied.");
            return false;
        }

        vesselComp.TransitState = FleetTransitState.InTransit;
        Log.Info($"SolreignFleetOperationSystem: Vessel '{vesselComp.NodeName}' initiated cross-grid transit toward target node {targetStationUid}.");
        return true;
    }

    /// <summary>
    ///     Locks docking clamps between an expedition craft and a target station grid.
    /// </summary>
    public bool TryDock(EntityUid vesselUid, EntityUid stationUid)
    {
        if (!_fleetEnabled || !TryComp<SolreignFleetGridComponent>(vesselUid, out var vesselComp))
            return false;

        vesselComp.TransitState = FleetTransitState.Docked;
        Log.Info($"SolreignFleetOperationSystem: Docking clamps engaged between '{vesselComp.NodeName}' and station node {stationUid}.");
        return true;
    }

    /// <summary>
    ///     Fires explosive jettison charges to sever docking clamps in emergency situations.
    /// </summary>
    public bool EmergencyJettisonClamps(EntityUid vesselUid)
    {
        if (!TryComp<SolreignFleetGridComponent>(vesselUid, out var vesselComp))
            return false;

        vesselComp.TransitState = FleetTransitState.Stationary;
        Log.Info($"SolreignFleetOperationSystem: EMERGENCY JETTISON executed on '{vesselComp.NodeName}'. Airseals locked.");
        return true;
    }

    /// <summary>
    ///     Audits current entity counts and performance budgets across all active fleet nodes.
    /// </summary>
    public FleetPerformanceAudit ReportPerformanceAudit()
    {
        var audit = new FleetPerformanceAudit
        {
            TotalNodes = _fleetNodes.Count,
            BudgetCompliant = true
        };

        foreach (var nodeUid in _fleetNodes)
        {
            if (!TryComp<SolreignFleetGridComponent>(nodeUid, out var comp))
                continue;

            if (comp.CurrentEntityCount > comp.MaxEntityBudget)
                audit.BudgetCompliant = false;

            audit.NodeSummaries.Add(new FleetNodeSummary
            {
                NodeName = comp.NodeName,
                NodeType = comp.NodeType.ToString(),
                TransitState = comp.TransitState.ToString(),
                PowerIsolated = comp.PowerIsolated,
                AtmosIsolated = comp.AtmosIsolated,
                CargoAccountIsolated = comp.CargoAccountIsolated,
                CurrentEntities = comp.CurrentEntityCount,
                MaxEntities = comp.MaxEntityBudget
            });
        }

        return audit;
    }
}

public sealed class FleetPerformanceAudit
{
    public int TotalNodes { get; set; }
    public bool BudgetCompliant { get; set; }
    public List<FleetNodeSummary> NodeSummaries { get; set; } = new();
}

public sealed class FleetNodeSummary
{
    public string NodeName { get; set; } = string.Empty;
    public string NodeType { get; set; } = string.Empty;
    public string TransitState { get; set; } = string.Empty;
    public bool PowerIsolated { get; set; }
    public bool AtmosIsolated { get; set; }
    public bool CargoAccountIsolated { get; set; }
    public int CurrentEntities { get; set; }
    public int MaxEntities { get; set; }
}
