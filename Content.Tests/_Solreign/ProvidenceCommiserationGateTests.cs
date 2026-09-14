using System;
using Content.Server._Solreign.Providence;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(ProvidenceCommiserationGate))]
public sealed class ProvidenceCommiserationGateTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    [Test]
    public void CanFire_IsTrueForAPlayerNeverSeenThisRound()
    {
        var gate = new ProvidenceCommiserationGate();

        Assert.That(gate.CanFire(Alice), Is.True);
    }

    [Test]
    public void MarkFired_ThenCanFire_IsFalseForTheSamePlayer()
    {
        var gate = new ProvidenceCommiserationGate();

        gate.MarkFired(Alice);

        Assert.That(gate.CanFire(Alice), Is.False);
    }

    [Test]
    public void MarkFired_DoesNotAffectOtherPlayers()
    {
        var gate = new ProvidenceCommiserationGate();

        gate.MarkFired(Alice);

        Assert.That(gate.CanFire(Bob), Is.True);
    }

    [Test]
    public void RepeatedMarkFired_ForSamePlayer_StaysBlocked()
    {
        var gate = new ProvidenceCommiserationGate();

        gate.MarkFired(Alice);
        gate.MarkFired(Alice);
        gate.MarkFired(Alice);

        Assert.That(gate.CanFire(Alice), Is.False);
    }

    [Test]
    public void Reset_ClearsEveryTrackedPlayer_SoTheRoundStartsFresh()
    {
        var gate = new ProvidenceCommiserationGate();
        gate.MarkFired(Alice);
        gate.MarkFired(Bob);

        gate.Reset();

        Assert.That(gate.CanFire(Alice), Is.True);
        Assert.That(gate.CanFire(Bob), Is.True);
    }

    [Test]
    public void CanFire_WithoutMarkFired_NeverConsumesTheSlot()
    {
        // Mirrors the system's real usage: a probability miss must not burn the per-round slot, only
        // an actual successful fire (MarkFired) does.
        var gate = new ProvidenceCommiserationGate();

        for (var i = 0; i < 10; i++)
            Assert.That(gate.CanFire(Alice), Is.True, $"check {i} should not consume anything by itself");
    }

    [TestCase(0.0, 0.5, ExpectedResult = true)]
    [TestCase(0.49, 0.5, ExpectedResult = true)]
    [TestCase(0.5, 0.5, ExpectedResult = false)]
    [TestCase(0.99, 0.5, ExpectedResult = false)]
    [TestCase(1.0, 0.5, ExpectedResult = false)]
    public bool ShouldFire_ComparesRollAgainstChance(double roll, double chance)
    {
        return ProvidenceCommiserationGate.ShouldFire(roll, chance);
    }

    [Test]
    public void ShouldFire_NeverFires_WhenChanceIsZero()
    {
        Assert.That(ProvidenceCommiserationGate.ShouldFire(0.0, 0.0), Is.False);
        Assert.That(ProvidenceCommiserationGate.ShouldFire(0.999, 0.0), Is.False);
    }

    [Test]
    public void ShouldFire_AlwaysFires_WhenChanceIsOne_ExceptOnTheRollEqualToOne()
    {
        Assert.That(ProvidenceCommiserationGate.ShouldFire(0.0, 1.0), Is.True);
        Assert.That(ProvidenceCommiserationGate.ShouldFire(0.999, 1.0), Is.True);
    }

    [Test]
    public void ShouldFire_ClampsAnOutOfRangeChance_AboveOne()
    {
        // A misconfigured chance > 1 must not behave as "more than certain" (e.g. via an unclamped
        // comparison bug) — it should behave identically to chance == 1.
        Assert.That(ProvidenceCommiserationGate.ShouldFire(0.999, 5.0), Is.True);
    }

    [Test]
    public void ShouldFire_ClampsAnOutOfRangeChance_BelowZero()
    {
        // A misconfigured negative chance must fail closed (never fire), not underflow into "always fires".
        Assert.That(ProvidenceCommiserationGate.ShouldFire(0.0, -5.0), Is.False);
    }
}
