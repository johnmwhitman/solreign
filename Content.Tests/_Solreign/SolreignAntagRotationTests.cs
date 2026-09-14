using Content.Server._Solreign.Antags;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
public sealed class SolreignAntagRotationTests
{
    [Test]
    public void AntagRotation_DefaultCooldown_EligibleAfterMinimumRounds()
    {
        var sys = new SolreignAntagRotationSystem();
        var comp = new SolreignAntagRotationComponent { RoundsSinceLastAntag = 0, MinimumCooldownRounds = 3 };

        Assert.That(sys.CheckEligibility(comp), Is.False);

        sys.IncrementNonAntagStreak(comp); // round 1
        Assert.That(comp.EligibleForAntag, Is.False);

        sys.IncrementNonAntagStreak(comp); // round 2
        Assert.That(comp.EligibleForAntag, Is.False);

        sys.IncrementNonAntagStreak(comp); // round 3
        Assert.That(comp.EligibleForAntag, Is.True);
        Assert.That(comp.AntagWeight, Is.EqualTo(1));
    }

    [Test]
    public void AntagRotation_StreakIncreasesWeight()
    {
        var sys = new SolreignAntagRotationSystem();
        var comp = new SolreignAntagRotationComponent { RoundsSinceLastAntag = 3, MinimumCooldownRounds = 3 };

        sys.IncrementNonAntagStreak(comp); // round 4
        Assert.That(comp.AntagWeight, Is.EqualTo(2));

        sys.IncrementNonAntagStreak(comp); // round 5
        Assert.That(comp.AntagWeight, Is.EqualTo(3));
    }

    [Test]
    public void AntagRotation_ResetOnAntagSelection()
    {
        var sys = new SolreignAntagRotationSystem();
        var comp = new SolreignAntagRotationComponent { RoundsSinceLastAntag = 5, MinimumCooldownRounds = 3, AntagWeight = 3, EligibleForAntag = true };

        sys.ResetAntagStreak(comp);

        Assert.That(comp.RoundsSinceLastAntag, Is.EqualTo(0));
        Assert.That(comp.EligibleForAntag, Is.False);
        Assert.That(comp.AntagWeight, Is.EqualTo(0));
    }
}
