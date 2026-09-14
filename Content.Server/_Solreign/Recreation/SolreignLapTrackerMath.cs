namespace Content.Server._Solreign.Recreation;

/// <summary>
/// Pure checkpoint-ordinal validation for go-kart lap tracking — gap #2 in
/// docs/specs/2026-07-11-recreation-spec.md. Kept dependency-free (no EntityUid, no components) so
/// NUnit can exercise it directly, per the spec's own note that this is "the actual logic worth
/// NUnit-testing: pure 'given current index + hit index + course length, what's the new state'
/// function, extractable with no ECS dependency". <see cref="SolreignLapTrackerSystem"/> just reads
/// its own ECS state into a <see cref="LapState"/> and calls into here.
/// </summary>
public static class SolreignLapTrackerMath
{
    /// <summary>
    /// A kart's progress around a course: which checkpoint ordinal it needs to hit next, and how
    /// many full laps it has completed so far.
    /// </summary>
    public readonly record struct LapState(int NextCheckpointOrdinal, int LapsCompleted);

    /// <summary>
    /// Advance lap state given a checkpoint hit. Checkpoints are ordinals <c>0..checkpointCount-1</c>
    /// in course order, with ordinal 0 doubling as the start/finish line. Rejects any hit that isn't
    /// exactly the next-expected ordinal — that covers both out-of-order hits (skipped a checkpoint,
    /// e.g. cut across the infield) and reverse driving (hit ordinal N-1 again instead of N) by
    /// simply leaving the state unchanged; neither counts as progress. Wrapping from the last
    /// checkpoint back around to ordinal 0 completes one lap.
    /// </summary>
    public static LapState HitCheckpoint(LapState state, int hitOrdinal, int checkpointCount)
    {
        if (checkpointCount <= 0)
            return state; // degenerate/unconfigured course — ignore rather than divide by zero

        if (hitOrdinal != state.NextCheckpointOrdinal)
            return state; // wrong checkpoint: out of order or reverse driving, ignored

        var nextOrdinal = (state.NextCheckpointOrdinal + 1) % checkpointCount;
        var laps = state.LapsCompleted;
        if (nextOrdinal == 0)
            laps++; // wrapped back to the start/finish line: one full lap done

        return new LapState(nextOrdinal, laps);
    }
}
