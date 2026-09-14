using Content.Server.Power.Pow3r;
using Content.Shared.Ghost.Components;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Prototypes;

namespace Content.Server._Solreign.PowerContractor;

/// <summary>
///     SPEC-ai-npc-phase1-v2.md §4.3 (deficit+reserve) and §4.4 (session-&gt;mind-&gt;job human
///     coverage) predicates. Both are read-only: nothing here mutates a zone, a component, or a
///     network -- callers (<c>ProvidencePowerContractorSystem.cs</c>'s monitor loop) feed the
///     results into <see cref="ProvidencePowerContractorGate"/>.
/// </summary>
public sealed partial class ProvidencePowerContractorSystem
{
    /// <summary>
    ///     Qualified job set for Phase 1's power scope (§4.4, §10 Open Question 4 -- roster content
    ///     pending John's confirmation; StationEngineer/ChiefEngineer/AtmosphericTechnician are the
    ///     spec's own suggested starting set, not yet balance-reviewed).
    /// </summary>
    private static readonly ProtoId<JobPrototype>[] QualifiedJobs =
    {
        "StationEngineer",
        "ChiefEngineer",
        "AtmosphericTechnician",
    };

    /// <summary>
    ///     A reserve ratio at or below this floor is treated as an outright, dispatch-eligible
    ///     failure regardless of the "declining since last sample" check (review finding #1): once a
    ///     battery is genuinely exhausted, <c>reserveRatio &lt; previous</c> can never hold again
    ///     (0 &lt; 0 is false), which would otherwise silently and permanently disqualify the WORST
    ///     blackout case from ever dispatching. Zero itself, not an epsilon -- InStorageCurrent can
    ///     legitimately read exactly 0 for a fully-drained Pow3r battery.
    /// </summary>
    private const float ReserveExhaustedFloor = 0f;

    /// <summary>
    ///     §4.3: a network is a dispatch candidate only when BOTH the sustained-deficit and
    ///     declining-reserve conditions hold this sample. Updates <paramref name="zone"/>'s
    ///     <see cref="ProvidencePowerContractorZone.PreviousReserveRatio"/> as a side effect (the
    ///     "declining since last sample" comparison is inherently stateful) -- this is the one
    ///     detection method that isn't purely read-only, by design, since the "declining" half of the
    ///     predicate has no meaning without a previous sample to compare against.
    /// </summary>
    private bool EvaluateDeficitAndReserve(ProvidencePowerContractorZone zone, PowerState.Network network)
    {
        var stats = _powerNet.GetNetworkStatistics(network);

        // review finding #2: NetworkPowerStatistics.SupplyCurrent is actually
        // network.LastCombinedMaxSupply (PowerNetSystem.GetNetworkStatistics' own assignment) --
        // i.e. NAMEPLATE capacity, not what the Pow3r solver actually delivered this tick. Reading
        // network.LastCombinedSupply directly is the one field that reflects a ramp-limited or
        // otherwise under-delivering supplier actually failing to keep up, which is exactly the
        // scenario this system exists to detect.
        var deliveredSupply = network.LastCombinedSupply;
        var unmetWatts = stats.Consumption - deliveredSupply;

        // Condition 1: sustained deficit -- consumption exceeds ACTUAL delivered supply by more than
        // _deficitFraction of consumption, AND by more than an absolute floor (review finding #7:
        // without this, a near-zero consumer with zero supply reads as a 100% fractional deficit and
        // would summon a full-sized contractor for a trivial load). Guard against a demand-less
        // network (Consumption == 0) reading as an infinite/undefined deficit.
        var deficitHeld = stats.Consumption > 0f
            && unmetWatts > stats.Consumption * _deficitFraction
            && unmetWatts >= _minUnmetWatts;

        // Condition 2: reserve failure -- below the floor AND (already fully exhausted OR lower than
        // last sample). A network with no storage at all (InStorageMax == 0, e.g. no SMES feeding it)
        // can never satisfy this -- that's intentional: without a reserve to measure, "failing" has
        // no signal, so such a network is never dispatch-eligible on reserve grounds (deficit alone
        // isn't enough per §4.3 -- both conditions must hold).
        var reserveRatio = stats.InStorageMax > 0f ? stats.InStorageCurrent / stats.InStorageMax : 0f;
        var previous = zone.PreviousReserveRatio;
        var exhausted = reserveRatio <= ReserveExhaustedFloor;
        var reserveHeld = stats.InStorageMax > 0f
            && reserveRatio < _reserveFloorFraction
            && (exhausted || (previous is { } prev && reserveRatio < prev));

        zone.PreviousReserveRatio = reserveRatio;

        return deficitHeld && reserveHeld;
    }

    /// <summary>
    ///     §4.4: per-zone human coverage, scoped to the OWNING STATION rather than the anchor's own
    ///     grid (review finding #7's documented rule): true if at least one connected, non-ghost,
    ///     non-AFK, alive session whose mind holds a qualified job is currently anywhere on
    ///     <paramref name="stationUid"/> -- ANY of that station's member grids, not just the one the
    ///     failing SMES happens to sit on. An engineer working cargo's HV subnet on the station's
    ///     cargo-shuttle grid counts as coverage for the main station's power-monitor grid too,
    ///     because they're the same crew answering the same page. The rule stops at the station
    ///     boundary, though: an engineer physically present on a DIFFERENT (sibling) station's grid
    ///     does NOT count -- <see cref="ProvidencePowerContractorSystem.TryGetAnchorStation"/> already
    ///     ensures this predicate is only ever invoked with the CORRECT owning station for the zone
    ///     being sampled. Reuses <c>AntagSelectionSystem.GetActivePlayers</c> for the
    ///     connected/non-ghost filter rather than re-implementing it (§4.4.1's grounding).
    /// </summary>
    private bool EvaluateHumanCoverage(EntityUid stationUid)
    {
        foreach (var session in _antag.GetActivePlayers())
        {
            if (session.AttachedEntity is not { } mob)
                continue;

            // GetActivePlayers already excludes ghosts, but a session's AttachedEntity can change
            // between calls in the same tick in theory -- keep the check cheap and explicit rather
            // than trust the upstream filter blindly for a safety-relevant predicate.
            if (HasComp<GhostComponent>(mob))
                continue;

            if (_afk.IsAfk(session))
                continue;

            if (!_mobState.IsAlive(mob))
                continue;

            if (!TryComp(mob, out TransformComponent? xform) || _station.GetOwningStation(mob, xform) != stationUid)
                continue;

            if (!_mind.TryGetMind(mob, out var mindId, out _))
                continue;

            if (!_job.MindTryGetJobId(mindId, out var jobId) || jobId is not { } resolvedJob)
                continue;

            var qualified = false;
            foreach (var candidate in QualifiedJobs)
            {
                if (candidate == resolvedJob)
                {
                    qualified = true;
                    break;
                }
            }

            if (qualified)
                return true;
        }

        return false;
    }
}
