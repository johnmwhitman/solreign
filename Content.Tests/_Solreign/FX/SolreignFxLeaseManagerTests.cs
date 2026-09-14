#nullable enable
using System.Collections.Generic;
using Content.Client._Solreign.FX;
using Content.Shared._Solreign.FX;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.Tests._Solreign.FX;

/// <summary>
///     Spec §3.1's unified atomic lease (grk #1/cdx #9) and client per-frame intake cap (cdx #10).
///     Covers the mission's explicitly-named "lease atomicity" and "intake cap under flood" rows.
/// </summary>
[TestFixture]
[TestOf(typeof(SolreignFxLeaseManager))]
public sealed class SolreignFxLeaseManagerTests
{
    private static SolreignFxAnchorKey Anchor(int id) => SolreignFxAnchorKey.FromEntity(new NetEntity(id));

    private static SolreignFxLeaseManager Make(int capacity, out SolreignFxPool pool, SolreignFxCategory category = SolreignFxCategory.ImpactLight)
    {
        pool = new SolreignFxPool(new Dictionary<SolreignFxCategory, int> { [category] = capacity });
        return new SolreignFxLeaseManager(pool);
    }

    [Test]
    public void TryAcquire_Success_GrantsAllResourceCountsFromCategoryDefaults()
    {
        var manager = Make(4, out _, SolreignFxCategory.ImpactHeavy);
        manager.IntakeCapPerFrame = 32;

        var ok = manager.TryAcquire(SolreignFxCategory.ImpactHeavy, "impact_heavy", Anchor(1), 0.0, out var lease, out var reason);

        Assert.That(ok, Is.True);
        Assert.That(reason, Is.EqualTo(SolreignFxLeaseManager.AcquireFailureReason.None));

        var defaults = SolreignFxCategoryTable.GetDefaults(SolreignFxCategory.ImpactHeavy);
        Assert.Multiple(() =>
        {
            Assert.That(lease.EntitiesGranted, Is.EqualTo(defaults.EntitiesPerCue));
            Assert.That(lease.LightsGranted, Is.EqualTo(defaults.LightsPerCue));
            Assert.That(lease.OverlayGranted, Is.EqualTo(defaults.HasOverlayInstance));
        });
    }

    [Test]
    public void TryAcquire_ZeroCapacityCategory_NeverPartiallyGrants()
    {
        var manager = Make(0, out _, SolreignFxCategory.Transformation);

        var ok = manager.TryAcquire(SolreignFxCategory.Transformation, "transformation_generic", Anchor(1), 0.0, out var lease, out var reason);

        Assert.That(ok, Is.False);
        Assert.That(reason, Is.EqualTo(SolreignFxLeaseManager.AcquireFailureReason.CategoryHasNoCapacity));
        Assert.That(lease, Is.EqualTo(default(SolreignFxLeaseManager.Lease)), "a failed acquire must never leave a half-populated lease struct behind");
    }

    [Test]
    public void TryAcquire_IntakeCapExceeded_DropsFreshActivationsButNotMerges()
    {
        var manager = Make(10, out _, SolreignFxCategory.Dust);
        manager.IntakeCapPerFrame = 2;

        Assert.That(manager.TryAcquire(SolreignFxCategory.Dust, "dust", Anchor(1), 0.0, out _, out _), Is.True);
        Assert.That(manager.TryAcquire(SolreignFxCategory.Dust, "dust", Anchor(2), 0.0, out _, out _), Is.True);
        Assert.That(manager.IntakeUsedThisFrameForTests, Is.EqualTo(2));

        // Third BRAND-NEW anchor this frame exceeds the cap of 2 -> dropped.
        var thirdOk = manager.TryAcquire(SolreignFxCategory.Dust, "dust", Anchor(3), 0.0, out _, out var thirdReason);
        Assert.That(thirdOk, Is.False);
        Assert.That(thirdReason, Is.EqualTo(SolreignFxLeaseManager.AcquireFailureReason.IntakeCapExceeded));

        // But re-triggering an ALREADY-active anchor (a merge) must still succeed even though the
        // intake cap for fresh activations is exhausted (spec §3: merges are an extension, not new
        // churn) — this is the "coalesced, not dropped" half of cdx #10's fix.
        var mergeOk = manager.TryAcquire(SolreignFxCategory.Dust, "dust", Anchor(1), 1.0, out _, out var mergeReason);
        Assert.That(mergeOk, Is.True);
        Assert.That(mergeReason, Is.EqualTo(SolreignFxLeaseManager.AcquireFailureReason.None));
        Assert.That(manager.IntakeUsedThisFrameForTests, Is.EqualTo(2), "a merge must not consume additional intake budget");
    }

    [Test]
    public void BeginFrame_ResetsIntakeCounterForTheNextFrame()
    {
        var manager = Make(10, out _, SolreignFxCategory.Dust);
        manager.IntakeCapPerFrame = 1;

        Assert.That(manager.TryAcquire(SolreignFxCategory.Dust, "dust", Anchor(1), 0.0, out _, out _), Is.True);
        Assert.That(manager.TryAcquire(SolreignFxCategory.Dust, "dust", Anchor(2), 0.0, out _, out var reason), Is.False);
        Assert.That(reason, Is.EqualTo(SolreignFxLeaseManager.AcquireFailureReason.IntakeCapExceeded));

        manager.BeginFrame();

        Assert.That(manager.IntakeUsedThisFrameForTests, Is.EqualTo(0));
        Assert.That(manager.TryAcquire(SolreignFxCategory.Dust, "dust", Anchor(2), 1.0, out _, out var reasonAfterReset), Is.True);
        Assert.That(reasonAfterReset, Is.EqualTo(SolreignFxLeaseManager.AcquireFailureReason.None));
    }

    [Test]
    public void Release_ReturnsTheSlotToThePool()
    {
        var manager = Make(1, out var pool, SolreignFxCategory.ImpactLight);
        manager.TryAcquire(SolreignFxCategory.ImpactLight, "impact_light", Anchor(1), 0.0, out var lease, out _);
        Assert.That(pool.ActiveCount(SolreignFxCategory.ImpactLight), Is.EqualTo(1));

        manager.Release(lease);

        Assert.That(pool.ActiveCount(SolreignFxCategory.ImpactLight), Is.EqualTo(0));
    }

    [Test]
    public void TryAcquire_FloodOfDistinctAnchors_NeverExceedsIntakeCapInOneFrame()
    {
        var manager = Make(1000, out _, SolreignFxCategory.ImpactLight);
        manager.IntakeCapPerFrame = 32;

        var grantedFresh = 0;
        for (var i = 0; i < 500; i++)
        {
            if (manager.TryAcquire(SolreignFxCategory.ImpactLight, "impact_light", Anchor(i), 0.0, out _, out _))
                grantedFresh++;
        }

        Assert.That(grantedFresh, Is.EqualTo(32), "a flood of 500 distinct anchors in one frame must be capped to the intake limit, regardless of how large the pool itself is");
    }
}
