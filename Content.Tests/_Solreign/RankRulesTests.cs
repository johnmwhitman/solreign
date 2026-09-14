using Content.Server._Solreign.SeasonLedger;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(RankRules))]
public sealed class RankRulesTests
{
    // Score = Tours + 2*CaptainClean + 2*AntagWins; EarlyDeaths weight 0. We drive the score purely
    // through Tours here so the boundary cases read directly against the thresholds.
    private static PlayerStats WithScore(int tours) => new(tours, 0, 0, 0);

    [Test]
    public void NewPlayer_IsProbationaryAsset()
    {
        var (rank, name, index) = RankRules.Compute(new PlayerStats(0, 0, 0, 0));
        Assert.That(rank, Is.EqualTo(CorporateRank.ProbationaryAsset));
        Assert.That(name, Is.EqualTo("Probationary Asset"));
        Assert.That(index, Is.EqualTo(0));
    }

    // --- Threshold boundaries: exactly-at promotes, just-below stays put. ---

    [Test]
    public void Score4_StaysProbationary_Score5_IsAssociate()
    {
        Assert.That(RankRules.Compute(WithScore(4)).Rank, Is.EqualTo(CorporateRank.ProbationaryAsset));

        var (rank, name, index) = RankRules.Compute(WithScore(5));
        Assert.That(rank, Is.EqualTo(CorporateRank.Associate));
        Assert.That(name, Is.EqualTo("Associate"));
        Assert.That(index, Is.EqualTo(1));
    }

    [Test]
    public void Score14_IsAssociate_Score15_IsSeniorAssociate()
    {
        Assert.That(RankRules.Compute(WithScore(14)).Rank, Is.EqualTo(CorporateRank.Associate));

        var (rank, name, index) = RankRules.Compute(WithScore(15));
        Assert.That(rank, Is.EqualTo(CorporateRank.SeniorAssociate));
        Assert.That(name, Is.EqualTo("Senior Associate"));
        Assert.That(index, Is.EqualTo(2));
    }

    [Test]
    public void Score29_IsSeniorAssociate_Score30_IsManager()
    {
        Assert.That(RankRules.Compute(WithScore(29)).Rank, Is.EqualTo(CorporateRank.SeniorAssociate));

        var (rank, name, index) = RankRules.Compute(WithScore(30));
        Assert.That(rank, Is.EqualTo(CorporateRank.Manager));
        Assert.That(name, Is.EqualTo("Manager"));
        Assert.That(index, Is.EqualTo(3));
    }

    [Test]
    public void Score49_IsManager_Score50_IsDirector()
    {
        Assert.That(RankRules.Compute(WithScore(49)).Rank, Is.EqualTo(CorporateRank.Manager));

        var (rank, name, index) = RankRules.Compute(WithScore(50));
        Assert.That(rank, Is.EqualTo(CorporateRank.Director));
        Assert.That(name, Is.EqualTo("Director"));
        Assert.That(index, Is.EqualTo(4));
    }

    [Test]
    public void Score79_IsDirector_Score80_IsVicePresident()
    {
        Assert.That(RankRules.Compute(WithScore(79)).Rank, Is.EqualTo(CorporateRank.Director));

        var (rank, name, index) = RankRules.Compute(WithScore(80));
        Assert.That(rank, Is.EqualTo(CorporateRank.VicePresident));
        Assert.That(name, Is.EqualTo("Vice President"));
        Assert.That(index, Is.EqualTo(5));
    }

    [Test]
    public void Score119_IsVicePresident_Score120_IsBoardMember()
    {
        Assert.That(RankRules.Compute(WithScore(119)).Rank, Is.EqualTo(CorporateRank.VicePresident));

        var (rank, name, index) = RankRules.Compute(WithScore(120));
        Assert.That(rank, Is.EqualTo(CorporateRank.BoardMember));
        Assert.That(name, Is.EqualTo("Board Member"));
        Assert.That(index, Is.EqualTo(6));
    }

