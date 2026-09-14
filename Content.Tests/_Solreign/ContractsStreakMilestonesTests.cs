using Content.Server._Solreign.SeasonLedger;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Pins the closed milestone-threshold set for the Solreign Contracts streak (v14 quest-board
///     extension, spec §3.2) — same shape/thresholds as Directives Fax's own streak milestones.
/// </summary>
[TestFixture]
[TestOf(typeof(ContractsStreakMilestones))]
public sealed class ContractsStreakMilestonesTests
{
    [TestCase(3)]
    [TestCase(5)]
    [TestCase(10)]
    [TestCase(25)]
    [TestCase(50)]
    public void IsMilestone_KnownThresholds_IsTrue(int streak)
    {
        Assert.That(ContractsStreakMilestones.IsMilestone(streak), Is.True);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(4)]
    [TestCase(6)]
    [TestCase(9)]
    [TestCase(11)]
    [TestCase(24)]
    [TestCase(26)]
    [TestCase(49)]
    [TestCase(51)]
    [TestCase(100)]
    public void IsMilestone_NonThresholdCounts_IsFalse(int streak)
    {
        Assert.That(ContractsStreakMilestones.IsMilestone(streak), Is.False);
    }

    [Test]
    public void ReasonLocKeyByStreak_HasExactlyFiveEntries()
    {
        Assert.That(ContractsStreakMilestones.ReasonLocKeyByStreak, Has.Count.EqualTo(5));
    }
}
