using Content.Server._Solreign.SeasonLedger;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     M3 rank-progression wiring (design spec §4.3): <see cref="RankProgression"/> feeds Solreign Contract
///     completions into <see cref="RankRules"/> without touching that (frozen) file. These tests pin the
///     exact-formula claim in <see cref="RankProgression"/>'s doc comment: folding contract_score into
///     Tours reproduces <c>tours + 2*captainClean + 2*antagWins + standingTotal/5 + contractScore/3</c>
///     bit-for-bit, including cases where a naive fold into StandingTotal would have rounded differently.
/// </summary>
[TestFixture]
[TestOf(typeof(RankProgression))]
public sealed class RankProgressionTests
{
    // --- WithContractScoreFedIn: the folding itself ---

    [Test]
    public void WithContractScoreFedIn_ZeroContractScore_LeavesToursUnchanged()
    {
        var stats = new PlayerStats(10, 0, 0, 0, StandingTotal: 0, ContractsCompleted: 0, ContractScore: 0);
        var fed = RankProgression.WithContractScoreFedIn(stats);

        Assert.That(fed.Tours, Is.EqualTo(10));
    }

    [Test]
    public void WithContractScoreFedIn_FloorsAtTheSpecDivisor()
    {
        // Divisor is 3 (spec §4.3): 2 -> +0, 3 -> +1, 5 -> +1, 6 -> +2.
        Assert.Multiple(() =>
        {
            Assert.That(RankProgression.WithContractScoreFedIn(new PlayerStats(0, 0, 0, 0, 0, 0, 2)).Tours,
                Is.EqualTo(0));
            Assert.That(RankProgression.WithContractScoreFedIn(new PlayerStats(0, 0, 0, 0, 0, 0, 3)).Tours,
                Is.EqualTo(1));
            Assert.That(RankProgression.WithContractScoreFedIn(new PlayerStats(0, 0, 0, 0, 0, 0, 5)).Tours,
                Is.EqualTo(1));
            Assert.That(RankProgression.WithContractScoreFedIn(new PlayerStats(0, 0, 0, 0, 0, 0, 6)).Tours,
                Is.EqualTo(2));
        });
    }

    [Test]
    public void WithContractScoreFedIn_AddsOnTopOfExistingTours()
    {
        var stats = new PlayerStats(4, 0, 0, 0, 0, 0, 9); // +3 from contract score
        var fed = RankProgression.WithContractScoreFedIn(stats);

        Assert.That(fed.Tours, Is.EqualTo(7));
    }

    [Test]
    public void WithContractScoreFedIn_LeavesContractScoreItselfUntouched()
    {
        // ContractScore still round-trips on the returned value for display/audit callers.
        var stats = new PlayerStats(0, 0, 0, 0, 0, 0, 9);
        var fed = RankProgression.WithContractScoreFedIn(stats);

        Assert.That(fed.ContractScore, Is.EqualTo(9));
    }

    [Test]
    public void WithContractScoreFedIn_OtherFieldsPassThroughUnchanged()
    {
        var stats = new PlayerStats(2, 3, 4, 5, StandingTotal: 20, ContractsCompleted: 7, ContractScore: 6);
        var fed = RankProgression.WithContractScoreFedIn(stats);

        Assert.Multiple(() =>
        {
            Assert.That(fed.CaptainClean, Is.EqualTo(3));
            Assert.That(fed.AntagWins, Is.EqualTo(4));
            Assert.That(fed.EarlyDeaths, Is.EqualTo(5));
            Assert.That(fed.StandingTotal, Is.EqualTo(20));
            Assert.That(fed.ContractsCompleted, Is.EqualTo(7));
        });
    }

    // --- Exact spec-formula reproduction: Score(fed) == tours + 2cc + 2aw + standing/5 + contractScore/3 ---

    [Test]
    public void ComputeCareerRank_ReproducesSpecFormula_ExactlyAcrossAGrid()
    {
        // The case that would break a naive "fold contractScore into StandingTotal" implementation:
        // standingTotal=4 (remainder 4, floors to 0) plus contractScore=1 (floors to 0 alone). Combining
        // them into one input BEFORE dividing by 5 would wrongly promote (4+5*1/3=4+1=5 -> /5=1). Folding
        // into Tours instead keeps the two divisions independent, so it can't leak a remainder across terms.
        for (var tours = 0; tours <= 12; tours += 3)
        for (var cap = 0; cap <= 4; cap++)
        for (var antag = 0; antag <= 4; antag++)
        for (var standing = 0; standing <= 20; standing += 4)
        for (var contractScore = 0; contractScore <= 20; contractScore++)
        {
            var stats = new PlayerStats(tours, cap, antag, 0, standing, 0, contractScore);
            var expectedScore = tours + 2 * cap + 2 * antag + standing / RankRules.StandingPerScorePoint
                                 + contractScore / RankProgression.ContractScorePerRankPoint;

            var actualScore = RankRules.Score(RankProgression.WithContractScoreFedIn(stats));

            Assert.That(actualScore, Is.EqualTo(expectedScore),
                $"tours={tours} cap={cap} antag={antag} standing={standing} contractScore={contractScore}");
        }
    }

