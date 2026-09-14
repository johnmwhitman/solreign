using System;
using Content.Client._Solreign.MovementBob;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(MovementBobMath))]
public sealed class MovementBobMathTests
{
    private const float Tolerance = 1e-5f;

    // --- OffsetUnits: curve shape ---

    [Test]
    public void OffsetUnits_TimeZero_NoPhase_IsZero()
    {
        Assert.That(MovementBobMath.OffsetUnits(0.0, 2f, 2f), Is.EqualTo(0f).Within(Tolerance));
    }

    [Test]
    public void OffsetUnits_QuarterPeriod_IsPeak()
    {
        // Rectified sine |sin(pi * hz * t)| peaks at t = 1 / (2 * hz).
        const float hz = 2f;
        const float amplitudePx = 2f;
        var peak = MovementBobMath.OffsetUnits(1.0 / (2.0 * hz), hz, amplitudePx);
        Assert.That(peak, Is.EqualTo(amplitudePx / MovementBobMath.PixelsPerUnit).Within(Tolerance));
    }

    [Test]
    public void OffsetUnits_FullPeriod_ReturnsToZero()
    {
        const float hz = 2.5f;
        Assert.That(MovementBobMath.OffsetUnits(1.0 / hz, hz, 1.5f), Is.EqualTo(0f).Within(Tolerance));
    }

    [Test]
    public void OffsetUnits_NeverNegative_AndNeverExceedsAmplitude()
    {
        const float hz = 2.5f;
        const float amplitudePx = 1.5f;
        var cap = amplitudePx / MovementBobMath.PixelsPerUnit;
        for (var i = 0; i <= 400; i++)
        {
            var t = i * 0.005;
            var offset = MovementBobMath.OffsetUnits(t, hz, amplitudePx);
            Assert.That(offset, Is.GreaterThanOrEqualTo(0f), $"negative offset at t={t}");
            Assert.That(offset, Is.LessThanOrEqualTo(cap + Tolerance), $"offset above amplitude at t={t}");
        }
    }

    [Test]
    public void OffsetUnits_RisesMonotonically_InFirstQuarterPeriod()
    {
        const float hz = 2f;
        var quarter = 1.0 / (2.0 * hz);
        var previous = -1f;
        for (var i = 0; i <= 20; i++)
        {
            var t = quarter * i / 20.0;
            var offset = MovementBobMath.OffsetUnits(t, hz, 2f);
            Assert.That(offset, Is.GreaterThanOrEqualTo(previous), $"non-monotonic at t={t}");
            previous = offset;
        }
    }

    // --- OffsetUnits: clamping ---

    [Test]
    public void OffsetUnits_AmplitudeAboveMax_ClampsToMax()
    {
        const float hz = 2f;
        var peak = MovementBobMath.OffsetUnits(1.0 / (2.0 * hz), hz, 100f);
        Assert.That(peak, Is.EqualTo(MovementBobMath.MaxAmplitudePixels / MovementBobMath.PixelsPerUnit).Within(Tolerance));
    }

    [Test]
    public void OffsetUnits_NegativeAmplitude_ClampsToZero()
    {
        const float hz = 2f;
        var peak = MovementBobMath.OffsetUnits(1.0 / (2.0 * hz), hz, -5f);
        Assert.That(peak, Is.EqualTo(0f).Within(Tolerance));
    }

    [Test]
    public void OffsetUnits_HzAboveMax_ClampsToMax()
    {
        // With hz clamped to MaxHz, the peak sits at 1 / (2 * MaxHz).
        var peak = MovementBobMath.OffsetUnits(1.0 / (2.0 * MovementBobMath.MaxHz), 1000f, 2f);
        Assert.That(peak, Is.EqualTo(2f / MovementBobMath.PixelsPerUnit).Within(Tolerance));
    }

    [Test]
    public void OffsetUnits_HzBelowMin_ClampsToMin()
    {
        var peak = MovementBobMath.OffsetUnits(1.0 / (2.0 * MovementBobMath.MinHz), 0f, 2f);
        Assert.That(peak, Is.EqualTo(2f / MovementBobMath.PixelsPerUnit).Within(Tolerance));
    }

    // --- OffsetUnits: non-finite inputs (a bad replicated CVar must never corrupt rendering) ---

    [Test]
    public void OffsetUnits_NaNAmplitude_IsZero()
    {
        Assert.That(MovementBobMath.OffsetUnits(0.25, 2f, float.NaN), Is.EqualTo(0f));
    }

    [Test]
    public void OffsetUnits_InfiniteAmplitude_IsZero()
    {
        Assert.That(MovementBobMath.OffsetUnits(0.25, 2f, float.PositiveInfinity), Is.EqualTo(0f));
    }

    [Test]
    public void OffsetUnits_NaNHz_IsFiniteAndNonNegative()
    {
        var offset = MovementBobMath.OffsetUnits(0.25, float.NaN, 2f);
        Assert.That(float.IsFinite(offset), Is.True);
        Assert.That(offset, Is.GreaterThanOrEqualTo(0f));
    }

