namespace Content.Server._Solreign.Season1;

/// <summary>
///     Pure, unit-testable "does this crew member's Season Ledger rank qualify for Beat 4's
///     bioluminescence" predicate — kept separate from <see cref="SolreignPrestigiousRadiationRule"/> so
///     <c>SolreignSeason1PrestigeRulesTests</c> can exercise the threshold check without spinning up the
///     game, mirroring how <see cref="SolreignSeason1BeatSequencer"/> is kept pure for the PA-line cadence
///     math. See docs/research/2026-07-11-season1-narrative-bible.md Section 1, Beat 4 ("Prestigious
///     Radiation") and <c>Content.Shared._Solreign.SeasonLedger.SeasonTitleComponent.RankIndex</c> for
///     what a rank index means (0 = Probationary Asset … 6 = Board Member, see
///     <c>Content.Server._Solreign.SeasonLedger.RankRules.CorporateRank</c>).
/// </summary>
public static class SolreignSeason1PrestigeRules
{
    /// <summary>
    ///     True once <paramref name="rankIndex"/> meets or exceeds <paramref name="threshold"/>. Plain
    ///     ordinal comparison, no clamping — a fresh account's default RankIndex is 0 and the ladder
    ///     currently tops out at 6, but this makes no assumption about either bound, so a future rung
    ///     added to the ladder (or a modified threshold) needs no change here.
    /// </summary>
    public static bool QualifiesForBioluminescence(int rankIndex, int threshold) => rankIndex >= threshold;
}
