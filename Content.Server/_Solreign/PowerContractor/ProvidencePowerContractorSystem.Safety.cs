using System;
using Content.Server.Power.Components;
using Content.Server.Power.Pow3r;
using Robust.Shared.GameObjects;

namespace Content.Server._Solreign.PowerContractor;

/// <summary>SPEC-ai-npc-phase1-v2.md §7.4-§7.5: TerminatingOrDeleted reconciliation, concurrency-cap
/// accounting, and the implicit-recall paths (destruction, grid removal, round cleanup).</summary>
public sealed partial class ProvidencePowerContractorSystem
{
    /// <summary>
    ///     Re-resolves a zone's target network fresh from its anchor's discharger EVERY call -- never
    ///     cached across ticks. Node-group remakes (<c>PowerNet.AfterRemake</c>) recreate the
    ///     underlying <c>PowerState.Network</c> object on topology changes, so a long-lived cached
    ///     reference would silently stop tracking the real network. Returns false if the anchor is
    ///     gone or no longer wired to an HV network -- the "TargetNetwork resolution fails" trigger
    ///     for the grid-removal implicit-recall path (§5).
    /// </summary>
    private bool TryResolveZoneNetwork(ProvidencePowerContractorZone zone, out PowerState.Network network)
    {
        network = null!;

        if (TerminatingOrDeleted(zone.AnchorUid))
            return false;

        if (!TryComp(zone.AnchorUid, out BatteryDischargerComponent? discharger))
            return false;

        if (discharger.Net is not { } net)
            return false;

        network = net.NetworkNode;
        return true;
    }

    /// <summary>True if the anchor is still alive and anchored to a live grid.</summary>
    private bool TryGetAnchorGrid(EntityUid anchorUid, out EntityUid gridUid)
    {
        gridUid = default;

        if (!TryComp(anchorUid, out TransformComponent? xform) || xform.GridUid is not { } grid)
            return false;

        if (TerminatingOrDeleted(grid))
            return false;

        gridUid = grid;
        return true;
    }

    /// <summary>
    ///     §7.4: every monitor pass, any zone whose live contractor has actually terminated (killed,
    ///     gibbed, exploded -- destroyed by something other than this system's own <c>RecallZone</c>
    ///     call) is treated as an implicit recall: release the cap slot and start the cooldown,
    ///     exactly as a clean recall would (§5's "Destroyed mid-lease" note). The entity's own
    ///     <c>PowerSupplierShutdown</c> handler already freed the solver-side supply slot by the time
    ///     this runs -- this method only handles OUR bookkeeping (cap, cooldown, zone state).
    /// </summary>
    private void ReconcileTerminatedContractors()
    {
        var now = _timing.CurTime;

        foreach (var zone in _zones.Values)
        {
            if (zone.ContractorUid is not { } contractorUid)
                continue;

            if (!TerminatingOrDeleted(contractorUid))
                continue;

            Log.Info($"providence.power_contractor: contractor {ToPrettyString(contractorUid)} for anchor {ToPrettyString(zone.AnchorUid)} was terminated externally -- treating as implicit recall");

            zone.ContractorUid = null;
            ReleaseCapSlot();
            zone.Gate.MarkDeleted(now, TimeSpan.FromSeconds(_redispatchCooldownSeconds));
        }
    }

    /// <summary>
    ///     Directed <c>EntityTerminatingEvent</c> handler -- the fast path for the same
    ///     implicit-recall accounting as <see cref="ReconcileTerminatedContractors"/>, firing the
    ///     moment the entity starts terminating rather than waiting up to one monitor interval.
    ///     Idempotent with the polling reconciler: both check <c>zone.ContractorUid</c> before
    ///     acting, so whichever runs first wins and the other becomes a no-op.
    /// </summary>
    private void OnContractorTerminating(EntityUid uid, ProvidencePowerContractorComponent component, ref EntityTerminatingEvent args)
    {
        if (!_zones.TryGetValue(component.AnchorUid, out var zone))
            return;

        if (zone.ContractorUid != uid)
            return;

        zone.ContractorUid = null;
        ReleaseCapSlot();
        zone.Gate.MarkDeleted(_timing.CurTime, TimeSpan.FromSeconds(_redispatchCooldownSeconds));
    }

    private void ReleaseCapSlot()
    {
        if (_liveContractorCount > 0)
            _liveContractorCount--;
    }

    /// <summary>§7.5's round-cleanup requirement: force every zone holding a live lease into
    /// RecallPending (and, since this build's recall sequencing is synchronous, all the way through
    /// to Deleted) rather than waiting for each zone's own monitor cadence to notice the round
    /// ended.
    ///
    /// Review finding #6: cleanup eligibility is driven by <c>zone.ContractorUid</c>/the gate's
    /// CURRENT phase, not gated behind <c>TryForceRecall()</c>'s own idempotency check. A gate that
    /// got stuck mid-recall from some earlier abnormal exit (e.g. an exception between
    /// <c>MarkRecalling()</c> and the ClearNet()+delete that follows it in <see cref="RecallZone"/>)
    /// would otherwise report "already recalling" on every subsequent pass and silently abandon the
    /// ownership record forever -- never actually calling ClearNet() or deleting the entity, and never
    /// releasing its concurrency-cap slot. <see cref="RecallZone"/> itself is idempotent and safe to
    /// call regardless of the gate's current phase (its own null-ContractorUid short-circuit, plus
    /// <c>MarkDeleted</c>'s unconditional phase reset, guarantee that).</summary>
    private void ForceRecallAllZones()
    {
        var now = _timing.CurTime;

        foreach (var (anchorUid, zone) in _zones)
        {
            if (zone.ContractorUid is null && zone.Gate.Phase == ProvidencePowerContractorPhase.Dormant)
                continue; // true no-op: nothing live, nothing pending

            // Best-effort phase bookkeeping for observability/logging -- deliberately NOT gating the
            // actual finalize call below on its result.
            zone.Gate.TryForceRecall();
            RecallZone(anchorUid, zone, now);
        }
    }

    /// <summary>
    ///     Round-restart / feature-disabled reset: force-recall anything live, then drop every zone
    ///     entirely so the next round (or the next time the feature is re-enabled) starts with zero
    ///     tracked state -- the "zero surviving state on round restart" guarantee. Also clears the
    ///     review finding #4 grid-cooldown-continuity memory, which must not survive a round restart
    ///     any more than a zone itself does.
    /// </summary>
    private void HardResetAllZones()
    {
        ForceRecallAllZones();
        _zones.Clear();
        _gridCooldownMemory.Clear();
        _liveContractorCount = 0;
    }
}
