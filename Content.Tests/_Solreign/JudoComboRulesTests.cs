using System;
using Content.Server._Solreign.MartialArts;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(JudoComboRules))]
public sealed class JudoComboRulesTests
{
    private static readonly TimeSpan T0 = TimeSpan.FromSeconds(500);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1.5);

    private static TimeSpan At(double seconds) => T0 + TimeSpan.FromSeconds(seconds);

    // --- Window math (same shape as CarpComboRulesTests) ---

    [Test]
    public void Window_InsideWindow_Chains()
    {
        Assert.That(JudoComboRules.WithinWindow(T0, At(0.5), Window), Is.True);
    }

    [Test]
    public void Window_ExactlyAtWindowEdge_StillChains()
    {
        Assert.That(JudoComboRules.WithinWindow(T0, At(1.5), Window), Is.True);
    }

    [Test]
    public void Window_OneMillisecondPastEdge_DoesNotChain()
    {
        Assert.That(JudoComboRules.WithinWindow(T0, At(1.5) + TimeSpan.FromMilliseconds(1), Window), Is.False);
    }

    [Test]
    public void Window_NegativeWindow_ClampsToSameTickOnly()
    {
        var negative = TimeSpan.FromSeconds(-5);
        Assert.That(JudoComboRules.WithinWindow(T0, T0, negative), Is.True);
        Assert.That(JudoComboRules.WithinWindow(T0, T0 + TimeSpan.FromMilliseconds(1), negative), Is.False);
    }

    // --- Advance: opening the chain ---

    [Test]
    public void Advance_PushFromEmpty_OpensChainWithoutThrow()
    {
        var thrown = JudoComboRules.Advance(
            JudoChainState.Empty, default, JudoStep.Push, T0, Window, sameTarget: false, out var next);

        Assert.That(thrown, Is.False);
        Assert.That(next, Is.EqualTo(JudoChainState.PushLanded));
    }

    [Test]
    public void Advance_ShoveFromEmpty_IsANoOp()
    {
        var thrown = JudoComboRules.Advance(
            JudoChainState.Empty, default, JudoStep.Shove, T0, Window, sameTarget: false, out var next);

        Assert.That(thrown, Is.False);
        Assert.That(next, Is.EqualTo(JudoChainState.Empty), "only Push may open the chain");
    }

    [Test]
    public void Advance_GrabFromEmpty_IsANoOp()
    {
        var thrown = JudoComboRules.Advance(
            JudoChainState.Empty, default, JudoStep.Grab, T0, Window, sameTarget: false, out var next);

        Assert.That(thrown, Is.False);
        Assert.That(next, Is.EqualTo(JudoChainState.Empty));
    }

    // --- Advance: the middle step (Shove after Push) ---

    [Test]
    public void Advance_ShoveAfterPush_QuickAndSameTarget_Continues()
    {
        var thrown = JudoComboRules.Advance(
            JudoChainState.PushLanded, T0, JudoStep.Shove, At(1), Window, sameTarget: true, out var next);

        Assert.That(thrown, Is.False, "Shove alone never throws -- Grab is the finisher");
        Assert.That(next, Is.EqualTo(JudoChainState.PushShoveLanded));
    }

    [Test]
    public void Advance_ShoveAfterPush_SlowFollowUp_ResetsToEmpty()
    {
        var thrown = JudoComboRules.Advance(
            JudoChainState.PushLanded, T0, JudoStep.Shove, At(2), Window, sameTarget: true, out var next);

        Assert.That(thrown, Is.False);
        Assert.That(next, Is.EqualTo(JudoChainState.Empty), "the stale Shove doesn't open a chain of its own");
    }

    [Test]
    public void Advance_ShoveAfterPush_DifferentTarget_ResetsToEmpty()
    {
        var thrown = JudoComboRules.Advance(
            JudoChainState.PushLanded, T0, JudoStep.Shove, At(1), Window, sameTarget: false, out var next);

        Assert.That(thrown, Is.False);
        Assert.That(next, Is.EqualTo(JudoChainState.Empty));
    }

    [Test]
    public void Advance_GrabAfterPush_OutOfOrder_ResetsToEmpty()
    {
        // Grab before Shove skips the middle step -- not a shortcut, just breaks the chain.
        var thrown = JudoComboRules.Advance(
            JudoChainState.PushLanded, T0, JudoStep.Grab, At(1), Window, sameTarget: true, out var next);

        Assert.That(thrown, Is.False);
        Assert.That(next, Is.EqualTo(JudoChainState.Empty));
    }

    [Test]
    public void Advance_PushAfterPush_RefreshesTheOpener()
    {
        // A second quick Push just re-times the opener instead of breaking it.
        var thrown = JudoComboRules.Advance(
            JudoChainState.PushLanded, T0, JudoStep.Push, At(1), Window, sameTarget: true, out var next);

        Assert.That(thrown, Is.False);
        Assert.That(next, Is.EqualTo(JudoChainState.PushLanded));
    }

    // --- Advance: the finisher (Grab after Push, Shove) ---

    [Test]
    public void Advance_GrabAfterPushShove_QuickAndSameTarget_Throws()
    {
        var thrown = JudoComboRules.Advance(
            JudoChainState.PushShoveLanded, T0, JudoStep.Grab, At(1), Window, sameTarget: true, out var next);

        Assert.That(thrown, Is.True);
        Assert.That(next, Is.EqualTo(JudoChainState.Empty), "a finished throw must consume the chain");
    }

    [Test]
    public void Advance_GrabAfterPushShove_SlowFollowUp_DoesNotThrow()
    {
        var thrown = JudoComboRules.Advance(
            JudoChainState.PushShoveLanded, T0, JudoStep.Grab, At(2), Window, sameTarget: true, out var next);

        Assert.That(thrown, Is.False);
        Assert.That(next, Is.EqualTo(JudoChainState.Empty));
    }

    [Test]
    public void Advance_GrabAfterPushShove_DifferentTarget_DoesNotThrow()
    {
        var thrown = JudoComboRules.Advance(
            JudoChainState.PushShoveLanded, T0, JudoStep.Grab, At(1), Window, sameTarget: false, out var next);

        Assert.That(thrown, Is.False);
        Assert.That(next, Is.EqualTo(JudoChainState.Empty));
    }

    [Test]
    public void Advance_ShoveAfterPushShove_ResetsInsteadOfDoubleCounting()
    {
        var thrown = JudoComboRules.Advance(
            JudoChainState.PushShoveLanded, T0, JudoStep.Shove, At(1), Window, sameTarget: true, out var next);

        Assert.That(thrown, Is.False);
        Assert.That(next, Is.EqualTo(JudoChainState.Empty));
    }

    [Test]
    public void Advance_PushAfterPushShove_OpensAFreshChain()
    {
        var thrown = JudoComboRules.Advance(
            JudoChainState.PushShoveLanded, T0, JudoStep.Push, At(1), Window, sameTarget: true, out var next);

        Assert.That(thrown, Is.False);
        Assert.That(next, Is.EqualTo(JudoChainState.PushLanded));
    }

    // --- Full sequence simulation (system loop shape) ---

    [Test]
    public void FullSequence_PushShoveGrab_ThrowsExactlyOnce()
    {
        var state = JudoChainState.Empty;
        var lastTime = default(TimeSpan);
        var throwCount = 0;

        foreach (var (step, when) in new[]
                 {
                     (JudoStep.Push, At(0)),
                     (JudoStep.Shove, At(0.5)),
                     (JudoStep.Grab, At(1)),
                 })
        {
            var thrown = JudoComboRules.Advance(state, lastTime, step, when, Window, sameTarget: true, out state);
            lastTime = when;
            if (thrown)
                throwCount++;
        }

        Assert.That(throwCount, Is.EqualTo(1));
        Assert.That(state, Is.EqualTo(JudoChainState.Empty));
    }

    [Test]
    public void FullSequence_RepeatedAfterThrow_ThrowsAgain()
    {
        // Simulates two back-to-back takedowns once cooldown allows it (cooldown itself is
        // ComboReady's job, exercised separately below).
        var state = JudoChainState.Empty;
        var lastTime = default(TimeSpan);
        var throwCount = 0;

        foreach (var (step, when) in new[]
                 {
                     (JudoStep.Push, At(0)),
                     (JudoStep.Shove, At(0.5)),
                     (JudoStep.Grab, At(1)),
                     (JudoStep.Push, At(1.5)),
                     (JudoStep.Shove, At(2)),
                     (JudoStep.Grab, At(2.5)),
                 })
        {
            var thrown = JudoComboRules.Advance(state, lastTime, step, when, Window, sameTarget: true, out state);
            lastTime = when;
            if (thrown)
                throwCount++;
        }

        Assert.That(throwCount, Is.EqualTo(2));
    }

    [Test]
    public void FullSequence_ShoveGrabWithoutPush_NeverThrows()
    {
        var state = JudoChainState.Empty;
        var lastTime = default(TimeSpan);
        var throwCount = 0;

        foreach (var (step, when) in new[]
                 {
                     (JudoStep.Shove, At(0)),
                     (JudoStep.Grab, At(0.5)),
                 })
        {
            var thrown = JudoComboRules.Advance(state, lastTime, step, when, Window, sameTarget: true, out state);
            lastTime = when;
            if (thrown)
                throwCount++;
        }

        Assert.That(throwCount, Is.EqualTo(0));
    }

    // --- Finisher cooldown math (same shape as CarpComboRulesTests / ZoneRules cooldowns) ---

    [Test]
    public void ComboReady_ExactlyAtNextComboTime_IsReady()
    {
        Assert.That(JudoComboRules.ComboReady(T0, T0), Is.True);
    }

    [Test]
    public void ComboReady_BeforeNextComboTime_IsNotReady()
    {
        Assert.That(JudoComboRules.ComboReady(T0, T0 + TimeSpan.FromMilliseconds(1)), Is.False);
    }

    [Test]
    public void FreshComponent_DefaultNextComboTime_IsImmediatelyReady()
    {
        Assert.That(JudoComboRules.ComboReady(TimeSpan.Zero, default), Is.True);
    }

    [Test]
    public void NextComboTime_AddsCooldown()
    {
        Assert.That(
            JudoComboRules.NextComboTime(T0, TimeSpan.FromSeconds(4)),
            Is.EqualTo(T0 + TimeSpan.FromSeconds(4)));
    }

    [Test]
    public void NextComboTime_NegativeCooldown_ClampsToZero()
    {
        Assert.That(JudoComboRules.NextComboTime(T0, TimeSpan.FromSeconds(-10)), Is.EqualTo(T0));
    }
}
