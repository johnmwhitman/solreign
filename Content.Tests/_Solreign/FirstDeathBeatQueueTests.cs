#nullable enable
using System;
using System.Linq;
using Content.Server._Solreign.Providence;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Coverage for the pure first-death beat queue — the
///     <see cref="ProvidenceFirstShiftPersonalQueueTests"/> shape: drain-once semantics, due/not-due
///     boundaries, per-kind counting, and the round-boundary clear.
/// </summary>
[TestFixture]
[TestOf(typeof(FirstDeathBeatQueue))]
public sealed class FirstDeathBeatQueueTests
{
    private static FirstDeathPendingBeat Beat(
        FirstDeathBeatKind kind,
        double fireAtSeconds,
        Guid? account = null)
    {
        return new FirstDeathPendingBeat(
            kind,
            account ?? Guid.NewGuid(),
            "public text",
            "private text",
            TimeSpan.FromSeconds(fireAtSeconds));
    }

    [Test]
    public void DrainDue_ReturnsOnlyDueEntries_AndRemovesThem()
    {
        var queue = new FirstDeathBeatQueue();
        queue.Schedule(Beat(FirstDeathBeatKind.DeathScene, 4));
        queue.Schedule(Beat(FirstDeathBeatKind.Rehire, 10));

        var due = queue.DrainDue(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(due, Has.Count.EqualTo(1));
            Assert.That(due[0].Kind, Is.EqualTo(FirstDeathBeatKind.DeathScene));
            Assert.That(queue.Count, Is.EqualTo(1), "the not-yet-due rehire beat must remain queued");
        });
    }

    [Test]
    public void DrainDue_AtExactlyFireAt_IsDue()
    {
        var queue = new FirstDeathBeatQueue();
        queue.Schedule(Beat(FirstDeathBeatKind.DeathScene, 4));

        Assert.That(queue.DrainDue(TimeSpan.FromSeconds(4)), Has.Count.EqualTo(1));
    }

    [Test]
    public void DrainDue_NeverHandsBackTheSameEntryTwice()
    {
        var queue = new FirstDeathBeatQueue();
        queue.Schedule(Beat(FirstDeathBeatKind.DeathScene, 1));

        var first = queue.DrainDue(TimeSpan.FromSeconds(2));
        var second = queue.DrainDue(TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(first, Has.Count.EqualTo(1));
            Assert.That(second, Is.Empty, "a drained beat must never fire twice");
            Assert.That(queue.Count, Is.EqualTo(0));
        });
    }

    [Test]
    public void CountOf_TracksKindsIndependently()
    {
        var queue = new FirstDeathBeatQueue();
        queue.Schedule(Beat(FirstDeathBeatKind.DeathScene, 4));
        queue.Schedule(Beat(FirstDeathBeatKind.Rehire, 6));
        queue.Schedule(Beat(FirstDeathBeatKind.Rehire, 8));

        Assert.Multiple(() =>
        {
            Assert.That(queue.CountOf(FirstDeathBeatKind.DeathScene), Is.EqualTo(1));
            Assert.That(queue.CountOf(FirstDeathBeatKind.Rehire), Is.EqualTo(2));
            Assert.That(queue.Count, Is.EqualTo(3));
        });
    }

    [Test]
    public void Clear_DropsEverything()
    {
        var queue = new FirstDeathBeatQueue();
        queue.Schedule(Beat(FirstDeathBeatKind.DeathScene, 1));
        queue.Schedule(Beat(FirstDeathBeatKind.Rehire, 2));

        queue.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(queue.Count, Is.EqualTo(0));
            Assert.That(queue.DrainDue(TimeSpan.FromDays(1)), Is.Empty,
                "a round boundary must swallow pending beats entirely");
        });
    }

    [Test]
    public void DrainDue_MultipleDueEntries_AllReturned()
    {
        var queue = new FirstDeathBeatQueue();
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        queue.Schedule(Beat(FirstDeathBeatKind.DeathScene, 1, accountA));
        queue.Schedule(Beat(FirstDeathBeatKind.DeathScene, 2, accountB));

        var due = queue.DrainDue(TimeSpan.FromSeconds(3));

        Assert.Multiple(() =>
        {
            Assert.That(due, Has.Count.EqualTo(2));
            Assert.That(due.Select(beat => beat.AccountId), Is.EquivalentTo(new[] { accountA, accountB }));
        });
    }
}
