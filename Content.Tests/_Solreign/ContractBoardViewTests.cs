using System;
using Content.Server._Solreign.Contracts;
using Content.Shared._Solreign.Contracts;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(ContractBoardView))]
public sealed class ContractBoardViewTests
{
    private static SolreignActiveContract MakeContract()
    {
        var contract = new SolreignActiveContract
        {
            Id = "SOL-WO-042",
            Prototype = "SolContractColaAudit",
            Scope = SolreignContractScope.Personal,
            Claimant = Guid.NewGuid(),
            ClaimantName = "K. Whitman",
            Launched = false,
            Required = new[] { 5, 10 },
            Progress = new[] { 3, 0 },
            StandingPerHead = 3,
        };
        return contract;
    }

    // --- Field mapping ---

    [Test]
    public void BuildListing_MapsEveryWireField()
    {
        var contract = MakeContract();
        var listing = ContractBoardView.BuildListing(contract);

        Assert.Multiple(() =>
        {
            Assert.That(listing.Id, Is.EqualTo("SOL-WO-042"));
            Assert.That(listing.Prototype, Is.EqualTo("SolContractColaAudit"));
            Assert.That(listing.Scope, Is.EqualTo(SolreignContractScope.Personal));
            Assert.That(listing.ClaimantName, Is.EqualTo("K. Whitman"));
            Assert.That(listing.Launched, Is.False);
            Assert.That(listing.Progress, Is.EqualTo(new[] { 3, 0 }));
            Assert.That(listing.Required, Is.EqualTo(new[] { 5, 10 }));
            Assert.That(listing.StandingPerHead, Is.EqualTo(3));
        });
    }

    [Test]
    public void BuildListing_UnclaimedContract_HasNullClaimantName()
    {
        var contract = MakeContract();
        contract.Claimant = null;
        contract.ClaimantName = null;

        var listing = ContractBoardView.BuildListing(contract);

        Assert.That(listing.ClaimantName, Is.Null);
    }

    // --- Privacy: the roster crosses the wire as a head-count, never as accounts ---

    [Test]
    public void BuildListing_ReducesRosterToHeadCount()
    {
        var contract = MakeContract();
        contract.Scope = SolreignContractScope.SalvageRaid;
        contract.Participants[Guid.NewGuid()] = "Asset One";
        contract.Participants[Guid.NewGuid()] = "Asset Two";

        var listing = ContractBoardView.BuildListing(contract);

        Assert.That(listing.Participants, Is.EqualTo(2));
    }

    // --- Snapshot semantics: the wire lists are copies, never aliases of live server arrays ---

    [Test]
    public void BuildListing_ProgressAndRequired_AreCopies()
    {
        var contract = MakeContract();
        var listing = ContractBoardView.BuildListing(contract);

        contract.Progress[0] = 99;
        contract.Required[0] = 99;

        Assert.Multiple(() =>
        {
            Assert.That(listing.Progress[0], Is.EqualTo(3), "listing progress must not alias the live array");
            Assert.That(listing.Required[0], Is.EqualTo(5), "listing required must not alias the live array");
        });
    }

    [Test]
    public void BuildListings_PreservesBoardOrder()
    {
        var first = MakeContract();
        var second = MakeContract();
        second.Id = "SOL-WO-043";

        var listings = ContractBoardView.BuildListings(new[] { first, second });

        Assert.Multiple(() =>
        {
            Assert.That(listings, Has.Count.EqualTo(2));
            Assert.That(listings[0].Id, Is.EqualTo("SOL-WO-042"));
            Assert.That(listings[1].Id, Is.EqualTo("SOL-WO-043"));
        });
    }

    // --- Skip cooldown: countdown for the client, clamped so it can never render negative ---

    [Test]
    public void UntilNextSkip_FutureSkipTime_ReturnsRemaining()
    {
        var remaining = ContractBoardView.UntilNextSkip(
            nextSkipTime: TimeSpan.FromMinutes(20),
            curTime: TimeSpan.FromMinutes(5));

        Assert.That(remaining, Is.EqualTo(TimeSpan.FromMinutes(15)));
    }

    [Test]
    public void UntilNextSkip_PastSkipTime_ClampsToZero()
    {
        var remaining = ContractBoardView.UntilNextSkip(
            nextSkipTime: TimeSpan.FromMinutes(5),
            curTime: TimeSpan.FromMinutes(20));

        Assert.That(remaining, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void UntilNextSkip_ExactBoundary_IsZero()
    {
        var now = TimeSpan.FromMinutes(7);

        Assert.That(ContractBoardView.UntilNextSkip(now, now), Is.EqualTo(TimeSpan.Zero));
    }
}
