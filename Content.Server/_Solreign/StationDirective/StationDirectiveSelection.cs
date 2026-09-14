namespace Content.Server._Solreign.StationDirective;

/// <summary>
///     Pure, unit-testable selection logic for the Station Directive system — no ECS, no I/O, no loc.
///     Kept separate from <see cref="StationDirectiveRuleSystem"/> so <c>StationDirectiveSelectionTests</c>
///     can exercise rotation and anti-spam gating without spinning up the game (same split as
///     <c>Corporate.CorporateScoring</c> / <c>Providence.ProvidenceWelcomeGate</c>).
/// </summary>
public static class StationDirectiveSelection
{
    /// <summary>
    ///     Picks which directive plays this round, round-robin by round id, so the "small rotating set"
    ///     cycles through every directive before any repeats rather than relying on chance. Deterministic
    ///     for a given (roundId, directiveCount) pair. A non-positive <paramref name="directiveCount"/>
    ///     degenerates to index 0 (caller should treat that as "nothing to pick").
    /// </summary>
    public static int SelectDirectiveIndex(int roundId, int directiveCount)
    {
        if (directiveCount <= 0)
            return 0;

        // C#'s % can return negative for negative operands (e.g. a rolled-back/dev round id of -1);
        // fold into [0, directiveCount) so this is always a valid array index.
        var index = roundId % directiveCount;
        return index < 0 ? index + directiveCount : index;
    }

    /// <summary>
    ///     Advances the ticker line index cyclically so back-to-back PA tickers for the same directive
    ///     never repeat the same flavor line. A non-positive <paramref name="lineCount"/> degenerates to
    ///     index 0.
    /// </summary>
    public static int NextTickerIndex(int currentIndex, int lineCount)
    {
        if (lineCount <= 0)
            return 0;

        var next = currentIndex + 1;
        return next >= lineCount ? 0 : next;
    }

    /// <summary>
    ///     Anti-spam gate: whether enough time has passed to fire the next PA ticker line. A non-positive
    ///     <paramref name="intervalSeconds"/> (e.g. a misconfigured CVar) always returns false, so the
    ///     ticker fails closed to silence rather than spamming every tick.
    /// </summary>
    public static bool ShouldFireTicker(float secondsSinceLastTicker, float intervalSeconds)
    {
        return intervalSeconds > 0f && secondsSinceLastTicker >= intervalSeconds;
    }
}
