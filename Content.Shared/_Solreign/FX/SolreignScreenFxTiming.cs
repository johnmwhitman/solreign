namespace Content.Shared._Solreign.FX;

/// <summary>
///     Pure timing rules for the Solreign screen-FX signature moment (the acid-green
///     colored-screen-border overlay). Kept free of any engine/ECS dependency so it is unit-testable
///     without spinning up a server or client — see Content.Tests/_Solreign/SolreignScreenFxTimingTests.cs.
/// </summary>
public static class SolreignScreenFxTiming
{
    /// <summary>Fallback duration (seconds) for callers that don't specify one.</summary>
    public const float DefaultDuration = 1.5f;

    /// <summary>Shortest hold the overlay is allowed, so a zero/negative request can't no-op it.</summary>
    public const float MinDuration = 0.1f;

    /// <summary>
    ///     Longest hold the overlay is allowed in one shot, so a runaway or malicious caller can't paint
    ///     the whole screen acid-green indefinitely.
    /// </summary>
    public const float MaxDuration = 5f;

    /// <summary>Clamps a requested duration into the sane [<see cref="MinDuration"/>, <see cref="MaxDuration"/>] range.</summary>
    public static float ClampDuration(float duration)
    {
        return Math.Clamp(duration, MinDuration, MaxDuration);
    }

    /// <summary>
    ///     Merges a newly-requested duration into an already-running countdown: overlapping triggers
    ///     extend the hold rather than restarting or stacking it, so a burst of Solreign events reads as
    ///     one held sting instead of a flicker.
    /// </summary>
    public static float ExtendRemaining(float currentRemaining, float requestedDuration)
    {
        return MathF.Max(currentRemaining, ClampDuration(requestedDuration));
    }
}
