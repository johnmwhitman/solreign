using System;
using System.Collections.Generic;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Server.Power.NodeGroups;
using Content.Server.Power.Nodes;
using Content.Server.Power.Pow3r;
using Content.Shared.Atmos.Components;
using Content.Shared.NodeContainer;
using Content.Shared.NodeContainer.NodeGroups;
using Content.Shared.Physics;
using Content.Shared.Spawning;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Server._Solreign.PowerContractor;

/// <summary>SPEC-ai-npc-phase1-v2.md §5-§7: dispatch (spawn validation + ring search), the
/// per-tick zone advance loop, and recall sequencing.</summary>
public sealed partial class ProvidencePowerContractorSystem
{
    /// <summary>
    ///     One monitor sample for every tracked zone (§4.5). §5's Dispatching/RecallPending/Recalling
    ///     phases never persist across ticks in this build: <see cref="DispatchZone"/> and
    ///     <see cref="RecallZone"/> both resolve synchronously within the same tick that triggers
    ///     them, since the underlying operations (spawn, ClearNet()) are atomic component/anchoring
    ///     calls with nothing to wait on (§5's "why no DoAfter wait" note applies equally to both
    ///     directions).
    /// </summary>
    private void AdvanceZones(TimeSpan now)
    {
        // Review finding #3: order the public station's zones first so that, when the concurrency
        // cap is contested by zones belonging to different stations in the same tick, the real/public
        // station always gets first crack at a scarce cap slot -- never a smaller secondary/event
        // station. A round with zero stations (or exactly one) makes this a stable no-op sort.
        var publicStation = ResolvePublicStation();
        var anchors = new List<EntityUid>(_zones.Keys);

        if (publicStation is not null)
        {
            anchors.Sort((a, b) =>
            {
                // A deleted anchor has no transform and GetOwningStation THROWS on it (box log
                // 2026-08-02: this comparer died every monitor tick once one tracked SMES was
                // deleted mid-round). It owns no station, so it sorts non-public — and it stays
                // in the list so the loop below finalizes its zone via the implicit-recall path.
                var aPublic = !Deleted(a) && _station.GetOwningStation(a) == publicStation;
                var bPublic = !Deleted(b) && _station.GetOwningStation(b) == publicStation;
                return aPublic == bPublic ? 0 : aPublic ? -1 : 1;
            });
        }

        // Snapshot the key list: RecallZone/DispatchZone never structurally mutate _zones today, but
        // iterating a live Dictionary while a per-entry callback runs is a standing footgun worth
        // guarding defensively, and this loop also removes stale entries directly.
        foreach (var anchorUid in anchors)
        {
            if (!_zones.TryGetValue(anchorUid, out var zone))
                continue;

            // Review finding #3: an anchor's grid can lose station membership mid-round (a shuttle
            // undocking, a grid split moving the SMES off the station) exactly as readily as it can
            // lose network resolution -- re-validate every tick, not just at discovery.
            if (!TryResolveZoneNetwork(zone, out var network)
                || !TryGetAnchorGrid(anchorUid, out var gridUid)
                || !TryGetAnchorStation(anchorUid, out var stationUid))
            {
                // §5 "Grid removal" implicit-recall path: the anchor's TargetNetwork/station
                // resolution failed. If a lease is live (or the gate is mid-transition), unconditionally
                // finalize it -- review finding #6: cleanup must be driven by zone.ContractorUid/phase,
                // not gated behind TryForceRecall's own idempotency check, or a gate stuck mid-recall
                // from an earlier abnormal exit could silently abandon the ownership record forever
                // without ever calling ClearNet()/deleting the entity. Otherwise there's nothing to
                // protect; if a redispatch cooldown is still running, review finding #4 transplants it
                // onto the same physical grid so a replaced/destroyed anchor doesn't silently forget it.
                if (zone.ContractorUid is not null || zone.Gate.Phase != ProvidencePowerContractorPhase.Dormant)
                {
                    zone.Gate.TryForceRecall();
                    RecallZone(anchorUid, zone, now);
                }
                else
                {
                    if (zone.Gate.InCooldown(now) && zone.LastKnownGridUid is { } lastGrid)
                        _gridCooldownMemory[lastGrid] = zone.Gate.CooldownDeadline;

                    _zones.Remove(anchorUid);
                }

                continue;
            }

            zone.LastKnownGridUid = gridUid;

            switch (zone.Gate.Phase)
            {
                case ProvidencePowerContractorPhase.Dormant:
                {
                    var gapHeld = EvaluateDeficitAndReserve(zone, network) && !EvaluateHumanCoverage(stationUid);
                    var signal = zone.Gate.SampleDormant(gapHeld, now, _hysteresisTicks);

                    if (signal == ProvidenceGateSignal.Dispatch)
                        DispatchZone(anchorUid, zone, network, now);

                    break;
                }

                case ProvidencePowerContractorPhase.Supplying:
                {
                    // Review finding #4 (split half): a topology remake can move the contractor's
                    // ACTUAL supply onto a different network fragment than the one this zone's anchor
                    // still resolves to. Only trigger on positive evidence of a mismatch (supplier.Net
                    // is non-null AND different) -- a freshly-dispatched contractor's Net can
                    // legitimately still be null for a few ticks while node-group connectivity catches
                    // up (see the acceptance test's own "let real node-group connectivity actually
                    // place the supplier" wait), and that must never be misread as a split.
                    if (zone.ContractorUid is { } contractorUid
                        && TryComp(contractorUid, out PowerSupplierComponent? splitSupplier)
                        && splitSupplier.Net is { } splitNet
                        && !ReferenceEquals(splitNet.NetworkNode, network))
                    {
                        Log.Info($"providence.power_contractor: topology split detected for zone anchor {ToPrettyString(anchorUid)} -- contractor {ToPrettyString(contractorUid)} now supplies a different network than the one being monitored, forcing recall");
                        RecallZone(anchorUid, zone, now);
                        break;
                    }

                    var gapHeld = EvaluateDeficitAndReserve(zone, network) && !EvaluateHumanCoverage(stationUid);
                    var signal = zone.Gate.SampleSupplying(gapHeld, now, _hysteresisTicks);

                    if (signal == ProvidenceGateSignal.Recall)
                        RecallZone(anchorUid, zone, now);

                    break;
                }

                default:
                    // Dispatching/RecallPending/Recalling: transient, already resolved synchronously
                    // by the call that entered them. Nothing to do on a later tick.
                    break;
            }
        }
    }

