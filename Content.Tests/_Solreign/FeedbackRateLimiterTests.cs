using System;
using Content.Server._Solreign.Feedback;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(FeedbackRateLimiter))]
public sealed class FeedbackRateLimiterTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private const int Max = 5;

    [Test]
    public void FirstSubmission_IsAlwaysAllowed()
    {
        var limiter = new FeedbackRateLimiter();

        var allowed = limiter.TryConsume(Alice, roundId: 1, Max, out var remaining);

        Assert.That(allowed, Is.True);
        Assert.That(remaining, Is.EqualTo(Max - 1));
    }

    [Test]
    public void UpToMax_SubmissionsInSameRound_AreAllAllowed()
    {
        var limiter = new FeedbackRateLimiter();

        for (var i = 0; i < Max; i++)
            Assert.That(limiter.TryConsume(Alice, roundId: 1, Max, out _), Is.True, $"submission {i} should be allowed");
    }

    [Test]
    public void SubmissionPastMax_InSameRound_IsBlocked()
    {
        var limiter = new FeedbackRateLimiter();
        for (var i = 0; i < Max; i++)
            limiter.TryConsume(Alice, roundId: 1, Max, out _);

        var allowed = limiter.TryConsume(Alice, roundId: 1, Max, out var remaining);

        Assert.That(allowed, Is.False);
        Assert.That(remaining, Is.EqualTo(0));
    }

    [Test]
    public void NewRound_ResetsTheCounter()
    {
        var limiter = new FeedbackRateLimiter();
        for (var i = 0; i < Max; i++)
            limiter.TryConsume(Alice, roundId: 1, Max, out _);

        // Round 1 exhausted; round 2 should start fresh.
        var allowed = limiter.TryConsume(Alice, roundId: 2, Max, out var remaining);

        Assert.That(allowed, Is.True);
        Assert.That(remaining, Is.EqualTo(Max - 1));
    }

    [Test]
    public void DifferentPlayers_DoNotShareALimit()
    {
        var limiter = new FeedbackRateLimiter();
        for (var i = 0; i < Max; i++)
            limiter.TryConsume(Alice, roundId: 1, Max, out _);

        var allowed = limiter.TryConsume(Bob, roundId: 1, Max, out var remaining);

        Assert.That(allowed, Is.True);
        Assert.That(remaining, Is.EqualTo(Max - 1));
    }

    [Test]
    public void Forget_ClearsTrackedState_SoNextSubmissionStartsFresh()
    {
        var limiter = new FeedbackRateLimiter();
        for (var i = 0; i < Max; i++)
            limiter.TryConsume(Alice, roundId: 1, Max, out _);

        limiter.Forget(Alice);

        var allowed = limiter.TryConsume(Alice, roundId: 1, Max, out var remaining);
        Assert.That(allowed, Is.True);
        Assert.That(remaining, Is.EqualTo(Max - 1));
    }

    [Test]
    public void Forget_UnknownPlayer_DoesNotThrow()
    {
        var limiter = new FeedbackRateLimiter();

        Assert.DoesNotThrow(() => limiter.Forget(Bob));
    }

    [Test]
    public void RepeatedBlockedAttempts_DoNotResurrectCapacity()
    {
        var limiter = new FeedbackRateLimiter();
        for (var i = 0; i < Max; i++)
            limiter.TryConsume(Alice, roundId: 1, Max, out _);

        limiter.TryConsume(Alice, roundId: 1, Max, out _);
        var allowed = limiter.TryConsume(Alice, roundId: 1, Max, out var remaining);

        Assert.That(allowed, Is.False);
        Assert.That(remaining, Is.EqualTo(0));
    }
}
