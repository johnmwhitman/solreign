using Content.Server._Solreign.Contracts;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Rank-gate coverage for the Solreign Executive Vendor (Contracts M4, "the ladder buys things").
///
///     The vendor's UI-open gate (<c>SolreignExecutiveAccessSystem</c>, an ECS event handler) is not
///     unit-testable without an integration harness (GROUND RULES: no dotnet build here), so it
///     deliberately does nothing but delegate to the SAME pure predicate every other rank-gated surface in
///     the fork already uses: <see cref="ContractRules.MeetsRankGate"/> (see
///     <c>ContractRulesTests.MeetsRankGate_*</c> for that predicate's exhaustive coverage — zero gate, exact
///     threshold, above/below, and the fresh-account floor).
///
///     What's new here, and worth pinning down separately: the Executive Vendor is wired to
///     <c>minRankIndex: 3</c> in <c>contracts_executive_vendor.yml</c> — the same Manager+ tier as the
///     Executive Lunch contract. These cases lock that specific configured threshold in so a future edit to
///     either the vendor's YAML or <see cref="ContractRules.MeetsRankGate"/> that silently changes which
///     ranks the vendor opens for gets caught here, without needing to spin up the game to open the vendor.
/// </summary>
[TestFixture]
[TestOf(typeof(ContractRules))]
public sealed class SolreignExecutiveVendorRankGateTests
{
    // contracts_executive_vendor.yml: SolreignExecutiveVendor -> SolreignExecutiveAccess.minRankIndex.
    private const int ExecutiveVendorMinRankIndex = 3;

    [Test]
    public void ExecutiveVendor_SubManagerRanks_AreDenied()
    {
        // Probationary Asset, Associate, Senior Associate (indices 0-2) never open the vendor.
        Assert.Multiple(() =>
        {
            Assert.That(ContractRules.MeetsRankGate(0, ExecutiveVendorMinRankIndex), Is.False);
            Assert.That(ContractRules.MeetsRankGate(1, ExecutiveVendorMinRankIndex), Is.False);
            Assert.That(ContractRules.MeetsRankGate(2, ExecutiveVendorMinRankIndex), Is.False);
        });
    }

    [Test]
    public void ExecutiveVendor_ManagerExactly_IsAdmitted()
    {
        // Manager (index 3) is the floor of the gate, not just Director+.
        Assert.That(ContractRules.MeetsRankGate(3, ExecutiveVendorMinRankIndex), Is.True);
    }

    [Test]
    public void ExecutiveVendor_AboveManager_IsAdmitted()
    {
        // Director, Vice President, Board Member (indices 4-6) all clear the gate.
        Assert.Multiple(() =>
        {
            Assert.That(ContractRules.MeetsRankGate(4, ExecutiveVendorMinRankIndex), Is.True);
            Assert.That(ContractRules.MeetsRankGate(5, ExecutiveVendorMinRankIndex), Is.True);
            Assert.That(ContractRules.MeetsRankGate(6, ExecutiveVendorMinRankIndex), Is.True);
        });
    }
}
