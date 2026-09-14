using Content.Server._Solreign.Season1;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(SolreignSeason1PrestigeRules))]
public sealed class SolreignSeason1PrestigeRulesTests
{
    [Test]
    public void QualifiesForBioluminescence_AtThreshold_Qualifies()
    {
        Assert.That(SolreignSeason1PrestigeRules.QualifiesForBioluminescence(4, 4), Is.True);
    }

    [Test]
    public void QualifiesForBioluminescence_AboveThreshold_Qualifies()
    {
        Assert.That(SolreignSeason1PrestigeRules.QualifiesForBioluminescence(6, 4), Is.True);
    }

    [Test]
    public void QualifiesForBioluminescence_BelowThreshold_DoesNotQualify()
    {
        Assert.That(SolreignSeason1PrestigeRules.QualifiesForBioluminescence(3, 4), Is.False);
    }

    [Test]
    public void QualifiesForBioluminescence_FreshAccount_DoesNotQualify()
    {
        Assert.That(SolreignSeason1PrestigeRules.QualifiesForBioluminescence(0, 4), Is.False);
    }

    [Test]
    public void QualifiesForBioluminescence_ZeroThreshold_EveryoneQualifies()
    {
        Assert.That(SolreignSeason1PrestigeRules.QualifiesForBioluminescence(0, 0), Is.True);
    }

    [Test]
    public void QualifiesForBioluminescence_TopOfLadder_Qualifies()
    {
        // 6 = Board Member, see RankRules.CorporateRank.
        Assert.That(SolreignSeason1PrestigeRules.QualifiesForBioluminescence(6, 6), Is.True);
    }
}
