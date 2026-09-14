#nullable enable
using System;
using Content.Server._Solreign.Providence;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Coverage for the pure per-round in-flight guard (spec §3.2/§7): one dispatch per account per
///     round, independence across accounts, reset on round boundary. Same shape as
///     <see cref="ProvidenceWelcomeGateTests"/> — this guard is the double-async-dispatch stopper,
///     NOT the exactly-once mechanism (that is the atomic claim row, covered by
///     <see cref="FirstDeathStoreTests"/>).
/// </summary>
[TestFixture]
[TestOf(typeof(FirstDeathRoundGuard))]
public sealed class FirstDeathRoundGuardTests
{
    [Test]
    public void FreshGuard_AllowsAnyAccount()
    {
        var guard = new FirstDeathRoundGuard();
        Assert.That(guard.CanFire(Guid.NewGuid()), Is.True);
    }

    [Test]
    public void MarkedAccount_IsBlocked_ForTheRestOfTheRound()
    {
        var guard = new FirstDeathRoundGuard();
        var player = Guid.NewGuid();

        Assert.That(guard.CanFire(player), Is.True);
        guard.MarkFired(player);
        Assert.That(guard.CanFire(player), Is.False,
            "a die → revive → die-again sequence must not double-dispatch in one round");
    }

    [Test]
    public void OtherAccounts_AreUnaffected_ByAMark()
    {
        var guard = new FirstDeathRoundGuard();
        var marked = Guid.NewGuid();
        var other = Guid.NewGuid();

        guard.MarkFired(marked);

        Assert.Multiple(() =>
        {
            Assert.That(guard.CanFire(marked), Is.False);
            Assert.That(guard.CanFire(other), Is.True);
        });
    }

    [Test]
    public void Reset_ClearsEveryMark()
    {
        var guard = new FirstDeathRoundGuard();
        var player = Guid.NewGuid();

        guard.MarkFired(player);
        guard.Reset();

        Assert.That(guard.CanFire(player), Is.True,
            "the round boundary must wipe the in-flight map (the persistent claim row is what makes " +
            "the scene once-EVER, not this guard)");
    }

    [Test]
    public void MarkingTwice_IsIdempotent()
    {
        var guard = new FirstDeathRoundGuard();
        var player = Guid.NewGuid();

        guard.MarkFired(player);
        Assert.DoesNotThrow(() => guard.MarkFired(player));
        Assert.That(guard.CanFire(player), Is.False);
    }
}