    // --- Score weighting: captaincies and antag wins are worth 2 each; early deaths never count. ---

    [Test]
    public void CleanCaptainciesAndAntagWins_AreWorthTwoEach()
    {
        // 1 tour + 2*2 clean captaincies + 2*2 antag wins = 1 + 4 + 4 = 9 -> Associate.
        Assert.That(RankRules.Score(new PlayerStats(1, 2, 2, 0)), Is.EqualTo(9));
        Assert.That(RankRules.Compute(new PlayerStats(1, 2, 2, 0)).Rank, Is.EqualTo(CorporateRank.Associate));
    }

    [Test]
    public void EarlyDeaths_NeverAffectScoreOrRank()
    {
        var clean = new PlayerStats(40, 3, 3, 0);
        var battered = new PlayerStats(40, 3, 3, 999);

        Assert.That(RankRules.Score(battered), Is.EqualTo(RankRules.Score(clean)));
        Assert.That(RankRules.Compute(battered).Index, Is.EqualTo(RankRules.Compute(clean).Index));
    }

    // --- Corporate Standing: career standing compounds into the score at StandingPerScorePoint per rank point. ---

    [Test]
    public void Standing_ContributesAtOnePointPerFivePointsOfStanding()
    {
        // StandingPerScorePoint = 5, integer division: 5 standing -> +1 score, 4 standing -> +0.
        Assert.That(RankRules.Score(new PlayerStats(0, 0, 0, 0, 5)), Is.EqualTo(1));
        Assert.That(RankRules.Score(new PlayerStats(0, 0, 0, 0, 4)), Is.EqualTo(0));
        Assert.That(RankRules.Score(new PlayerStats(0, 0, 0, 0, 12)), Is.EqualTo(2));

        // 2 tours + 15 standing (=3 score) = 5 -> Associate, promoting a career that Tours alone would not.
        Assert.That(RankRules.Score(new PlayerStats(2, 0, 0, 0, 15)), Is.EqualTo(5));
        Assert.That(RankRules.Compute(new PlayerStats(2, 0, 0, 0, 15)).Rank, Is.EqualTo(CorporateRank.Associate));
    }

    [Test]
    public void Standing_RaisesRank_ButNeverLowersIt()
    {
        // A pile of standing on top of an existing career can only ever push the rank up.
        var withoutStanding = new PlayerStats(20, 2, 2, 0, 0);
        var withStanding = withoutStanding with { StandingTotal = 300 };

        Assert.That(RankRules.Score(withStanding), Is.GreaterThan(RankRules.Score(withoutStanding)));
        Assert.That(RankRules.Compute(withStanding).Index,
            Is.GreaterThanOrEqualTo(RankRules.Compute(withoutStanding).Index));
        // Concretely: base score 20+4+4 = 28 (Senior Associate); +300/5 = +60 -> 88 (Vice President).
        Assert.That(RankRules.Compute(withoutStanding).Rank, Is.EqualTo(CorporateRank.SeniorAssociate));
        Assert.That(RankRules.Compute(withStanding).Rank, Is.EqualTo(CorporateRank.VicePresident));
    }

    /// <summary>
    ///     Monotonicity survives the new field: across a broad grid, adding +1 Corporate Standing never
    ///     lowers the rank index (standing is non-negative and weighted non-negatively).
    /// </summary>
    [Test]
    public void Monotonic_AddingStanding_NeverLowersRank()
    {
        for (var tours = 0; tours <= 60; tours += 5)
        for (var cap = 0; cap <= 20; cap += 5)
        for (var antag = 0; antag <= 20; antag += 5)
        for (var standing = 0; standing <= 200; standing += 7)
        {
            var baseStats = new PlayerStats(tours, cap, antag, 0, standing);
            var baseIndex = RankRules.Compute(baseStats).Index;
            var plusStanding = RankRules.Compute(baseStats with { StandingTotal = standing + 1 }).Index;

            Assert.That(plusStanding, Is.GreaterThanOrEqualTo(baseIndex),
                $"+1 StandingTotal lowered rank at {baseStats}");
        }
    }

