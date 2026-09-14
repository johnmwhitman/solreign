using System;
using Content.Shared._Solreign.Sprint;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(SprintMath))]
public sealed class SprintMathTests
{
    // The shipped tuning: see SprintComponent defaults.
    private const float DrainPerSecond = 12f;
    private const float CritThreshold = 100f;
    private const float AutoDropFraction = 0.9f;
    private const float CooldownSeconds = 4f;

    // --- StaminaDamageAfterDrain ---

    [Test]
    public void StaminaDamageAfterDrain_OneSecond_AddsDrainRate()
    {
        Assert.That(SprintMath.StaminaDamageAfterDrain(0f, DrainPerSecond, 1f), Is.EqualTo(12f).Within(1e-6));
    }

    [Test]
    public void StaminaDamageAfterDrain_MultipleSeconds_ScalesLinearly()
    {
        Assert.That(SprintMath.StaminaDamageAfterDrain(10f, DrainPerSecond, 2.5f), Is.EqualTo(40f).Within(1e-6));
    }

    [Test]
    public void StaminaDamageAfterDrain_ZeroSeconds_Unchanged()
    {
        Assert.That(SprintMath.StaminaDamageAfterDrain(50f, DrainPerSecond, 0f), Is.EqualTo(50f));
    }

    [Test]
    public void StaminaDamageAfterDrain_NegativeSeconds_Unchanged()
    {
        Assert.That(SprintMath.StaminaDamageAfterDrain(50f, DrainPerSecond, -3f), Is.EqualTo(50f));
    }

    [Test]
    public void StaminaDamageAfterDrain_ZeroDrainRate_Unchanged()
    {
        Assert.That(SprintMath.StaminaDamageAfterDrain(50f, 0f, 10f), Is.EqualTo(50f));
    }

    [Test]
    public void StaminaDamageAfterDrain_NegativeDrainRate_Unchanged()
    {
        Assert.That(SprintMath.StaminaDamageAfterDrain(50f, -5f, 10f), Is.EqualTo(50f));
    }

    // --- DamageFraction ---

    [Test]
    public void DamageFraction_Half()
    {
        Assert.That(SprintMath.DamageFraction(50f, CritThreshold), Is.EqualTo(0.5f).Within(1e-6));
    }

    [Test]
    public void DamageFraction_Overshoot_ClampsToOne()
    {
        Assert.That(SprintMath.DamageFraction(150f, CritThreshold), Is.EqualTo(1f));
    }

    [Test]
    public void DamageFraction_NegativeDamage_ClampsToZero()
    {
        Assert.That(SprintMath.DamageFraction(-10f, CritThreshold), Is.EqualTo(0f));
    }

    [Test]
    public void DamageFraction_DegenerateThreshold_FailsClosedToFull()
    {
        Assert.That(SprintMath.DamageFraction(0f, 0f), Is.EqualTo(1f));
        Assert.That(SprintMath.DamageFraction(0f, -5f), Is.EqualTo(1f));
    }

    // --- IsAutoDropTriggered ---

    [Test]
    public void IsAutoDropTriggered_BelowLine_False()
    {
        Assert.That(SprintMath.IsAutoDropTriggered(0.5f, AutoDropFraction), Is.False);
    }

    [Test]
    public void IsAutoDropTriggered_AtLine_True()
    {
        Assert.That(SprintMath.IsAutoDropTriggered(0.9f, AutoDropFraction), Is.True);
    }

    [Test]
    public void IsAutoDropTriggered_AboveLine_True()
    {
        Assert.That(SprintMath.IsAutoDropTriggered(0.95f, AutoDropFraction), Is.True);
    }

    [Test]
    public void IsAutoDropTriggered_NegativeConfiguredFraction_FailsClosed_AlwaysTriggers()
    {
        // A misconfigured negative auto-drop fraction clamps to 0, so even undamaged (0) mobs
        // are considered "over the line" - i.e. sprint can never start. Fail closed, not open.
        Assert.That(SprintMath.IsAutoDropTriggered(0f, -1f), Is.True);
    }

    [Test]
    public void IsAutoDropTriggered_OverOneConfiguredFraction_ClampsToOne()
    {
        Assert.That(SprintMath.IsAutoDropTriggered(0.99f, 1.5f), Is.False);
        Assert.That(SprintMath.IsAutoDropTriggered(1f, 1.5f), Is.True);
    }

    // --- CooldownEndTime / IsOnCooldown ---

    [Test]
    public void CooldownEndTime_AddsConfiguredSeconds()
    {
        var now = TimeSpan.FromSeconds(10);
        Assert.That(SprintMath.CooldownEndTime(now, CooldownSeconds), Is.EqualTo(TimeSpan.FromSeconds(14)));
    }

