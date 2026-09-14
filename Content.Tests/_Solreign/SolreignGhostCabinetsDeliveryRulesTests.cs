using Content.Server._Solreign.Season1;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(SolreignGhostCabinetsDeliveryRules))]
public sealed class SolreignGhostCabinetsDeliveryRulesTests
{
    [TestCase(false, false, SolreignGhostCabinetsDeliveryOutcome.InsertFailed)]
    [TestCase(true, false, SolreignGhostCabinetsDeliveryOutcome.DroppedAdjacent)]
    [TestCase(true, true, SolreignGhostCabinetsDeliveryOutcome.Inserted)]
    public void ClassifyInsertion_UsesTheObservedContainmentPostcondition(
        bool insertReturned,
        bool contained,
        SolreignGhostCabinetsDeliveryOutcome expected)
    {
        Assert.That(
            SolreignGhostCabinetsDeliveryRules.ClassifyInsertion(insertReturned, contained),
            Is.EqualTo(expected));
    }
}
