using System;
using Content.Server._Solreign.PlayerDelight.Wingmates;
using NUnit.Framework;
using Robust.Shared.Network;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(WingmateFixedWindowLimiter))]
public sealed class WingmateFixedWindowLimiterTests
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    [Test]
    public void FreshEntriesAreNotPrunedWhileStillInsideTheirWindow()
    {
        var limiter = new WingmateFixedWindowLimiter(3, Window);
        var now = TimeSpan.Zero;

        for (var i = 0; i < 200; i++)
            limiter.TryConsume(new NetUserId(Guid.NewGuid()), now);

        Assert.That(limiter.WindowCountForTests, Is.EqualTo(200),
            "nothing has expired yet, so an opportunistic sweep must not prune anything");
    }

    [Test]
    public void ExpiredEntriesFromUniqueAccountHoppingArePrunedRatherThanAccumulatingForever()
    {
        var limiter = new WingmateFixedWindowLimiter(3, Window);
        var now = TimeSpan.Zero;

        // A first wave of unique accounts, each consuming once at t=0.
        for (var i = 0; i < 200; i++)
            limiter.TryConsume(new NetUserId(Guid.NewGuid()), now);

        // Advance well past the window, then churn a second wave of unique accounts at the new time.
        // Enough calls occur here to guarantee at least one opportunistic sweep pass (SweepInterval is
        // an internal implementation constant well under 200), and every entry from the first wave is
        // fully expired relative to this new `now`.
        now += Window + TimeSpan.FromSeconds(1);
        for (var i = 0; i < 200; i++)
            limiter.TryConsume(new NetUserId(Guid.NewGuid()), now);

        Assert.That(limiter.WindowCountForTests, Is.LessThanOrEqualTo(200),
            "unique-account hopping across an expired window must not grow the map monotonically — " +
            "the first wave's now-expired entries must be swept, leaving at most the live second wave");
    }

    [Test]
    public void ReconnectWithinTheActiveWindowStillSeesConsumedQuotaAfterAPruneSweep()
    {
        var limiter = new WingmateFixedWindowLimiter(2, Window);
        var user = new NetUserId(Guid.NewGuid());
        var now = TimeSpan.Zero;

        Assert.That(limiter.TryConsume(user, now), Is.True);
        Assert.That(limiter.TryConsume(user, now), Is.True);

        // Churn enough distinct, still-in-window accounts (same `now`, so none of them are prunable
        // either) to force at least one opportunistic sweep pass without ever advancing past the
        // original user's own window.
        for (var i = 0; i < 200; i++)
            limiter.TryConsume(new NetUserId(Guid.NewGuid()), now);

        // A sweep must never evict an entry that has not yet expired — otherwise a reconnecting player
        // would silently regain quota mid-window, defeating the whole point of keying by account.
        Assert.That(limiter.TryConsume(user, now), Is.False,
            "a still-active window's consumed quota must survive an opportunistic prune sweep");
    }

    [Test]
    public void ClearResetsBothTheWindowsAndThePruneCounter()
    {
        var limiter = new WingmateFixedWindowLimiter(1, Window);
        for (var i = 0; i < 10; i++)
            limiter.TryConsume(new NetUserId(Guid.NewGuid()), TimeSpan.Zero);

        limiter.Clear();

        Assert.That(limiter.WindowCountForTests, Is.Zero);
        var user = new NetUserId(Guid.NewGuid());
        Assert.That(limiter.TryConsume(user, TimeSpan.Zero), Is.True);
    }
}
