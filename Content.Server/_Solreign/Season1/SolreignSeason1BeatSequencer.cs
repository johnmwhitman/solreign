namespace Content.Server._Solreign.Season1;

/// <summary>
///     Pure, unit-testable "which PA line is due" calculator shared by the six Season 1 beat station events
///     (<see cref="SolreignLedgerDiscrepancyRule"/>, <see cref="SolreignGreenSeepRule"/>,
///     <see cref="SolreignGhostCabinetsRule"/>, <see cref="SolreignPrestigiousRadiationRule"/>,
///     <see cref="SolreignCosmicTypewriterRule"/>, <see cref="SolreignFinalBalanceSheetRule"/>). No ECS, no I/O, no loc — kept pure so
///     <c>SolreignSeason1BeatSequencerTests</c> can exercise the cadence math without spinning up the game,
///     mirroring how <see cref="Corporate.CorporateScoring"/> is kept pure for the same reason.
///
///     Each beat has a fixed narrative order of PA lines (see the bible's 3-line "AI Announcements" list per
///     beat). Line 0 always fires the instant the event starts; each following line becomes due once another
///     <c>cadenceSeconds</c> has elapsed. The sequence never wraps back to an earlier line and never advances
///     past the last line — callers are expected to track the highest index already announced themselves
///     (see each rule's <c>LastLineIndex</c> field) so this function can be called every tick without
///     re-firing a line that already played.
/// </summary>
public static class SolreignSeason1BeatSequencer
{
    /// <summary>
    ///     The 0-based index (clamped to <c>[0, lineCount - 1]</c>) of the PA line that should be showing once
    ///     <paramref name="elapsedSeconds"/> have passed since the event started. A non-positive
    ///     <paramref name="lineCount"/> is treated as exactly one line (always index 0). A non-positive
    ///     <paramref name="cadenceSeconds"/> is floored to a small positive value so the division below can
    ///     never divide by zero — degenerate config just means every line is immediately "due".
    /// </summary>
    public static int LineIndexForElapsed(float elapsedSeconds, float cadenceSeconds, int lineCount)
    {
        if (lineCount <= 1)
            return 0;

        if (elapsedSeconds <= 0f)
            return 0;

        var safeCadence = MathF.Max(cadenceSeconds, 0.01f);
        var index = (int)(elapsedSeconds / safeCadence);
        return Math.Clamp(index, 0, lineCount - 1);
    }
}
