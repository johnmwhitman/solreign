using Content.Server._Solreign.Wardrobe;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(WardrobeRules))]
public sealed class WardrobeRulesTests
{
    // The one upgrade the wardrobe performs: the sparse stock admin-ghost template
    // (back/id/head/mask only) widens to the full-visual wardrobe template.
    [Test]
    public void StockAghostTemplate_UpgradesToWardrobe()
    {
        Assert.That(WardrobeRules.ResolveTemplateUpgrade("aghost"),
            Is.EqualTo(WardrobeRules.WardrobeTemplateId));
    }

    [Test]
    public void WardrobeTemplate_IsLeftAlone()
    {
        Assert.That(WardrobeRules.ResolveTemplateUpgrade(WardrobeRules.WardrobeTemplateId), Is.Null);
    }

    // Full templates must never be clobbered — a ghost that somehow carries the human
    // template already has every visual slot.
    [Test]
    public void HumanTemplate_IsLeftAlone()
    {
        Assert.That(WardrobeRules.ResolveTemplateUpgrade("human"), Is.Null);
    }

    // Unknown templates are refused rather than guessed at: never downgrade, never clobber.
    [Test]
    public void UnrecognizedTemplate_IsLeftAlone()
    {
        Assert.That(WardrobeRules.ResolveTemplateUpgrade("someFutureTemplate"), Is.Null);
        Assert.That(WardrobeRules.ResolveTemplateUpgrade(""), Is.Null);
    }

    // Prototype ids are case-sensitive; the policy must not fuzzy-match.
    [Test]
    public void TemplateMatch_IsCaseSensitive()
    {
        Assert.That(WardrobeRules.ResolveTemplateUpgrade("Aghost"), Is.Null);
        Assert.That(WardrobeRules.ResolveTemplateUpgrade("AGHOST"), Is.Null);
    }
}
