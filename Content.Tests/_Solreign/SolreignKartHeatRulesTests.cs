using System;
using Content.Server._Solreign.Recreation;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(SolreignKartHeatRules))]
public sealed class SolreignKartHeatRulesTests
{
    [TestCase(false, true, false, false, TestName = "Disabled_FinishedNewDriver_DoesNotReset")]
    [TestCase(true, false, false, false, TestName = "Enabled_UnfinishedNewDriver_DoesNotReset")]
    [TestCase(true, true, true, false, TestName = "Enabled_FinishedSameDriver_DoesNotReset")]
    [TestCase(true, true, false, true, TestName = "Enabled_FinishedNewDriver_Resets")]
    public void ShouldResetForDriver(
        bool enabled,
        bool finished,
        bool sameAsFinishingDriver,
        bool expected)
    {
        Assert.That(
            SolreignKartHeatRules.ShouldResetForDriver(enabled, finished, sameAsFinishingDriver),
            Is.EqualTo(expected));
    }

    [Test]
    public void FreezeElapsed_StartedHeat_ReturnsFrozenDuration()
    {
        var startedAt = TimeSpan.FromSeconds(12);
        var finishedAt = TimeSpan.FromSeconds(19.5);

        Assert.That(
            SolreignKartHeatRules.FreezeElapsed(startedAt, finishedAt),
            Is.EqualTo(TimeSpan.FromSeconds(7.5)));
    }

    [Test]
    public void FreezeElapsed_HeatNeverStarted_ReturnsNull()
    {
        Assert.That(
            SolreignKartHeatRules.FreezeElapsed(null, TimeSpan.FromSeconds(20)),
            Is.Null);
    }

    [Test]
    public void FreezeElapsed_ClockMovedBack_ClampsToZero()
    {
        Assert.That(
            SolreignKartHeatRules.FreezeElapsed(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(19)),
            Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void ShouldStartTiming_FirstValidOrdinalZero_Starts()
    {
        var before = new SolreignLapTrackerMath.LapState(0, 0);
        var after = SolreignLapTrackerMath.HitCheckpoint(before, 0, 4);

        Assert.That(
            SolreignKartHeatRules.ShouldStartTiming(true, 3, null, before, after, 0),
            Is.True);
    }

    [Test]
    public void ShouldStartTiming_Disabled_DoesNotStart()
    {
        var before = new SolreignLapTrackerMath.LapState(0, 0);
        var after = SolreignLapTrackerMath.HitCheckpoint(before, 0, 4);

        Assert.That(
            SolreignKartHeatRules.ShouldStartTiming(false, 3, null, before, after, 0),
            Is.False);
    }

    [Test]
    public void ShouldStartTiming_RejectedCheckpoint_DoesNotStart()
    {
        var before = new SolreignLapTrackerMath.LapState(0, 0);

        Assert.That(
            SolreignKartHeatRules.ShouldStartTiming(true, 3, null, before, before, 2),
            Is.False);
    }

    [Test]
    public void ShouldStartTiming_AlreadyStarted_DoesNotRestart()
    {
        var before = new SolreignLapTrackerMath.LapState(0, 1);
        var after = SolreignLapTrackerMath.HitCheckpoint(before, 0, 4);

        Assert.That(
            SolreignKartHeatRules.ShouldStartTiming(
                true,
                3,
                TimeSpan.FromSeconds(5),
                before,
                after,
                0),
            Is.False);
    }

    [Test]
    public void ShouldStartTiming_InvalidLapCount_FallsBackToLegacy()
    {
        var before = new SolreignLapTrackerMath.LapState(0, 0);
        var after = SolreignLapTrackerMath.HitCheckpoint(before, 0, 4);

        Assert.That(
            SolreignKartHeatRules.ShouldStartTiming(true, 0, null, before, after, 0),
            Is.False);
    }

    [Test]
    public void TimedFinish_WaitsForOrdinalZeroAfterFinalCircuit()
    {
        var beforeLastCheckpoint = new SolreignLapTrackerMath.LapState(3, 2);
        var afterLastCheckpoint =
            SolreignLapTrackerMath.HitCheckpoint(beforeLastCheckpoint, 3, 4);

        Assert.That(
            SolreignKartHeatRules.IsTimedFinishLineCrossing(
                true,
                beforeLastCheckpoint,
                afterLastCheckpoint,
                3,
                totalLaps: 3),
            Is.False,
            "the final nonzero checkpoint is not the physical finish line");

        var afterFinishLine =
            SolreignLapTrackerMath.HitCheckpoint(afterLastCheckpoint, 0, 4);
        Assert.That(
            SolreignKartHeatRules.IsTimedFinishLineCrossing(
                true,
                afterLastCheckpoint,
                afterFinishLine,
                0,
                totalLaps: 3),
            Is.True);
    }

    [Test]
    public void TimedFinish_Disabled_PreservesLegacyBoundary()
    {
        var before = new SolreignLapTrackerMath.LapState(0, 3);
        var after = SolreignLapTrackerMath.HitCheckpoint(before, 0, 4);

        Assert.That(
            SolreignKartHeatRules.IsTimedFinishLineCrossing(false, before, after, 0, 3),
            Is.False);
    }
}
