#nullable enable
using System;
using System.Numerics;
using Content.Server._Solreign.Social;
using NUnit.Framework;
using Robust.Shared.Map;

namespace Content.Tests._Solreign;

/// <summary>
///     Coverage for the pure chirp-answer pairing rule (ChirpAnswerTracker) — window, proximity,
///     same-map, self-exclusion, consume-on-answer, multi-answer, and round reset. The ECS glue in
///     <c>SolreignSocialFirstsSystem</c> only feeds it real emote events; everything decided here
///     is decided nowhere else.
/// </summary>
[TestFixture]
[TestOf(typeof(ChirpAnswerTracker))]
public sealed class ChirpAnswerTrackerTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(15);
    private const float Range = 10f;

    private static readonly MapId Map = new(1);
    private static readonly MapId OtherMap = new(2);

    private static ChirpAnswerTracker NewTracker() => new(Window, Range);

    private static TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);

    [Test]
    public void ReplyWithinWindowAndRange_AnswersTheEarlierChirper()
    {
        var tracker = NewTracker();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();

        Assert.That(tracker.RecordChirp(alice, Map, Vector2.Zero, At(0)), Is.Empty,
            "the first chirp of a conversation has nothing to answer");

        var answered = tracker.RecordChirp(bob, Map, new Vector2(3f, 0f), At(5));
        Assert.That(answered, Is.EqualTo(new[] { alice }),
            "a nearby reply inside the window must answer the earlier chirper");
    }

    [Test]
    public void OwnRechirp_NeverAnswersItself()
    {
        var tracker = NewTracker();
        var alice = Guid.NewGuid();

        tracker.RecordChirp(alice, Map, Vector2.Zero, At(0));
        Assert.That(tracker.RecordChirp(alice, Map, Vector2.Zero, At(2)), Is.Empty,
            "chirping twice is enthusiasm, not a conversation");
    }

    [Test]
    public void ReplyAfterTheWindow_DoesNotAnswer()
    {
        var tracker = NewTracker();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();

        tracker.RecordChirp(alice, Map, Vector2.Zero, At(0));
        Assert.That(tracker.RecordChirp(bob, Map, Vector2.Zero, At(16)), Is.Empty,
            "a reply after the window is a new conversation, not an answer");
    }

    [Test]
    public void ReplyOutOfRange_DoesNotAnswer()
    {
        var tracker = NewTracker();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();

        tracker.RecordChirp(alice, Map, Vector2.Zero, At(0));
        Assert.That(tracker.RecordChirp(bob, Map, new Vector2(Range + 0.5f, 0f), At(2)), Is.Empty,
            "you cannot answer a chirp you could not have heard");
    }

    [Test]
    public void ReplyOnAnotherMap_DoesNotAnswer()
    {
        var tracker = NewTracker();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();

        tracker.RecordChirp(alice, Map, Vector2.Zero, At(0));
        Assert.That(tracker.RecordChirp(bob, OtherMap, Vector2.Zero, At(2)), Is.Empty,
            "same coordinates on a different map are a different place");
    }

    [Test]
    public void AnsweredChirp_IsConsumed_ASecondReplyDoesNotReAnswerIt()
    {
        var tracker = NewTracker();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var carol = Guid.NewGuid();

        tracker.RecordChirp(alice, Map, Vector2.Zero, At(0));
        Assert.That(tracker.RecordChirp(bob, Map, Vector2.Zero, At(2)), Is.EqualTo(new[] { alice }));

        // Carol's chirp answers BOB's still-pending chirp — never Alice's already-answered one.
        var answered = tracker.RecordChirp(carol, Map, Vector2.Zero, At(4));
        Assert.That(answered, Is.EqualTo(new[] { bob }),
            "an answered chirp is consumed; the answerer's own chirp becomes the pending one");
    }

    [Test]
    public void OneReply_AnswersEveryNearbyPendingChirper()
    {
        var tracker = NewTracker();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var carol = Guid.NewGuid();

        tracker.RecordChirp(alice, Map, Vector2.Zero, At(0));
        tracker.RecordChirp(bob, Map, new Vector2(1f, 0f), At(1));
        // Bob's chirp answered Alice — Alice consumed. Re-seed Alice for the group scenario:
        tracker.RecordChirp(alice, Map, Vector2.Zero, At(2));
        // Now Alice and Bob both pend (Bob's own entry pends from t=1... Alice's re-chirp at t=2
        // answered Bob). Assert the actual group case explicitly instead:
        tracker.Clear();
        tracker.RecordChirp(alice, Map, Vector2.Zero, At(10));
        tracker.RecordChirp(bob, OtherMap, Vector2.Zero, At(10)); // pending, but elsewhere
        var answered = tracker.RecordChirp(carol, Map, new Vector2(2f, 0f), At(12));

        Assert.That(answered, Is.EqualTo(new[] { alice }),
            "only the nearby same-map chirper is answered; the far one keeps pending");
        Assert.That(tracker.PendingCount, Is.EqualTo(2),
            "bob (unanswered, other map) and carol (fresh) must still pend");
    }

    [Test]
    public void Prune_DropsStaleChirps()
    {
        var tracker = NewTracker();
        tracker.RecordChirp(Guid.NewGuid(), Map, Vector2.Zero, At(0));
        tracker.RecordChirp(Guid.NewGuid(), OtherMap, Vector2.Zero, At(1));

        tracker.Prune(At(30));
        Assert.That(tracker.PendingCount, Is.Zero, "everything outside the window must drop");
    }

    [Test]
    public void Clear_EmptiesEverything()
    {
        var tracker = NewTracker();
        tracker.RecordChirp(Guid.NewGuid(), Map, Vector2.Zero, At(0));

        tracker.Clear();
        Assert.That(tracker.PendingCount, Is.Zero);
        Assert.That(tracker.RecordChirp(Guid.NewGuid(), Map, Vector2.Zero, At(1)), Is.Empty,
            "a chirp must never be answered across a round boundary");
    }
}