    [Test]
    public void ComputeCareerRank_TheBreakingCase_StandingFourContractScoreOne()
    {
        // standingTotal/5 = 0, contractScore/3 = 0 -> spec score contribution from these two terms is 0.
        var stats = new PlayerStats(0, 0, 0, 0, StandingTotal: 4, ContractsCompleted: 0, ContractScore: 1);

        Assert.That(RankRules.Score(RankProgression.WithContractScoreFedIn(stats)), Is.EqualTo(0));
    }

    [Test]
    public void ComputeCareerRank_ThreeContractScorePoints_PromotesLikeOneTour()
    {
        // 3 contract_score folds to exactly +1 Tours-equivalent — a Probationary Asset with 2 tours and 3
        // contract_score (career score 3) should NOT outrank a plain 3-tour account, but should exactly
        // match it (both land just under the Associate threshold of 5).
        var viaContracts = new PlayerStats(2, 0, 0, 0, 0, 0, 3);
        var viaTours = new PlayerStats(3, 0, 0, 0);

        Assert.That(RankRules.Score(RankProgression.WithContractScoreFedIn(viaContracts)),
            Is.EqualTo(RankRules.Score(viaTours)));
    }

    [Test]
    public void ComputeCareerRank_EnoughContractScore_PromotesRank()
    {
        // 15 contract_score alone (=5 Tours-equivalent) crosses the Associate threshold (score >= 5) with
        // nothing else on the account.
        var stats = new PlayerStats(0, 0, 0, 0, 0, 0, 15);

        var (rank, _, index) = RankProgression.ComputeCareerRank(stats);

        Assert.Multiple(() =>
        {
            Assert.That(rank, Is.EqualTo(CorporateRank.Associate));
            Assert.That(index, Is.EqualTo(1));
        });
    }

    [Test]
    public void ComputeCareerRank_BelowContractScoreThreshold_StaysProbationary()
    {
        // 14 contract_score -> floor(14/3) = 4, still short of the Associate threshold.
        var stats = new PlayerStats(0, 0, 0, 0, 0, 0, 14);

        Assert.That(RankProgression.ComputeCareerRank(stats).Rank, Is.EqualTo(CorporateRank.ProbationaryAsset));
    }

    // --- Monotonicity: contract_score can only ever help, never hurt, a career rank ---

    [Test]
    public void Monotonic_AddingContractScore_NeverLowersRank()
    {
        for (var tours = 0; tours <= 30; tours += 5)
        for (var standing = 0; standing <= 40; standing += 8)
        for (var contractScore = 0; contractScore <= 60; contractScore += 3)
        {
            var baseStats = new PlayerStats(tours, 0, 0, 0, standing, 0, contractScore);
            var baseIndex = RankProgression.ComputeCareerRank(baseStats).Index;

            var plusOne = RankProgression.ComputeCareerRank(baseStats with { ContractScore = contractScore + 1 }).Index;

            Assert.That(plusOne, Is.GreaterThanOrEqualTo(baseIndex),
                $"+1 ContractScore lowered rank at tours={tours} standing={standing} contractScore={contractScore}");
        }
    }

    [Test]
    public void ComputeCareerRank_NeverDemotes_RelativeToPlainRankRulesCompute()
    {
        // The M3 feed is purely additive: for any stats, the fed-in rank index is never LOWER than what
        // bare RankRules.Compute would have said without the contract bonus.
        for (var tours = 0; tours <= 20; tours += 4)
        for (var standing = 0; standing <= 30; standing += 6)
        for (var contractScore = 0; contractScore <= 30; contractScore += 5)
        {
            var stats = new PlayerStats(tours, 0, 0, 0, standing, 0, contractScore);

            var plain = RankRules.Compute(stats).Index;
            var fed = RankProgression.ComputeCareerRank(stats).Index;

            Assert.That(fed, Is.GreaterThanOrEqualTo(plain),
                $"fed-in rank fell below the un-fed rank at tours={tours} standing={standing} contractScore={contractScore}");
        }
    }
}
