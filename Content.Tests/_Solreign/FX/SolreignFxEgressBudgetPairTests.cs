#nullable enable
using Content.Server._Solreign.FX;
using Content.Shared._Solreign.FX;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.Tests._Solreign.FX;

/// <summary>
///     Regression coverage for grk review findings H-A/H-B on <see cref="SolreignFxEgressBudget.TryConsumePair"/>:
///     the original <c>RaiseSecretRoleCue</c> sequentially called <c>TryConsume</c> twice (detail
///     then generic) — if the detail call succeeded (spending a real token, marking its key
///     coalesced) and the generic call then failed, the overall raise correctly returned failure,
///     but the detail key was left incorrectly marked "already covered" for the rest of the tick —
///     a LATER identical-looking call that tick would then wrongly report success via
///     <c>CoalescedWithinTick</c> even though NEITHER half of the pair was ever actually raised.
///     <c>TryConsumePair</c> peeks both categories before committing either, closing this gap.
/// </summary>
[TestFixture]
[TestOf(typeof(SolreignFxEgressBudget))]
public sealed class SolreignFxEgressBudgetPairTests
{
    private static SolreignFxAnchorKey Anchor(int id) => SolreignFxAnchorKey.FromEntity(new NetEntity(id));

    [Test]
    public void TryConsumePair_BothAvailable_ConsumesBothAndMarksBothCoalesced()
    {
        var budget = new SolreignFxEgressBudget { GlobalTickCap = 1000 };
        var tick = new GameTick(1);

        var result = budget.TryConsumePair(
            SolreignFxCategory.Transformation, "transformation",
            SolreignFxCategory.Transformation, "transformation_generic",
            Anchor(1), tick, 0.0);

        Assert.That(result, Is.EqualTo(SolreignFxEgressBudget.ConsumeResult.Consumed));
        Assert.That(budget.GlobalUsedThisTickForTests, Is.EqualTo(2), "a pair costs 2 global-tick units, not 1");
    }

    [Test]
    public void TryConsumePair_RepeatSameDetailWithinTick_Coalesces()
    {
        var budget = new SolreignFxEgressBudget { GlobalTickCap = 1000 };
        var tick = new GameTick(1);

        budget.TryConsumePair(SolreignFxCategory.Transformation, "transformation",
            SolreignFxCategory.Transformation, "transformation_generic", Anchor(1), tick, 0.0);

        var second = budget.TryConsumePair(SolreignFxCategory.Transformation, "transformation",
            SolreignFxCategory.Transformation, "transformation_generic", Anchor(1), tick, 0.0);

        Assert.That(second, Is.EqualTo(SolreignFxEgressBudget.ConsumeResult.CoalescedWithinTick));
        Assert.That(budget.GlobalUsedThisTickForTests, Is.EqualTo(2), "a coalesced repeat must not consume additional global-tick units");
    }

    [Test]
    public void TryConsumePair_GenericBucketExhausted_LeavesDetailUntouchedAndUncoalesced()
    {
        // The exact H-A/H-B regression scenario: drain the GENERIC category's bucket completely
        // first (using a different anchor so it doesn't collide with the pair below), then attempt
        // a pair whose detail half has plenty of budget but whose generic half does not.
        var budget = new SolreignFxEgressBudget { GlobalTickCap = 1000 };
        var castRingDefaults = SolreignFxCategoryTable.GetDefaults(SolreignFxCategory.CastRing); // small burst (8)

        var tick = new GameTick(1);
        for (var i = 0; i < castRingDefaults.ConcurrentCap; i++)
            budget.TryConsume(SolreignFxCategory.CastRing, "cast_ring", Anchor(1000 + i), tick, 0.0);

        var usedBeforePairAttempt = budget.GlobalUsedThisTickForTests;

        // Now attempt a pair: detail = StaminaBreak (plenty of budget), generic = CastRing (exhausted).
        var result = budget.TryConsumePair(
            SolreignFxCategory.StaminaBreak, "stamina_break",
            SolreignFxCategory.CastRing, "cast_ring",
            Anchor(2), tick, 0.0);

        Assert.That(result, Is.EqualTo(SolreignFxEgressBudget.ConsumeResult.DroppedCategoryBudget));
        Assert.That(budget.GlobalUsedThisTickForTests, Is.EqualTo(usedBeforePairAttempt),
            "a failed pair (generic side exhausted) must not consume ANY global-tick units for the detail side either — nothing was actually raised");

        // The critical regression assertion: a SECOND identical attempt this same tick must NOT be
        // told "already covered" — the detail key must never have been marked coalesced, because
        // the pair never actually went out.
        var retry = budget.TryConsumePair(
            SolreignFxCategory.StaminaBreak, "stamina_break",
            SolreignFxCategory.CastRing, "cast_ring",
            Anchor(2), tick, 0.0);

        Assert.That(retry, Is.EqualTo(SolreignFxEgressBudget.ConsumeResult.DroppedCategoryBudget),
            "must still fail for the same reason — NEVER silently succeed (CoalescedWithinTick) for a pair that has never once actually gone out");
    }

