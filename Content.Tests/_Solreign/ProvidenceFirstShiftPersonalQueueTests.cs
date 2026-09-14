using System;
using Content.Server._Solreign.Providence;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(ProvidenceFirstShiftPersonalQueue))]
public sealed class ProvidenceFirstShiftPersonalQueueTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly EntityUid MobA = new(101);
    private static readonly EntityUid MobB = new(202);

    [Test]
    public void DrainDue_BeforeFireTime_ReturnsNothing_AndKeepsTheEntryQueued()
    {
        var queue = new ProvidenceFirstShiftPersonalQueue();
        queue.Schedule(Alice, MobA, TimeSpan.FromSeconds(10));

        var due = queue.DrainDue(TimeSpan.FromSeconds(5));

        Assert.That(due, Is.Empty);
        Assert.That(queue.Count, Is.EqualTo(1));
    }

    [Test]
    public void DrainDue_AtOrAfterFireTime_ReturnsTheEntry_AndRemovesItFromTheQueue()
    {
        var queue = new ProvidenceFirstShiftPersonalQueue();
        queue.Schedule(Alice, MobA, TimeSpan.FromSeconds(10));

        var due = queue.DrainDue(TimeSpan.FromSeconds(10));

        Assert.That(due, Has.Count.EqualTo(1));
        Assert.That(due[0].AccountId, Is.EqualTo(Alice));
        Assert.That(due[0].SpawnMob, Is.EqualTo(MobA));
        Assert.That(queue.Count, Is.EqualTo(0));
    }

    [Test]
    public void DrainDue_CalledTwice_NeverReturnsTheSameEntryTwice()
    {
        // The exact failure mode this queue exists to prevent: a due entry must be consumed
        // (removed) before the caller's side effects run, so even a re-entrant/duplicate Update
        // tick can never double-fire the same personal beat.
        var queue = new ProvidenceFirstShiftPersonalQueue();
        queue.Schedule(Alice, MobA, TimeSpan.FromSeconds(10));

        var first = queue.DrainDue(TimeSpan.FromSeconds(10));
        var second = queue.DrainDue(TimeSpan.FromSeconds(10));

        Assert.That(first, Has.Count.EqualTo(1));
        Assert.That(second, Is.Empty);
    }

    [Test]
    public void DrainDue_OnlyDrainsEntriesThatAreActuallyDue_LeavesOthersQueued()
    {
        var queue = new ProvidenceFirstShiftPersonalQueue();
        queue.Schedule(Alice, MobA, TimeSpan.FromSeconds(10));
        queue.Schedule(Bob, MobB, TimeSpan.FromSeconds(20));

        var due = queue.DrainDue(TimeSpan.FromSeconds(15));

        Assert.That(due, Has.Count.EqualTo(1));
        Assert.That(due[0].AccountId, Is.EqualTo(Alice));
        Assert.That(queue.Count, Is.EqualTo(1));

        var laterDue = queue.DrainDue(TimeSpan.FromSeconds(20));
        Assert.That(laterDue, Has.Count.EqualTo(1));
        Assert.That(laterDue[0].AccountId, Is.EqualTo(Bob));
    }

    [Test]
    public void Clear_RemovesAllPendingEntries_SoARoundBoundaryNeverLeaksAFire()
    {
        var queue = new ProvidenceFirstShiftPersonalQueue();
        queue.Schedule(Alice, MobA, TimeSpan.FromSeconds(10));
        queue.Schedule(Bob, MobB, TimeSpan.FromSeconds(20));

        queue.Clear();

        Assert.That(queue.Count, Is.EqualTo(0));
        Assert.That(queue.DrainDue(TimeSpan.FromSeconds(1000)), Is.Empty);
    }

    [Test]
    public void Schedule_SamePlayerTwice_QueuesBothEntries_QueueDoesNotDedupe()
    {
        // Dedup/one-shot-per-round is the SYSTEM's responsibility (ProvidenceWelcomeGate is already
        // consulted before scheduling ever happens) — this pure queue is intentionally dumb storage,
        // not a second anti-fatigue layer, so it must not silently swallow a second Schedule call.
        var queue = new ProvidenceFirstShiftPersonalQueue();

        queue.Schedule(Alice, MobA, TimeSpan.FromSeconds(10));
        queue.Schedule(Alice, MobA, TimeSpan.FromSeconds(10));

        Assert.That(queue.Count, Is.EqualTo(2));
    }
}