    /// <summary>
    ///     Core promise of the ladder: a rank can only ever go up. Across a broad grid of stats, adding +1
    ///     to any career-positive stat (Tours / CaptainClean / AntagWins) must NEVER lower the rank index.
    /// </summary>
    [Test]
    public void Monotonic_AddingAnyPositiveStat_NeverLowersRank()
    {
        for (var tours = 0; tours <= 60; tours += 3)
        for (var cap = 0; cap <= 20; cap += 2)
        for (var antag = 0; antag <= 20; antag += 2)
        for (var deaths = 0; deaths <= 4; deaths += 2)
        {
            var baseStats = new PlayerStats(tours, cap, antag, deaths);
            var baseIndex = RankRules.Compute(baseStats).Index;

            var plusTour = RankRules.Compute(baseStats with { Tours = tours + 1 }).Index;
            var plusCap = RankRules.Compute(baseStats with { CaptainClean = cap + 1 }).Index;
            var plusAntag = RankRules.Compute(baseStats with { AntagWins = antag + 1 }).Index;

            Assert.That(plusTour, Is.GreaterThanOrEqualTo(baseIndex),
                $"+1 Tours lowered rank at {baseStats}");
            Assert.That(plusCap, Is.GreaterThanOrEqualTo(baseIndex),
                $"+1 CaptainClean lowered rank at {baseStats}");
            Assert.That(plusAntag, Is.GreaterThanOrEqualTo(baseIndex),
                $"+1 AntagWins lowered rank at {baseStats}");
        }
    }

    [Test]
    public void Monotonic_AddingEarlyDeath_NeverChangesRank()
    {
        for (var tours = 0; tours <= 60; tours += 5)
        for (var cap = 0; cap <= 20; cap += 5)
        {
            var baseStats = new PlayerStats(tours, cap, 0, 0);
            var baseIndex = RankRules.Compute(baseStats).Index;
            var plusDeath = RankRules.Compute(baseStats with { EarlyDeaths = baseStats.EarlyDeaths + 1 }).Index;

            Assert.That(plusDeath, Is.EqualTo(baseIndex),
                $"+1 EarlyDeaths changed rank at {baseStats}");
        }
    }

    // --- Executive tiers (Beta Feedback 01, Lane B): gated on BOTH Tours (time-played proxy) AND HR Points. ---
    // Tours=120 clears the Board Member score bar (120) AND every executive tenure floor (max 90), so these
    // boundary cases isolate the HR Points axis cleanly.

    [Test]
    public void Points399_StaysBoardMember_Points400_IsSeniorBoardMember()
    {
        Assert.That(RankRules.Compute(new PlayerStats(120, 0, 0, 0, HrPoints: 399)).Rank,
            Is.EqualTo(CorporateRank.BoardMember));

        var (rank, name, index) = RankRules.Compute(new PlayerStats(120, 0, 0, 0, HrPoints: 400));
        Assert.That(rank, Is.EqualTo(CorporateRank.SeniorBoardMember));
        Assert.That(name, Is.EqualTo("Senior Board Member"));
        Assert.That(index, Is.EqualTo(7));
    }

    [Test]
    public void Points699_StaysSeniorBoardMember_Points700_IsChiefExecutiveOfficer()
    {
        Assert.That(RankRules.Compute(new PlayerStats(120, 0, 0, 0, HrPoints: 699)).Rank,
            Is.EqualTo(CorporateRank.SeniorBoardMember));

        var (rank, name, index) = RankRules.Compute(new PlayerStats(120, 0, 0, 0, HrPoints: 700));
        Assert.That(rank, Is.EqualTo(CorporateRank.ChiefExecutiveOfficer));
        Assert.That(name, Is.EqualTo("Chief Executive Officer"));
        Assert.That(index, Is.EqualTo(8));
    }

