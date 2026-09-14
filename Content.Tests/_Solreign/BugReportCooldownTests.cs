using System;
using Content.Server._Solreign.BugReport;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(BugReportCooldown))]
public sealed class BugReportCooldownTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);

    [Test]
    public void FirstSubmission_IsAlwaysAllowed()
    {
        var cooldown = new BugReportCooldown();

        var allowed = cooldown.TryConsume(Alice, TimeSpan.FromSeconds(100), Cooldown, out var remaining);

        Assert.That(allowed, Is.True);
        Assert.That(remaining, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void SecondSubmission_WithinCooldown_IsBlocked()
    {
        var cooldown = new BugReportCooldown();
        cooldown.TryConsume(Alice, TimeSpan.FromSeconds(0), Cooldown, out _);

        var allowed = cooldown.TryConsume(Alice, TimeSpan.FromSeconds(29), Cooldown, out var remaining);

        Assert.That(allowed, Is.False);
        Assert.That(remaining, Is.EqualTo(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void SecondSubmission_ExactlyAtCooldownBoundary_IsAllowed()
    {
        var cooldown = new BugReportCooldown();
        cooldown.TryConsume(Alice, TimeSpan.FromSeconds(0), Cooldown, out _);

        var allowed = cooldown.TryConsume(Alice, TimeSpan.FromSeconds(30), Cooldown, out var remaining);

        Assert.That(allowed, Is.True);
        Assert.That(remaining, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void SecondSubmission_AfterCooldownElapses_IsAllowed()
    {
        var cooldown = new BugReportCooldown();
        cooldown.TryConsume(Alice, TimeSpan.FromSeconds(0), Cooldown, out _);

        var allowed = cooldown.TryConsume(Alice, TimeSpan.FromSeconds(31), Cooldown, out var remaining);

        Assert.That(allowed, Is.True);
        Assert.That(remaining, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void DifferentPlayers_DoNotShareACooldown()
    {
        var cooldown = new BugReportCooldown();
        cooldown.TryConsume(Alice, TimeSpan.FromSeconds(0), Cooldown, out _);

        var allowed = cooldown.TryConsume(Bob, TimeSpan.FromSeconds(1), Cooldown, out var remaining);

        Assert.That(allowed, Is.True);
        Assert.That(remaining, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void Forget_ClearsTrackedState_SoNextSubmissionIsAllowedImmediately()
    {
        var cooldown = new BugReportCooldown();
        cooldown.TryConsume(Alice, TimeSpan.FromSeconds(0), Cooldown, out _);

        cooldown.Forget(Alice);

        var allowed = cooldown.TryConsume(Alice, TimeSpan.FromSeconds(1), Cooldown, out var remaining);
        Assert.That(allowed, Is.True);
        Assert.That(remaining, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void Forget_UnknownPlayer_DoesNotThrow()
    {
        var cooldown = new BugReportCooldown();

        Assert.DoesNotThrow(() => cooldown.Forget(Bob));
    }

    [Test]
    public void RepeatedBlockedAttempts_DoNotResetTheOriginalTimestamp()
    {
        var cooldown = new BugReportCooldown();
        cooldown.TryConsume(Alice, TimeSpan.FromSeconds(0), Cooldown, out _);

        // Two blocked attempts in a row must not push the cooldown window back.
        cooldown.TryConsume(Alice, TimeSpan.FromSeconds(5), Cooldown, out _);
        var allowed = cooldown.TryConsume(Alice, TimeSpan.FromSeconds(31), Cooldown, out var remaining);

        Assert.That(allowed, Is.True);
        Assert.That(remaining, Is.EqualTo(TimeSpan.Zero));
    }
}
