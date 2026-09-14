using System;
using Content.Shared._Solreign.Medical;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(TopicalTiming))]
public sealed class TopicalTimingTests
{
    // The shipped tuning: see HealingComponent defaults (ointment/gauze land near this).
    private static readonly TimeSpan ShippedDelay = TimeSpan.FromSeconds(3);

    // W18 verifier fix: TimeSpan * float (DelayMultiplier = 0.9f) goes through a double
    // intermediate, so the result can land 1-2 ticks (100-200ns) off the mathematically exact
    // value - e.g. 10s * 0.9f produced 00:00:01.0000002 of "lost" time instead of exactly
    // 00:00:01 when checked via subtraction. Real, but irrelevant at gameplay precision (a
    // doAfter timer off by 200ns is imperceptible), so these compare Within a generous
    // microsecond-scale tolerance instead of exact equality.
    private static readonly TimeSpan Tolerance = TimeSpan.FromTicks(10);

    [Test]
    public void ScaledDelay_ShippedTuning_Is90PercentOfBase()
    {
        Assert.That(TopicalTiming.ScaledDelay(ShippedDelay), Is.EqualTo(TimeSpan.FromSeconds(2.7)).Within(Tolerance));
    }

    [Test]
    public void ScaledDelay_IsTenPercentFaster()
    {
        var baseDelay = TimeSpan.FromSeconds(10);
        var scaled = TopicalTiming.ScaledDelay(baseDelay);

        Assert.That(scaled, Is.LessThan(baseDelay));
        Assert.That((baseDelay - scaled), Is.EqualTo(TimeSpan.FromSeconds(1)).Within(Tolerance));
    }

    [Test]
    public void ScaledDelay_Zero_Unchanged()
    {
        Assert.That(TopicalTiming.ScaledDelay(TimeSpan.Zero), Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void ScaledDelay_Negative_FailsClosed_Unchanged()
    {
        var negative = TimeSpan.FromSeconds(-5);
        Assert.That(TopicalTiming.ScaledDelay(negative), Is.EqualTo(negative));
    }

    [Test]
    public void ScaledDelay_SelfAndOther_BothScaledFromSameBase()
    {
        // TryHeal (Content.Shared.Medical.Healing.HealingSystem) reads the same
        // HealingComponent.Delay for both branches - others use it directly, self multiplies it
        // by SelfHealPenaltyMultiplier on top - so scaling the base once covers both "self AND
        // others" per the beta feedback ask.
        const float selfHealPenalty = 3f;

        var scaledBase = TopicalTiming.ScaledDelay(ShippedDelay);
        var otherDelay = scaledBase;
        var selfDelay = TimeSpan.FromTicks((long)(scaledBase.Ticks * selfHealPenalty));

        Assert.That(otherDelay, Is.EqualTo(TimeSpan.FromSeconds(2.7)).Within(Tolerance));
        Assert.That(selfDelay, Is.EqualTo(TimeSpan.FromSeconds(8.1)).Within(Tolerance));

        // Both are still faster than they would have been on the unscaled base.
        Assert.That(otherDelay, Is.LessThan(ShippedDelay));
        Assert.That(selfDelay, Is.LessThan(TimeSpan.FromTicks((long)(ShippedDelay.Ticks * selfHealPenalty))));
    }
}
