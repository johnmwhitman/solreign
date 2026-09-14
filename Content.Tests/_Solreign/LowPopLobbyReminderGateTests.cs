using System;
using Content.Server._Solreign.LowPop;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(LowPopLobbyReminderGate))]
public sealed class LowPopLobbyReminderGateTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    [Test]
    public void CanFire_IsTrueForAPlayerNeverRemindedThisRound()
    {
        var gate = new LowPopLobbyReminderGate();

        Assert.That(gate.CanFire(Alice), Is.True);
    }

    [Test]
    public void MarkFired_ThenCanFire_IsFalseForTheSamePlayer()
    {
        var gate = new LowPopLobbyReminderGate();

        gate.MarkFired(Alice);

        Assert.That(gate.CanFire(Alice), Is.False);
    }

    [Test]
    public void MarkFired_DoesNotAffectOtherPlayers()
    {
        var gate = new LowPopLobbyReminderGate();

        gate.MarkFired(Alice);

        Assert.That(gate.CanFire(Bob), Is.True);
    }

    [Test]
    public void RepeatedMarkFired_ForSamePlayer_StaysBlocked()
    {
        // Mirrors the system's real usage: a player who reconnects multiple times in the same lobby
        // wait must only ever be marked once-and-done, never re-fire.
        var gate = new LowPopLobbyReminderGate();

        gate.MarkFired(Alice);
        gate.MarkFired(Alice);
        gate.MarkFired(Alice);

        Assert.That(gate.CanFire(Alice), Is.False);
    }

    [Test]
    public void Reset_ClearsEveryTrackedPlayer_SoTheRoundStartsFresh()
    {
        var gate = new LowPopLobbyReminderGate();
        gate.MarkFired(Alice);
        gate.MarkFired(Bob);

        gate.Reset();

        Assert.That(gate.CanFire(Alice), Is.True);
        Assert.That(gate.CanFire(Bob), Is.True);
    }

    [Test]
    public void MultipleRounds_EachGetsItsOwnReminder()
    {
        // A player reminded last round is eligible again after a round boundary reset.
        var gate = new LowPopLobbyReminderGate();

        gate.MarkFired(Alice);
        Assert.That(gate.CanFire(Alice), Is.False);

        gate.Reset();
        Assert.That(gate.CanFire(Alice), Is.True);

        gate.MarkFired(Alice);
        Assert.That(gate.CanFire(Alice), Is.False);
    }
}
