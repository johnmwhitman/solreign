using Content.Server._Solreign.SeasonLedger;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(TitleRules))]
public sealed class TitleRulesTests
{
    [Test]
    public void NewPlayer_GetsDefaultTitle()
    {
        var (title, tours) = TitleRules.Compute(new PlayerStats(0, 0, 0, 0));
        Assert.That(title, Is.EqualTo("Probationary Asset"));
        Assert.That(tours, Is.EqualTo(0));
    }

    [Test]
    public void ThreeCleanCaptaincies_GetsBrandAmbassador()
    {
        var (title, tours) = TitleRules.Compute(new PlayerStats(10, 3, 0, 0));
        Assert.That(title, Is.EqualTo("Brand Ambassador"));
        Assert.That(tours, Is.EqualTo(10));
    }

    [Test]
    public void ThreeAntagWins_GetsRestructuringSpecialist()
    {
        var (title, _) = TitleRules.Compute(new PlayerStats(4, 0, 3, 0));
        Assert.That(title, Is.EqualTo("Restructuring Specialist"));
    }

    [Test]
    public void ThreeEarlyDeaths_GetsAmortizedAsset()
    {
        var (title, _) = TitleRules.Compute(new PlayerStats(3, 0, 0, 3));
        Assert.That(title, Is.EqualTo("Amortized Asset"));
    }

    [Test]
    public void VeteranWithNothingNotable_GetsSubOptimalContributor()
    {
        var (title, _) = TitleRules.Compute(new PlayerStats(6, 0, 0, 0));
        Assert.That(title, Is.EqualTo("Sub-optimal Contributor"));
    }

    [Test]
    public void CaptaincyOutranksAntagWins()
    {
        // Both conditions met — the higher-status Captain title must win.
        var (title, _) = TitleRules.Compute(new PlayerStats(9, 3, 3, 0));
        Assert.That(title, Is.EqualTo("Brand Ambassador"));
    }

    [Test]
    public void FewToursNoAchievements_StaysProbationary()
    {
        // Under the veteran threshold and nothing notable → default.
        var (title, _) = TitleRules.Compute(new PlayerStats(2, 0, 0, 0));
        Assert.That(title, Is.EqualTo("Probationary Asset"));
    }

    [Test]
    public void ToursAlwaysEchoed()
    {
        var (_, tours) = TitleRules.Compute(new PlayerStats(42, 1, 1, 1));
        Assert.That(tours, Is.EqualTo(42));
    }

    // --- Season-Ledger title expansion (5-8 new titles, all off already-tracked PlayerStats columns) ---

    [Test]
    public void ContractScoreAtThreshold_GetsScopeOverachiever()
    {
        var (title, _) = TitleRules.Compute(new PlayerStats(1, 0, 0, 0,
            ContractScore: TitleRules.ScopeOverachieverContractScore));
        Assert.That(title, Is.EqualTo("Scope Overachiever"));
    }

    [Test]
    public void ContractScoreJustBelowThreshold_DoesNotGetScopeOverachiever()
    {
        var (title, _) = TitleRules.Compute(new PlayerStats(1, 0, 0, 0,
            ContractScore: TitleRules.ScopeOverachieverContractScore - 1));
        Assert.That(title, Is.Not.EqualTo("Scope Overachiever"));
    }

    [Test]
    public void HrPointsAtThreshold_GetsBoardsFavorite()
    {
        var (title, _) = TitleRules.Compute(new PlayerStats(1, 0, 0, 0,
            HrPoints: TitleRules.BoardsFavoriteHrPoints));
        Assert.That(title, Is.EqualTo("Board's Favorite"));
    }

    [Test]
    public void HrPointsJustBelowThreshold_DoesNotGetBoardsFavorite()
    {
        var (title, _) = TitleRules.Compute(new PlayerStats(1, 0, 0, 0,
            HrPoints: TitleRules.BoardsFavoriteHrPoints - 1));
        Assert.That(title, Is.Not.EqualTo("Board's Favorite"));
    }

    [Test]
    public void StandingTotalAtThreshold_GetsQuarterlyStandout()
    {
        var (title, _) = TitleRules.Compute(new PlayerStats(1, 0, 0, 0,
            StandingTotal: TitleRules.QuarterlyStandoutStanding));
        Assert.That(title, Is.EqualTo("Quarterly Standout"));
    }

