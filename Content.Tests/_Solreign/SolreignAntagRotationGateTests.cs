using System;
using System.Collections.Generic;
using Content.Server._Solreign.Antags;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     The rotation gate must spread antag turns WITHOUT ever starving selection.
///
///     SOLREIGN runs at 0-5 players. A naive "skip everyone who was recently antag" rule is
///     actively harmful there: with three players, two of whom drew antag last shift, a hard
///     exclusion can leave nobody eligible and ship a round with no antagonist at all. The cap is
///     therefore the load-bearing part of this feature, not a nicety, and it is what these tests
///     pin down.
/// </summary>
[TestFixture]
public sealed class SolreignAntagRotationGateTests
{
    private static Guid G(int n) => new($"{n:D8}-0000-0000-0000-000000000000");

    private static SolreignAntagRotationGate Gate() => new();

    [Test]
    public void ThreePlayers_TwoRecentAntags_StillLeavesTwoEligible()
    {
        // The exact low-pop trap: excluding both recent antags would leave one candidate, and
        // excluding a third would leave none.
        var gate = Gate();
        gate.ApplyCapped(new HashSet<Guid> { G(1), G(2) }, knownCandidates: 3);

        Assert.That(gate.CooldownSet, Has.Count.EqualTo(1),
            "with 3 players only one may be gated, so at least 2 remain selectable");
    }

    [Test]
    public void TwoPlayers_NobodyIsEverGated()
    {
        var gate = Gate();
        gate.ApplyCapped(new HashSet<Guid> { G(1), G(2) }, knownCandidates: 2);

        Assert.That(gate.CooldownSet, Is.Empty,
            "at the minimum pool size the gate must do nothing at all");
    }

    [Test]
    public void OnePlayer_NobodyIsEverGated()
    {
        var gate = Gate();
        gate.ApplyCapped(new HashSet<Guid> { G(1) }, knownCandidates: 1);

        Assert.That(gate.CooldownSet, Is.Empty);
    }

    [Test]
    public void HealthyPopulation_GatesEveryRecentAntag()
    {
        // With room to spare the feature does its actual job.
        var gate = Gate();
        gate.ApplyCapped(new HashSet<Guid> { G(1), G(2), G(3) }, knownCandidates: 12);

        Assert.That(gate.CooldownSet, Has.Count.EqualTo(3));
    }

    [Test]
    public void NoRecentAntags_GatesNobody()
    {
        var gate = Gate();
        gate.ApplyCapped(new HashSet<Guid>(), knownCandidates: 10);

        Assert.That(gate.CooldownSet, Is.Empty);
    }

    [Test]
    public void RebuildingReplacesThePreviousRoundsSet()
    {
        var gate = Gate();
        gate.ApplyCapped(new HashSet<Guid> { G(1) }, knownCandidates: 10);
        gate.ApplyCapped(new HashSet<Guid> { G(2) }, knownCandidates: 10);

        Assert.Multiple(() =>
        {
            Assert.That(gate.CooldownSet, Does.Contain(G(2)));
            Assert.That(gate.CooldownSet, Does.Not.Contain(G(1)),
                "last round's gating must not accumulate forever");
        });
    }

    [Test]
    public void ZeroCandidates_IsSafe()
    {
        var gate = Gate();
        gate.ApplyCapped(new HashSet<Guid> { G(1) }, knownCandidates: 0);

        Assert.That(gate.CooldownSet, Is.Empty, "a negative budget must not throw or gate anyone");
    }
}
