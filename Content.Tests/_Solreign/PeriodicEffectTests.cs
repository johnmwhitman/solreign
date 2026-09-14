using System;
using Content.Server._Solreign.Effects;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(PeriodicEffectTiming))]
public sealed class PeriodicEffectTests
{
    private static readonly TimeSpan CurTime = TimeSpan.FromSeconds(1000);

    // --- Window interpolation ---

    [Test]
    public void SampleZero_FiresAtMinInterval()
    {
        var next = PeriodicEffectTiming.NextFireTime(CurTime, 120f, 600f, 0.0);

        Assert.That(next, Is.EqualTo(CurTime + TimeSpan.FromSeconds(120)));
    }

    [Test]
    public void SampleOne_FiresAtMaxInterval()
    {
        var next = PeriodicEffectTiming.NextFireTime(CurTime, 120f, 600f, 1.0);

        Assert.That(next, Is.EqualTo(CurTime + TimeSpan.FromSeconds(600)));
    }

    [Test]
    public void SampleHalf_FiresAtWindowMidpoint()
    {
        var next = PeriodicEffectTiming.NextFireTime(CurTime, 120f, 600f, 0.5);

        Assert.That(next, Is.EqualTo(CurTime + TimeSpan.FromSeconds(360)));
    }

    [Test]
    public void AnySample_StaysInsideUnicornWindow()
    {
        // The corporate unicorn's shipping config: 120-600s.
        for (var i = 0; i <= 100; i++)
        {
            var next = PeriodicEffectTiming.NextFireTime(CurTime, 120f, 600f, i / 100.0);

            Assert.That(next, Is.GreaterThanOrEqualTo(CurTime + TimeSpan.FromSeconds(120)));
            Assert.That(next, Is.LessThanOrEqualTo(CurTime + TimeSpan.FromSeconds(600)));
        }
    }

    [Test]
    public void LargerSample_NeverFiresEarlier()
    {
        var previous = PeriodicEffectTiming.NextFireTime(CurTime, 120f, 600f, 0.0);

        for (var i = 1; i <= 20; i++)
        {
            var next = PeriodicEffectTiming.NextFireTime(CurTime, 120f, 600f, i / 20.0);

            Assert.That(next, Is.GreaterThanOrEqualTo(previous));
            previous = next;
        }
    }

    [Test]
    public void ResultIsRelativeToCurTime()
    {
        var later = TimeSpan.FromHours(3);

        var fromZero = PeriodicEffectTiming.NextFireTime(TimeSpan.Zero, 120f, 600f, 0.25);
        var fromLater = PeriodicEffectTiming.NextFireTime(later, 120f, 600f, 0.25);

        Assert.That(fromLater - later, Is.EqualTo(fromZero));
    }

    // --- Degenerate windows ---

    [Test]
    public void EqualMinAndMax_AlwaysFiresAtExactInterval()
    {
        var next = PeriodicEffectTiming.NextFireTime(CurTime, 300f, 300f, 0.7);

        Assert.That(next, Is.EqualTo(CurTime + TimeSpan.FromSeconds(300)));
    }

    [Test]
    public void InvertedWindow_CollapsesToMin()
    {
        // min > max is YAML misconfiguration; the window collapses to min rather than throwing.
        var next = PeriodicEffectTiming.NextFireTime(CurTime, 600f, 120f, 0.9);

        Assert.That(next, Is.EqualTo(CurTime + TimeSpan.FromSeconds(600)));
    }

    [Test]
    public void NegativeIntervals_ClampToZero()
    {
        var next = PeriodicEffectTiming.NextFireTime(CurTime, -50f, -10f, 1.0);

        Assert.That(next, Is.EqualTo(CurTime));
    }

    [Test]
    public void NegativeMinWithPositiveMax_ClampsLowerEdgeToZero()
    {
        var next = PeriodicEffectTiming.NextFireTime(CurTime, -50f, 10f, 0.0);

        Assert.That(next, Is.EqualTo(CurTime));
    }

    // --- Sample sanitization ---

    [Test]
    public void SampleBelowZero_ClampsToMin()
    {
        var next = PeriodicEffectTiming.NextFireTime(CurTime, 120f, 600f, -3.0);

        Assert.That(next, Is.EqualTo(CurTime + TimeSpan.FromSeconds(120)));
    }

    [Test]
    public void SampleAboveOne_ClampsToMax()
    {
        var next = PeriodicEffectTiming.NextFireTime(CurTime, 120f, 600f, 2.5);

        Assert.That(next, Is.EqualTo(CurTime + TimeSpan.FromSeconds(600)));
    }
}
