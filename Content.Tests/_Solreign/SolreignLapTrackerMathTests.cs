using Content.Server._Solreign.Recreation;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(SolreignLapTrackerMath))]
public sealed class SolreignLapTrackerMathTests
{
    private static SolreignLapTrackerMath.LapState Start => new(NextCheckpointOrdinal: 0, LapsCompleted: 0);

    // --- In-order progress ---

    [Test]
    public void HitCheckpoint_FirstExpectedOrdinal_AdvancesNext()
    {
        var next = SolreignLapTrackerMath.HitCheckpoint(Start, hitOrdinal: 0, checkpointCount: 4);

        Assert.That(next.NextCheckpointOrdinal, Is.EqualTo(1));
        Assert.That(next.LapsCompleted, Is.EqualTo(0));
    }

    [Test]
    public void HitCheckpoint_FullLap_CompletesOneLap()
    {
        var state = Start;
        for (var ordinal = 0; ordinal < 4; ordinal++)
            state = SolreignLapTrackerMath.HitCheckpoint(state, ordinal, checkpointCount: 4);

        Assert.That(state.LapsCompleted, Is.EqualTo(1));
        Assert.That(state.NextCheckpointOrdinal, Is.EqualTo(0));
    }

    [Test]
    public void HitCheckpoint_MultipleLaps_AccumulatesLapCount()
    {
        var state = Start;
        for (var lap = 0; lap < 3; lap++)
        {
            for (var ordinal = 0; ordinal < 4; ordinal++)
                state = SolreignLapTrackerMath.HitCheckpoint(state, ordinal, checkpointCount: 4);
        }

        Assert.That(state.LapsCompleted, Is.EqualTo(3));
        Assert.That(state.NextCheckpointOrdinal, Is.EqualTo(0));
    }

    [Test]
    public void HitCheckpoint_SingleCheckpointCourse_EveryHitIsALap()
    {
        var state = Start;
        for (var i = 0; i < 3; i++)
            state = SolreignLapTrackerMath.HitCheckpoint(state, hitOrdinal: 0, checkpointCount: 1);

        Assert.That(state.LapsCompleted, Is.EqualTo(3));
        Assert.That(state.NextCheckpointOrdinal, Is.EqualTo(0));
    }

    // --- Out-of-order / reverse driving rejection ---

    [Test]
    public void HitCheckpoint_SkippedCheckpoint_IsRejected()
    {
        // Expecting ordinal 0, but the kart cut across and hit ordinal 2 instead.
        var state = SolreignLapTrackerMath.HitCheckpoint(Start, hitOrdinal: 2, checkpointCount: 4);

        Assert.That(state, Is.EqualTo(Start));
    }

    [Test]
    public void HitCheckpoint_ReverseDriving_IsRejected()
    {
        var afterFirst = SolreignLapTrackerMath.HitCheckpoint(Start, hitOrdinal: 0, checkpointCount: 4);

        // Drive backwards over the start/finish line again instead of forward to checkpoint 1.
        var reversed = SolreignLapTrackerMath.HitCheckpoint(afterFirst, hitOrdinal: 0, checkpointCount: 4);

        Assert.That(reversed, Is.EqualTo(afterFirst));
    }

    [Test]
    public void HitCheckpoint_RepeatedSameCheckpoint_DoesNotDoubleCount()
    {
        var afterFirst = SolreignLapTrackerMath.HitCheckpoint(Start, hitOrdinal: 0, checkpointCount: 4);
        var repeated = SolreignLapTrackerMath.HitCheckpoint(afterFirst, hitOrdinal: 0, checkpointCount: 4);

        Assert.That(repeated, Is.EqualTo(afterFirst));
    }

    // --- Degenerate input ---

    [Test]
    public void HitCheckpoint_ZeroCheckpointCourse_LeavesStateUnchanged()
    {
        var state = SolreignLapTrackerMath.HitCheckpoint(Start, hitOrdinal: 0, checkpointCount: 0);

        Assert.That(state, Is.EqualTo(Start));
    }

    [Test]
    public void HitCheckpoint_NegativeCheckpointCount_LeavesStateUnchanged()
    {
        var state = SolreignLapTrackerMath.HitCheckpoint(Start, hitOrdinal: 0, checkpointCount: -1);

        Assert.That(state, Is.EqualTo(Start));
    }
}
