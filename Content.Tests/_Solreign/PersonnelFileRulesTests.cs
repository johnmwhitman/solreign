using Content.Server._Solreign.SeasonLedger;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Pure formatting rules for the Season Ledger's on-examine "personnel file" flourish. The ECS layer
///     (<c>SeasonLedgerSystem.OnExamined</c>) only dispatches loc strings; every decision it acts on lives
///     here, so both the record gate and the standing-descriptor ladder are provable without spinning up
///     an entity/examine pipeline.
/// </summary>
[TestFixture]
[TestOf(typeof(PersonnelFileRules))]
public sealed class PersonnelFileRulesTests
{
    [Test]
    public void FreshAccount_AllZero_HasNoRecord()
    {
        Assert.That(PersonnelFileRules.HasRecord(tours: 0, rankIndex: 0, hrPoints: 0), Is.False);
    }

    [Test]
    public void NonZeroTours_HasRecord()
    {
        Assert.That(PersonnelFileRules.HasRecord(tours: 1, rankIndex: 0, hrPoints: 0), Is.True);
    }

    [Test]
    public void NonZeroRankIndex_HasRecord()
    {
        // A returning veteran at the start of a fresh season: 0 season tours yet, but career rank
        // (and therefore RankIndex) already above the probationary floor.
        Assert.That(PersonnelFileRules.HasRecord(tours: 0, rankIndex: 1, hrPoints: 0), Is.True);
    }

    [Test]
    public void NonZeroHrPoints_HasRecord()
    {
        Assert.That(PersonnelFileRules.HasRecord(tours: 0, rankIndex: 0, hrPoints: 10), Is.True);
    }

    [TestCase(false, false, false)]
    [TestCase(true, false, true)]
    [TestCase(false, true, true)]
    public void FreshAccount_CosmeticGrantControlsVisibleStanding(
        bool hasAdminGrant,
        bool hasGoldenNamePerk,
        bool expected)
    {
        Assert.That(
            PersonnelFileRules.ShouldDisplay(
                tours: 0,
                rankIndex: 0,
                hrPoints: 0,
                hasAdminGrant,
                hasGoldenNamePerk),
            Is.EqualTo(expected));
    }

    [Test]
    public void EstablishedAccount_DisplaysWithoutCosmeticGrant()
    {
        Assert.That(
            PersonnelFileRules.ShouldDisplay(
                tours: 1,
                rankIndex: 0,
                hrPoints: 0,
                hasAdminGrant: false,
                hasGoldenNamePerk: false),
            Is.True);
    }

    [Test]
    public void ProbationaryAsset_DescribedAsUnverified()
    {
        Assert.That(PersonnelFileRules.DescribeCareerStanding(0), Is.EqualTo("unverified"));
    }

    [Test]
    public void BoardMember_DescribedAsExceptional()
    {
        Assert.That(PersonnelFileRules.DescribeCareerStanding(6), Is.EqualTo("exceptional"));
    }

    [Test]
    public void FounderEmeritus_DescribedAsImmortalized()
    {
        Assert.That(PersonnelFileRules.DescribeCareerStanding(10), Is.EqualTo("immortalized"));
    }

    [Test]
    public void OutOfRangeHighIndex_ClampsToFounderEmeritusDescriptor()
    {
        // Defensive: RankIndex should never exceed 10 in practice, but examine must never throw.
        Assert.That(PersonnelFileRules.DescribeCareerStanding(99), Is.EqualTo("immortalized"));
    }

    [Test]
    public void NegativeIndex_ClampsToProbationaryDescriptor()
    {
        Assert.That(PersonnelFileRules.DescribeCareerStanding(-1), Is.EqualTo("unverified"));
    }

    [Test]
    public void EveryRankIndexZeroToTen_ProducesADistinctDescriptor()
    {
        // Ladder integrity: each of the 11 real rank tiers should read as a different flavor word, so
        // climbing the ladder always changes the personnel-file line, never repeats a lower tier's copy.
        var seen = new System.Collections.Generic.HashSet<string>();
        for (var i = 0; i <= 10; i++)
            Assert.That(seen.Add(PersonnelFileRules.DescribeCareerStanding(i)), Is.True,
                $"RankIndex {i} produced a descriptor already used by a lower tier.");
    }
}