    [Test]
    public void TryConsumePair_DetailBucketExhausted_NeverTouchesGenericBucket()
    {
        var budget = new SolreignFxEgressBudget { GlobalTickCap = 1000 };
        var castRingDefaults = SolreignFxCategoryTable.GetDefaults(SolreignFxCategory.CastRing);
        var tick = new GameTick(1);

        for (var i = 0; i < castRingDefaults.ConcurrentCap; i++)
            budget.TryConsume(SolreignFxCategory.CastRing, "cast_ring", Anchor(2000 + i), tick, 0.0);

        var result = budget.TryConsumePair(
            SolreignFxCategory.CastRing, "cast_ring", // exhausted detail
            SolreignFxCategory.StaminaBreak, "stamina_break", // healthy generic
            Anchor(3), tick, 0.0);

        Assert.That(result, Is.EqualTo(SolreignFxEgressBudget.ConsumeResult.DroppedCategoryBudget));

        // The generic (StaminaBreak) bucket must still be fully intact — verify by draining its
        // ENTIRE nominal burst afterward and confirming every one of those succeeds.
        var staminaDefaults = SolreignFxCategoryTable.GetDefaults(SolreignFxCategory.StaminaBreak);
        for (var i = 0; i < staminaDefaults.ConcurrentCap; i++)
        {
            var r = budget.TryConsume(SolreignFxCategory.StaminaBreak, "stamina_break", Anchor(4000 + i), tick, 0.0);
            Assert.That(r, Is.EqualTo(SolreignFxEgressBudget.ConsumeResult.Consumed),
                $"StaminaBreak bucket unit #{i} should still be available — the failed pair attempt must not have consumed any of it");
        }
    }

    [Test]
    public void TryConsumePair_SameCategoryBothHalves_RequiresTwoTokensNotJustOne()
    {
        // grk review round-2 finding M-new-1: transformation + transformation_generic both map to
        // the SAME category (Transformation). Peeking "≥1 available" independently twice against
        // ONE shared bucket is wrong when exactly 1 token remains — both peeks would pass
        // individually. This must require 2 full tokens from the one bucket, not 1.
        var budget = new SolreignFxEgressBudget { GlobalTickCap = 1000 };
        var tick = new GameTick(1);
        var burst = SolreignFxCategoryTable.GetDefaults(SolreignFxCategory.Transformation).ConcurrentCap; // 0 -> nominal egress-only burst applies internally

        // Drain the Transformation bucket down to exactly 1 remaining token via unrelated single
        // consumes (different anchors so they don't collide with the pair's anchor below).
        var nominalBurst = 8; // MinimumEgressBurstForZeroCapCategories, mirrored here for clarity
        for (var i = 0; i < nominalBurst - 1; i++)
            budget.TryConsume(SolreignFxCategory.Transformation, "body_shock_generic", Anchor(5000 + i), tick, 0.0);

        var result = budget.TryConsumePair(
            SolreignFxCategory.Transformation, "transformation",
            SolreignFxCategory.Transformation, "transformation_generic",
            Anchor(9), tick, 0.0);

        Assert.That(result, Is.EqualTo(SolreignFxEgressBudget.ConsumeResult.DroppedCategoryBudget),
            "exactly 1 token remains in the shared bucket — a pair needs 2, so it must be dropped, never silently accepted with an overspend");
        Assert.That(burst, Is.EqualTo(0)); // sanity: confirms this category really is the zero-pool-cap shape the nominal burst carve-out exists for

        // Confirm the single remaining token is STILL there (untouched by the failed pair attempt).
        var singleConsume = budget.TryConsume(SolreignFxCategory.Transformation, "stamina_break_analog_probe", Anchor(9999), tick, 0.0);
        Assert.That(singleConsume, Is.EqualTo(SolreignFxEgressBudget.ConsumeResult.Consumed),
            "the failed pair attempt must not have partially spent the shared bucket's last remaining token");
    }

    [Test]
    public void TryConsumePair_GlobalCapHasOnlyOneUnitLeft_FailsRatherThanPartiallyConsuming()
    {
        var budget = new SolreignFxEgressBudget { GlobalTickCap = 3 };
        var tick = new GameTick(1);

        // Consume 2 of the 3 global units via unrelated single raises, leaving exactly 1.
        budget.TryConsume(SolreignFxCategory.ImpactLight, "impact_light", Anchor(1), tick, 0.0);
        budget.TryConsume(SolreignFxCategory.Dust, "dust", Anchor(2), tick, 0.0);
        Assert.That(budget.GlobalUsedThisTickForTests, Is.EqualTo(2));

        var result = budget.TryConsumePair(
            SolreignFxCategory.Transformation, "transformation",
            SolreignFxCategory.Transformation, "transformation_generic",
            Anchor(3), tick, 0.0);

        Assert.That(result, Is.EqualTo(SolreignFxEgressBudget.ConsumeResult.DroppedGlobalCap));
        Assert.That(budget.GlobalUsedThisTickForTests, Is.EqualTo(2), "a pair needing 2 units with only 1 remaining must consume NEITHER unit, not one");
    }
}
