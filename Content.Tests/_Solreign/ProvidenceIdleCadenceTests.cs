#nullable enable
using Content.Server._Solreign.Providence;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     ALIVENESS P1 #7: pure coverage for <see cref="ProvidenceVoiceSystem.IdleWindow"/>, the
///     low-pop idle-musings cadence clamp. No IoC, no timing, no audio — exercises the pure
///     static seam directly, the <c>PeriodicEffectTests</c>/<c>ProvidenceVoiceMapTests</c>
///     precedent for Providence-adjacent pure logic.
/// </summary>
[TestFixture]
[TestOf(typeof(ProvidenceVoiceSystem))]
public sealed class ProvidenceIdleCadenceTests
{
    private const int DefaultThreshold = 3;

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void AtOrBelowThreshold_WindowClampsToTheLowPopBand(int playerCount)
    {
        var (min, max) = ProvidenceVoiceSystem.IdleWindow(playerCount, DefaultThreshold);

        Assert.Multiple(() =>
        {
            Assert.That(min, Is.EqualTo(ProvidenceVoiceSystem.LowPopIdleMinSeconds));
            Assert.That(max, Is.EqualTo(ProvidenceVoiceSystem.LowPopIdleMaxSeconds));
        });
    }

    [TestCase(4)]
    [TestCase(10)]
    [TestCase(100)]
    public void AboveThreshold_WindowReturnsToTheDefaultBand(int playerCount)
    {
        var (min, max) = ProvidenceVoiceSystem.IdleWindow(playerCount, DefaultThreshold);

        Assert.Multiple(() =>
        {
            Assert.That(min, Is.EqualTo(ProvidenceVoiceSystem.IdleMinSeconds));
            Assert.That(max, Is.EqualTo(ProvidenceVoiceSystem.IdleMaxSeconds));
        });
    }

    [Test]
    public void ThresholdBoundary_IsInclusive_AtEqualsClampedOneAboveDoesNot()
    {
        var atThreshold = ProvidenceVoiceSystem.IdleWindow(DefaultThreshold, DefaultThreshold);
        var oneAbove = ProvidenceVoiceSystem.IdleWindow(DefaultThreshold + 1, DefaultThreshold);

        Assert.Multiple(() =>
        {
            Assert.That(atThreshold.MinSeconds, Is.EqualTo(ProvidenceVoiceSystem.LowPopIdleMinSeconds));
            Assert.That(oneAbove.MinSeconds, Is.EqualTo(ProvidenceVoiceSystem.IdleMinSeconds));
        });
    }

    [Test]
    public void OperatorSetsThresholdToZero_ClampNeverEngagesForAnyRealPopulation()
    {
        // PlayerCount is never below 0 for a live scheduling call; threshold 0 still clamps a
        // fully-empty server (harmless — nobody hears it) and NEVER clamps with 1+ connected.
        var (min, _) = ProvidenceVoiceSystem.IdleWindow(1, 0);

        Assert.That(min, Is.EqualTo(ProvidenceVoiceSystem.IdleMinSeconds));
    }

    [Test]
    public void LowPopBand_IsStrictlySLOWERThanTheDefaultBand()
    {
        // DIRECTION REVERSED 2026-07-22, deliberately.
        //
        // This assertion previously required the OPPOSITE (low-pop strictly FASTER), encoding
        // ALIVENESS P1 #7's bet that PROVIDENCE should lean in as the station empties. A real
        // player on a near-empty server reported the result as irritating and repetitive, which
        // the arithmetic supports: IdleMusings draws from ~4 lines, so an 8-15 minute cadence
        // replayed the same line several times a shift.
        //
        // The guardrail is kept, pointing the other way: a future change that makes the low-pop
        // window faster than the default would silently reintroduce the behaviour a player
        // already told us was the problem.
        // Both edges shifted later than the default band's corresponding edge. The bands may
        // overlap (20-40 vs 30-60) -- requiring them to be disjoint would be a stricter claim
        // than the design makes, and only the direction of the shift matters here.
        Assert.Multiple(() =>
        {
            Assert.That(ProvidenceVoiceSystem.LowPopIdleMinSeconds, Is.GreaterThan(ProvidenceVoiceSystem.IdleMinSeconds));
            Assert.That(ProvidenceVoiceSystem.LowPopIdleMaxSeconds, Is.GreaterThan(ProvidenceVoiceSystem.IdleMaxSeconds));
            Assert.That(ProvidenceVoiceSystem.LowPopIdleMinSeconds, Is.LessThan(ProvidenceVoiceSystem.LowPopIdleMaxSeconds));
            Assert.That(ProvidenceVoiceSystem.LowPopIdleMinSeconds, Is.GreaterThan(0f));
        });
    }

    [Test]
    public void ClampedWindow_FeedsPeriodicEffectTimingWithoutSanitizationKickingIn()
    {
        // End-to-end through the same pure math ScheduleIdle actually calls: the 30-60 minute
        // window must come back exactly interpolated, proving the clamp values are well-formed
        // inputs for PeriodicEffectTiming (no negative/inverted-window sanitization engaged).
        var now = System.TimeSpan.FromMinutes(100);

        var atMin = Content.Server._Solreign.Effects.PeriodicEffectTiming.NextFireTime(
            now, ProvidenceVoiceSystem.LowPopIdleMinSeconds, ProvidenceVoiceSystem.LowPopIdleMaxSeconds, 0d);
        var atMax = Content.Server._Solreign.Effects.PeriodicEffectTiming.NextFireTime(
            now, ProvidenceVoiceSystem.LowPopIdleMinSeconds, ProvidenceVoiceSystem.LowPopIdleMaxSeconds, 1d);

        Assert.Multiple(() =>
        {
            Assert.That(atMin, Is.EqualTo(now + System.TimeSpan.FromMinutes(30)));
            Assert.That(atMax, Is.EqualTo(now + System.TimeSpan.FromMinutes(60)));
        });
    }
}
