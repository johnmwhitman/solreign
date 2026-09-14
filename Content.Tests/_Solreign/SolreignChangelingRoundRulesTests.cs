using Content.Server._Solreign.Changeling;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(SolreignChangelingRoundRules))]
public sealed class SolreignChangelingRoundRulesTests
{
    // --- ClampObjectiveTarget: keeps the rolled objective target inside the antag's own cap
    // (spec §3 rule 4) — this is the "selection gating" logic for what counts as a valid,
    // completable round objective. ---

    [Test]
    public void ClampObjectiveTarget_BelowCap_Unchanged()
    {
        Assert.That(SolreignChangelingRoundRules.ClampObjectiveTarget(3, 6), Is.EqualTo(3));
    }

    [Test]
    public void ClampObjectiveTarget_AboveCap_ClampsDownToCap()
    {
        Assert.That(SolreignChangelingRoundRules.ClampObjectiveTarget(10, 6), Is.EqualTo(6));
    }

    [Test]
    public void ClampObjectiveTarget_EqualToCap_Unchanged()
    {
        Assert.That(SolreignChangelingRoundRules.ClampObjectiveTarget(6, 6), Is.EqualTo(6));
    }

    [Test]
    public void ClampObjectiveTarget_ZeroOrNegativeTarget_FloorsToOne()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SolreignChangelingRoundRules.ClampObjectiveTarget(0, 6), Is.EqualTo(1));
            Assert.That(SolreignChangelingRoundRules.ClampObjectiveTarget(-5, 6), Is.EqualTo(1));
        });
    }

    [Test]
    public void ClampObjectiveTarget_ZeroOrNegativeCap_TreatsCapAsOne()
    {
        Assert.That(SolreignChangelingRoundRules.ClampObjectiveTarget(3, 0), Is.EqualTo(1));
    }

    // --- GetObjectiveProgress ---

    [Test]
    public void GetObjectiveProgress_NoAliasesYet_Zero()
    {
        Assert.That(SolreignChangelingRoundRules.GetObjectiveProgress(0, 3), Is.EqualTo(0f));
    }

    [Test]
    public void GetObjectiveProgress_PartialProgress_Fraction()
    {
        Assert.That(SolreignChangelingRoundRules.GetObjectiveProgress(1, 3), Is.EqualTo(1f / 3f).Within(0.0001f));
    }

    [Test]
    public void GetObjectiveProgress_MeetsTarget_One()
    {
        Assert.That(SolreignChangelingRoundRules.GetObjectiveProgress(3, 3), Is.EqualTo(1f));
    }

    [Test]
    public void GetObjectiveProgress_ExceedsTarget_ClampedToOne()
    {
        Assert.That(SolreignChangelingRoundRules.GetObjectiveProgress(5, 3), Is.EqualTo(1f));
    }

    [Test]
    public void GetObjectiveProgress_ZeroOrLessTarget_AlwaysComplete()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SolreignChangelingRoundRules.GetObjectiveProgress(0, 0), Is.EqualTo(1f));
            Assert.That(SolreignChangelingRoundRules.GetObjectiveProgress(2, -1), Is.EqualTo(1f));
        });
    }
}
