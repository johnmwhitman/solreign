namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     M3 rank-progression wiring (design spec §4.3, "The Ladder Pays"): folds Solreign Contract
///     completions into the career rank <see cref="RankRules"/> computes — WITHOUT touching
///     <c>RankRules.cs</c>. That file is the frozen, red-teamed formula (thresholds + weights locked by
///     <c>RankRulesTests</c>'s monotonicity proofs); this class only ever FEEDS it an input.
///
///     The spec's target formula is:
///     <code>Score = tours + 2*captainClean + 2*antagWins + standingTotal/5 + contractScore/3</code>
///
///     <see cref="RankRules.Score"/> already computes everything except the last term. Rather than adding
///     a new field/branch to that method, this class pre-divides <c>ContractScore</c> by the spec's own
///     divisor (<see cref="ContractScorePerRankPoint"/>) and folds the *already-floored* result into the
///     <c>Tours</c> input, which <see cref="RankRules.Score"/> weights at exactly 1 with no further
///     division. Because the two terms are independently floored before they ever share an input field,
///     the composition is exact — <c>RankRules.Score(WithContractScoreFedIn(s))</c> equals the spec formula
///     bit-for-bit, not an approximation (see <c>RankProgressionTests</c> for worked cases where naively
///     folding into <c>StandingTotal</c> instead would have rounded differently and drifted from spec).
///
///     Non-negative by construction (<see cref="ContractRules"/>'s never-demote invariant, spec §6.9):
///     <c>PlayerStats.ContractScore</c> is itself a clamped-non-negative accumulator
///     (<c>SeasonLedgerSystem.SubmitContractCompletion</c> floors negative scores to 0), so the folded-in
///     bonus can never be negative and can never lower a rank — <c>RankRulesTests.Monotonic_*</c> already
///     proves that a non-negative bump to <c>Tours</c> never lowers the rank index.
/// </summary>
public static class RankProgression
{
    /// <summary>Points of weighted contract_score worth one point of rank score (spec §4.3, divisor 3).</summary>
    public const int ContractScorePerRankPoint = 3;

    /// <summary>
    ///     Returns a copy of <paramref name="stats"/> with its <c>ContractScore</c> folded into
    ///     <c>Tours</c> at the spec's contractScore/3 weight, ready to hand to
    ///     <see cref="RankRules.Score"/>/<see cref="RankRules.Compute"/>. <c>ContractScore</c> itself is
    ///     left untouched on the returned value (it still round-trips for display/audit purposes) — only
    ///     the derived <c>Tours</c> input changes.
    /// </summary>
    public static PlayerStats WithContractScoreFedIn(PlayerStats stats)
    {
        // ContractScore is already guaranteed non-negative (never-demote invariant), but guard anyway —
        // this feed must never be able to subtract from a career's Tours.
        var bonus = stats.ContractScore > 0 ? stats.ContractScore / ContractScorePerRankPoint : 0;
        return stats with { Tours = stats.Tours + bonus };
    }

    /// <summary>
    ///     Drop-in replacement for a bare <c>RankRules.Compute(stats)</c> call at any site that wants
    ///     Solreign Contracts to count toward career rank (spec §4.3). Feeds the input, then hands off to
    ///     the untouched <see cref="RankRules.Compute"/>.
    /// </summary>
    public static (CorporateRank Rank, string Name, int Index) ComputeCareerRank(PlayerStats stats)
    {
        return RankRules.Compute(WithContractScoreFedIn(stats));
    }
}
