using Content.Shared._Solreign.VesselIdentity;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(SolreignVesselIdentityMath))]
public sealed class SolreignVesselIdentityMathTests
{
    // --- Name Sanitization Tests ---

    [Test]
    public void SanitizeName_TrimsAndCollapsesWhitespace()
    {
        Assert.That(SolreignVesselIdentityMath.SanitizeName("   Starlight   Voyager   "), Is.EqualTo("Starlight Voyager"));
        Assert.That(SolreignVesselIdentityMath.SanitizeName("\tAegis-One\n"), Is.EqualTo("Aegis-One"));
        Assert.That(SolreignVesselIdentityMath.SanitizeName(""), Is.EqualTo(string.Empty));
        Assert.That(SolreignVesselIdentityMath.SanitizeName("   "), Is.EqualTo(string.Empty));
    }

    // --- Structural Name Format Validation ---

    [Test]
    public void IsValidNameFormat_ValidNames_ReturnsTrue()
    {
        Assert.That(SolreignVesselIdentityMath.IsValidNameFormat("Starlight Voyager"), Is.True);
        Assert.That(SolreignVesselIdentityMath.IsValidNameFormat("Aegis-7"), Is.True);
        Assert.That(SolreignVesselIdentityMath.IsValidNameFormat("O'Connor's Pride"), Is.True);
        Assert.That(SolreignVesselIdentityMath.IsValidNameFormat("SV-Expedition 9"), Is.True);
    }

    [Test]
    public void IsValidNameFormat_InvalidNames_ReturnsFalse()
    {
        // Too short (< 3 chars)
        Assert.That(SolreignVesselIdentityMath.IsValidNameFormat("SV"), Is.False);

        // Too long (> 32 chars)
        Assert.That(SolreignVesselIdentityMath.IsValidNameFormat("Supercalifragilisticexpialidocious Voyager Vessel 999"), Is.False);

        // Special characters / symbols disallowed
        Assert.That(SolreignVesselIdentityMath.IsValidNameFormat("Ship!@#$%^&*()"), Is.False);
        Assert.That(SolreignVesselIdentityMath.IsValidNameFormat("<script>alert(1)</script>"), Is.False);
        Assert.That(SolreignVesselIdentityMath.IsValidNameFormat("; DROP TABLE Vessels;"), Is.False);
    }

    // --- Moderation & Blocklist Screening ---

    [Test]
    public void ContainsBlockedKeywords_DetectsProfanityAndSystemTokens()
    {
        Assert.That(SolreignVesselIdentityMath.ContainsBlockedKeywords("Admin Ship"), Is.True);
        Assert.That(SolreignVesselIdentityMath.ContainsBlockedKeywords("Grief Master"), Is.True);
        Assert.That(SolreignVesselIdentityMath.ContainsBlockedKeywords("The Bitch Express"), Is.True);
        Assert.That(SolreignVesselIdentityMath.ContainsBlockedKeywords("Honest Salvage Vessel"), Is.False);
    }

    [Test]
    public void EvaluateNameModeration_ValidAndInvalidInputs()
    {
        Assert.That(
            SolreignVesselIdentityMath.EvaluateNameModeration("Iron Horizon"),
            Is.EqualTo(SolreignVesselModerationState.Approved));

        Assert.That(
            SolreignVesselIdentityMath.EvaluateNameModeration("Admin Cruiser"),
            Is.EqualTo(SolreignVesselModerationState.Flagged));

        Assert.That(
            SolreignVesselIdentityMath.EvaluateNameModeration("X"),
            Is.EqualTo(SolreignVesselModerationState.Flagged));
    }

    // --- Cosmetic Registry Mark Tier Calculations ---

    [Test]
    public void CalculateRegistryMark_MilestoneTiers()
    {
        // Tier 0: Unmarked
        Assert.That(SolreignVesselIdentityMath.CalculateRegistryMark(0, 0f), Is.EqualTo(SolreignVesselRegistryMark.Unmarked));
        Assert.That(SolreignVesselIdentityMath.CalculateRegistryMark(2, 5000f), Is.EqualTo(SolreignVesselRegistryMark.Unmarked));

        // Tier 1: BronzeStripe (3+ missions OR 10k+ salvage)
        Assert.That(SolreignVesselIdentityMath.CalculateRegistryMark(3, 0f), Is.EqualTo(SolreignVesselRegistryMark.BronzeStripe));
        Assert.That(SolreignVesselIdentityMath.CalculateRegistryMark(1, 12000f), Is.EqualTo(SolreignVesselRegistryMark.BronzeStripe));

        // Tier 2: SilverInsignia (10+ missions OR 50k+ salvage)
        Assert.That(SolreignVesselIdentityMath.CalculateRegistryMark(10, 0f), Is.EqualTo(SolreignVesselRegistryMark.SilverInsignia));
        Assert.That(SolreignVesselIdentityMath.CalculateRegistryMark(5, 60000f), Is.EqualTo(SolreignVesselRegistryMark.SilverInsignia));

        // Tier 3: GoldEmblem (25+ missions OR 150k+ salvage)
        Assert.That(SolreignVesselIdentityMath.CalculateRegistryMark(25, 0f), Is.EqualTo(SolreignVesselRegistryMark.GoldEmblem));
        Assert.That(SolreignVesselIdentityMath.CalculateRegistryMark(15, 200000f), Is.EqualTo(SolreignVesselRegistryMark.GoldEmblem));

        // Tier 4: VeteranPennant (50+ missions OR 500k+ salvage)
        Assert.That(SolreignVesselIdentityMath.CalculateRegistryMark(50, 0f), Is.EqualTo(SolreignVesselRegistryMark.VeteranPennant));
        Assert.That(SolreignVesselIdentityMath.CalculateRegistryMark(30, 600000f), Is.EqualTo(SolreignVesselRegistryMark.VeteranPennant));

        // Tier 5: CorporateChevrons (100+ missions OR 1M+ salvage)
        Assert.That(SolreignVesselIdentityMath.CalculateRegistryMark(100, 0f), Is.EqualTo(SolreignVesselRegistryMark.CorporateChevrons));
        Assert.That(SolreignVesselIdentityMath.CalculateRegistryMark(75, 1200000f), Is.EqualTo(SolreignVesselRegistryMark.CorporateChevrons));
    }

    // --- Registry Mark Cosmetic Titles ---

    [Test]
    public void GetRegistryMarkTitle_ReturnsExpectedTitles()
    {
        Assert.That(
            SolreignVesselIdentityMath.GetRegistryMarkTitle(SolreignVesselRegistryMark.CorporateChevrons),
            Is.EqualTo("Solreign Prime Fleet Chevron"));

        Assert.That(
            SolreignVesselIdentityMath.GetRegistryMarkTitle(SolreignVesselRegistryMark.Unmarked),
            Is.EqualTo("Standard Registry Craft"));
    }
}
