using System;
using Content.Server._Solreign.MartialArts;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(CarpComboRules))]
public sealed class CarpComboRulesTests
{
    private static readonly TimeSpan T0 = TimeSpan.FromSeconds(500);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1.5);

    private static TimeSpan At(double seconds) => T0 + TimeSpan.FromSeconds(seconds);

    // --- Pair table: the three hardcoded combos and nothing else ---

    [Test]
    public void Pair_StrikeStrike_IsCarpRush()
    {
        Assert.That(CarpComboRules.Pair(CarpStep.Strike, CarpStep.Strike), Is.EqualTo(CarpCombo.CarpRush));
    }

    [Test]
    public void Pair_StrikeShove_IsRisingTide()
    {
        Assert.That(CarpComboRules.Pair(CarpStep.Strike, CarpStep.Shove), Is.EqualTo(CarpCombo.RisingTide));
    }

    [Test]
    public void Pair_ShoveShove_IsGentleCurrent()
    {
        Assert.That(CarpComboRules.Pair(CarpStep.Shove, CarpStep.Shove), Is.EqualTo(CarpCombo.GentleCurrent));
    }

    [Test]
    public void Pair_ShoveStrike_IsNotACombo()
    {
        Assert.That(CarpComboRules.Pair(CarpStep.Shove, CarpStep.Strike), Is.EqualTo(CarpCombo.None));
    }

    [Test]
    public void Pair_WithNoneStep_IsNeverACombo()
    {
        Assert.That(CarpComboRules.Pair(CarpStep.None, CarpStep.Strike), Is.EqualTo(CarpCombo.None));
        Assert.That(CarpComboRules.Pair(CarpStep.Strike, CarpStep.None), Is.EqualTo(CarpCombo.None));
        Assert.That(CarpComboRules.Pair(CarpStep.None, CarpStep.None), Is.EqualTo(CarpCombo.None));
    }

    // --- Window math ---

    [Test]
    public void Window_InsideWindow_Chains()
    {
        Assert.That(CarpComboRules.WithinWindow(T0, At(0.5), Window), Is.True);
    }

    [Test]
    public void Window_ExactlyAtWindowEdge_StillChains()
    {
        // The edge is inclusive: a follow-up landing exactly ComboWindow later counts.
        Assert.That(CarpComboRules.WithinWindow(T0, At(1.5), Window), Is.True);
    }

    [Test]
    public void Window_OneMillisecondPastEdge_DoesNotChain()
    {
        Assert.That(CarpComboRules.WithinWindow(T0, At(1.5) + TimeSpan.FromMilliseconds(1), Window), Is.False);
    }

    [Test]
    public void Window_SameInstant_Chains()
    {
        Assert.That(CarpComboRules.WithinWindow(T0, T0, Window), Is.True);
    }

    [Test]
    public void Window_PreviousStepInFuture_NeverChains()
    {
        // Clock weirdness (previous step timestamped after now) must not chain.
        Assert.That(CarpComboRules.WithinWindow(At(1), T0, Window), Is.False);
    }

    [Test]
    public void Window_NegativeWindow_ClampsToSameTickOnly()
    {
        var negative = TimeSpan.FromSeconds(-5);
        Assert.That(CarpComboRules.WithinWindow(T0, T0, negative), Is.True);
        Assert.That(CarpComboRules.WithinWindow(T0, T0 + TimeSpan.FromMilliseconds(1), negative), Is.False);
    }

    // --- Advance: chain building ---

    [Test]
    public void Advance_FirstStep_OpensChainWithoutCombo()
    {
        var combo = CarpComboRules.Advance(
            CarpStep.None, default, CarpStep.Strike, T0, Window, sameTarget: false, out var next);

        Assert.That(combo, Is.EqualTo(CarpCombo.None));
        Assert.That(next, Is.EqualTo(CarpStep.Strike));
    }

    [Test]
    public void Advance_TwoQuickStrikes_FireCarpRushAndConsumeChain()
    {
        var combo = CarpComboRules.Advance(
            CarpStep.Strike, T0, CarpStep.Strike, At(1), Window, sameTarget: true, out var next);

        Assert.That(combo, Is.EqualTo(CarpCombo.CarpRush));
        Assert.That(next, Is.EqualTo(CarpStep.None), "a finished combo must consume the chain");
    }

    [Test]
    public void Advance_StrikeThenShove_FiresRisingTide()
    {
        var combo = CarpComboRules.Advance(
            CarpStep.Strike, T0, CarpStep.Shove, At(1), Window, sameTarget: true, out var next);

        Assert.That(combo, Is.EqualTo(CarpCombo.RisingTide));
        Assert.That(next, Is.EqualTo(CarpStep.None));
    }

    [Test]
    public void Advance_ShoveThenShove_FiresGentleCurrent()
    {
        var combo = CarpComboRules.Advance(
            CarpStep.Shove, T0, CarpStep.Shove, At(1), Window, sameTarget: true, out var next);

        Assert.That(combo, Is.EqualTo(CarpCombo.GentleCurrent));
        Assert.That(next, Is.EqualTo(CarpStep.None));
    }

    [Test]
    public void Advance_ShoveThenStrike_NoComboButStrikeOpensNewChain()
    {
        var combo = CarpComboRules.Advance(
            CarpStep.Shove, T0, CarpStep.Strike, At(1), Window, sameTarget: true, out var next);

        Assert.That(combo, Is.EqualTo(CarpCombo.None));
        Assert.That(next, Is.EqualTo(CarpStep.Strike), "the unmatched strike must become the new opener");
    }

    [Test]
    public void Advance_SlowFollowUp_OpensNewChainInstead()
    {
        var combo = CarpComboRules.Advance(
            CarpStep.Strike, T0, CarpStep.Strike, At(2), Window, sameTarget: true, out var next);

        Assert.That(combo, Is.EqualTo(CarpCombo.None));
        Assert.That(next, Is.EqualTo(CarpStep.Strike));
    }

    [Test]
    public void Advance_DifferentTarget_BreaksTheChain()
    {
        // Fast enough for Carp Rush, but the second strike hit somebody else.
        var combo = CarpComboRules.Advance(
            CarpStep.Strike, T0, CarpStep.Strike, At(1), Window, sameTarget: false, out var next);

        Assert.That(combo, Is.EqualTo(CarpCombo.None));
        Assert.That(next, Is.EqualTo(CarpStep.Strike));
    }

    [Test]
    public void Advance_NoneStep_IsANoOpOnTheChain()
    {
        var combo = CarpComboRules.Advance(
            CarpStep.Strike, T0, CarpStep.None, At(1), Window, sameTarget: true, out var next);

        Assert.That(combo, Is.EqualTo(CarpCombo.None));
        Assert.That(next, Is.EqualTo(CarpStep.Strike), "a None step must not disturb the opener");
    }

    [Test]
    public void Advance_FourQuickStrikes_YieldExactlyTwoCarpRushes()
    {
        // Simulates the system loop: strike every 0.5s on one target.
        var previous = CarpStep.None;
        var previousTime = default(TimeSpan);
        var rushes = 0;

        for (var i = 0; i < 4; i++)
        {
            var now = At(i * 0.5);
            var combo = CarpComboRules.Advance(
                previous, previousTime, CarpStep.Strike, now, Window, sameTarget: true, out previous);
            previousTime = now;

            if (combo == CarpCombo.CarpRush)
                rushes++;
        }

        Assert.That(rushes, Is.EqualTo(2), "hits 1+2 and hits 3+4 combo; hit 3 must not chain off consumed hit 2");
    }

    // --- Finisher cooldown math (same shape as ZoneRules cooldowns) ---

    [Test]
    public void ComboReady_ExactlyAtNextComboTime_IsReady()
    {
        Assert.That(CarpComboRules.ComboReady(T0, T0), Is.True);
    }

    [Test]
    public void ComboReady_BeforeNextComboTime_IsNotReady()
    {
        Assert.That(CarpComboRules.ComboReady(T0, T0 + TimeSpan.FromMilliseconds(1)), Is.False);
    }

    [Test]
    public void FreshComponent_DefaultNextComboTime_IsImmediatelyReady()
    {
        Assert.That(CarpComboRules.ComboReady(TimeSpan.Zero, default), Is.True);
    }

    [Test]
    public void NextComboTime_AddsCooldown()
    {
        Assert.That(
            CarpComboRules.NextComboTime(T0, TimeSpan.FromSeconds(2)),
            Is.EqualTo(T0 + TimeSpan.FromSeconds(2)));
    }

    [Test]
    public void NextComboTime_NegativeCooldown_ClampsToZero()
    {
        Assert.That(CarpComboRules.NextComboTime(T0, TimeSpan.FromSeconds(-10)), Is.EqualTo(T0));
    }
}
