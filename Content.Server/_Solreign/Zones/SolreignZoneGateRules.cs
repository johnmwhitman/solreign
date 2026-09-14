namespace Content.Server._Solreign.Zones;

/// <summary>
///     Pure, unit-testable decision logic backing <see cref="SolreignZoneGateSystem"/> — Phase2 B1,
///     closing ZONES' zero-coverage gap. No ECS, no I/O — kept dependency-free (the same
///     <c>ContractRules</c>/<c>SolreignLapTrackerMath</c> precedent: extract the actual branching
///     logic to a static class so NUnit can exercise every branch without spinning up the game) so
///     <c>ZoneGateRulesTests</c> can pin every decision down directly. The system itself stays a thin
///     ECS shell: gather the booleans off its components/dependencies, hand them to these
///     predicates, act on the answer. Behavior is unchanged from before this extraction — every
///     method here is a straight lift of an <c>if</c> condition that used to live inline in
///     <see cref="SolreignZoneGateSystem"/>.
/// </summary>
public static class SolreignZoneGateRules
{
    /// <summary>
    ///     May a traveler pass an entry gate (e.g. the Sublevel H gate)? An unlocked gate (no
    ///     <c>AccessReaderComponent</c> at all — <paramref name="hasAccessReader"/> is false) always
    ///     admits, regardless of <paramref name="isAllowed"/>. A locked gate admits only when the
    ///     caller's own access check came back true. Mirrors
    ///     <see cref="SolreignZoneGateSystem"/>.OnEntryActivate's original
    ///     <c>HasComp&lt;AccessReaderComponent&gt;(uid) &amp;&amp; !IsAllowed(...)</c> short-circuit
    ///     exactly — the caller is expected to have already skipped the (possibly expensive)
    ///     <c>IsAllowed</c> check when <paramref name="hasAccessReader"/> is false, same as before.
    /// </summary>
    public static bool IsEntryAllowed(bool hasAccessReader, bool isAllowed)
    {
        return !hasAccessReader || isAllowed;
    }

    /// <summary>
    ///     May a traveler use a return gate (Sublevel H's exit shaft, or the Atrium departure lift)?
    ///     Only if they are carrying a recorded return trip (<see cref="SolreignZoneReturnComponent"/>,
    ///     stamped by the entry gate). Arriving at a return gate with nothing recorded to return
    ///     from — e.g. an admin teleport straight onto zone_hell.yml/zone_heaven.yml — is always
    ///     refused.
    /// </summary>
    public static bool CanUseReturnGate(bool hasReturnRecord)
    {
        return hasReturnRecord;
    }

    /// <summary>
    ///     Is a lazily-loaded zone's cached map/grid still good to reuse, or does
    ///     EnsureHellZoneLoaded/EnsureHeavenZoneLoaded need to load a fresh instance? Valid only when
    ///     both a map and a grid were previously cached AND the grid entity hasn't since been
    ///     deleted or queued for deletion out from under us (e.g. an admin nuking the zone, or a
    ///     round-restart teardown racing a late activation). A half-populated cache (one field set,
    ///     the other not — shouldn't happen in practice, since both fields are only ever assigned
    ///     together) is never treated as valid.
    /// </summary>
    public static bool IsZoneCacheValid(bool hasCachedMap, bool hasCachedGrid, bool gridDeleted, bool gridTerminating)
    {
        return hasCachedMap && hasCachedGrid && !gridDeleted && !gridTerminating;
    }
}
