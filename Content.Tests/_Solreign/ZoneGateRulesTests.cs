using Content.Server._Solreign.Zones;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Coverage for <see cref="SolreignZoneGateRules"/> — the pure decision logic extracted out of
///     <see cref="SolreignZoneGateSystem"/> (Phase2 B1, "ZONES was the only untested subsystem").
///     Three surfaces: the entry-gate access check (Sublevel H's AccessReader lock), the return-gate
///     "do you have a trip recorded to go back to" check, and the lazy-load cache-validity check
///     shared by <c>EnsureHellZoneLoaded</c> and <c>EnsureHeavenZoneLoaded</c>.
///
///     This file is pure boolean-algebra coverage only — no ECS, no grid loading. The actual
///     teleport behavior (does the traveler really land on zone_hell.yml/zone_heaven.yml, does the
///     cache genuinely get reused) is covered separately by
///     Content.IntegrationTests/Tests/_Solreign/SolreignZoneGateIntegrationTest.cs, which drives a
///     real server pair.
/// </summary>
[TestFixture]
[TestOf(typeof(SolreignZoneGateRules))]
public sealed class ZoneGateRulesTests
{
    // --- IsEntryAllowed ---

    [Test]
    public void IsEntryAllowed_NoAccessReader_AlwaysAdmits()
    {
        // An unlocked gate (e.g. SolreignHeavenZoneLadder, which never carries an AccessReader at
        // all) admits regardless of whatever "isAllowed" value the caller happens to have computed
        // — it never should have been gated in the first place.
        Assert.Multiple(() =>
        {
            Assert.That(SolreignZoneGateRules.IsEntryAllowed(hasAccessReader: false, isAllowed: false), Is.True);
            Assert.That(SolreignZoneGateRules.IsEntryAllowed(hasAccessReader: false, isAllowed: true), Is.True);
        });
    }

    [Test]
    public void IsEntryAllowed_GatedAndAllowed_Admits()
    {
        // The Sublevel H gate with a valid Crimson Keycard presented.
        Assert.That(SolreignZoneGateRules.IsEntryAllowed(hasAccessReader: true, isAllowed: true), Is.True);
    }

    [Test]
    public void IsEntryAllowed_GatedAndDenied_Refuses()
    {
        // The Sublevel H gate with no keycard (or the wrong one) presented.
        Assert.That(SolreignZoneGateRules.IsEntryAllowed(hasAccessReader: true, isAllowed: false), Is.False);
    }

    // --- CanUseReturnGate ---

    [Test]
    public void CanUseReturnGate_HasReturnRecord_Admits()
    {
        Assert.That(SolreignZoneGateRules.CanUseReturnGate(hasReturnRecord: true), Is.True);
    }

    [Test]
    public void CanUseReturnGate_NoReturnRecord_Refuses()
    {
        // Someone who wandered onto zone_heaven.yml/zone_hell.yml without ever stepping through the
        // Sublevel H entry gate (e.g. an admin teleport straight there) has nowhere recorded to go
        // back to.
        Assert.That(SolreignZoneGateRules.CanUseReturnGate(hasReturnRecord: false), Is.False);
    }

    // --- IsZoneCacheValid ---

    [Test]
    public void IsZoneCacheValid_NeverLoaded_NeedsFreshLoad()
    {
        // First traveler of the round: nothing cached yet.
        Assert.That(SolreignZoneGateRules.IsZoneCacheValid(
            hasCachedMap: false, hasCachedGrid: false, gridDeleted: false, gridTerminating: false), Is.False);
    }

    [Test]
    public void IsZoneCacheValid_CachedAndAlive_IsReused()
    {
        // Second (and every subsequent) traveler of the round: reuse the same map/grid.
        Assert.That(SolreignZoneGateRules.IsZoneCacheValid(
            hasCachedMap: true, hasCachedGrid: true, gridDeleted: false, gridTerminating: false), Is.True);
    }

    [Test]
    public void IsZoneCacheValid_GridDeleted_NeedsFreshLoad()
    {
        // Covers an admin nuking the zone (or a round-restart teardown) deleting the grid out from
        // under the cached reference — the next activation must not hand back a dead EntityUid.
        Assert.That(SolreignZoneGateRules.IsZoneCacheValid(
            hasCachedMap: true, hasCachedGrid: true, gridDeleted: true, gridTerminating: false), Is.False);
    }

    [Test]
    public void IsZoneCacheValid_GridTerminating_NeedsFreshLoad()
    {
        // Mid-deletion (QueueDel fired, not yet Deleted) must also be treated as unusable.
        Assert.That(SolreignZoneGateRules.IsZoneCacheValid(
            hasCachedMap: true, hasCachedGrid: true, gridDeleted: false, gridTerminating: true), Is.False);
    }

    [Test]
    public void IsZoneCacheValid_MapCachedButGridMissing_NeedsFreshLoad()
    {
        // Shouldn't happen in practice (both fields are only ever assigned together) but the rule
        // must not treat a half-populated cache as valid.
        Assert.That(SolreignZoneGateRules.IsZoneCacheValid(
            hasCachedMap: true, hasCachedGrid: false, gridDeleted: false, gridTerminating: false), Is.False);
    }

    [Test]
    public void IsZoneCacheValid_GridCachedButMapMissing_NeedsFreshLoad()
    {
        Assert.That(SolreignZoneGateRules.IsZoneCacheValid(
            hasCachedMap: false, hasCachedGrid: true, gridDeleted: false, gridTerminating: false), Is.False);
    }
}
