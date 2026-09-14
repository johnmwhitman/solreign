#nullable enable
using System.Collections.Generic;
using Content.Client._Solreign.FX;
using Content.Shared._Solreign.FX;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Tests._Solreign.FX;

/// <summary>
///     Spec §7's "Unit — budget pool" row: <see cref="SolreignFxPool"/>'s oldest-slot-recycle-at-
///     capacity behavior, merge-by-(EffectId, anchor), and per-category isolation (cdx #16) — pure
///     logic, no client/server harness needed, exactly as the spec names it.
/// </summary>
[TestFixture]
[TestOf(typeof(SolreignFxPool))]
public sealed class SolreignFxPoolTests
{
    private static SolreignFxAnchorKey EntityAnchor(uint id) => SolreignFxAnchorKey.FromEntity(new NetEntity((int) id));
    private static SolreignFxAnchorKey CoordAnchor(float x) => SolreignFxAnchorKey.FromCoordinates(new NetCoordinates(new NetEntity(1), x, 0f));

    private static SolreignFxPool MakePool(int capacity, SolreignFxCategory category = SolreignFxCategory.ImpactLight)
    {
        return new SolreignFxPool(new Dictionary<SolreignFxCategory, int> { [category] = capacity });
    }

    [Test]
    public void TryActivate_FreshSlot_UsesAnIdleSlotFirst()
    {
        var pool = MakePool(3);

        var ok = pool.TryActivate(SolreignFxCategory.ImpactLight, "impact_light", EntityAnchor(1), 0.0, out var slot, out var kind);

        Assert.That(ok, Is.True);
        Assert.That(kind, Is.EqualTo(SolreignFxPool.ActivationKind.Fresh));
        Assert.That(pool.ActiveCount(SolreignFxCategory.ImpactLight), Is.EqualTo(1));
        Assert.That(slot, Is.InRange(0, 2));
    }

    [Test]
    public void TryActivate_SameEffectIdAndAnchor_Merges()
    {
        var pool = MakePool(3);
        pool.TryActivate(SolreignFxCategory.ImpactLight, "impact_light", EntityAnchor(1), 0.0, out var firstSlot, out _);

        var ok = pool.TryActivate(SolreignFxCategory.ImpactLight, "impact_light", EntityAnchor(1), 1.0, out var secondSlot, out var kind);

        Assert.That(ok, Is.True);
        Assert.That(kind, Is.EqualTo(SolreignFxPool.ActivationKind.Merged));
        Assert.That(secondSlot, Is.EqualTo(firstSlot), "a repeat cue on the same (EffectId, anchor) must extend/refresh the SAME slot, never allocate a new one");
        Assert.That(pool.ActiveCount(SolreignFxCategory.ImpactLight), Is.EqualTo(1));
    }

    [Test]
    public void TryActivate_DifferentAnchor_SameEffectId_DoesNotMerge()
    {
        var pool = MakePool(3);
        pool.TryActivate(SolreignFxCategory.ImpactLight, "impact_light", EntityAnchor(1), 0.0, out var firstSlot, out _);

        var ok = pool.TryActivate(SolreignFxCategory.ImpactLight, "impact_light", EntityAnchor(2), 0.0, out var secondSlot, out var kind);

        Assert.That(ok, Is.True);
        Assert.That(kind, Is.EqualTo(SolreignFxPool.ActivationKind.Fresh));
        Assert.That(secondSlot, Is.Not.EqualTo(firstSlot));
        Assert.That(pool.ActiveCount(SolreignFxCategory.ImpactLight), Is.EqualTo(2));
    }

    [Test]
    public void TryActivate_CoordinateAnchors_CompareByExactPosition()
    {
        var pool = MakePool(3);
        pool.TryActivate(SolreignFxCategory.ImpactLight, "impact_light", CoordAnchor(5f), 0.0, out var firstSlot, out _);

        // Exact same position -> merge.
        var mergeOk = pool.TryActivate(SolreignFxCategory.ImpactLight, "impact_light", CoordAnchor(5f), 1.0, out var mergeSlot, out var mergeKind);
        Assert.That(mergeOk, Is.True);
        Assert.That(mergeKind, Is.EqualTo(SolreignFxPool.ActivationKind.Merged));
        Assert.That(mergeSlot, Is.EqualTo(firstSlot));

        // Different position, even by a hair -> fresh, never a fuzzy/epsilon match.
        var freshOk = pool.TryActivate(SolreignFxCategory.ImpactLight, "impact_light", CoordAnchor(5.0001f), 1.0, out var freshSlot, out var freshKind);
        Assert.That(freshOk, Is.True);
        Assert.That(freshKind, Is.EqualTo(SolreignFxPool.ActivationKind.Fresh));
        Assert.That(freshSlot, Is.Not.EqualTo(firstSlot));
    }

