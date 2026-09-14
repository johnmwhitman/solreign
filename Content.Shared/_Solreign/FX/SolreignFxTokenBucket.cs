namespace Content.Shared._Solreign.FX;

/// <summary>
///     A pure, engine-free token-bucket rate limiter — the shape spec §3.1 calls for both server-side
///     (per-primitive-category egress budget, <c>SolreignFxEgressBudget</c>) and client-side
///     (per-frame intake cap, <c>SolreignFxLeaseManager</c>). Neither consumer exists yet — this is
///     scaffolding only, wired but with no consumers (W2 owns both the egress budget and the lease
///     manager per its file boundary). Shipping the shape now means W2 wires an already-tested
///     primitive instead of inventing and re-testing rate-limiting math under time pressure.
///
///     Kept as a mutable struct rather than a class so a caller can hold many buckets (one per
///     primitive category) cheaply in an array/dictionary without extra allocations. Takes an
///     externally-supplied "now" (seconds) rather than reading <c>IGameTiming</c> itself, so it
///     stays directly unit-testable without a server/client harness — same split as every other
///     pure-math type in this directory.
/// </summary>
public struct SolreignFxTokenBucket
{
    /// <summary>Tokens added per second (spec §3.1 default: 2x the category's concurrent cap per second).</summary>
    public readonly float RefillPerSecond;

    /// <summary>Maximum tokens the bucket can hold (spec §3.1 default: the category's concurrent cap).</summary>
    public readonly float BurstCapacity;

    private float _tokens;
    private double _lastRefillSeconds;

    /// <summary>Creates a bucket starting fully topped up (burst capacity available immediately).</summary>
    public SolreignFxTokenBucket(float refillPerSecond, float burstCapacity, double nowSeconds)
    {
        RefillPerSecond = float.IsFinite(refillPerSecond) && refillPerSecond > 0f ? refillPerSecond : 0f;
        BurstCapacity = float.IsFinite(burstCapacity) && burstCapacity > 0f ? burstCapacity : 0f;
        _tokens = BurstCapacity;
        _lastRefillSeconds = double.IsFinite(nowSeconds) ? nowSeconds : 0d;
    }

    /// <summary>Current available tokens, after refilling up to <paramref name="nowSeconds"/>.</summary>
    public float Available(double nowSeconds)
    {
        Refill(nowSeconds);
        return _tokens;
    }

    /// <summary>
    ///     Attempts to atomically consume <paramref name="cost"/> tokens (default 1). Refills first,
    ///     then either deducts and returns <c>true</c>, or leaves the bucket untouched and returns
    ///     <c>false</c> — never a partial deduction. A non-finite or negative <paramref name="cost"/>
    ///     always fails closed (never throws, never grants free tokens).
    /// </summary>
    public bool TryConsume(double nowSeconds, float cost = 1f)
    {
        Refill(nowSeconds);

        if (!float.IsFinite(cost) || cost < 0f)
            return false;

        if (_tokens < cost)
            return false;

        _tokens -= cost;
        return true;
    }

    private void Refill(double nowSeconds)
    {
        if (!double.IsFinite(nowSeconds))
            return;

        var elapsed = nowSeconds - _lastRefillSeconds;
        if (elapsed <= 0d)
            return;

        var grown = _tokens + (float) elapsed * RefillPerSecond;
        _tokens = grown < BurstCapacity ? grown : BurstCapacity;
        _lastRefillSeconds = nowSeconds;
    }
}
