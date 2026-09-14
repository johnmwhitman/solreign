using System;
using Content.Server._Solreign.Antags.Vampire;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(VampireThirstMath))]
public sealed class VampireThirstMathTests
{
    private static TimeSpan Mins(double m) => TimeSpan.FromMinutes(m);

    // --- Band classification: boundaries are inclusive-upward ---

    [Test]
    public void Band_Boundaries()
    {
        Assert.Multiple(() =>
        {
            Assert.That(VampireThirstMath.Band(0), Is.EqualTo(ThirstBand.Sated));
            Assert.That(VampireThirstMath.Band(24), Is.EqualTo(ThirstBand.Sated));
            Assert.That(VampireThirstMath.Band(25), Is.EqualTo(ThirstBand.Peckish));
            Assert.That(VampireThirstMath.Band(59), Is.EqualTo(ThirstBand.Peckish));
            Assert.That(VampireThirstMath.Band(60), Is.EqualTo(ThirstBand.Thirsty));
            Assert.That(VampireThirstMath.Band(84), Is.EqualTo(ThirstBand.Thirsty));
            Assert.That(VampireThirstMath.Band(85), Is.EqualTo(ThirstBand.Ravenous));
            Assert.That(VampireThirstMath.Band(100), Is.EqualTo(ThirstBand.Ravenous));
        });
    }

    [Test]
    public void Band_OutOfRangeInputs_ClampBeforeClassifying()
    {
        Assert.Multiple(() =>
        {
            Assert.That(VampireThirstMath.Band(-50), Is.EqualTo(ThirstBand.Sated));
            Assert.That(VampireThirstMath.Band(9000), Is.EqualTo(ThirstBand.Ravenous));
        });
    }

    // --- Accumulate: accrual, coffin reversal, clamping ---

    [Test]
    public void Accumulate_OutsideCoffin_RisesAtRate()
    {
        var thirst = VampireThirstMath.Accumulate(
            10, Mins(5), accrualPerMinute: 1.0, inCoffin: false,
            coffinRecoveryPerMinute: 4.0, garlicNearby: false);

        Assert.That(thirst, Is.EqualTo(15).Within(1e-9));
    }

    [Test]
    public void Accumulate_InCoffin_FallsAtRecoveryRate()
    {
        var thirst = VampireThirstMath.Accumulate(
            50, Mins(5), accrualPerMinute: 1.0, inCoffin: true,
            coffinRecoveryPerMinute: 4.0, garlicNearby: false);

        Assert.That(thirst, Is.EqualTo(30).Within(1e-9));
    }

    [Test]
    public void Accumulate_GarlicNearby_AccruesFiftyPercentFaster()
    {
        var thirst = VampireThirstMath.Accumulate(
            10, Mins(10), accrualPerMinute: 1.0, inCoffin: false,
            coffinRecoveryPerMinute: 4.0, garlicNearby: true);

        Assert.That(thirst, Is.EqualTo(25).Within(1e-9)); // 10 + 10 * 1.5
    }

    [Test]
    public void Accumulate_GarlicDoesNotAffectCoffinRecovery()
    {
        // Inside the pod the garlic multiplier is irrelevant: recovery is recovery.
        var thirst = VampireThirstMath.Accumulate(
            50, Mins(5), accrualPerMinute: 1.0, inCoffin: true,
            coffinRecoveryPerMinute: 4.0, garlicNearby: true);

        Assert.That(thirst, Is.EqualTo(30).Within(1e-9));
    }

    [Test]
    public void Accumulate_CapsAtOneHundred()
    {
        var thirst = VampireThirstMath.Accumulate(
            95, Mins(60), accrualPerMinute: 1.0, inCoffin: false,
            coffinRecoveryPerMinute: 4.0, garlicNearby: false);

        Assert.That(thirst, Is.EqualTo(VampireThirstMath.MaxThirst));
    }

    [Test]
    public void Accumulate_FloorsAtZero_InCoffin()
    {
        var thirst = VampireThirstMath.Accumulate(
            5, Mins(60), accrualPerMinute: 1.0, inCoffin: true,
            coffinRecoveryPerMinute: 4.0, garlicNearby: false);

        Assert.That(thirst, Is.EqualTo(VampireThirstMath.MinThirst));
    }

