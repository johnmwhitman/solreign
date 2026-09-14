using System;
using Content.Server._Solreign.PlayerDelight.Vista;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Unit tests for the pure once-per-player-per-round vista-beat gate — same coverage shape as
///     <c>ProvidenceWelcomeGateTests</c>, which tests the idiom this gate mirrors.
/// </summary>
[TestFixture]
[TestOf(typeof(VistaBeatGate))]
public sealed class VistaBeatGateTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    [Test]
    public void CanFire_IsTrueForAPlayerNeverDeliveredThisRound()
    {
        var gate = new VistaBeatGate();

        Assert.That(gate.CanFire(Alice), Is.True);
    }

    [Test]
    public void MarkDelivered_ThenCanFire_IsFalseForTheSamePlayer()
    {
        var gate = new VistaBeatGate();

        gate.MarkDelivered(Alice);

        Assert.That(gate.CanFire(Alice), Is.False);
    }

    [Test]
    public void MarkDelivered_IsIdempotent_StillBlocksAfterRepeatedMarks()
    {
        var gate = new VistaBeatGate();

        gate.MarkDelivered(Alice);
        gate.MarkDelivered(Alice);

        Assert.That(gate.CanFire(Alice), Is.False);
    }

    [Test]
    public void MarkDelivered_DoesNotAffectOtherPlayers()
    {
        var gate = new VistaBeatGate();

        gate.MarkDelivered(Alice);

        Assert.That(gate.CanFire(Bob), Is.True);
    }

    [Test]
    public void Reset_ClearsAllTrackedPlayers()
    {
        var gate = new VistaBeatGate();

        gate.MarkDelivered(Alice);
        gate.MarkDelivered(Bob);
        gate.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(gate.CanFire(Alice), Is.True);
            Assert.That(gate.CanFire(Bob), Is.True);
        });
    }
}
