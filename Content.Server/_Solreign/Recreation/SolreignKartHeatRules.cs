namespace Content.Server._Solreign.Recreation;

/// <summary>
/// Pure transition rules for optional repeatable, server-timed kart heats.
/// </summary>
public static class SolreignKartHeatRules
{
    public static bool ShouldResetForDriver(
        bool enabled,
        bool finished,
        bool sameAsFinishingDriver)
    {
        return enabled && finished && !sameAsFinishingDriver;
    }

    public static bool ShouldStartTiming(
        bool enabled,
        int totalLaps,
        TimeSpan? startedAt,
        SolreignLapTrackerMath.LapState before,
        SolreignLapTrackerMath.LapState after,
        int hitOrdinal)
    {
        return enabled
            && totalLaps > 0
            && startedAt == null
            && hitOrdinal == 0
            && before.NextCheckpointOrdinal == 0
            && before.LapsCompleted == 0
            && after != before;
    }

    public static bool IsTimedFinishLineCrossing(
        bool heatUsesTimedFinish,
        SolreignLapTrackerMath.LapState before,
        SolreignLapTrackerMath.LapState after,
        int hitOrdinal,
        int totalLaps)
    {
        return heatUsesTimedFinish
            && totalLaps > 0
            && hitOrdinal == 0
            && before.NextCheckpointOrdinal == 0
            && before.LapsCompleted >= totalLaps
            && after != before;
    }

    public static TimeSpan? FreezeElapsed(TimeSpan? startedAt, TimeSpan finishedAt)
    {
        if (startedAt == null)
            return null;

        return TimeSpan.FromTicks(Math.Max(0, (finishedAt - startedAt.Value).Ticks));
    }
}
