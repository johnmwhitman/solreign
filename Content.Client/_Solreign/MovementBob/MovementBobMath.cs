namespace Content.Client._Solreign.MovementBob;

/// <summary>
/// SR-W-083: pure math for the procedural humanoid movement bob.
/// Kept engine-free so <c>Content.Tests._Solreign.MovementBobMathTests</c> can table-test it.
/// </summary>
public static class MovementBobMath
{
    /// <summary>
    /// Sprite offsets are in world units; one tile is 32 texture pixels
    /// (mirrors <c>EyeManager.PixelsPerMeter</c>, duplicated here to stay engine-free).
    /// </summary>
    public const float PixelsPerUnit = 32f;

    /// <summary>
    /// Defensive caps: the bob is a 1-2px garnish. Whatever the replicated CVars say,
    /// the client never renders more than this.
    /// </summary>
    public const float MinAmplitudePixels = 0f;
    public const float MaxAmplitudePixels = 4f;
    public const float MinHz = 0.25f;
    public const float MaxHz = 8f;

    /// <summary>
    /// Amplitude after sanitizing: non-finite replicated values (NaN/infinity) mean the
    /// bob is off, everything else clamps into the defensive range. Math.Clamp alone is
    /// NOT enough — it passes NaN through.
    /// </summary>
    public static float EffectiveAmplitude(float amplitudePixels)
    {
        if (!float.IsFinite(amplitudePixels))
            return 0f;

        return Math.Clamp(amplitudePixels, MinAmplitudePixels, MaxAmplitudePixels);
    }

    /// <summary>
    /// Frequency after sanitizing: non-finite values fall back to the minimum, everything
    /// else clamps into the defensive range.
    /// </summary>
    public static float EffectiveHz(float hz)
    {
        if (!float.IsFinite(hz))
            return MinHz;

        return Math.Clamp(hz, MinHz, MaxHz);
    }

    /// <summary>
    /// Vertical sprite offset (world units) at a given time. Rectified sine
    /// (<c>|sin(pi * hz * t + phase)|</c>): a footstep-style bounce that never dips
    /// below the sprite's baseline. <paramref name="hz"/> is bounces per second.
    /// Always returns a finite, non-negative value no matter what the inputs are.
    /// </summary>
    public static float OffsetUnits(double timeSeconds, float hz, float amplitudePixels, float phase = 0f)
    {
        if (!double.IsFinite(timeSeconds) || !float.IsFinite(phase))
            return 0f;

        var amplitude = EffectiveAmplitude(amplitudePixels);
        var frequency = EffectiveHz(hz);
        // Reduce onto one full sine period BEFORE the float cast — keeps the "always finite"
        // invariant literal even for absurd (but finite) double times.
        var cycle = timeSeconds % (2.0 / frequency);
        var wave = MathF.Abs(MathF.Sin((float) (Math.PI * frequency * cycle) + phase));
        return amplitude * wave / PixelsPerUnit;
    }

    /// <summary>
    /// Per-entity phase in [0, tau) derived from a stable seed, so a crowd of
    /// walkers doesn't bounce in lockstep.
    /// </summary>
    public static float Phase(int seed)
    {
        return (seed & 0xFF) * (MathF.Tau / 256f);
    }

    /// <summary>
    /// The single gate for whether a bob may render this frame. Standing/buckle/orbit/floating
    /// guards keep the bob off the rotation- and offset-channels other systems own (downed
    /// rotation visuals, buckle snapping, orbit offset animation, weightless floaters).
    /// Live offset ANIMATIONS (jitter/stamina/float) are handled before this gate as channel
    /// occupancy in MovementBobSystem — full suspension, not a reset.
    /// </summary>
    public static bool ShouldBob(bool enabled, bool reducedMotion, bool isMoving, bool standing, bool buckled, bool orbiting, bool floating)
    {
        return enabled && !reducedMotion && isMoving && standing && !buckled && !orbiting && !floating;
    }
}
