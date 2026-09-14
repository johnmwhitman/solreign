using System;
using Content.Server._Solreign.Terminator;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(FixationRules))]
public sealed class FixationRulesTests
{
    // Convenience: an eligible baseline candidate well past the grace window.
    private static FixationCandidate Crew(
        int wanted = 0,
        int demerits = 0,
        float minutes = 30f,
        bool alive = true,
        bool inCustody = false,
        bool enforcement = false)
    {
        return new FixationCandidate(wanted, demerits, minutes, alive, inCustody, enforcement);
    }

    // --- Eligibility ---

    [Test]
    public void DeadCandidate_IsNotEligible()
    {
        Assert.That(FixationRules.IsEligible(Crew(alive: false)), Is.False);
    }

    [Test]
    public void CandidateInCustody_IsNotEligible()
    {
        Assert.That(FixationRules.IsEligible(Crew(inCustody: true)), Is.False);
    }

    [Test]
    public void EnforcementCandidate_IsNotEligible()
    {
        Assert.That(FixationRules.IsEligible(Crew(enforcement: true)), Is.False);
    }

    [Test]
    public void NewJoinInsideGraceWindow_IsNotEligible()
    {
        Assert.That(FixationRules.IsEligible(Crew(minutes: 9.9f)), Is.False);
    }

    [Test]
    public void CandidateExactlyAtGraceBoundary_IsEligible()
    {
        Assert.That(FixationRules.IsEligible(Crew(minutes: FixationRules.DefaultGraceMinutes)), Is.True);
    }

    [Test]
    public void NegativeGraceMinutes_ClampsToZero_SoFreshSpawnIsEligible()
    {
        Assert.That(FixationRules.IsEligible(Crew(minutes: 0f), graceMinutes: -5f), Is.True);
    }

    // --- Compliance score ---

    [Test]
    public void WantedLevel_DominatesDemerits()
    {
        // One wanted level outranks 99 demerits — a fugitive beats a paperwork disaster.
        Assert.That(
            FixationRules.ComplianceScore(Crew(wanted: 1)),
            Is.GreaterThan(FixationRules.ComplianceScore(Crew(demerits: 99))));
    }

    [Test]
    public void NegativeWantedAndDemerits_ClampToZero()
    {
        Assert.That(FixationRules.ComplianceScore(Crew(wanted: -3, demerits: -50)), Is.EqualTo(0));
    }

    [Test]
    public void DemeritPile_ClampsAt99_SoItNeverOutranksAWantedLevel()
    {
        // 500 demerits score exactly like 99 — always below wanted level 1's 100.
        Assert.That(FixationRules.ComplianceScore(Crew(demerits: 500)), Is.EqualTo(99));
        Assert.That(
            FixationRules.ComplianceScore(Crew(wanted: 1)),
            Is.GreaterThan(FixationRules.ComplianceScore(Crew(demerits: 500))));
    }

    // --- Selection: pool filtering ---

    [Test]
    public void EmptyPool_ReturnsMinusOne()
    {
        Assert.That(FixationRules.SelectTarget(Array.Empty<FixationCandidate>(), 0.5), Is.EqualTo(-1));
    }

    [Test]
    public void AllIneligible_ReturnsMinusOne()
    {
        var pool = new[]
        {
            Crew(alive: false),
            Crew(inCustody: true),
            Crew(enforcement: true),
            Crew(minutes: 1f), // inside grace
        };

        Assert.That(FixationRules.SelectTarget(pool, 0.5), Is.EqualTo(-1));
    }

    [Test]
    public void SingleEligibleCandidate_IsAlwaysChosen()
    {
        var pool = new[]
        {
            Crew(alive: false),
            Crew(wanted: 0, minutes: 15f), // the only live option
            Crew(enforcement: true),
        };

        Assert.That(FixationRules.SelectTarget(pool, 0.0), Is.EqualTo(1));
        Assert.That(FixationRules.SelectTarget(pool, 1.0), Is.EqualTo(1));
    }

    // --- Selection: criteria ordering ---

    [Test]
    public void HighestWantedLevel_Wins()
    {
        var pool = new[]
        {
            Crew(wanted: 1, minutes: 200f), // long shift can't beat a higher wanted level
            Crew(wanted: 2, minutes: 15f),
            Crew(wanted: 0, demerits: 500), // demerit pile still can't outrank wanted level

        };

        Assert.That(FixationRules.SelectTarget(pool, 0.5), Is.EqualTo(1));
    }

