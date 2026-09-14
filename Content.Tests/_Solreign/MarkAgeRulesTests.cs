#nullable enable
using System;
using Content.Server._Solreign.PlayerDelight.Mark;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Coverage for the pure wall-clock aging law (MARK-SPEC §3.4): stage thresholds at 0/2/7/21
///     days with boundary exactness; the growth scalar monotonic, capped at 60 days, rendered to
///     one invariant decimal; determinism (same inputs, same outputs, forever); and the clock-skew
///     clamps that keep a corrupt timestamp from ever throwing inside a projection.
/// </summary>
[TestFixture]
[TestOf(typeof(MarkAgeRules))]
public sealed class MarkAgeRulesTests
{
    private static readonly DateTime Planted = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    private static DateTime AfterDays(double days) => Planted.AddDays(days);

    // --- Stages -----------------------------------------------------------------------------------

    [TestCase(0.0, 0)]
    [TestCase(1.0, 0)]
    [TestCase(1.999, 0)]
    [TestCase(2.0, 1, Description = "the >= 2d boundary is EXACT — day two IS stage 1")]
    [TestCase(3.5, 1)]
    [TestCase(6.999, 1)]
    [TestCase(7.0, 2)]
    [TestCase(20.999, 2)]
    [TestCase(21.0, 3)]
    [TestCase(60.0, 3)]
    [TestCase(1000.0, 3, Description = "stage 3 is terminal — nothing past it")]
    public void StageAt_MatchesTheSpecThresholds(double days, int expected)
    {
        Assert.That(MarkAgeRules.StageAt(Planted, AfterDays(days)), Is.EqualTo(expected));
    }

    [Test]
    public void StageAt_ClockSkew_ClampsToStageZero()
    {
        Assert.That(MarkAgeRules.StageAt(Planted, AfterDays(-3)), Is.EqualTo(0),
            "a now BEFORE planted (skew, corrupt row) must clamp, never throw or go weird");
    }

    [Test]
    public void StageAt_NeverExceedsMaxStage_AndNeverRegresses()
    {
        var previous = 0;
        for (var days = 0.0; days <= 90; days += 0.25)
        {
            var stage = MarkAgeRules.StageAt(Planted, AfterDays(days));
            Assert.Multiple(() =>
            {
                Assert.That(stage, Is.InRange(0, MarkAgeRules.MaxStage));
                Assert.That(stage, Is.GreaterThanOrEqualTo(previous),
                    $"stage regressed at day {days} — aging must be monotonic");
            });
            previous = stage;
        }
    }

    // --- Growth scalar ----------------------------------------------------------------------------

    [TestCase(0.0, 0.0)]
    [TestCase(1.0, 0.7)]
    [TestCase(3.0, 2.1, Description = "day 3 renders the council's own beat: 'grown 2.1 cm'")]
    [TestCase(10.0, 7.0)]
    [TestCase(60.0, 42.0)]
    [TestCase(100.0, 42.0, Description = "capped at 60 days — growth plateaus, never runs away")]
    public void GrowthScalar_IsMinDaysCapTimesRate(double days, double expected)
    {
        Assert.That(MarkAgeRules.GrowthScalar(Planted, AfterDays(days)), Is.EqualTo(expected).Within(1e-9));
    }

    [Test]
    public void GrowthScalar_ClockSkew_ClampsToZero()
    {
        Assert.That(MarkAgeRules.GrowthScalar(Planted, AfterDays(-1)), Is.Zero);
    }

    [Test]
    public void GrowthScalar_IsMonotonic_NonDecreasing()
    {
        var previous = -1.0;
        for (var days = 0.0; days <= 90; days += 0.5)
        {
            var scalar = MarkAgeRules.GrowthScalar(Planted, AfterDays(days));
            Assert.That(scalar, Is.GreaterThanOrEqualTo(previous),
                $"growth shrank at day {days} — the scalar must be monotonic");
            previous = scalar;
        }
    }

    [Test]
    public void GrowthDelta_IsScalarNowMinusScalarLastVisit()
    {
        // Planted, visited at day 1, returning at day 4: 2.8 - 0.7 = 2.1 (the return line's number).
        var delta = MarkAgeRules.GrowthDelta(Planted, AfterDays(1), AfterDays(4));
        Assert.That(delta, Is.EqualTo(2.1).Within(1e-9));
    }

    [Test]
    public void GrowthDelta_OutOfOrderTimestamps_ClampToZero()
    {
        Assert.That(MarkAgeRules.GrowthDelta(Planted, AfterDays(5), AfterDays(2)), Is.Zero,
            "growth never renders negative — monotonicity is a law");
    }

    [Test]
    public void GrowthDelta_PastTheCap_IsZero()
    {
        Assert.That(MarkAgeRules.GrowthDelta(Planted, AfterDays(70), AfterDays(80)), Is.Zero,
            "both visits past the 60-day cap: the mark has settled");
    }

    // --- Rendering --------------------------------------------------------------------------------

    [TestCase(2.1, "2.1")]
    [TestCase(0.0, "0.0")]
    [TestCase(42.0, "42.0")]
    [TestCase(0.7, "0.7")]
    [TestCase(2.0999999, "2.1")]
    public void RenderGrowth_OneInvariantDecimal(double scalar, string expected)
    {
        Assert.That(MarkAgeRules.RenderGrowth(scalar), Is.EqualTo(expected));
    }

    // --- Determinism ------------------------------------------------------------------------------

    [Test]
    public void SameInputs_SameOutputs_Forever()
    {
        var now = AfterDays(8.25);
        var firstStage = MarkAgeRules.StageAt(Planted, now);
        var firstScalar = MarkAgeRules.GrowthScalar(Planted, now);
        Assert.Multiple(() =>
        {
            Assert.That(MarkAgeRules.StageAt(Planted, now), Is.EqualTo(firstStage));
            Assert.That(MarkAgeRules.GrowthScalar(Planted, now), Is.EqualTo(firstScalar));
        });
    }

    // --- Ledger timestamp parsing -----------------------------------------------------------------

    [Test]
    public void TryParseLedgerUtc_RoundTripsTheStoresIsoFormat()
    {
        // The store stamps DateTime.UtcNow.ToString("o") — the parse must round-trip it exactly.
        var stamped = new DateTime(2026, 7, 17, 4, 30, 15, 123, DateTimeKind.Utc).AddTicks(4567);
        var ledger = stamped.ToString("o");

        Assert.That(MarkAgeRules.TryParseLedgerUtc(ledger, out var parsed), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.EqualTo(stamped));
            Assert.That(parsed.Kind, Is.EqualTo(DateTimeKind.Utc));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("not a timestamp")]
    public void TryParseLedgerUtc_Garbage_ReturnsFalse_NeverThrows(string? garbage)
    {
        Assert.That(MarkAgeRules.TryParseLedgerUtc(garbage, out _), Is.False);
    }
}
