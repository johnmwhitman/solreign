namespace Content.Server._Solreign.Effects;

/// <summary>
///     Pure next-fire-time math for <see cref="SolreignPeriodicEffectSystem"/>. Kept free of IoC/engine
///     types so it is directly unit-testable (Content.Tests/_Solreign/PeriodicEffectTests.cs), mirroring
///     how <c>CorporateScoring</c> and <c>RankRules</c> isolate pure logic elsewhere in _Solreign.
/// </summary>
public static class PeriodicEffectTiming
{
    /// <summary>
    ///     Computes the next fire time as <paramref name="curTime"/> plus an interval interpolated inside
    ///     the [min, max] window by <paramref name="sample"/> (a uniform roll in [0, 1], e.g.
    ///     <c>IRobustRandom.NextDouble()</c>).
    ///
    ///     Misconfiguration is sanitized rather than thrown, because these values come from YAML:
    ///     negative intervals clamp to zero, an inverted window (min &gt; max) collapses to min, and
    ///     out-of-range samples clamp into [0, 1].
    /// </summary>
    public static TimeSpan NextFireTime(TimeSpan curTime, float minIntervalSeconds, float maxIntervalSeconds, double sample)
    {
        var min = MathF.Max(0f, minIntervalSeconds);
        var max = MathF.Max(min, maxIntervalSeconds);
        var roll = Math.Clamp(sample, 0d, 1d);

        var intervalSeconds = min + (max - min) * roll;
        return curTime + TimeSpan.FromSeconds(intervalSeconds);
    }
}