    [Test]
    public void StandingTotalJustBelowThreshold_DoesNotGetQuarterlyStandout()
    {
        var (title, _) = TitleRules.Compute(new PlayerStats(1, 0, 0, 0,
            StandingTotal: TitleRules.QuarterlyStandoutStanding - 1));
        Assert.That(title, Is.Not.EqualTo("Quarterly Standout"));
    }

    [Test]
    public void ContractsCompletedAtThreshold_GetsChainCloser()
    {
        var (title, _) = TitleRules.Compute(new PlayerStats(1, 0, 0, 0,
            ContractsCompleted: TitleRules.ChainCloserContracts));
        Assert.That(title, Is.EqualTo("Chain Closer"));
    }

    [Test]
    public void ContractsCompletedJustBelowThreshold_DoesNotGetChainCloser()
    {
        var (title, _) = TitleRules.Compute(new PlayerStats(1, 0, 0, 0,
            ContractsCompleted: TitleRules.ChainCloserContracts - 1));
        Assert.That(title, Is.Not.EqualTo("Chain Closer"));
    }

    [Test]
    public void ToursAtThresholdWithNoEarlyDeaths_GetsCompliantAsset()
    {
        var (title, _) = TitleRules.Compute(new PlayerStats(TitleRules.CompliantAssetTours, 0, 0, 0));
        Assert.That(title, Is.EqualTo("Compliant Asset"));
    }

    [Test]
    public void ToursJustBelowThreshold_DoesNotGetCompliantAsset()
    {
        // Falls through to the existing "veteran with nothing notable" title instead.
        var (title, _) = TitleRules.Compute(new PlayerStats(TitleRules.CompliantAssetTours - 1, 0, 0, 0));
        Assert.That(title, Is.Not.EqualTo("Compliant Asset"));
        Assert.That(title, Is.EqualTo("Sub-optimal Contributor"));
    }

    [Test]
    public void ToursAtThresholdButWithEarlyDeath_DoesNotGetCompliantAsset()
    {
        // The zero-early-deaths half of the condition must hold too, not just the tour count.
        var (title, _) = TitleRules.Compute(new PlayerStats(TitleRules.CompliantAssetTours, 0, 0, 1));
        Assert.That(title, Is.Not.EqualTo("Compliant Asset"));
    }

    [Test]
    public void OneAntagWin_GetsIndependentContractor()
    {
        var (title, _) = TitleRules.Compute(new PlayerStats(1, 0, 1, 0));
        Assert.That(title, Is.EqualTo("Independent Contractor"));
    }

    [Test]
    public void NoAntagWins_DoesNotGetIndependentContractor()
    {
        var (title, _) = TitleRules.Compute(new PlayerStats(1, 0, 0, 0));
        Assert.That(title, Is.Not.EqualTo("Independent Contractor"));
    }

    [Test]
    public void OneCleanCaptaincy_GetsPosterChild()
    {
        var (title, _) = TitleRules.Compute(new PlayerStats(1, 1, 0, 0));
        Assert.That(title, Is.EqualTo("Poster Child"));
    }

    [Test]
    public void NoCleanCaptaincies_DoesNotGetPosterChild()
    {
        var (title, _) = TitleRules.Compute(new PlayerStats(1, 0, 0, 0));
        Assert.That(title, Is.Not.EqualTo("Poster Child"));
    }

    [Test]
    public void HigherStatusNewTitleOutranksLowerNewTitle()
    {
        // Scope Overachiever (highest of the new titles) must win over Chain Closer, Quarterly Standout,
        // Board's Favorite, Independent Contractor, and Poster Child all being simultaneously satisfied.
        var (title, _) = TitleRules.Compute(new PlayerStats(1, 1, 1, 0,
            StandingTotal: TitleRules.QuarterlyStandoutStanding,
            ContractsCompleted: TitleRules.ChainCloserContracts,
            ContractScore: TitleRules.ScopeOverachieverContractScore,
            HrPoints: TitleRules.BoardsFavoriteHrPoints));
        Assert.That(title, Is.EqualTo("Scope Overachiever"));
    }
}
