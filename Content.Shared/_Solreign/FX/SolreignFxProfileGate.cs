namespace Content.Shared._Solreign.FX;

/// <summary>The four FX Language v1 accessibility profiles (spec §4), selected by <c>solreign.fx.profile</c>.</summary>
public enum SolreignFxProfile : byte
{
    Full = 0,
    ReducedMotion = 1,
    LowVfx = 2,
    CosmeticMinimal = 3,
}

/// <summary>
///     Pure computed-gate logic for FX Language v1's accessibility profiles (spec §4), mirroring
///     <c>MovementBobSystem.FeatureActive</c>'s "one gate property, engine-free, unit-tested" shape.
///     A future client system (W2) subscribes these CVars and calls into this class every time one
///     changes, exactly as <c>MovementBobSystem.RefreshActive</c> does — no such system exists yet
///     in W1, so this class is pure math with no consumer wired in.
/// </summary>
public static class SolreignFxProfileGate
{
    /// <summary>WCAG 2.3.1 general flash threshold (spec §4.0): the hard cap on luminance transitions/second for ANY flicker-capable primitive, in EVERY profile — not just reduced ones.</summary>
    public const float MaxFlashTransitionsPerSecond = 3f;

    /// <summary>
    ///     Parses the <c>solreign.fx.profile</c> CVar string. Unrecognized/null/empty values fall
    ///     back to <see cref="SolreignFxProfile.Full"/> rather than throwing — a typo'd or
    ///     corrupted client config must never crash, only lose the requested accessibility
    ///     narrowing (which itself defaults to the least restrictive, most-features-on profile,
    ///     matching the CVar's own documented default).
    /// </summary>
    public static SolreignFxProfile ParseProfile(string? raw)
    {
        return raw switch
        {
            "reduced_motion" => SolreignFxProfile.ReducedMotion,
            "low_vfx" => SolreignFxProfile.LowVfx,
            "cosmetic_minimal" => SolreignFxProfile.CosmeticMinimal,
            _ => SolreignFxProfile.Full,
        };
    }

    /// <summary>
    ///     Composes the explicit <c>solreign.fx.profile</c> selection with the engine's own
    ///     <c>CCVars.ReducedMotion</c> (spec §4: "the engine accessibility setting is never
    ///     overridable by a Solreign feature flag"). The engine setting can only ever STRENGTHEN an
    ///     explicit <see cref="SolreignFxProfile.Full"/> selection up to
    ///     <see cref="SolreignFxProfile.ReducedMotion"/> — it never weakens an already-stricter
    ///     explicit choice (<see cref="SolreignFxProfile.LowVfx"/>/<see cref="SolreignFxProfile.CosmeticMinimal"/>
    ///     already comply with reduced-motion's camera-stillness rule by construction per §4's
    ///     profile table).
    /// </summary>
    public static SolreignFxProfile EffectiveProfile(SolreignFxProfile explicitProfile, bool engineReducedMotion)
    {
        if (engineReducedMotion && explicitProfile == SolreignFxProfile.Full)
            return SolreignFxProfile.ReducedMotion;

        return explicitProfile;
    }

    /// <summary>The master kill-switch gate: whether the FX cue system may do anything at all on this client/server pair. Mirrors <c>MovementBobSystem.FeatureActive</c>'s enabled-flag half.</summary>
    public static bool CueSystemActive(bool masterCueV1Enabled)
    {
        return masterCueV1Enabled;
    }

    /// <summary>
    ///     Whether a flicker-capable primitive's transition rate is within the photosensitivity cap
    ///     (spec §4.0, grk #4C). Applies in EVERY profile, not just reduced ones. Non-finite input
    ///     is never "safe" — it fails closed.
    /// </summary>
    public static bool WithinFlashSafeRate(float transitionsPerSecond)
    {
        return float.IsFinite(transitionsPerSecond) && transitionsPerSecond <= MaxFlashTransitionsPerSecond;
    }

    /// <summary>
    ///     Whether flicker-capable primitives may pulse at all (spec §4.0): the dedicated
    ///     <c>solreign.fx.no_flash</c> toggle zeroes ALL light-pulse behavior independently of
    ///     profile — it is not implied by any profile selection, only by this explicit switch.
    /// </summary>
    public static bool FlickerAllowed(bool noFlashEnabled)
    {
        return !noFlashEnabled;
    }
}
