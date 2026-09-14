using Content.Server._Solreign.EasterEggs;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(SolreignCuriosityExamineRules))]
public sealed class SolreignCuriosityExamineRulesTests
{
    private static readonly int[] Thresholds = { 1, 3, 6 };

    // --- TierIndexForCount ---

    [Test]
    public void TierIndexForCount_BelowFirstThreshold_IsMinusOne()
    {
        Assert.That(SolreignCuriosityExamineRules.TierIndexForCount(0, Thresholds), Is.EqualTo(-1));
    }

    [Test]
    public void TierIndexForCount_ExactlyAtFirstThreshold_IsTierZero()
    {
        Assert.That(SolreignCuriosityExamineRules.TierIndexForCount(1, Thresholds), Is.EqualTo(0));
    }

    [Test]
    public void TierIndexForCount_BetweenTiers_StaysAtTheLowerTier()
    {
        Assert.That(SolreignCuriosityExamineRules.TierIndexForCount(2, Thresholds), Is.EqualTo(0));
    }

    [Test]
    public void TierIndexForCount_ExactlyAtSecondThreshold_IsTierOne()
    {
        Assert.That(SolreignCuriosityExamineRules.TierIndexForCount(3, Thresholds), Is.EqualTo(1));
    }

    [Test]
    public void TierIndexForCount_ExactlyAtTopThreshold_IsTopTier()
    {
        Assert.That(SolreignCuriosityExamineRules.TierIndexForCount(6, Thresholds), Is.EqualTo(2));
    }

    [Test]
    public void TierIndexForCount_WellPastTopThreshold_StaysAtTopTier()
    {
        Assert.That(SolreignCuriosityExamineRules.TierIndexForCount(500, Thresholds), Is.EqualTo(2));
    }

    [Test]
    public void TierIndexForCount_EmptyThresholds_IsMinusOne()
    {
        Assert.That(SolreignCuriosityExamineRules.TierIndexForCount(10, System.Array.Empty<int>()), Is.EqualTo(-1));
    }

    // --- ShouldShowRareAside ---

    [Test]
    public void ShouldShowRareAside_BelowTopTier_NeverFires()
    {
        Assert.That(SolreignCuriosityExamineRules.ShouldShowRareAside(1, 2, 0.0, 1f), Is.False);
    }

    [Test]
    public void ShouldShowRareAside_NoTopTierDefined_NeverFires()
    {
        Assert.That(SolreignCuriosityExamineRules.ShouldShowRareAside(0, -1, 0.0, 1f), Is.False);
    }

    [Test]
    public void ShouldShowRareAside_AtTopTier_RollUnderChance_Fires()
    {
        Assert.That(SolreignCuriosityExamineRules.ShouldShowRareAside(2, 2, 0.1, 0.5f), Is.True);
    }

    [Test]
    public void ShouldShowRareAside_AtTopTier_RollAboveChance_DoesNotFire()
    {
        Assert.That(SolreignCuriosityExamineRules.ShouldShowRareAside(2, 2, 0.9, 0.5f), Is.False);
    }

    [Test]
    public void ShouldShowRareAside_PastTopTier_StillEligible()
    {
        Assert.That(SolreignCuriosityExamineRules.ShouldShowRareAside(5, 2, 0.1, 0.5f), Is.True);
    }

    [Test]
    public void ShouldShowRareAside_ChanceAboveOne_ClampsToAlwaysFire()
    {
        Assert.That(SolreignCuriosityExamineRules.ShouldShowRareAside(2, 2, 0.999, 5f), Is.True);
    }

    [Test]
    public void ShouldShowRareAside_NegativeChance_ClampsToNeverFire()
    {
        Assert.That(SolreignCuriosityExamineRules.ShouldShowRareAside(2, 2, 0.0, -1f), Is.False);
    }
}