    [Test]
    public void OffsetUnits_NonFiniteTimeOrPhase_IsZero()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MovementBobMath.OffsetUnits(double.NaN, 2f, 2f), Is.EqualTo(0f));
            Assert.That(MovementBobMath.OffsetUnits(double.PositiveInfinity, 2f, 2f), Is.EqualTo(0f));
            Assert.That(MovementBobMath.OffsetUnits(0.25, 2f, 2f, float.NaN), Is.EqualTo(0f));
        });
    }

    [Test]
    public void OffsetUnits_HugeFiniteTime_StaysFiniteAndBounded()
    {
        // Period-reduce before the float cast: even absurd finite doubles must not become NaN.
        var offset = MovementBobMath.OffsetUnits(1e300, 2.5f, 1.5f);
        Assert.That(float.IsFinite(offset), Is.True);
        Assert.That(offset, Is.GreaterThanOrEqualTo(0f));
        Assert.That(offset, Is.LessThanOrEqualTo(1.5f / MovementBobMath.PixelsPerUnit + 1e-5f));
    }

    [Test]
    public void EffectiveAmplitude_SanitizesAndClamps()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MovementBobMath.EffectiveAmplitude(float.NaN), Is.EqualTo(0f));
            Assert.That(MovementBobMath.EffectiveAmplitude(float.NegativeInfinity), Is.EqualTo(0f));
            Assert.That(MovementBobMath.EffectiveAmplitude(-3f), Is.EqualTo(MovementBobMath.MinAmplitudePixels));
            Assert.That(MovementBobMath.EffectiveAmplitude(100f), Is.EqualTo(MovementBobMath.MaxAmplitudePixels));
            Assert.That(MovementBobMath.EffectiveAmplitude(1.5f), Is.EqualTo(1.5f));
        });
    }

    [Test]
    public void EffectiveHz_SanitizesAndClamps()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MovementBobMath.EffectiveHz(float.NaN), Is.EqualTo(MovementBobMath.MinHz));
            Assert.That(MovementBobMath.EffectiveHz(0f), Is.EqualTo(MovementBobMath.MinHz));
            Assert.That(MovementBobMath.EffectiveHz(1000f), Is.EqualTo(MovementBobMath.MaxHz));
            Assert.That(MovementBobMath.EffectiveHz(2.5f), Is.EqualTo(2.5f));
        });
    }

    // --- OffsetUnits: phase ---

    [Test]
    public void OffsetUnits_PhaseShiftsCurve()
    {
        // A phase of pi/2 at t=0 lands on the rectified-sine peak.
        var offset = MovementBobMath.OffsetUnits(0.0, 2f, 2f, MathF.PI / 2f);
        Assert.That(offset, Is.EqualTo(2f / MovementBobMath.PixelsPerUnit).Within(Tolerance));
    }

    [Test]
    public void Phase_AnySeed_IsWithinOneTurn()
    {
        foreach (var seed in new[] { int.MinValue, -12345, -1, 0, 1, 42, 256, 99999, int.MaxValue })
        {
            var phase = MovementBobMath.Phase(seed);
            Assert.That(phase, Is.GreaterThanOrEqualTo(0f), $"negative phase for seed {seed}");
            Assert.That(phase, Is.LessThan(MathF.Tau), $"phase >= tau for seed {seed}");
        }
    }

    [Test]
    public void Phase_DifferentSeeds_Decorrelate()
    {
        Assert.That(MovementBobMath.Phase(1), Is.Not.EqualTo(MovementBobMath.Phase(2)));
    }

    // --- ShouldBob gate ---

    [Test]
    public void ShouldBob_AllGreen_IsTrue()
    {
        Assert.That(MovementBobMath.ShouldBob(
            enabled: true, reducedMotion: false, isMoving: true,
            standing: true, buckled: false, orbiting: false, floating: false), Is.True);
    }

    [Test]
    public void ShouldBob_Disabled_IsFalse()
    {
        Assert.That(MovementBobMath.ShouldBob(
            enabled: false, reducedMotion: false, isMoving: true,
            standing: true, buckled: false, orbiting: false, floating: false), Is.False);
    }

    [Test]
    public void ShouldBob_ReducedMotion_IsFalse()
    {
        Assert.That(MovementBobMath.ShouldBob(
            enabled: true, reducedMotion: true, isMoving: true,
            standing: true, buckled: false, orbiting: false, floating: false), Is.False);
    }

    [Test]
    public void ShouldBob_NotMoving_IsFalse()
    {
        Assert.That(MovementBobMath.ShouldBob(
            enabled: true, reducedMotion: false, isMoving: false,
            standing: true, buckled: false, orbiting: false, floating: false), Is.False);
    }

    [Test]
    public void ShouldBob_Downed_IsFalse()
    {
        Assert.That(MovementBobMath.ShouldBob(
            enabled: true, reducedMotion: false, isMoving: true,
            standing: false, buckled: false, orbiting: false, floating: false), Is.False);
    }

    [Test]
    public void ShouldBob_Buckled_IsFalse()
    {
        Assert.That(MovementBobMath.ShouldBob(
            enabled: true, reducedMotion: false, isMoving: true,
            standing: true, buckled: true, orbiting: false, floating: false), Is.False);
    }

    [Test]
    public void ShouldBob_Orbiting_IsFalse()
    {
        Assert.That(MovementBobMath.ShouldBob(
            enabled: true, reducedMotion: false, isMoving: true,
            standing: true, buckled: false, orbiting: true, floating: false), Is.False);
    }

    [Test]
    public void ShouldBob_WeightlessFloating_IsFalse()
    {
        // The floating animation keyframes the same sprite offset channel — bob must yield.
        Assert.That(MovementBobMath.ShouldBob(
            enabled: true, reducedMotion: false, isMoving: true,
            standing: true, buckled: false, orbiting: false, floating: true), Is.False);
    }
}
