using Content.Server._Solreign.SeasonLedger;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Pure gate for the ID-card/PDA standing badge — the surface where OTHER crewmembers glance at a
///     player's Season Ledger standing in-round. The ECS layer
///     (<c>SeasonLedgerSystem.StampIdCardStanding</c>) only dispatches this decision and copies fields
///     onto the card; the "is this account on record at all" call lives here so it is provable without
///     spinning up an entity/inventory/examine pipeline.
/// </summary>
[TestFixture]
[TestOf(typeof(IdCardStandingRules))]
public sealed class IdCardStandingRulesTests
{
    [Test]
    public void FreshAccount_AllZero_DoesNotStamp()
    {
        Assert.That(IdCardStandingRules.ShouldStamp(tours: 0, rankIndex: 0, hrPoints: 0), Is.False);
    }

    [Test]
    public void NonZeroTours_Stamps()
    {
        Assert.That(IdCardStandingRules.ShouldStamp(tours: 1, rankIndex: 0, hrPoints: 0), Is.True);
    }

    [Test]
    public void NonZeroRankIndex_Stamps()
    {
        // Returning veteran, fresh season: 0 season tours yet, but career rank already above the floor.
        Assert.That(IdCardStandingRules.ShouldStamp(tours: 0, rankIndex: 1, hrPoints: 0), Is.True);
    }

    [Test]
    public void NonZeroHrPoints_Stamps()
    {
        Assert.That(IdCardStandingRules.ShouldStamp(tours: 0, rankIndex: 0, hrPoints: 10), Is.True);
    }

    [Test]
    public void FreshGoldenNameAccount_Stamps()
    {
        Assert.That(
            IdCardStandingRules.ShouldStamp(
                tours: 0,
                rankIndex: 0,
                hrPoints: 0,
                hasAdminGrant: false,
                hasGoldenNamePerk: true),
            Is.True);
    }

    [Test]
    public void FreshAdminGrantAccount_Stamps()
    {
        Assert.That(
            IdCardStandingRules.ShouldStamp(
                tours: 0,
                rankIndex: 0,
                hrPoints: 0,
                hasAdminGrant: true,
                hasGoldenNamePerk: false),
            Is.True);
    }

    [Test]
    public void AgreesWithPersonnelFileRules_ForTheSameInputs()
    {
        // The ID badge and the personnel-file examine must never disagree about which accounts are
        // "on record" — same underlying gate, two surfaces.
        for (var tours = 0; tours <= 2; tours++)
        for (var rankIndex = 0; rankIndex <= 2; rankIndex++)
        for (var hrPoints = 0; hrPoints <= 20; hrPoints += 10)
        {
            Assert.That(
                IdCardStandingRules.ShouldStamp(tours, rankIndex, hrPoints),
                Is.EqualTo(PersonnelFileRules.HasRecord(tours, rankIndex, hrPoints)));
        }
    }
}