    [Test]
    public void Points1099_StaysChiefExecutiveOfficer_Points1100_IsChairmanOfTheBoard()
    {
        Assert.That(RankRules.Compute(new PlayerStats(120, 0, 0, 0, HrPoints: 1099)).Rank,
            Is.EqualTo(CorporateRank.ChiefExecutiveOfficer));

        var (rank, name, index) = RankRules.Compute(new PlayerStats(120, 0, 0, 0, HrPoints: 1100));
        Assert.That(rank, Is.EqualTo(CorporateRank.ChairmanOfTheBoard));
        Assert.That(name, Is.EqualTo("Chairman of the Board"));
        Assert.That(index, Is.EqualTo(9));
    }

    [Test]
    public void Points1599_StaysChairmanOfTheBoard_Points1600_IsFounderEmeritus()
    {
        Assert.That(RankRules.Compute(new PlayerStats(120, 0, 0, 0, HrPoints: 1599)).Rank,
            Is.EqualTo(CorporateRank.ChairmanOfTheBoard));

        var (rank, name, index) = RankRules.Compute(new PlayerStats(120, 0, 0, 0, HrPoints: 1600));
        Assert.That(rank, Is.EqualTo(CorporateRank.FounderEmeritus));
        Assert.That(name, Is.EqualTo("Founder Emeritus"));
        Assert.That(index, Is.EqualTo(10));
    }

    [Test]
    public void ExecutiveTiers_AreAndGated_HrPointsAloneCannotBuyTenure()
    {
        // Score = 20 + 2*50 = 120 -> clears Board Member on Score alone, but Tours (the tenure proxy) is
        // only 20 — short of even the lowest executive tenure floor (30). A mountain of HR Points must not
        // vault this account past Board Member without the career tenure to match.
        var stats = new PlayerStats(20, 50, 0, 0, HrPoints: 5000);

        Assert.That(RankRules.Compute(stats).Rank, Is.EqualTo(CorporateRank.BoardMember));
    }

    [Test]
    public void ExecutiveTiers_AreAndGated_TenureAloneCannotBuyPoints()
    {
        // Tours=120 clears every tenure floor (even Founder Emeritus's 90) and the Score bar, but HrPoints
        // is 0 — a long, otherwise-unremarkable career must not buy the executive suite on tenure alone.
        var stats = new PlayerStats(120, 0, 0, 0, HrPoints: 0);

        Assert.That(RankRules.Compute(stats).Rank, Is.EqualTo(CorporateRank.BoardMember));
    }

    [Test]
    public void Monotonic_AddingHrPoints_NeverLowersRank()
    {
        for (var tours = 0; tours <= 130; tours += 10)
        for (var points = 0; points <= 2000; points += 133)
        {
            var baseStats = new PlayerStats(tours, 0, 0, 0, HrPoints: points);
            var baseIndex = RankRules.Compute(baseStats).Index;
            var plusPoint = RankRules.Compute(baseStats with { HrPoints = points + 1 }).Index;

            Assert.That(plusPoint, Is.GreaterThanOrEqualTo(baseIndex),
                $"+1 HrPoints lowered rank at {baseStats}");
        }
    }

    [Test]
    public void Monotonic_ExecutiveTiers_AddingTours_NeverLowersRank()
    {
        for (var tours = 0; tours <= 130; tours += 10)
        for (var points = 0; points <= 2000; points += 250)
        {
            var baseStats = new PlayerStats(tours, 0, 0, 0, HrPoints: points);
            var baseIndex = RankRules.Compute(baseStats).Index;
            var plusTour = RankRules.Compute(baseStats with { Tours = tours + 1 }).Index;

            Assert.That(plusTour, Is.GreaterThanOrEqualTo(baseIndex),
                $"+1 Tours lowered rank at {baseStats}");
        }
    }
}
