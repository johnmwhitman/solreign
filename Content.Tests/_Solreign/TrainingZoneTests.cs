using System;
using Content.Server._Solreign.TrainingCombat;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(ZoneRules))]
public sealed class TrainingZoneTests
{
    // --- Zone weighting: equal weights ---

    [Test]
    public void EqualWeights_LowRoll_PicksHead()
    {
        Assert.That(ZoneRules.PickZone(1f, 1f, 1f, 0.0), Is.EqualTo(TrainingZone.Head));
        Assert.That(ZoneRules.PickZone(1f, 1f, 1f, 0.32), Is.EqualTo(TrainingZone.Head));
    }

    [Test]
    public void EqualWeights_MidRoll_PicksLegs()
    {
        Assert.That(ZoneRules.PickZone(1f, 1f, 1f, 0.34), Is.EqualTo(TrainingZone.Legs));
        Assert.That(ZoneRules.PickZone(1f, 1f, 1f, 0.65), Is.EqualTo(TrainingZone.Legs));
    }

    [Test]
    public void EqualWeights_HighRoll_PicksHands()
    {
        Assert.That(ZoneRules.PickZone(1f, 1f, 1f, 0.67), Is.EqualTo(TrainingZone.Hands));
        Assert.That(ZoneRules.PickZone(1f, 1f, 1f, 0.99), Is.EqualTo(TrainingZone.Hands));
    }

    // --- Zone weighting: boundaries ---

    [Test]
    public void RollExactlyAtFirstBoundary_FallsToSecondZone()
    {
        // Cumulative head edge for weights 1/1/2 is 0.25; the comparison is strict-less-than.
        Assert.That(ZoneRules.PickZone(1f, 1f, 2f, 0.25), Is.EqualTo(TrainingZone.Legs));
    }

    [Test]
    public void RollOfExactlyOne_PicksLastPositiveZone()
    {
        Assert.That(ZoneRules.PickZone(1f, 1f, 1f, 1.0), Is.EqualTo(TrainingZone.Hands));
    }

    [Test]
    public void RollOfExactlyOne_WithZeroHandsWeight_NeverPicksHands()
    {
        Assert.That(ZoneRules.PickZone(1f, 1f, 0f, 1.0), Is.EqualTo(TrainingZone.Legs));
    }

    [Test]
    public void RollOfExactlyOne_WithOnlyHeadWeighted_PicksHead()
    {
        Assert.That(ZoneRules.PickZone(1f, 0f, 0f, 1.0), Is.EqualTo(TrainingZone.Head));
    }

    // --- Zone weighting: skewed and single-zone weights ---

    [Test]
    public void ZeroWeightZone_IsNeverPicked()
    {
        for (var i = 0; i <= 100; i++)
        {
            var zone = ZoneRules.PickZone(1f, 0f, 1f, i / 100.0);
            Assert.That(zone, Is.Not.EqualTo(TrainingZone.Legs));
        }
    }

    [Test]
    public void SingleWeightedZone_AlwaysWins()
    {
        for (var i = 0; i <= 100; i++)
        {
            Assert.That(ZoneRules.PickZone(0f, 1f, 0f, i / 100.0), Is.EqualTo(TrainingZone.Legs));
        }
    }

    [Test]
    public void WeightsNeedNotSumToOne()
    {
        // 2/6/2 => head edge at 0.2, legs edge at 0.8.
        Assert.That(ZoneRules.PickZone(2f, 6f, 2f, 0.19), Is.EqualTo(TrainingZone.Head));
        Assert.That(ZoneRules.PickZone(2f, 6f, 2f, 0.5), Is.EqualTo(TrainingZone.Legs));
        Assert.That(ZoneRules.PickZone(2f, 6f, 2f, 0.81), Is.EqualTo(TrainingZone.Hands));
    }

    // --- Zone weighting: sanitization ---

    [Test]
    public void AllZeroWeights_YieldNone()
    {
        Assert.That(ZoneRules.PickZone(0f, 0f, 0f, 0.5), Is.EqualTo(TrainingZone.None));
    }

    [Test]
    public void NegativeWeights_ClampToZero()
    {
        // Negative head weight is YAML misconfiguration; it must not distort the split.
        Assert.That(ZoneRules.PickZone(-5f, 1f, 0f, 0.99), Is.EqualTo(TrainingZone.Legs));
        Assert.That(ZoneRules.PickZone(-5f, -5f, -5f, 0.5), Is.EqualTo(TrainingZone.None));
    }

    [Test]
    public void RollBelowZero_ClampsToFirstPositiveZone()
    {
        Assert.That(ZoneRules.PickZone(1f, 1f, 1f, -2.0), Is.EqualTo(TrainingZone.Head));
    }

    [Test]
    public void RollAboveOne_ClampsToLastPositiveZone()
    {
        Assert.That(ZoneRules.PickZone(1f, 1f, 1f, 7.0), Is.EqualTo(TrainingZone.Hands));
    }

    // --- Cooldown math ---

    private static readonly TimeSpan CurTime = TimeSpan.FromSeconds(500);

    [Test]
    public void Cooldown_ReadyExactlyAtNextEffectTime()
    {
        Assert.That(ZoneRules.CooldownReady(CurTime, CurTime), Is.True);
    }

    [Test]
    public void Cooldown_NotReadyBeforeNextEffectTime()
    {
        Assert.That(ZoneRules.CooldownReady(CurTime, CurTime + TimeSpan.FromMilliseconds(1)), Is.False);
    }

    [Test]
    public void Cooldown_ReadyAfterNextEffectTime()
    {
        Assert.That(ZoneRules.CooldownReady(CurTime + TimeSpan.FromSeconds(2), CurTime), Is.True);
    }

    [Test]
    public void NextEffectTime_AddsCooldownToCurTime()
    {
        var next = ZoneRules.NextEffectTime(CurTime, TimeSpan.FromSeconds(1.5));

        Assert.That(next, Is.EqualTo(CurTime + TimeSpan.FromSeconds(1.5)));
    }

    [Test]
    public void NextEffectTime_NegativeCooldown_ClampsToZero()
    {
        var next = ZoneRules.NextEffectTime(CurTime, TimeSpan.FromSeconds(-10));

        Assert.That(next, Is.EqualTo(CurTime));
    }

    [Test]
    public void FreshComponent_DefaultNextEffectTime_IsImmediatelyReady()
    {
        // A newly spawned baton has NextEffectTime == default(TimeSpan); the first hit must work.
        Assert.That(ZoneRules.CooldownReady(TimeSpan.Zero, default), Is.True);
    }
}
