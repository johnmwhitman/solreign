using System.Linq;
using Content.Server._Solreign.Onboarding;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(SolreignOnboardingLines))]
public sealed class SolreignOnboardingLinesTests
{
    [Test]
    public void Keys_HasFourDistinctNonEmptyEntries()
    {
        // 3-4 rotating variants per the onboarding-beat spec; must be non-empty and unique so the
        // rotation is actually meaningful.
        Assert.That(SolreignOnboardingLines.Keys.Count, Is.InRange(3, 4));
        Assert.That(SolreignOnboardingLines.Keys.Distinct().Count(), Is.EqualTo(SolreignOnboardingLines.Keys.Count));
        Assert.That(SolreignOnboardingLines.Keys, Has.None.Null.Or.Empty);
    }

    [Test]
    public void Pick_InRangeRoll_ReturnsMatchingKey()
    {
        for (var i = 0; i < SolreignOnboardingLines.Keys.Count; i++)
        {
            Assert.That(SolreignOnboardingLines.Pick(i), Is.EqualTo(SolreignOnboardingLines.Keys[i]));
        }
    }

    [Test]
    public void Pick_RollEqualToCount_WrapsToFirstKey()
    {
        Assert.That(SolreignOnboardingLines.Pick(SolreignOnboardingLines.Keys.Count), Is.EqualTo(SolreignOnboardingLines.Keys[0]));
    }

    [Test]
    public void Pick_NegativeRoll_StillReturnsAValidKey()
    {
        // Pick makes no assumption that its input is non-negative (defensive — IRobustRandom.Next()
        // shouldn't produce one, but the function shouldn't throw or index out of range if it did).
        Assert.That(SolreignOnboardingLines.Keys, Has.Member(SolreignOnboardingLines.Pick(-1)));
        Assert.That(SolreignOnboardingLines.Keys, Has.Member(SolreignOnboardingLines.Pick(-SolreignOnboardingLines.Keys.Count)));
    }

    [Test]
    public void Pick_LargeRoll_StaysInBounds()
    {
        Assert.That(SolreignOnboardingLines.Keys, Has.Member(SolreignOnboardingLines.Pick(int.MaxValue)));
    }
}
