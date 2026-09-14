using System;
using Content.Shared._Solreign.HotPotato;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(HotPotatoFuseMath))]
public sealed class HotPotatoTests
{
    private static readonly TimeSpan Fuse = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MinBeep = TimeSpan.FromSeconds(0.15);
    private static readonly TimeSpan MaxBeep = TimeSpan.FromSeconds(1.25);

    private static TimeSpan Secs(double s) => TimeSpan.FromSeconds(s);

    // --- BeepInterval: accelerating countdown ---

    [Test]
    public void BeepInterval_FullFuse_IsMaxInterval()
    {
        var interval = HotPotatoFuseMath.BeepInterval(Fuse, Fuse, MinBeep, MaxBeep);

        Assert.That(interval, Is.EqualTo(MaxBeep));
    }

    [Test]
    public void BeepInterval_NoTimeRemaining_IsMinInterval()
    {
        var interval = HotPotatoFuseMath.BeepInterval(TimeSpan.Zero, Fuse, MinBeep, MaxBeep);

        Assert.That(interval, Is.EqualTo(MinBeep));
    }

    [Test]
    public void BeepInterval_HalfFuse_IsMidpoint()
    {
        var interval = HotPotatoFuseMath.BeepInterval(Secs(30), Fuse, MinBeep, MaxBeep);

        var midpoint = (MinBeep + MaxBeep) / 2;
        Assert.That(interval.TotalSeconds, Is.EqualTo(midpoint.TotalSeconds).Within(1e-9));
    }

    [Test]
    public void BeepInterval_RemainingAboveFuse_ClampsToMax()
    {
        var interval = HotPotatoFuseMath.BeepInterval(Secs(120), Fuse, MinBeep, MaxBeep);

        Assert.That(interval, Is.EqualTo(MaxBeep));
    }

    [Test]
    public void BeepInterval_NegativeRemaining_ClampsToMin()
    {
        var interval = HotPotatoFuseMath.BeepInterval(Secs(-5), Fuse, MinBeep, MaxBeep);

        Assert.That(interval, Is.EqualTo(MinBeep));
    }

    [Test]
    public void BeepInterval_ZeroFuse_DegeneratesToMin()
    {
        var interval = HotPotatoFuseMath.BeepInterval(Secs(10), TimeSpan.Zero, MinBeep, MaxBeep);

        Assert.That(interval, Is.EqualTo(MinBeep));
    }

    [Test]
    public void BeepInterval_MinNotBelowMax_DegeneratesToMin()
    {
        var interval = HotPotatoFuseMath.BeepInterval(Secs(30), Fuse, MaxBeep, MinBeep);

        Assert.That(interval, Is.EqualTo(MaxBeep)); // "min" argument wins in the degenerate case
    }

    [Test]
    public void BeepInterval_MonotonicallyShrinksAsFuseRunsDown()
    {
        var previous = TimeSpan.MaxValue;
        for (var remaining = 60; remaining >= 0; remaining -= 5)
        {
            var interval = HotPotatoFuseMath.BeepInterval(Secs(remaining), Fuse, MinBeep, MaxBeep);

            Assert.That(interval, Is.LessThanOrEqualTo(previous),
                $"Interval should never grow as the fuse runs down (remaining={remaining}s)");
            Assert.That(interval, Is.InRange(MinBeep, MaxBeep));
            previous = interval;
        }
    }

    // --- CanTransfer: 1s anti-ping-pong cooldown ---

    [Test]
    public void CanTransfer_BeforeCooldownElapses_IsFalse()
    {
        var nextAllowed = Secs(10);

        Assert.That(HotPotatoFuseMath.CanTransfer(Secs(9.5), nextAllowed), Is.False);
    }

    [Test]
    public void CanTransfer_ExactlyAtCooldownEnd_IsTrue()
    {
        var nextAllowed = Secs(10);

        Assert.That(HotPotatoFuseMath.CanTransfer(Secs(10), nextAllowed), Is.True);
    }

    [Test]
    public void CanTransfer_AfterCooldown_IsTrue()
    {
        var nextAllowed = Secs(10);

        Assert.That(HotPotatoFuseMath.CanTransfer(Secs(11), nextAllowed), Is.True);
    }

    [Test]
    public void NextTransferTime_AddsCooldown()
    {
        var next = HotPotatoFuseMath.NextTransferTime(Secs(42), Secs(1));

        Assert.That(next, Is.EqualTo(Secs(43)));
    }

    [Test]
    public void TransferChain_CooldownBlocksImmediatePingPong()
    {
        // A pass at t=5 with a 1s cooldown must block the return bump on the next tick
        // (t=5.03) but allow a pass at t=6.
        var cooldown = Secs(1);
        var nextAllowed = HotPotatoFuseMath.NextTransferTime(Secs(5), cooldown);

        Assert.That(HotPotatoFuseMath.CanTransfer(Secs(5.03), nextAllowed), Is.False);
        Assert.That(HotPotatoFuseMath.CanTransfer(Secs(6), nextAllowed), Is.True);
    }

    // --- Remaining: fuse never resets, clamps at zero ---

    [Test]
    public void Remaining_BeforeDetonation_IsDifference()
    {
        var remaining = HotPotatoFuseMath.Remaining(Secs(10), Secs(70));

        Assert.That(remaining, Is.EqualTo(Secs(60)));
    }

    [Test]
    public void Remaining_AtDetonation_IsZero()
    {
        Assert.That(HotPotatoFuseMath.Remaining(Secs(70), Secs(70)), Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void Remaining_PastDetonation_ClampsToZero()
    {
        Assert.That(HotPotatoFuseMath.Remaining(Secs(80), Secs(70)), Is.EqualTo(TimeSpan.Zero));
    }
}
