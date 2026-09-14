using System.Collections.Generic;

namespace Content.Shared._Solreign.FX;

/// <summary>
///     Rate-limited, aggregated drop diagnostics (spec §1.3b: "Any drop increments a per-reason
///     aggregate counter and logs rate-limited, aggregated diagnostics — at most one summary line
///     per reason per N seconds, with sampled CorrelationIds — never one log line per cue, which
///     would let a hostile server turn rejection into a log/IO amplifier," cdx #20). Shared by both
///     the client's <c>SolreignFxCueSystem</c> (receive-side drops) and the server's
///     <c>SolreignFxEgressBudget</c>/<c>SolreignFxServerSystem</c> (send-side drops) — the exact same
///     amplification hazard exists on both sides of the wire, so the same fix applies to both.
///
///     Pure and engine-free: the caller supplies "now" (seconds) rather than this type reading
///     <c>IGameTiming</c> itself, same split as every other pure-math type in this directory.
/// </summary>
public sealed class SolreignFxDropAggregator
{
    public readonly record struct Summary(string Reason, int Count, uint SampleCorrelationId);

    private sealed class Bucket
    {
        public int Count;
        public uint SampleCorrelationId;
        public double LastFlushSeconds = double.NegativeInfinity;
    }

    private readonly Dictionary<string, Bucket> _buckets = new();
    private readonly double _intervalSeconds;

    public SolreignFxDropAggregator(double intervalSeconds = 5.0)
    {
        _intervalSeconds = intervalSeconds > 0d ? intervalSeconds : 5.0;
    }

    /// <summary>Records one dropped cue under <paramref name="reason"/>. Never logs by itself — accumulates only.</summary>
    public void Record(string reason, uint correlationId)
    {
        if (!_buckets.TryGetValue(reason, out var bucket))
        {
            bucket = new Bucket();
            _buckets[reason] = bucket;
        }

        bucket.Count++;
        bucket.SampleCorrelationId = correlationId;
    }

    /// <summary>
    ///     Returns one <see cref="Summary"/> per reason bucket that has BOTH pending drops AND has
    ///     not been flushed within <see cref="_intervalSeconds"/>, resetting those buckets'
    ///     counters. A reason with zero pending drops, or one flushed too recently, is skipped —
    ///     callers should call this every frame/tick cheaply; it is a no-op dictionary scan when
    ///     nothing is due.
    /// </summary>
    public IReadOnlyList<Summary> Flush(double nowSeconds)
    {
        List<Summary>? flushed = null;

        foreach (var (reason, bucket) in _buckets)
        {
            if (bucket.Count == 0)
                continue;

            if (nowSeconds - bucket.LastFlushSeconds < _intervalSeconds)
                continue;

            flushed ??= new List<Summary>();
            flushed.Add(new Summary(reason, bucket.Count, bucket.SampleCorrelationId));
            bucket.Count = 0;
            bucket.LastFlushSeconds = nowSeconds;
        }

        return (IReadOnlyList<Summary>?) flushed ?? System.Array.Empty<Summary>();
    }
}
