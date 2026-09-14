using Content.Server._Solreign.SeasonLedger;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Ceremony gate + bulletin copy for Season Ledger title grants. The ECS layer
///     (<c>SeasonLedgerSystem.Ceremony</c>) only dispatches; every decision it acts on lives in these
///     pure rules, so the guards ("new grants only, never reloads") are provable here.
/// </summary>
[TestFixture]
[TestOf(typeof(TitleRules))]
public sealed class TitleCeremonyTests
{
    [Test]
    public void FirstEarnedTitle_IsNewGrant()
    {
        // No previously announced title on record → the first real title rates a ceremony.
        Assert.That(TitleRules.IsNewGrant(null, "Brand Ambassador"), Is.True);
    }

    [Test]
    public void DefaultTitle_NeverGetsACeremony()
    {
        // "Probationary Asset" is the fallback, not an achievement — no bulletin for showing up.
        Assert.That(TitleRules.IsNewGrant(null, TitleRules.DefaultTitle), Is.False);
    }

    [Test]
    public void SameTitleOnReload_IsNotANewGrant()
    {
        // Respawn/reconnect with an unchanged title must stay silent — this is the anti-spam guard.
        Assert.That(TitleRules.IsNewGrant("Brand Ambassador", "Brand Ambassador"), Is.False);
    }

    [Test]
    public void TitleUpgrade_IsNewGrant()
    {
        // Moving up (or sideways) the ladder to a different earned title re-fires the ceremony.
        Assert.That(TitleRules.IsNewGrant("Amortized Asset", "Brand Ambassador"), Is.True);
    }

    [Test]
    public void FallingBackToDefault_IsNotANewGrant()
    {
        // e.g. after a season reset the computed title regresses to the default — no ceremony for demotion.
        Assert.That(TitleRules.IsNewGrant("Brand Ambassador", TitleRules.DefaultTitle), Is.False);
    }

    [Test]
    public void EmptyTitle_IsNotANewGrant()
    {
        Assert.That(TitleRules.IsNewGrant(null, ""), Is.False);
    }

    [Test]
    public void Bulletin_SpeaksInCorporateVoice()
    {
        var bulletin = TitleRules.FormatCeremonyBulletin("Kolton Whitman", "Brand Ambassador");
        Assert.That(bulletin, Is.EqualTo(
            "SOLREIGN HR BULLETIN: Asset Kolton Whitman has been designated BRAND AMBASSADOR. Compliance is its own reward."));
    }

    [Test]
    public void Bulletin_UppercasesTheTitleOnly()
    {
        var bulletin = TitleRules.FormatCeremonyBulletin("Asset Name", "Sub-optimal Contributor");
        Assert.That(bulletin, Does.Contain("SUB-OPTIMAL CONTRIBUTOR"));
        Assert.That(bulletin, Does.Contain("Asset Asset Name has been designated"));
    }
}
