#nullable enable
using Content.Server._Solreign.FX.Consumers;
using Content.Shared._Solreign.FX.Consumers;
using NUnit.Framework;

namespace Content.Tests._Solreign.FX.Consumers;

[TestFixture]
[TestOf(typeof(SolreignWorldFeedbackDispatchRules))]
public sealed class SolreignWorldFeedbackDispatchRulesTests
{
    [TestCase(false, false, false)]
    [TestCase(false, true, true)]
    [TestCase(true, false, true)]
    [TestCase(true, true, true)]
    public void ShouldHandle_ReturnsClosedGateDecision(bool deliveryEnabled, bool observeEnabled, bool expected)
    {
        Assert.That(
            SolreignWorldFeedbackDispatchRules.ShouldHandle(deliveryEnabled, observeEnabled),
            Is.EqualTo(expected));
    }

    [TestCase(SolreignDamageFeedbackCue.None)]
    [TestCase(SolreignDamageFeedbackCue.NonKinetic)]
    public void TryGetImpactCandidate_NonImpactClassification_ReturnsFalse(SolreignDamageFeedbackCue classification)
    {
        Assert.That(
            SolreignWorldFeedbackDispatchRules.TryGetImpactCandidate(classification, out _),
            Is.False);
    }

    [Test]
    public void TryGetImpactCandidate_Light_ReturnsReleasedParameters()
    {
        Assert.That(
            SolreignWorldFeedbackDispatchRules.TryGetImpactCandidate(
                SolreignDamageFeedbackCue.KineticLight,
                out var candidate),
            Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(candidate.EffectId, Is.EqualTo(SolreignWorldFeedbackRules.LightImpactEffectId));
            Assert.That(candidate.Intensity, Is.EqualTo(0.6f));
            Assert.That(candidate.Duration, Is.EqualTo(0.4f));
        });
    }

    [Test]
    public void TryGetImpactCandidate_Heavy_ReturnsReleasedParameters()
    {
        Assert.That(
            SolreignWorldFeedbackDispatchRules.TryGetImpactCandidate(
                SolreignDamageFeedbackCue.KineticHeavy,
                out var candidate),
            Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(candidate.EffectId, Is.EqualTo(SolreignWorldFeedbackRules.HeavyImpactEffectId));
            Assert.That(candidate.Intensity, Is.EqualTo(0.8f));
            Assert.That(candidate.Duration, Is.EqualTo(0.8f));
        });
    }
}