    /// <summary>
    ///     §5 Dormant -&gt; Dispatching -&gt; Supplying (or fail clean back to Dormant). Enforces the
    ///     §7.5 concurrency cap as a gating condition on the transition actually completing (the pure
    ///     <see cref="ProvidencePowerContractorGate"/> has no visibility into the system-wide live
    ///     count by design -- see that class's doc comment on why the cap lives here instead).
    /// </summary>
    private void DispatchZone(EntityUid anchorUid, ProvidencePowerContractorZone zone, PowerState.Network network, TimeSpan now)
    {
        if (_liveContractorCount >= _concurrencyCap)
        {
            Log.Info($"providence.power_contractor: concurrency cap ({_concurrencyCap}) reached, zone anchor {ToPrettyString(anchorUid)} stays Dormant");
            zone.Gate.MarkDispatchFailed();
            return;
        }

        if (!TryFindAndSpawnContractor(anchorUid, network, out var contractor))
        {
            Log.Info($"providence.power_contractor: spawn validation failed for zone anchor {ToPrettyString(anchorUid)} -- no valid DeployTile within ring radius {_spawnRingRadius}, staying Dormant, retrying next monitor tick");
            zone.Gate.MarkDispatchFailed();
            return;
        }

        var contractorComp = EnsureComp<ProvidencePowerContractorComponent>(contractor);
        contractorComp.AnchorUid = anchorUid;

        if (TryComp(contractor, out PowerSupplierComponent? supplier))
            supplier.MaxSupply = _leaseWatts;

        zone.ContractorUid = contractor;
        zone.Gate.MarkSupplying(now, TimeSpan.FromSeconds(_leaseTtlSeconds));
        _liveContractorCount++;

        // Deterministic canned deploy line -- §11 (MiniMax/voice wiring) is out of scope for this
        // cut; this is a plain log line, not a call into ProvidenceVoiceSystem, so the feature is
        // fully functional with zero dependency on the council-held LLM lane.
        Log.Info($"providence.power_contractor: dispatched contractor {ToPrettyString(contractor)} to zone anchor {ToPrettyString(anchorUid)} (lease_watts={_leaseWatts}, ttl_seconds={_leaseTtlSeconds})");
    }

    /// <summary>
    ///     §5 Supplying -&gt; RecallPending -&gt; Recalling -&gt; Deleted, collapsed into one
    ///     synchronous call. Ordering is asserted, not incidental: <c>ClearNet()</c> fires (§3.3)
    ///     before the entity is queued for deletion, so a caller reading network membership
    ///     immediately after this call already sees the supply gone.
    /// </summary>
    private void RecallZone(EntityUid anchorUid, ProvidencePowerContractorZone zone, TimeSpan now)
    {
        var cooldown = TimeSpan.FromSeconds(_redispatchCooldownSeconds);

        if (zone.ContractorUid is not { } contractorUid)
        {
            // Nothing live (e.g. a forced recall on a zone that never actually dispatched) -- just
            // reset the gate and start the cooldown.
            zone.Gate.MarkDeleted(now, cooldown);
            return;
        }

        zone.Gate.MarkRecalling();

        // §3.3/§7: synchronous, explicit ClearNet() BEFORE despawn -- double-guarded together with
        // the unconditional PowerSupplierShutdown cleanup that fires on delete below (safe no-op if
        // already cleared, per BaseNetConnectorComponent.ClearNet()'s own _net != null guard).
        if (TryComp(contractorUid, out PowerSupplierComponent? supplier))
            supplier.ClearNet();

        Log.Info($"providence.power_contractor: recalling contractor {ToPrettyString(contractorUid)} from zone anchor {ToPrettyString(anchorUid)}");

        if (!TerminatingOrDeleted(contractorUid))
            QueueDel(contractorUid);

        zone.ContractorUid = null;
        ReleaseCapSlot();
        zone.Gate.MarkDeleted(now, cooldown);
    }

