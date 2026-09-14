#nullable enable
using Content.Server._Solreign.FX;
using Content.Shared._Solreign.FX;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.Tests._Solreign.FX;

/// <summary>
///     Spec §3.1's server-side egress budget (grk #1's top-ranked finding across both external
///     reviews): per-tick global cap, per-category token-bucket burst/refill, and within-tick
///     coalescing by (EffectId, anchor). Covers the mission's "budget enforcement (burst -> throttle
///     curves)" row.
/// </summary>
[TestFixture]
[TestOf(typeof(SolreignFxEgressBudget))]
public sealed class SolreignFxEgressBudgetTests
{
    private static SolreignFxAnchorKey Anchor(int id) => SolreignFxAnchorKey.FromEntity(new NetEntity(id));

    [Test]
    public void TryConsume_WithinCategoryBurst_Succeeds()
    {
        var budget = new SolreignFxEgressBudget { GlobalTickCap = 1000 };
        var defaults = SolreignFxCategoryTable.GetDefaults(SolreignFxCategory.ImpactLight);

        for (var i = 0; i < defaults.ConcurrentCap; i++)
        {
            var result = budget.TryConsume(SolreignFxCategory.ImpactLight, "impact_light", Anchor(i), new GameTick((uint) i), 0.0);
            Assert.That(result, Is.EqualTo(SolreignFxEgressBudget.ConsumeResult.Consumed), $"raise #{i} should still be within the category's burst capacity ({defaults.ConcurrentCap})");
        }
    }

    [Test]
    public void TryConsume_BeyondCategoryBurst_DropsWithoutRefillTime()
    {
        var budget = new SolreignFxEgressBudget { GlobalTickCap = 100000 };
        var defaults = SolreignFxCategoryTable.GetDefaults(SolreignFxCategory.CastRing); // small burst (8) for a tight test

        for (var i = 0; i < defaults.ConcurrentCap; i++)
            budget.TryConsume(SolreignFxCategory.CastRing, "cast_ring", Anchor(1000 + i), new GameTick((uint) i), 0.0);

        // One more, same instant (no refill time elapsed) -> burst is exhausted.
        var over = budget.TryConsume(SolreignFxCategory.CastRing, "cast_ring", Anchor(9999), new GameTick((uint) defaults.ConcurrentCap), 0.0);

        Assert.That(over, Is.EqualTo(SolreignFxEgressBudget.ConsumeResult.DroppedCategoryBudget));
    }

    [Test]
    public void TryConsume_AfterRefillWindow_TokensReturn()
    {
        var budget = new SolreignFxEgressBudget { GlobalTickCap = 100000, RefillMultiplier = 2f };
        var defaults = SolreignFxCategoryTable.GetDefaults(SolreignFxCategory.CastRing);

        for (var i = 0; i < defaults.ConcurrentCap; i++)
            budget.TryConsume(SolreignFxCategory.CastRing, "cast_ring", Anchor(2000 + i), new GameTick((uint) i), 0.0);

        // Refill rate is 2x cap/sec, so after a full second every token should be back.
        var afterRefill = budget.TryConsume(SolreignFxCategory.CastRing, "cast_ring", Anchor(3000), new GameTick(9999), 1.0);

        Assert.That(afterRefill, Is.EqualTo(SolreignFxEgressBudget.ConsumeResult.Consumed));
    }

    [Test]
    public void TryConsume_GlobalTickCap_DropsEvenWithCategoryBudgetRemaining()
    {
        var budget = new SolreignFxEgressBudget { GlobalTickCap = 2 };

        var tick = new GameTick(1);
        Assert.That(budget.TryConsume(SolreignFxCategory.ImpactLight, "impact_light", Anchor(1), tick, 0.0), Is.EqualTo(SolreignFxEgressBudget.ConsumeResult.Consumed));
        Assert.That(budget.TryConsume(SolreignFxCategory.Dust, "dust", Anchor(2), tick, 0.0), Is.EqualTo(SolreignFxEgressBudget.ConsumeResult.Consumed));

        // Third raise this tick, a totally different category with plenty of its OWN budget left —
        // the GLOBAL per-tick cap still drops it.
        var third = budget.TryConsume(SolreignFxCategory.Smoke, "smoke", Anchor(3), tick, 0.0);
        Assert.That(third, Is.EqualTo(SolreignFxEgressBudget.ConsumeResult.DroppedGlobalCap));
    }

