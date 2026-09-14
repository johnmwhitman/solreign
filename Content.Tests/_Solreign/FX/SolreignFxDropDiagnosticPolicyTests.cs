#nullable enable
using System;
using Content.Client._Solreign.FX;
using Content.Shared._Solreign.FX;
using NUnit.Framework;

namespace Content.Tests._Solreign.FX;

[TestFixture]
[TestOf(typeof(SolreignFxDropDiagnosticPolicy))]
public sealed class SolreignFxDropDiagnosticPolicyTests
{
    [Test]
    public void IsExpectedLoss_OnlyMissingBroadcastEntityAnchorQualifies()
    {
        foreach (var failure in Enum.GetValues<SolreignFxCueValidateFailureReason>())
        {
            Assert.That(
                SolreignFxDropDiagnosticPolicy.IsExpectedMissingAnchorLoss(
                    failure,
                    classificationKnown: true,
                    SolreignFxAudienceClassification.Broadcast,
                    entityAnchorMissing: true),
                Is.EqualTo(failure == SolreignFxCueValidateFailureReason.AnchorEntityUnresolvable),
                failure.ToString());
        }
    }

    [TestCase(false, SolreignFxAudienceClassification.Broadcast, true)]
    [TestCase(true, SolreignFxAudienceClassification.DetailOnly, true)]
    [TestCase(true, SolreignFxAudienceClassification.Broadcast, false)]
    [TestCase(false, SolreignFxAudienceClassification.DetailOnly, false)]
    public void IsExpectedLoss_AmbiguousOrConfidentialCasesRemainWarnings(
        bool classificationKnown,
        SolreignFxAudienceClassification classification,
        bool entityAnchorMissing)
    {
        Assert.That(
            SolreignFxDropDiagnosticPolicy.IsExpectedMissingAnchorLoss(
                SolreignFxCueValidateFailureReason.AnchorEntityUnresolvable,
                classificationKnown,
                classification,
                entityAnchorMissing),
            Is.False);
    }

    [Test]
    public void RequiresWarning_OnlyDedicatedExpectedLossBucketIsInformational()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                SolreignFxDropDiagnosticPolicy.RequiresWarning(
                    SolreignFxDropDiagnosticPolicy.ExpectedMissingBroadcastAnchorReason),
                Is.False);
            Assert.That(
                SolreignFxDropDiagnosticPolicy.RequiresWarning("Validate:AnchorEntityUnresolvable"),
                Is.True);
            Assert.That(
                SolreignFxDropDiagnosticPolicy.RequiresWarning(
                    $"{SolreignFxDropDiagnosticPolicy.ExpectedMissingBroadcastAnchorReason}:extra"),
                Is.True);
            Assert.That(SolreignFxDropDiagnosticPolicy.RequiresWarning(null), Is.True);
        });
    }
}
