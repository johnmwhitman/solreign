#nullable enable
using System;
using Content.Server._Solreign.Social;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.Tests._Solreign;

/// <summary>
///     Coverage for the pure hand-off resolution rule (HandOffTracker) — window, self-exclusion,
///     consume-on-resolve, latest-release-wins, prune, and round reset. The ECS glue in
///     <c>SolreignSocialFirstsSystem</c> only feeds it real hand equip/unequip events.
/// </summary>
[TestFixture]
[TestOf(typeof(HandOffTracker))]
public sealed class HandOffTrackerTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(20);

    private static HandOffTracker NewTracker() => new(Window);

    private static TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);

    [Test]
    public void PickupWithinWindow_ByAnotherAccount_ResolvesTheGiver()
    {
        var tracker = NewTracker();
        var item = new EntityUid(42);
        var giver = Guid.NewGuid();
        var receiver = Guid.NewGuid();

        tracker.RecordRelease(item, giver, At(0));

        Assert.That(tracker.TryTakeGiver(item, receiver, At(5), out var resolved), Is.True);
        Assert.That(resolved, Is.EqualTo(giver));
    }

    [Test]
    public void ResolvedRelease_IsConsumed_ASecondPickupFindsNothing()
    {
        var tracker = NewTracker();
        var item = new EntityUid(42);
        var giver = Guid.NewGuid();
        var receiver = Guid.NewGuid();

        tracker.RecordRelease(item, giver, At(0));
        Assert.That(tracker.TryTakeGiver(item, receiver, At(5), out _), Is.True);
        Assert.That(tracker.TryTakeGiver(item, receiver, At(6), out _), Is.False,
            "one release is one hand-off — never two");
    }

    [Test]
    public void PickingUpYourOwnItem_ResolvesNothing_AndClearsTheEntry()
    {
        var tracker = NewTracker();
        var item = new EntityUid(7);
        var owner = Guid.NewGuid();
        var later = Guid.NewGuid();

        tracker.RecordRelease(item, owner, At(0));
        Assert.That(tracker.TryTakeGiver(item, owner, At(2), out _), Is.False,
            "taking your own item back is housekeeping, not a gift");
        Assert.That(tracker.TryTakeGiver(item, later, At(3), out _), Is.False,
            "the reclaimed item's stale release must not mis-credit a later pickup");
    }

    [Test]
    public void PickupAfterTheWindow_ResolvesNothing()
    {
        var tracker = NewTracker();
        var item = new EntityUid(9);

        tracker.RecordRelease(item, Guid.NewGuid(), At(0));
        Assert.That(tracker.TryTakeGiver(item, Guid.NewGuid(), At(21), out _), Is.False,
            "an item found a minute later was found, not handed");
    }

    [Test]
    public void NewerRelease_Overwrites_TheLastHolderIsTheGiver()
    {
        var tracker = NewTracker();
        var item = new EntityUid(11);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var receiver = Guid.NewGuid();

        tracker.RecordRelease(item, first, At(0));
        tracker.RecordRelease(item, second, At(3));

        Assert.That(tracker.TryTakeGiver(item, receiver, At(4), out var resolved), Is.True);
        Assert.That(resolved, Is.EqualTo(second), "the LAST hands the item left are the giver");
    }

    [Test]
    public void UnknownItem_ResolvesNothing()
    {
        var tracker = NewTracker();
        Assert.That(tracker.TryTakeGiver(new EntityUid(1), Guid.NewGuid(), At(0), out _), Is.False);
    }

    [Test]
    public void Prune_DropsStaleReleases_KeepsFreshOnes()
    {
        var tracker = NewTracker();
        tracker.RecordRelease(new EntityUid(1), Guid.NewGuid(), At(0));
        tracker.RecordRelease(new EntityUid(2), Guid.NewGuid(), At(25));

        tracker.Prune(At(30));
        Assert.That(tracker.PendingCount, Is.EqualTo(1),
            "only the release outside the window may drop");
    }

    [Test]
    public void Clear_EmptiesEverything()
    {
        var tracker = NewTracker();
        var item = new EntityUid(3);
        tracker.RecordRelease(item, Guid.NewGuid(), At(0));

        tracker.Clear();
        Assert.That(tracker.PendingCount, Is.Zero);
        Assert.That(tracker.TryTakeGiver(item, Guid.NewGuid(), At(1), out _), Is.False,
            "a release must never resolve across a round boundary");
    }
}