    [Test]
    public void CooldownEndTime_ZeroOrNegativeCooldown_ExpiresImmediately()
    {
        var now = TimeSpan.FromSeconds(10);
        Assert.That(SprintMath.CooldownEndTime(now, 0f), Is.EqualTo(now));
        Assert.That(SprintMath.CooldownEndTime(now, -2f), Is.EqualTo(now));
    }

    [Test]
    public void IsOnCooldown_BeforeEndTime_True()
    {
        var now = TimeSpan.FromSeconds(10);
        var end = TimeSpan.FromSeconds(14);
        Assert.That(SprintMath.IsOnCooldown(now, end), Is.True);
    }

    [Test]
    public void IsOnCooldown_AtEndTime_False()
    {
        var end = TimeSpan.FromSeconds(14);
        Assert.That(SprintMath.IsOnCooldown(end, end), Is.False);
    }

    [Test]
    public void IsOnCooldown_AfterEndTime_False()
    {
        var now = TimeSpan.FromSeconds(15);
        var end = TimeSpan.FromSeconds(14);
        Assert.That(SprintMath.IsOnCooldown(now, end), Is.False);
    }

    // --- ShouldSprint ---

    [Test]
    public void ShouldSprint_KeyNotHeld_False()
    {
        Assert.That(SprintMath.ShouldSprint(false, onCooldown: false, staminaCritical: false, damageFraction: 0f, autoDropFraction: AutoDropFraction), Is.False);
    }

    [Test]
    public void ShouldSprint_OnCooldown_False()
    {
        Assert.That(SprintMath.ShouldSprint(true, onCooldown: true, staminaCritical: false, damageFraction: 0f, autoDropFraction: AutoDropFraction), Is.False);
    }

    [Test]
    public void ShouldSprint_StaminaCritical_False()
    {
        Assert.That(SprintMath.ShouldSprint(true, onCooldown: false, staminaCritical: true, damageFraction: 0f, autoDropFraction: AutoDropFraction), Is.False);
    }

    [Test]
    public void ShouldSprint_OverAutoDropLine_False()
    {
        Assert.That(SprintMath.ShouldSprint(true, onCooldown: false, staminaCritical: false, damageFraction: 0.95f, autoDropFraction: AutoDropFraction), Is.False);
    }

    [Test]
    public void ShouldSprint_KeyHeldRestedNotOnCooldown_True()
    {
        Assert.That(SprintMath.ShouldSprint(true, onCooldown: false, staminaCritical: false, damageFraction: 0.1f, autoDropFraction: AutoDropFraction), Is.True);
    }

    // --- EffectiveSpeedModifier ---

    [Test]
    public void EffectiveSpeedModifier_NotSprinting_IsOne()
    {
        Assert.That(SprintMath.EffectiveSpeedModifier(false, 1.35f), Is.EqualTo(1f));
    }

    [Test]
    public void EffectiveSpeedModifier_Sprinting_ReturnsConfiguredModifier()
    {
        Assert.That(SprintMath.EffectiveSpeedModifier(true, 1.35f), Is.EqualTo(1.35f).Within(1e-6));
    }

    [Test]
    public void EffectiveSpeedModifier_SprintingWithNegativeModifier_ClampsToZero()
    {
        Assert.That(SprintMath.EffectiveSpeedModifier(true, -0.5f), Is.EqualTo(0f));
    }

    // --- End-to-end style: a few seconds of sprinting against the shipped tuning ---

    [Test]
    public void ShippedTuning_DrainsToAutoDropLineInAboutSevenSeconds()
    {
        // 100 * 0.9 = 90 stamina damage triggers the drop; at 12/sec that's 7.5s, so the 8th
        // lumped drain tick (applied at t=8s, having predicted 96 >= 90) is the one that's
        // refused - i.e. sprint survives 7 full seconds of draining before auto-dropping.
        var damage = 0f;

        for (var second = 1; second <= 7; second++)
        {
            var predicted = SprintMath.StaminaDamageAfterDrain(damage, DrainPerSecond, 1f);
            Assert.That(SprintMath.IsAutoDropTriggered(SprintMath.DamageFraction(predicted, CritThreshold), AutoDropFraction), Is.False,
                $"should not have dropped yet at second {second}");
            damage = predicted;
        }

        var eighthTickPredicted = SprintMath.StaminaDamageAfterDrain(damage, DrainPerSecond, 1f);
        Assert.That(SprintMath.IsAutoDropTriggered(SprintMath.DamageFraction(eighthTickPredicted, CritThreshold), AutoDropFraction), Is.True);
    }
}