    /// <summary>
    ///     §7.2: ring-search (bounded by <c>solreign.power_contractor.spawn_ring_radius</c>) for the
    ///     first tile adjacent to the anchor that (a) already has an HV cable on the SAME target
    ///     network -- required for the contractor's own CableDeviceNode to actually join it
    ///     (§3.2/CableDeviceNode.GetReachableNodes' same-tile CableNode requirement) -- (b) isn't a
    ///     space tile, and (c) passes <c>SpawnIfUnobstructed</c>. Radius starts at 1, never 0: the
    ///     anchor's own tile is excluded per §7.1 ("never coincident with... any machinery entity") --
    ///     it's also guaranteed obstructed by the anchor's own collision anyway.
    /// </summary>
    private bool TryFindAndSpawnContractor(EntityUid anchorUid, PowerState.Network network, out EntityUid contractor)
    {
        contractor = default;

        if (!TryComp(anchorUid, out TransformComponent? anchorXform))
            return false;

        if (anchorXform.GridUid is not { } gridUid || !TryComp(gridUid, out MapGridComponent? grid))
            return false;

        if (TerminatingOrDeleted(gridUid))
            return false;

        var gridEntity = new Entity<MapGridComponent>(gridUid, grid);
        var centerTile = _mapSystem.TileIndicesFor(gridEntity, anchorXform.Coordinates);
        var nodeQuery = GetEntityQuery<NodeContainerComponent>();

        // Atmos sanity (§7.2) only applies when the grid actually simulates atmosphere --
        // AtmosphereSystem.IsTileSpace defaults to "is space" (true) when nothing handles the query
        // at all (its own doc comment), which would wrongly reject every candidate tile on a grid
        // with no GridAtmosphereComponent rather than correctly signal "no data available."
        var hasAtmosphere = HasComp<GridAtmosphereComponent>(gridUid);

        for (var radius = 1; radius <= _spawnRingRadius; radius++)
        {
            foreach (var offset in RingOffsets(radius))
            {
                var candidateTile = centerTile + offset;

                if (!HasCableOnTargetNetwork(nodeQuery, gridEntity, candidateTile, network))
                    continue;

                if (hasAtmosphere && _atmosphere.IsTileSpace((gridUid, null), null, candidateTile))
                    continue;

                var candidateCoords = _mapSystem.ToCoordinates(gridUid, candidateTile, grid);
                var spawned = EntityManager.SpawnIfUnobstructed(ContractorPrototypeId, candidateCoords, CollisionGroup.MobMask);

                if (spawned is not { } spawnedUid)
                    continue;

                contractor = spawnedUid;
                return true;
            }
        }

        return false;
    }

    /// <summary>True if <paramref name="tile"/> already carries a CableNode on the HV network
    /// identity-equal to <paramref name="targetNetwork"/> -- the same
    /// NodeContainerComponent-node-group walk <c>CableDeviceNode.GetReachableNodes</c> itself relies
    /// on, read-only here since nothing is being connected yet.</summary>
    private bool HasCableOnTargetNetwork(
        EntityQuery<NodeContainerComponent> nodeQuery,
        Entity<MapGridComponent> grid,
        Vector2i tile,
        PowerState.Network targetNetwork)
    {
        foreach (var node in NodeHelpers.GetNodesInTile(nodeQuery, grid, tile, _mapSystem))
        {
            if (node is not CableNode cable)
                continue;

            if (cable.NodeGroupID != NodeGroupID.HVPower)
                continue;

            if (cable.NodeGroup is IBasePowerNet net && ReferenceEquals(net.NetworkNode, targetNetwork))
                return true;
        }

        return false;
    }

    /// <summary>Chebyshev-distance square ring of tile offsets at exactly <paramref name="radius"/> --
    /// deterministic, bounded, no repeats across radii.</summary>
    private static IEnumerable<Vector2i> RingOffsets(int radius)
    {
        for (var x = -radius; x <= radius; x++)
        {
            yield return new Vector2i(x, radius);
            yield return new Vector2i(x, -radius);
        }

        for (var y = -radius + 1; y <= radius - 1; y++)
        {
            yield return new Vector2i(radius, y);
            yield return new Vector2i(-radius, y);
        }
    }
}
