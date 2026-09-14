using System;
using Content.Server._Solreign.MartialArts;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(ChefComboRules))]
public sealed class ChefComboRulesTests
{
    private static readonly TimeSpan T0 = TimeSpan.FromSeconds(500);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1.5);

    private static TimeSpan At(double seconds) => T0 + TimeSpan.FromSeconds(seconds);

    // --- Pair table: the three hardcoded combos and nothing else ---

    [Test]
    public void Pair_SwatSwat_IsHeatCheck()
    {
        Assert.That(ChefComboRules.Pair(ChefStep.Swat, ChefStep.Swat), Is.EqualTo(ChefCombo.HeatCheck));
    }

    [Test]
    public void Pair_SwatToss_IsEightySixed()
    {
        Assert.That(ChefComboRules.Pair(ChefStep.Swat, ChefStep.Toss), Is.EqualTo(ChefCombo.EightySixed));
    }

    [Test]
    public void Pair_TossToss_IsOrderUp()
    {
        Assert.That(ChefComboRules.Pair(ChefStep.Toss, ChefStep.Toss), Is.EqualTo(ChefCombo.OrderUp));
    }

    [Test]
    public void Pair_TossSwat_IsNotACombo()
    {
        Assert.That(ChefComboRules.Pair(ChefStep.Toss, ChefStep.Swat), Is.EqualTo(ChefCombo.None));
    }

    [Test]
    public void Pair_WithNoneStep_IsNeverACombo()
    {
        Assert.That(ChefComboRules.Pair(ChefStep.None, ChefStep.Swat), Is.EqualTo(ChefCombo.None));
        Assert.That(ChefComboRules.Pair(ChefStep.Swat, ChefStep.None), Is.EqualTo(ChefCombo.None));
        Assert.That(ChefComboRules.Pair(ChefStep.None, ChefStep.None), Is.EqualTo(ChefCombo.None));
    }

    // --- Window math ---

    [Test]
    public void Window_InsideWindow_Chains()
    {
        Assert.That(ChefComboRules.WithinWindow(T0, At(0.5), Window), Is.True);
    }

    [Test]
    public void Window_ExactlyAtWindowEdge_StillChains()
    {
        // The edge is inclusive: a follow-up landing exactly ComboWindow later counts.
        Assert.That(ChefComboRules.WithinWindow(T0, At(1.5), Window), Is.True);
    }

    [Test]
    public void Window_OneMillisecondPastEdge_DoesNotChain()
    {
        Assert.That(ChefComboRules.WithinWindow(T0, At(1.5) + TimeSpan.FromMilliseconds(1), Window), Is.False);
    }

    [Test]
    public void Window_SameInstant_Chains()
    {
        Assert.That(ChefComboRules.WithinWindow(T0, T0, Window), Is.True);
    }

    [Test]
    public void Window_PreviousStepInFuture_NeverChains()
    {
        // Clock weirdness (previous step timestamped after now) must not chain.
        Assert.That(ChefComboRules.WithinWindow(At(1), T0, Window), Is.False);
    }

    [Test]
    public void Window_NegativeWindow_ClampsToSameTickOnly()
    {
        var negative = TimeSpan.FromSeconds(-5);
        Assert.That(ChefComboRules.WithinWindow(T0, T0, negative), Is.True);
        Assert.That(ChefComboRules.WithinWindow(T0, T0 + TimeSpan.FromMilliseconds(1), negative), Is.False);
    }

    // --- Advance: chain building ---

    [Test]
    public void Advance_FirstStep_OpensChainWithoutCombo()
    {
        var combo = ChefComboRules.Advance(
            ChefStep.None, default, ChefStep.Swat, T0, Window, sameTarget: false, out var next);

        Assert.That(combo, Is.EqualTo(ChefCombo.None));
        Assert.That(next, Is.EqualTo(ChefStep.Swat));
    }

    [Test]
    public void Advance_TwoQuickSwats_FireHeatCheckAndConsumeChain()
    {
        var combo = ChefComboRules.Advance(
            ChefStep.Swat, T0, ChefStep.Swat, At(1), Window, sameTarget: true, out var next);

        Assert.That(combo, Is.EqualTo(ChefCombo.HeatCheck));
        Assert.That(next, Is.EqualTo(ChefStep.None), "a finished combo must consume the chain");
    }

    [Test]
    public void Advance_SwatThenToss_FiresEightySixed()
    {
        var combo = ChefComboRules.Advance(
            ChefStep.Swat, T0, ChefStep.Toss, At(1), Window, sameTarget: true, out var next);

        Assert.That(combo, Is.EqualTo(ChefCombo.EightySixed));
        Assert.That(next, Is.EqualTo(ChefStep.None));
    }

    [Test]
    public void Advance_TossThenToss_FiresOrderUp()
    {
        var combo = ChefComboRules.Advance(
            ChefStep.Toss, T0, ChefStep.Toss, At(1), Window, sameTarget: true, out var next);

        Assert.That(combo, Is.EqualTo(ChefCombo.OrderUp));
        Assert.That(next, Is.EqualTo(ChefStep.None));
    }

    [Test]
    public void Advance_TossThenSwat_NoComboButSwatOpensNewChain()
    {
        var combo = ChefComboRules.Advance(
            ChefStep.Toss, T0, ChefStep.Swat, At(1), Window, sameTarget: true, out var next);

        Assert.That(combo, Is.EqualTo(ChefCombo.None));
        Assert.That(next, Is.EqualTo(ChefStep.Swat), "the unmatched swat must become the new opener");
    }

    [Test]
    public void Advance_SlowFollowUp_OpensNewChainInstead()
    {
        var combo = ChefComboRules.Advance(
            ChefStep.Swat, T0, ChefStep.Swat, At(2), Window, sameTarget: true, out var next);

        Assert.That(combo, Is.EqualTo(ChefCombo.None));
        Assert.That(next, Is.EqualTo(ChefStep.Swat));
    }

    [Test]
    public void Advance_DifferentTarget_BreaksTheChain()
    {
        // Fast enough for Heat Check, but the second swat hit somebody else.
        var combo = ChefComboRules.Advance(
            ChefStep.Swat, T0, ChefStep.Swat, At(1), Window, sameTarget: false, out var next);

        Assert.That(combo, Is.EqualTo(ChefCombo.None));
        Assert.That(next, Is.EqualTo(ChefStep.Swat));
    }

    [Test]
    public void Advance_NoneStep_IsANoOpOnTheChain()
    {
        var combo = ChefComboRules.Advance(
            ChefStep.Swat, T0, ChefStep.None, At(1), Window, sameTarget: true, out var next);

        Assert.That(combo, Is.EqualTo(ChefCombo.None));
        Assert.That(next, Is.EqualTo(ChefStep.Swat), "a None step must not disturb the opener");
    }

    [Test]
    public void Advance_FourQuickSwats_YieldExactlyTwoHeatChecks()
    {
        // Simulates the system loop: swat every 0.5s on one target.
        var previous = ChefStep.None;
        var previousTime = default(TimeSpan);
        var checks = 0;

        for (var i = 0; i < 4; i++)
        {
            var now = At(i * 0.5);
            var combo = ChefComboRules.Advance(
                previous, previousTime, ChefStep.Swat, now, Window, sameTarget: true, out previous);
            previousTime = now;

            if (combo == ChefCombo.HeatCheck)
                checks++;
        }

        Assert.That(checks, Is.EqualTo(2), "swats 1+2 and swats 3+4 combo; swat 3 must not chain off consumed swat 2");
    }

    // --- Finisher cooldown math (same shape as CarpComboRules/JudoComboRules) ---

    [Test]
    public void ComboReady_ExactlyAtNextComboTime_IsReady()
    {
        Assert.That(ChefComboRules.ComboReady(T0, T0), Is.True);
    }

    [Test]
    public void ComboReady_BeforeNextComboTime_IsNotReady()
    {
        Assert.That(ChefComboRules.ComboReady(T0, T0 + TimeSpan.FromMilliseconds(1)), Is.False);
    }

    [Test]
    public void FreshComponent_DefaultNextComboTime_IsImmediatelyReady()
    {
        Assert.That(ChefComboRules.ComboReady(TimeSpan.Zero, default), Is.True);
    }

    [Test]
    public void NextComboTime_AddsCooldown()
    {
        Assert.That(
            ChefComboRules.NextComboTime(T0, TimeSpan.FromSeconds(2)),
            Is.EqualTo(T0 + TimeSpan.FromSeconds(2)));
    }

    [Test]
    public void NextComboTime_NegativeCooldown_ClampsToZero()
    {
        Assert.That(ChefComboRules.NextComboTime(T0, TimeSpan.FromSeconds(-10)), Is.EqualTo(T0));
    }
}
