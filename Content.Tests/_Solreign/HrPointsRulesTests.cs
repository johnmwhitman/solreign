using Content.Server._Solreign.SeasonLedger;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Coverage for <see cref="HrPointsRules"/> — the pure HR Points payout math (Beta Feedback 01, Lane B,
///     ALWAYS-CUMULATIVE model). <c>ForRoundCompletion</c> is exercised here; the title-earned bonus constant
///     (<see cref="HrPointsRules.TitleEarnedPoints"/>) is only ever consumed directly by
///     <c>SeasonLedgerSystem.LoadTitle</c>, so it is pinned as a plain constant-value assertion.
/// </summary>
[TestFixture]
[TestOf(typeof(HrPointsRules))]
public sealed class HrPointsRulesTests
{
    [Test]
    public void ForRoundCompletion_NoContracts_IsJustTheFlatBonus()
    {
        Assert.That(HrPointsRules.ForRoundCompletion(0), Is.EqualTo(HrPointsRules.RoundCompletionPoints));
    }

    [Test]
    public void ForRoundCompletion_EachContractAddsItsOwnBonus()
    {
        Assert.That(HrPointsRules.ForRoundCompletion(1),
            Is.EqualTo(HrPointsRules.RoundCompletionPoints + HrPointsRules.ContractCompletionPoints));

        Assert.That(HrPointsRules.ForRoundCompletion(3),
            Is.EqualTo(HrPointsRules.RoundCompletionPoints + 3 * HrPointsRules.ContractCompletionPoints));
    }

    [Test]
    public void ForRoundCompletion_NegativeContracts_ClampsToZero_NeverGoesBelowTheFlatBonus()
    {
        // Defensive only — contractsCompleted is itself a non-negative accumulator upstream (anti-grief
        // rule 9), but this guards the payout from ever being pushed backward from this call site.
        Assert.That(HrPointsRules.ForRoundCompletion(-5), Is.EqualTo(HrPointsRules.RoundCompletionPoints));
    }

    [Test]
    public void Monotonic_MoreContracts_NeverPaysFewerPoints()
    {
        for (var contracts = 0; contracts < 20; contracts++)
        {
            var current = HrPointsRules.ForRoundCompletion(contracts);
            var plusOne = HrPointsRules.ForRoundCompletion(contracts + 1);

            Assert.That(plusOne, Is.GreaterThan(current),
                $"+1 contract did not increase the round's HR Points payout at contracts={contracts}");
        }
    }

    [Test]
    public void AllPointValues_AreNonNegative()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HrPointsRules.RoundCompletionPoints, Is.GreaterThanOrEqualTo(0));
            Assert.That(HrPointsRules.ContractCompletionPoints, Is.GreaterThanOrEqualTo(0));
            Assert.That(HrPointsRules.TitleEarnedPoints, Is.GreaterThanOrEqualTo(0));
        });
    }
}
