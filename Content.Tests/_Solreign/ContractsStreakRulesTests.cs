#nullable enable
using System;
using System.Collections.Generic;
using Content.Server._Solreign.SeasonLedger;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Pure fold rules for the Contracts streak (v14 quest-board extension) — cross-scope and
///     disconnected-roster guards that the store layer cannot express alone.
/// </summary>
[TestFixture]
[TestOf(typeof(ContractsStreakRules))]
public sealed class ContractsStreakRulesTests
{
    [Test]
    public void ShouldFoldAccount_Disconnected_IsFalse()
    {
        Assert.That(ContractsStreakRules.ShouldFoldAccount(connected: false), Is.False,
            "disconnected roster members must be excluded from streak advance/reset");
    }

    [Test]
    public void ShouldFoldAccount_Connected_IsTrue()
    {
        Assert.That(ContractsStreakRules.ShouldFoldAccount(connected: true), Is.True);
    }

    [Test]
    public void CompletedPersonalThisRound_Zero_IsFalse()
    {
        Assert.That(ContractsStreakRules.CompletedPersonalThisRound(0), Is.False);
    }

    [Test]
    public void CompletedPersonalThisRound_Positive_IsTrue()
    {
        Assert.That(ContractsStreakRules.CompletedPersonalThisRound(1), Is.True);
    }

    [Test]
    public void CountPersonalCompletions_IgnoresSalvageAndOtherUsers()
    {
        var user = Guid.NewGuid();
        var other = Guid.NewGuid();
        var log = new List<ContractLogRecord>
        {
            new(RoundId: 1, user, "SolContractColaAudit", "personal"),
            new(RoundId: 1, user, "SolRaidScrapReclamation", "salvage-raid"),
            new(RoundId: 1, other, "SolContractPenRecovery", "personal"),
            new(RoundId: 1, user, "SolContractMandatoryFun", "personal"),
        };

        Assert.That(ContractsStreakRules.CountPersonalCompletions(log, user), Is.EqualTo(2),
            "salvage participation must not count toward the Personal streak");
        Assert.That(ContractsStreakRules.CompletedPersonalThisRound(
                ContractsStreakRules.CountPersonalCompletions(log, user)), Is.True);

        // Cross-scope: salvage-only participant must not advance the Personal streak.
        var salvageOnly = Guid.NewGuid();
        var salvageLog = new List<ContractLogRecord>
        {
            new(RoundId: 1, salvageOnly, "SolRaidScrapReclamation", "salvage-raid"),
        };
        Assert.That(ContractsStreakRules.CountPersonalCompletions(salvageLog, salvageOnly), Is.EqualTo(0));
        Assert.That(ContractsStreakRules.CompletedPersonalThisRound(
                ContractsStreakRules.CountPersonalCompletions(salvageLog, salvageOnly)), Is.False,
            "aggregate ContractsCompleted would be >0 here — personal-scope fold must stay zero");
    }

    [Test]
    public void ShouldNotifyMilestone_Replay_IsFalse_EvenAtMilestoneStreak()
    {
        // Same/older-round replay returns the existing milestone count; caller must not re-notify.
        Assert.That(ContractsStreakRules.ShouldNotifyMilestone(
                completedThisRound: true, wasReplay: true, currentStreak: 3), Is.False);
    }

    [Test]
    public void ShouldNotifyMilestone_FreshMilestone_IsTrue()
    {
        Assert.That(ContractsStreakRules.ShouldNotifyMilestone(
                completedThisRound: true, wasReplay: false, currentStreak: 3), Is.True);
    }

    [Test]
    public void ShouldNotifyMilestone_FreshNonMilestone_IsFalse()
    {
        Assert.That(ContractsStreakRules.ShouldNotifyMilestone(
                completedThisRound: true, wasReplay: false, currentStreak: 2), Is.False);
    }
}
