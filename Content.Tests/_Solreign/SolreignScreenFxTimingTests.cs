using Content.Shared._Solreign.FX;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(SolreignScreenFxTiming))]
public sealed class SolreignScreenFxTimingTests
{
    // --- ClampDuration ---

    [Test]
    public void ClampDuration_WithinRange_Unchanged()
    {
        Assert.That(SolreignScreenFxTiming.ClampDuration(2f), Is.EqualTo(2f));
    }

    [Test]
    public void ClampDuration_BelowMin_ClampsToMin()
    {
        Assert.That(SolreignScreenFxTiming.ClampDuration(0f), Is.EqualTo(SolreignScreenFxTiming.MinDuration));
        Assert.That(SolreignScreenFxTiming.ClampDuration(-5f), Is.EqualTo(SolreignScreenFxTiming.MinDuration));
    }

    [Test]
    public void ClampDuration_AboveMax_ClampsToMax()
    {
        Assert.That(SolreignScreenFxTiming.ClampDuration(999f), Is.EqualTo(SolreignScreenFxTiming.MaxDuration));
    }

    // --- ExtendRemaining ---

    [Test]
    public void ExtendRemaining_NewRequestLonger_ExtendsToRequest()
    {
        Assert.That(SolreignScreenFxTiming.ExtendRemaining(0.5f, 2f), Is.EqualTo(2f));
    }

    [Test]
    public void ExtendRemaining_NewRequestShorter_KeepsCurrentRemaining()
    {
        Assert.That(SolreignScreenFxTiming.ExtendRemaining(3f, 1f), Is.EqualTo(3f));
    }

    [Test]
    public void ExtendRemaining_RequestBelowMin_StillClampedBeforeCompare()
    {
        // A near-zero request shouldn't be able to shrink an active hold, but it also shouldn't be
        // compared as literally 0 — it's clamped to MinDuration first either way.
        Assert.That(SolreignScreenFxTiming.ExtendRemaining(0f, 0f), Is.EqualTo(SolreignScreenFxTiming.MinDuration));
    }

    [Test]
    public void ExtendRemaining_RequestAboveMax_ClampedBeforeExtending()
    {
        Assert.That(SolreignScreenFxTiming.ExtendRemaining(0f, 999f), Is.EqualTo(SolreignScreenFxTiming.MaxDuration));
    }

    // --- SolreignScreenFxEvent construction clamps too ---

    [Test]
    public void Event_DefaultDuration_IsWithinRange()
    {
        var ev = new SolreignScreenFxEvent();

        Assert.That(ev.Duration, Is.EqualTo(SolreignScreenFxTiming.DefaultDuration));
    }

    [Test]
    public void Event_OutOfRangeDuration_IsClampedOnConstruction()
    {
        var tooLong = new SolreignScreenFxEvent(999f);
        var tooShort = new SolreignScreenFxEvent(-1f);

        Assert.That(tooLong.Duration, Is.EqualTo(SolreignScreenFxTiming.MaxDuration));
        Assert.That(tooShort.Duration, Is.EqualTo(SolreignScreenFxTiming.MinDuration));
    }
}
