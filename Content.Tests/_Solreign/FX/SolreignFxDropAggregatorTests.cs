#nullable enable
using Content.Shared._Solreign.FX;
using NUnit.Framework;

namespace Content.Tests._Solreign.FX;

/// <summary>
///     Spec §1.3b/cdx #20: drop diagnostics must be rate-limited and aggregated — never one log
///     line per cue (a hostile server could otherwise turn rejection itself into a log/IO
///     amplifier). <see cref="SolreignFxDropAggregator"/> is shared by both the client's receive-side
///     drops and the server's egress-budget/raise-refusal drops.
/// </summary>
[TestFixture]
[TestOf(typeof(SolreignFxDropAggregator))]
public sealed class SolreignFxDropAggregatorTests
{
    [Test]
    public void Flush_FirstRecordEver_ReportsImmediately()
    {
        // No prior flush has ever happened for this reason (baseline is "never"), so the very
        // first drop is surfaced right away rather than waiting a full interval — an operator
        // should learn about a NEW problem immediately, not after a mandatory first delay.
        var aggregator = new SolreignFxDropAggregator(intervalSeconds: 5.0);
        aggregator.Record("SomeReason", 1);

        var flushed = aggregator.Flush(0.0);

        Assert.That(flushed, Has.Count.EqualTo(1));
        Assert.That(flushed[0].Reason, Is.EqualTo("SomeReason"));
    }

    [Test]
    public void Flush_SecondBurstWithinInterval_DoesNotReportAgainYet()
    {
        var aggregator = new SolreignFxDropAggregator(intervalSeconds: 5.0);
        aggregator.Record("SomeReason", 1);
        aggregator.Flush(0.0); // first report, immediate

        aggregator.Record("SomeReason", 2);
        var tooSoon = aggregator.Flush(1.0); // well within the 5s interval

        Assert.That(tooSoon, Is.Empty, "a reason that already reported must wait out the full interval before reporting again");
    }

    [Test]
    public void Flush_AfterIntervalElapses_ReportsOneAggregatedSummaryRegardlessOfCount()
    {
        var aggregator = new SolreignFxDropAggregator(intervalSeconds: 5.0);

        for (uint i = 0; i < 1000; i++)
            aggregator.Record("Flood", i);

        var flushed = aggregator.Flush(6.0);

        Assert.That(flushed, Has.Count.EqualTo(1), "1000 drops under one reason must produce exactly ONE summary line, never one per drop");
        Assert.That(flushed[0].Reason, Is.EqualTo("Flood"));
        Assert.That(flushed[0].Count, Is.EqualTo(1000));
    }

    [Test]
    public void Flush_ResetsTheCounterAfterReporting()
    {
        var aggregator = new SolreignFxDropAggregator(intervalSeconds: 5.0);
        aggregator.Record("Reason", 1);
        aggregator.Flush(6.0);

        var secondFlush = aggregator.Flush(6.0);

        Assert.That(secondFlush, Is.Empty, "a reason with zero new drops since the last flush must not re-report");
    }

    [Test]
    public void Flush_DifferentReasons_AreIndependentBuckets()
    {
        var aggregator = new SolreignFxDropAggregator(intervalSeconds: 5.0);
        aggregator.Record("A", 1);
        aggregator.Record("B", 2);
        aggregator.Record("B", 3);

        var flushed = aggregator.Flush(6.0);

        Assert.That(flushed, Has.Count.EqualTo(2));
        var byReason = new System.Collections.Generic.Dictionary<string, int>();
        foreach (var summary in flushed)
            byReason[summary.Reason] = summary.Count;

        Assert.That(byReason["A"], Is.EqualTo(1));
        Assert.That(byReason["B"], Is.EqualTo(2));
    }

    [Test]
    public void Flush_WithinTheSameIntervalTwice_DoesNotDoubleReport()
    {
        var aggregator = new SolreignFxDropAggregator(intervalSeconds: 5.0);
        aggregator.Record("Reason", 1);

        aggregator.Flush(6.0);
        aggregator.Record("Reason", 2); // one more drop, still well inside the next interval
        var tooSoon = aggregator.Flush(6.5);

        Assert.That(tooSoon, Is.Empty, "a flush attempted before the interval re-elapses must not report yet, even with pending drops");
    }
}
