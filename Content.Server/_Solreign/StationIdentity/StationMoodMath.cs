using System.Numerics;

namespace Content.Server._Solreign.StationIdentity;

/// <summary>
///     Pure decision/interpolation logic for the Solreign ambient-identity engine
///     (<see cref="SolreignStationMoodSystem"/>). Kept free of IoC/engine types so it is directly
///     unit-testable (Content.Tests/_Solreign/StationMoodMathTests.cs) — same reasoning as
///     <c>PeriodicEffectTiming</c>, <c>CorporateScoring</c>, and <c>SolreignZoneGateRules</c> elsewhere
///     in _Solreign. <see cref="Color"/> is a plain value struct with no IoC dependency (same idiom as
///     upstream's own <c>SharedLightCycleSystem.CalculateColorLevel</c>), so it's fine to use here too.
/// </summary>
public static class StationMoodMath
{
    /// <summary>
    ///     Computes the day/night ambient color for a map at <paramref name="elapsed"/> time into its
    ///     cycle: a continuous back-and-forth (triangle-wave) interpolation between
    ///     <paramref name="colorA"/> and <paramref name="colorB"/> across
    ///     <paramref name="periodSeconds"/> — 0 sits at <paramref name="colorA"/>, the half-period mark
    ///     sits at <paramref name="colorB"/>, and a full period returns to <paramref name="colorA"/>.
    ///     Deliberately a triangle wave rather than a sine: it's cheaper, and "slowly lerps" per the
    ///     wave brief doesn't call for the easing curve upstream's <c>LightCycleComponent</c> uses for
    ///     a realistic dawn/dusk — a plain linear back-and-forth reads as "mood shifting", which is the
    ///     ask here.
    ///
    ///     <paramref name="colorA"/>/<paramref name="colorB"/> are treated as sRGB (the natural space
    ///     for YAML-authored hex colors); the caller is responsible for converting the result through
    ///     <see cref="Color.FromSrgb"/> before assigning it to
    ///     <c>Robust.Shared.Map.Components.MapLightComponent.AmbientLightColor</c>, which — per that
    ///     component's own doc comment — expects linear-light values.
    ///
    ///     Misconfiguration is sanitized rather than thrown, because these values come from YAML: a
    ///     non-positive period clamps to 1 second (matching <c>PeriodicEffectTiming</c>'s "sanitize, don't
    ///     throw" precedent) rather than dividing by zero.
    /// </summary>
    public static Color LerpDayNightColor(TimeSpan elapsed, float periodSeconds, Color colorA, Color colorB)
    {
        var period = MathF.Max(1f, periodSeconds);

        var seconds = (float)elapsed.TotalSeconds % period;
        if (seconds < 0f)
            seconds += period;

        var t = seconds / period; // [0, 1)
        var triangle = t < 0.5f ? t * 2f : (1f - t) * 2f; // 0 -> 1 -> 0 across the period

        return Color.InterpolateBetween(colorA, colorB, triangle);
    }

    /// <summary>
    ///     Advances a drifting cosmetic entity's local position by <paramref name="velocity"/> over
    ///     <paramref name="frameTime"/> seconds. Trivial by design — <see cref="SolreignSporeParticleSystem"/>
    ///     is the only caller, and keeping it a pure one-liner here (rather than inline in the system)
    ///     keeps all of this wave's interpolation math in one tested module.
    /// </summary>
    public static Vector2 AdvanceDrift(Vector2 position, Vector2 velocity, float frameTime)
    {
        return position + velocity * frameTime;
    }

    /// <summary>
    ///     Picks which weather-style event id should run next, biasing toward a map's declared
    ///     preferences (<see cref="SolreignStationMoodComponent.WeatherEventPrototypes"/>) over a
    ///     general fallback pool. This is the pure decision logic for the Solreign weather
    ///     director/scheduler described in the StationIdentity wave's brief — "other lanes reference it
    ///     by prototype only": a future scheduler wave just needs to pass a map's preferred-id list as
    ///     <paramref name="preferred"/> and its normal weighted event pool as <paramref name="fallback"/>.
    ///     No such scheduler is wired up yet (see <see cref="SolreignSolarFlareRule"/>/
    ///     <see cref="SolreignSporeDriftRule"/>'s doc comments for what IS wired this wave — a simple
    ///     membership check via <see cref="SolreignStationMoodSystem.IsWeatherEventPreferred"/>), so this
    ///     is a documented, tested, not-yet-consumed hook — same precedent as
    ///     <c>ProvidenceVoiceSystem</c>'s unwired line categories.
    ///
    ///     Deterministic given its two [0, 1) samples (e.g. <c>IRobustRandom.NextDouble()</c>), so it's
    ///     fully unit-testable without any engine dependency:
    ///     <paramref name="branchSample"/> decides preferred-pool vs. fallback-pool (weighted by
    ///     <paramref name="preferredBias"/>), then <paramref name="indexSample"/> picks uniformly inside
    ///     whichever pool was chosen. Falls back to the other pool if the chosen one is empty; returns
    ///     null only if both are empty.
    /// </summary>
    public static string? PickWeatherEvent(
        IReadOnlyList<string> preferred,
        IReadOnlyList<string> fallback,
        double branchSample,
        double indexSample,
        float preferredBias = 0.7f)
    {
        var bias = Math.Clamp(preferredBias, 0f, 1f);
        var wantsPreferred = Math.Clamp(branchSample, 0d, 1d) < bias;

        var pool = wantsPreferred ? preferred : fallback;
        if (pool.Count == 0)
            pool = wantsPreferred ? fallback : preferred;

        if (pool.Count == 0)
            return null;

        var clampedIndexSample = Math.Clamp(indexSample, 0d, 0.999999d);
        var index = (int)(clampedIndexSample * pool.Count);
        return pool[index];
    }
}