    [Test]
    public void EqualWanted_DemeritsBreakTheTie()
    {
        var pool = new[]
        {
            Crew(wanted: 1, demerits: 5),
            Crew(wanted: 1, demerits: 20),
            Crew(wanted: 1, demerits: 0),
        };

        Assert.That(FixationRules.SelectTarget(pool, 0.5), Is.EqualTo(1));
    }

    [Test]
    public void EqualScore_LongestShiftBreaksTheTie()
    {
        var pool = new[]
        {
            Crew(minutes: 45f),
            Crew(minutes: 90f), // most overdue for review
            Crew(minutes: 60f),
        };

        Assert.That(FixationRules.SelectTarget(pool, 0.5), Is.EqualTo(1));
    }

    [Test]
    public void IneligibleHighScorer_IsSkippedForEligibleRunnerUp()
    {
        var pool = new[]
        {
            Crew(wanted: 3, inCustody: true), // juiciest target, already detained
            Crew(wanted: 1),
        };

        Assert.That(FixationRules.SelectTarget(pool, 0.5), Is.EqualTo(1));
    }

    // --- Selection: exact ties fall to the roll, deterministically ---

    [Test]
    public void ExactTie_LowRoll_PicksFirstTiedCandidate()
    {
        var pool = new[] { Crew(minutes: 30f), Crew(minutes: 30f), Crew(minutes: 30f) };

        Assert.That(FixationRules.SelectTarget(pool, 0.0), Is.EqualTo(0));
    }

    [Test]
    public void ExactTie_HighRoll_PicksLastTiedCandidate()
    {
        var pool = new[] { Crew(minutes: 30f), Crew(minutes: 30f), Crew(minutes: 30f) };

        Assert.That(FixationRules.SelectTarget(pool, 0.99), Is.EqualTo(2));
    }

    [Test]
    public void ExactTie_RollOfExactlyOne_ClampsToLastTiedCandidate()
    {
        var pool = new[] { Crew(minutes: 30f), Crew(minutes: 30f) };

        Assert.That(FixationRules.SelectTarget(pool, 1.0), Is.EqualTo(1));
    }

    [Test]
    public void ExactTie_OutOfRangeRolls_ClampIntoBounds()
    {
        var pool = new[] { Crew(minutes: 30f), Crew(minutes: 30f) };

        Assert.That(FixationRules.SelectTarget(pool, -0.5), Is.EqualTo(0));
        Assert.That(FixationRules.SelectTarget(pool, 3.7), Is.EqualTo(1));
    }

    [Test]
    public void TieRoll_OnlySelectsAmongTiedCandidates()
    {
        var pool = new[]
        {
            Crew(minutes: 30f),
            Crew(minutes: 15f), // eligible but strictly worse; roll must never land here
            Crew(minutes: 30f),
        };

        Assert.That(FixationRules.SelectTarget(pool, 0.0), Is.EqualTo(0));
        Assert.That(FixationRules.SelectTarget(pool, 0.9), Is.EqualTo(2));
    }

    // --- Retargeting ---

    [Test]
    public void DeadTarget_ForcesRetarget()
    {
        Assert.That(FixationRules.ShouldRetarget(targetAlive: false, targetInCustody: false, targetOnStation: true), Is.True);
    }

    [Test]
    public void DetainedTarget_ForcesRetarget()
    {
        Assert.That(FixationRules.ShouldRetarget(targetAlive: true, targetInCustody: true, targetOnStation: true), Is.True);
    }

    [Test]
    public void DepartedTarget_ForcesRetarget()
    {
        Assert.That(FixationRules.ShouldRetarget(targetAlive: true, targetInCustody: false, targetOnStation: false), Is.True);
    }

    [Test]
    public void HealthyPresentTarget_KeepsTheFixation()
    {
        Assert.That(FixationRules.ShouldRetarget(targetAlive: true, targetInCustody: false, targetOnStation: true), Is.False);
    }

    // --- EMP stagger math ---

    [Test]
    public void Stagger_ActiveBeforeExpiry_InactiveAfter()
    {
        var until = FixationRules.StaggerUntil(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(8));

        Assert.That(FixationRules.IsStaggered(TimeSpan.FromSeconds(107), until), Is.True);
        Assert.That(FixationRules.IsStaggered(TimeSpan.FromSeconds(108), until), Is.False);
    }

    [Test]
    public void NegativeStaggerDuration_ClampsToZero()
    {
        var now = TimeSpan.FromSeconds(100);

        Assert.That(FixationRules.StaggerUntil(now, TimeSpan.FromSeconds(-5)), Is.EqualTo(now));
    }
}