    [Test]
    public void Accumulate_HostileConfig_ClampsInsteadOfThrowing()
    {
        // Negative rates and negative elapsed time all clamp to zero: the meter simply holds.
        var negativeRate = VampireThirstMath.Accumulate(
            40, Mins(5), accrualPerMinute: -3.0, inCoffin: false,
            coffinRecoveryPerMinute: 4.0, garlicNearby: false);
        var negativeElapsed = VampireThirstMath.Accumulate(
            40, Mins(-5), accrualPerMinute: 1.0, inCoffin: false,
            coffinRecoveryPerMinute: 4.0, garlicNearby: false);
        var negativeRecovery = VampireThirstMath.Accumulate(
            40, Mins(5), accrualPerMinute: 1.0, inCoffin: true,
            coffinRecoveryPerMinute: -4.0, garlicNearby: false);

        Assert.Multiple(() =>
        {
            Assert.That(negativeRate, Is.EqualTo(40).Within(1e-9));
            Assert.That(negativeElapsed, Is.EqualTo(40).Within(1e-9));
            Assert.That(negativeRecovery, Is.EqualTo(40).Within(1e-9));
        });
    }

    // --- Drink ---

    [Test]
    public void Drink_ReducesThirst()
    {
        Assert.That(VampireThirstMath.Drink(60, 25), Is.EqualTo(35).Within(1e-9));
    }

    [Test]
    public void Drink_FloorsAtZero()
    {
        Assert.That(VampireThirstMath.Drink(10, 25), Is.EqualTo(VampireThirstMath.MinThirst));
    }

    [Test]
    public void Drink_NegativeAmount_NeverIncreasesThirst()
    {
        Assert.That(VampireThirstMath.Drink(40, -25), Is.EqualTo(40).Within(1e-9));
    }

    // --- Feeding gate: garlic/chapel block, Ravenous never overrides ---

    [Test]
    public void CanFeed_TruthTable()
    {
        foreach (var band in new[]
                 {
                     ThirstBand.Sated, ThirstBand.Peckish, ThirstBand.Thirsty, ThirstBand.Ravenous,
                 })
        {
            Assert.Multiple(() =>
            {
                Assert.That(VampireThirstMath.CanFeed(band, garlicNearby: false, inChapel: false), Is.True);
                Assert.That(VampireThirstMath.CanFeed(band, garlicNearby: true, inChapel: false), Is.False);
                Assert.That(VampireThirstMath.CanFeed(band, garlicNearby: false, inChapel: true), Is.False);
                Assert.That(VampireThirstMath.CanFeed(band, garlicNearby: true, inChapel: true), Is.False);
            });
        }
    }

    // --- Regen multiplier: pod doubles, chapel and Ravenous shut off ---

    [Test]
    public void RegenMultiplier_Rules()
    {
        Assert.Multiple(() =>
        {
            Assert.That(VampireThirstMath.RegenMultiplier(ThirstBand.Sated, inCoffin: false, inChapel: false), Is.EqualTo(1d));
            Assert.That(VampireThirstMath.RegenMultiplier(ThirstBand.Peckish, inCoffin: true, inChapel: false), Is.EqualTo(2d));
            Assert.That(VampireThirstMath.RegenMultiplier(ThirstBand.Ravenous, inCoffin: true, inChapel: false), Is.EqualTo(0d));
            Assert.That(VampireThirstMath.RegenMultiplier(ThirstBand.Sated, inCoffin: true, inChapel: true), Is.EqualTo(0d));
        });
    }

    // --- IsCured: Sunrise Clause threshold (build-pass extension, spec §4.4) ---

    [Test]
    public void IsCured_BelowThreshold_False()
    {
        Assert.That(VampireThirstMath.IsCured(99.9), Is.False);
    }

    [Test]
    public void IsCured_AtThreshold_True()
    {
        Assert.That(VampireThirstMath.IsCured(100), Is.True);
    }

    [Test]
    public void IsCured_OvershootFromMisconfiguredCurePerRitual_StillTrue()
    {
        Assert.That(VampireThirstMath.IsCured(134), Is.True);
    }

    // --- SpeedMultiplier: hunger only ever weakens (build-pass extension, spec §4) ---

    [Test]
    public void SpeedMultiplier_Rules()
    {
        Assert.Multiple(() =>
        {
            Assert.That(VampireThirstMath.SpeedMultiplier(ThirstBand.Sated), Is.EqualTo(1.1d));
            Assert.That(VampireThirstMath.SpeedMultiplier(ThirstBand.Peckish), Is.EqualTo(1.0d));
            Assert.That(VampireThirstMath.SpeedMultiplier(ThirstBand.Thirsty), Is.EqualTo(1.0d));
            Assert.That(VampireThirstMath.SpeedMultiplier(ThirstBand.Ravenous), Is.EqualTo(0.85d));
        });
    }
}
