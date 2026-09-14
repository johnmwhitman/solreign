using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Server._Solreign.StationIdentity;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests._Solreign;

/// <summary>
///     Coverage for <see cref="StationMoodMath"/> — the pure lerp/scheduling logic behind the
///     StationIdentity wave's shared ambient-identity engine (day/night ambient color, spore drift
///     translation, and the future weather-event-preference picker).
/// </summary>
[TestFixture]
[TestOf(typeof(StationMoodMath))]
public sealed class StationMoodMathTests
{
    private static readonly Color ColorA = Color.FromHex("#000000");
    private static readonly Color ColorB = Color.FromHex("#FFFFFF");

    // --- LerpDayNightColor ---

    [Test]
    public void AtZeroElapsed_SitsAtColorA()
    {
        var result = StationMoodMath.LerpDayNightColor(TimeSpan.Zero, 1000f, ColorA, ColorB);

        Assert.That(result, Is.EqualTo(ColorA));
    }

    [Test]
    public void AtHalfPeriod_SitsAtColorB()
    {
        var result = StationMoodMath.LerpDayNightColor(TimeSpan.FromSeconds(500), 1000f, ColorA, ColorB);

        Assert.That(result, Is.EqualTo(ColorB));
    }

    [Test]
    public void AtFullPeriod_ReturnsToColorA()
    {
        var result = StationMoodMath.LerpDayNightColor(TimeSpan.FromSeconds(1000), 1000f, ColorA, ColorB);

        Assert.That(result, Is.EqualTo(ColorA));
    }

    [Test]
    public void AtQuarterPeriod_IsHalfwayBetween()
    {
        var result = StationMoodMath.LerpDayNightColor(TimeSpan.FromSeconds(250), 1000f, ColorA, ColorB);

        Assert.That(result.R, Is.EqualTo(0.5f).Within(0.01f));
    }

    [Test]
    public void AtThreeQuarterPeriod_IsHalfwayBetween()
    {
        // Triangle wave: past the midpoint it swings back down, so 750/1000 == 250/1000 in value.
        var atQuarter = StationMoodMath.LerpDayNightColor(TimeSpan.FromSeconds(250), 1000f, ColorA, ColorB);
        var atThreeQuarter = StationMoodMath.LerpDayNightColor(TimeSpan.FromSeconds(750), 1000f, ColorA, ColorB);

        Assert.That(atThreeQuarter.R, Is.EqualTo(atQuarter.R).Within(0.001f));
    }

    [Test]
    public void WrapsAcrossMultiplePeriods()
    {
        var atZero = StationMoodMath.LerpDayNightColor(TimeSpan.Zero, 1000f, ColorA, ColorB);
        var atThreePeriods = StationMoodMath.LerpDayNightColor(TimeSpan.FromSeconds(3000), 1000f, ColorA, ColorB);

        Assert.That(atThreePeriods, Is.EqualTo(atZero));
    }

    [Test]
    public void NonPositivePeriod_ClampsToOneSecond_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => StationMoodMath.LerpDayNightColor(TimeSpan.FromSeconds(2), 0f, ColorA, ColorB));
        Assert.DoesNotThrow(() => StationMoodMath.LerpDayNightColor(TimeSpan.FromSeconds(2), -50f, ColorA, ColorB));
    }

    [Test]
    public void SameColorEndpoints_AlwaysReturnsThatColor()
    {
        var result = StationMoodMath.LerpDayNightColor(TimeSpan.FromSeconds(123), 1000f, ColorA, ColorA);

        Assert.That(result, Is.EqualTo(ColorA));
    }

    // --- AdvanceDrift ---

    [Test]
    public void AdvanceDrift_MovesByVelocityTimesFrameTime()
    {
        var start = new Vector2(1f, 2f);
        var velocity = new Vector2(0.5f, -0.25f);

        var result = StationMoodMath.AdvanceDrift(start, velocity, 2f);

        Assert.That(result.X, Is.EqualTo(2f).Within(0.0001f));
        Assert.That(result.Y, Is.EqualTo(1.5f).Within(0.0001f));
    }

    [Test]
    public void AdvanceDrift_ZeroVelocity_DoesNotMove()
    {
        var start = new Vector2(3f, 4f);

        var result = StationMoodMath.AdvanceDrift(start, Vector2.Zero, 5f);

        Assert.That(result, Is.EqualTo(start));
    }

    // --- PickWeatherEvent ---

    [Test]
    public void BranchSampleBelowBias_PicksFromPreferred()
    {
        var preferred = new List<string> { "SolreignSolarFlare" };
        var fallback = new List<string> { "SolarFlare", "VentClog" };

        var result = StationMoodMath.PickWeatherEvent(preferred, fallback, branchSample: 0.1, indexSample: 0.0, preferredBias: 0.7f);

        Assert.That(result, Is.EqualTo("SolreignSolarFlare"));
    }

    [Test]
    public void BranchSampleAboveBias_PicksFromFallback()
    {
        var preferred = new List<string> { "SolreignSolarFlare" };
        var fallback = new List<string> { "SolarFlare", "VentClog" };

        var result = StationMoodMath.PickWeatherEvent(preferred, fallback, branchSample: 0.9, indexSample: 0.0, preferredBias: 0.7f);

        Assert.That(result, Is.EqualTo("SolarFlare"));
    }

    [Test]
    public void IndexSample_SelectsCorrectSlotInsidePool()
    {
        var fallback = new List<string> { "First", "Second", "Third" };

        var last = StationMoodMath.PickWeatherEvent(new List<string>(), fallback, branchSample: 0.99, indexSample: 0.99, preferredBias: 0.7f);
        var first = StationMoodMath.PickWeatherEvent(new List<string>(), fallback, branchSample: 0.99, indexSample: 0.0, preferredBias: 0.7f);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo("First"));
            Assert.That(last, Is.EqualTo("Third"));
        });
    }

    [Test]
    public void EmptyPreferred_AlwaysFallsBackRegardlessOfBranchSample()
    {
        var fallback = new List<string> { "SolarFlare" };

        var result = StationMoodMath.PickWeatherEvent(new List<string>(), fallback, branchSample: 0.0, indexSample: 0.0, preferredBias: 0.7f);

        Assert.That(result, Is.EqualTo("SolarFlare"));
    }

    [Test]
    public void EmptyFallback_AlwaysUsesPreferredEvenWhenBranchSampleIsHigh()
    {
        var preferred = new List<string> { "SolreignSolarFlare" };

        var result = StationMoodMath.PickWeatherEvent(preferred, new List<string>(), branchSample: 0.99, indexSample: 0.0, preferredBias: 0.7f);

        Assert.That(result, Is.EqualTo("SolreignSolarFlare"));
    }

    [Test]
    public void BothPoolsEmpty_ReturnsNull()
    {
        var result = StationMoodMath.PickWeatherEvent(new List<string>(), new List<string>(), branchSample: 0.0, indexSample: 0.0, preferredBias: 0.7f);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void OutOfRangeSamples_AreClampedNotThrown()
    {
        var preferred = new List<string> { "A" };
        var fallback = new List<string> { "B" };

        Assert.DoesNotThrow(() => StationMoodMath.PickWeatherEvent(preferred, fallback, branchSample: -5.0, indexSample: 5.0, preferredBias: 0.7f));
        Assert.DoesNotThrow(() => StationMoodMath.PickWeatherEvent(preferred, fallback, branchSample: 5.0, indexSample: -5.0, preferredBias: 0.7f));
    }
}
