using System;
using Content.Server._Solreign.Providence;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(ProvidenceWelcomeGate))]
public sealed class ProvidenceWelcomeGateTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    [Test]
    public void CanFire_IsTrueForAPlayerNeverGreetedThisRound()
    {
        var gate = new ProvidenceWelcomeGate();

        Assert.That(gate.CanFire(Alice), Is.True);
    }

    [Test]
    public void MarkFired_ThenCanFire_IsFalseForTheSamePlayer()
    {
        var gate = new ProvidenceWelcomeGate();

        gate.MarkFired(Alice);

        Assert.That(gate.CanFire(Alice), Is.False);
    }

    [Test]
    public void MarkFired_DoesNotAffectOtherPlayers()
    {
        var gate = new ProvidenceWelcomeGate();

        gate.MarkFired(Alice);

        Assert.That(gate.CanFire(Bob), Is.True);
    }

    [Test]
    public void RepeatedMarkFired_ForSamePlayer_StaysBlocked()
    {
        // Mirrors the system's real usage: a player who respawns multiple times in a round (death,
        // cryo return, ghost-role swap) must only ever be marked once-and-done, never re-fire.
        var gate = new ProvidenceWelcomeGate();

        gate.MarkFired(Alice);
        gate.MarkFired(Alice);
        gate.MarkFired(Alice);

        Assert.That(gate.CanFire(Alice), Is.False);
    }

    [Test]
    public void Reset_ClearsEveryTrackedPlayer_SoTheRoundStartsFresh()
    {
        var gate = new ProvidenceWelcomeGate();
        gate.MarkFired(Alice);
        gate.MarkFired(Bob);

        gate.Reset();

        Assert.That(gate.CanFire(Alice), Is.True);
        Assert.That(gate.CanFire(Bob), Is.True);
    }

    [Test]
    public void MultipleRounds_EachGetsItsOwnWelcome()
    {
        // A player who was greeted last round is eligible again after a round boundary reset.
        var gate = new ProvidenceWelcomeGate();

        gate.MarkFired(Alice);
        Assert.That(gate.CanFire(Alice), Is.False);

        gate.Reset();
        Assert.That(gate.CanFire(Alice), Is.True);

        gate.MarkFired(Alice);
        Assert.That(gate.CanFire(Alice), Is.False);
    }
}