    [Test]
    public void TryConsume_GlobalTickCap_ResetsOnNextTick()
    {
        var budget = new SolreignFxEgressBudget { GlobalTickCap = 1 };

        Assert.That(budget.TryConsume(SolreignFxCategory.ImpactLight, "impact_light", Anchor(1), new GameTick(1), 0.0), Is.EqualTo(SolreignFxEgressBudget.ConsumeResult.Consumed));
        Assert.That(budget.TryConsume(SolreignFxCategory.ImpactLight, "impact_light", Anchor(2), new GameTick(1), 0.0), Is.EqualTo(SolreignFxEgressBudget.ConsumeResult.DroppedGlobalCap));

        var nextTick = budget.TryConsume(SolreignFxCategory.ImpactLight, "impact_light", Anchor(2), new GameTick(2), 0.0);
        Assert.That(nextTick, Is.EqualTo(SolreignFxEgressBudget.ConsumeResult.Consumed));
    }

    [Test]
    public void TryConsume_RepeatSameAnchorWithinTick_Coalesces()
    {
        var budget = new SolreignFxEgressBudget { GlobalTickCap = 1000 };
        var tick = new GameTick(5);

        var first = budget.TryConsume(SolreignFxCategory.ImpactLight, "impact_light", Anchor(1), tick, 0.0);
        var second = budget.TryConsume(SolreignFxCategory.ImpactLight, "impact_light", Anchor(1), tick, 0.0);

        Assert.That(first, Is.EqualTo(SolreignFxEgressBudget.ConsumeResult.Consumed));
        Assert.That(second, Is.EqualTo(SolreignFxEgressBudget.ConsumeResult.CoalescedWithinTick));
        Assert.That(budget.GlobalUsedThisTickForTests, Is.EqualTo(1), "a coalesced repeat must not consume a second unit of the global per-tick budget");
    }

    [Test]
    public void TryConsume_SameAnchorDifferentEffectId_DoesNotCoalesce()
    {
        var budget = new SolreignFxEgressBudget { GlobalTickCap = 1000 };
        var tick = new GameTick(5);

        var first = budget.TryConsume(SolreignFxCategory.Transformation, "transformation", Anchor(1), tick, 0.0);
        var second = budget.TryConsume(SolreignFxCategory.Transformation, "transformation_generic", Anchor(1), tick, 0.0);

        Assert.That(first, Is.EqualTo(SolreignFxEgressBudget.ConsumeResult.Consumed));
        Assert.That(second, Is.EqualTo(SolreignFxEgressBudget.ConsumeResult.Consumed), "different EffectIds at the same anchor are two distinct raises (e.g. the generic+detail pair), never coalesced into one");
    }

    [Test]
    public void TryConsume_ZeroPoolCapCategory_StillGetsANominalEgressBurst()
    {
        // Transformation's pool concurrent cap is 0 (spec §3: it never touches the client pool at
        // all) — that must NOT translate into an egress bucket with burst 0, which would silently
        // drop every transformation cue ever raised. See SolreignFxEgressBudget's own remarks.
        var budget = new SolreignFxEgressBudget { GlobalTickCap = 1000 };

        var result = budget.TryConsume(SolreignFxCategory.Transformation, "transformation_generic", Anchor(1), new GameTick(1), 0.0);

        Assert.That(result, Is.EqualTo(SolreignFxEgressBudget.ConsumeResult.Consumed));
    }
}