    [Test]
    public void TryActivate_AtCapacity_RecyclesTheOldestActivatedSlot()
    {
        var pool = MakePool(3);
        pool.TryActivate(SolreignFxCategory.ImpactLight, "impact_light", EntityAnchor(1), 10.0, out var slotA, out _);
        pool.TryActivate(SolreignFxCategory.ImpactLight, "impact_light", EntityAnchor(2), 20.0, out var slotB, out _); // oldest overall will be slotA (t=10)
        pool.TryActivate(SolreignFxCategory.ImpactLight, "impact_light", EntityAnchor(3), 5.0, out var slotC, out _); // actually oldest by time is this one (t=5)

        // Pool is now full (3/3). A brand-new, non-matching anchor must recycle the slot with the
        // SMALLEST ActivatedAtSeconds — slotC (t=5), not insertion order.
        var ok = pool.TryActivate(SolreignFxCategory.ImpactLight, "impact_light", EntityAnchor(4), 30.0, out var recycledSlot, out var kind);

        Assert.That(ok, Is.True);
        Assert.That(kind, Is.EqualTo(SolreignFxPool.ActivationKind.Recycled));
        Assert.That(recycledSlot, Is.EqualTo(slotC));
        Assert.That(pool.ActiveCount(SolreignFxCategory.ImpactLight), Is.EqualTo(3), "recycling reuses a slot, it never grows past capacity");

        // The recycled slot no longer matches its old (EffectId, anchor) — a repeat of anchor 3's
        // original cue must now be treated as fresh/recycle again, not a merge.
        Assert.That(pool.HasActiveMatch(SolreignFxCategory.ImpactLight, "impact_light", EntityAnchor(3)), Is.False);
    }

    [Test]
    public void TryActivate_NeverEvictsAnotherCategory()
    {
        var pool = new SolreignFxPool(new Dictionary<SolreignFxCategory, int>
        {
            [SolreignFxCategory.ImpactLight] = 1,
            [SolreignFxCategory.Dust] = 1,
        });

        pool.TryActivate(SolreignFxCategory.ImpactLight, "impact_light", EntityAnchor(1), 0.0, out var impactSlot, out _);
        pool.TryActivate(SolreignFxCategory.Dust, "dust", EntityAnchor(2), 0.0, out _, out _);

        // Flooding ImpactLight (already at its own cap of 1) must never touch Dust's slot.
        pool.TryActivate(SolreignFxCategory.ImpactLight, "impact_light", EntityAnchor(99), 100.0, out _, out var kind);

        Assert.That(kind, Is.EqualTo(SolreignFxPool.ActivationKind.Recycled));
        Assert.That(pool.ActiveCount(SolreignFxCategory.Dust), Is.EqualTo(1), "cdx #16: caps are per-category — cosmetic spam in one category can never evict another category's active cue");
    }

    [Test]
    public void TryActivate_ZeroCapacityCategory_AlwaysFails()
    {
        var pool = MakePool(0, SolreignFxCategory.Transformation);

        var ok = pool.TryActivate(SolreignFxCategory.Transformation, "transformation", EntityAnchor(1), 0.0, out _, out _);

        Assert.That(ok, Is.False, "Transformation ships with 0 pooled entities per spec §3 — it must never claim a slot");
    }

    [Test]
    public void TryActivate_UnregisteredCategory_Fails()
    {
        var pool = MakePool(3, SolreignFxCategory.ImpactLight);

        var ok = pool.TryActivate(SolreignFxCategory.Smoke, "smoke", EntityAnchor(1), 0.0, out _, out _);

        Assert.That(ok, Is.False);
    }

    [Test]
    public void Release_FreesTheSlotForReuse()
    {
        var pool = MakePool(1);
        pool.TryActivate(SolreignFxCategory.ImpactLight, "impact_light", EntityAnchor(1), 0.0, out var slot, out _);

        pool.Release(SolreignFxCategory.ImpactLight, slot);

        Assert.That(pool.ActiveCount(SolreignFxCategory.ImpactLight), Is.EqualTo(0));
        Assert.That(pool.HasActiveMatch(SolreignFxCategory.ImpactLight, "impact_light", EntityAnchor(1)), Is.False);

        var ok = pool.TryActivate(SolreignFxCategory.ImpactLight, "impact_heavy", EntityAnchor(2), 1.0, out var reused, out var kind);
        Assert.That(ok, Is.True);
        Assert.That(kind, Is.EqualTo(SolreignFxPool.ActivationKind.Fresh));
        Assert.That(reused, Is.EqualTo(slot));
    }

    [Test]
    public void Resize_Shrinking_DropsSlotsBeyondNewCapacity()
    {
        var pool = MakePool(3);
        pool.TryActivate(SolreignFxCategory.ImpactLight, "impact_light", EntityAnchor(1), 0.0, out _, out _);
        pool.TryActivate(SolreignFxCategory.ImpactLight, "impact_light", EntityAnchor(2), 0.0, out _, out _);
        pool.TryActivate(SolreignFxCategory.ImpactLight, "impact_light", EntityAnchor(3), 0.0, out _, out _);

        pool.Resize(SolreignFxCategory.ImpactLight, 1);

        Assert.That(pool.Capacity(SolreignFxCategory.ImpactLight), Is.EqualTo(1));
        Assert.That(pool.ActiveCount(SolreignFxCategory.ImpactLight), Is.LessThanOrEqualTo(1));
    }

    [Test]
    public void Resize_Growing_PreservesExistingActiveSlots()
    {
        var pool = MakePool(1);
        pool.TryActivate(SolreignFxCategory.ImpactLight, "impact_light", EntityAnchor(1), 0.0, out var slot, out _);

        pool.Resize(SolreignFxCategory.ImpactLight, 3);

        Assert.That(pool.Capacity(SolreignFxCategory.ImpactLight), Is.EqualTo(3));
        Assert.That(pool.HasActiveMatch(SolreignFxCategory.ImpactLight, "impact_light", EntityAnchor(1)), Is.True);
        Assert.That(slot, Is.EqualTo(0));
    }

    [Test]
    public void Constructor_NegativeCapacity_ClampsToZero()
    {
        var pool = MakePool(-5);

        Assert.That(pool.Capacity(SolreignFxCategory.ImpactLight), Is.EqualTo(0));
    }
}
