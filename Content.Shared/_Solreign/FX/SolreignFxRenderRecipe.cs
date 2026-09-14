namespace Content.Shared._Solreign.FX;

/// <summary>
///     W3's per-primitive, per-profile RENDER decision table (spec §2's primitive table + §4's
///     accessibility matrix), made mechanical and pure — same "math half only" split as
///     <see cref="SolreignFxProfilePolicy"/> (which W2 shipped for the LEASE/BUDGET gate: "may this
///     category acquire a resource at all"). This type answers the next question, once a lease is
///     already granted: "given the category, the effect id (only meaningfully different from category
///     for <c>transformation</c>/<c>transformation_generic</c>, which share one category), the
///     effective profile, and the no-flash toggle, what should actually be drawn/played?"
///
///     Deliberately engine-free (no <c>IEntityManager</c>/<c>IPrototypeManager</c>/Robust rendering
///     types) so it is directly unit-testable without a client harness — the mission's "per-profile
///     rendering-path tests" requirement is satisfied by exhaustively testing THIS table, one row per
///     (category, profile) pair, rather than requiring a live graphical integration test per
///     primitive. <c>Content.Client._Solreign.FX</c>'s renderer (W3's engine-wired half) consumes a
///     <see cref="SolreignFxRenderPlan"/> and does the actual `Spawn`/`PointLight`/`Overlay`/`PlayPvs`
///     work — it contains no primitive-specific branching of its own beyond "how do I draw a plan."
///
///     Every row here is a strict NARROWING of <see cref="SolreignFxProfilePolicy.GetBehavior"/>'s
///     already-shipped (category, profile) → (SpriteAllowed/OverlayAllowed/LightAllowed/
///     CameraImpulseAllowed/MustNeverFullyDrop) gate — this type never contradicts that table (e.g.
///     it never enables a sprite the policy table forbids), it only adds the RENDER-STYLE detail the
///     budget/lease layer never needed to know (icon-flash substitution, flicker-vs-single-pulse,
///     frozen-vs-animated overlay fill, screen-sting eligibility, countdown-text eligibility).
/// </summary>
public readonly record struct SolreignFxRenderPlan(
    bool SpriteEnabled,
    bool SpriteIsIconFlash,
    float SpriteScaleMultiplier,
    bool LightEnabled,
    bool LightFlickers,
    float LightFlashRatePerSecond,
    bool OverlayEnabled,
    bool OverlayAnimated,
    bool CameraImpulseEnabled,
    bool AudioEnabled,
    bool ScreenStingEnabled,
    bool CountdownTextEnabled,
    uint VariationIndex)
{
    /// <summary>The inert, all-off plan — returned whenever the category is fully dropped under the current profile (spec §4.0: never the sole information carrier, so dropping to nothing is always safe).</summary>
    public static readonly SolreignFxRenderPlan None = new(
        SpriteEnabled: false,
        SpriteIsIconFlash: false,
        SpriteScaleMultiplier: 1f,
        LightEnabled: false,
        LightFlickers: false,
        LightFlashRatePerSecond: 0f,
        OverlayEnabled: false,
        OverlayAnimated: false,
        CameraImpulseEnabled: false,
        AudioEnabled: false,
        ScreenStingEnabled: false,
        CountdownTextEnabled: false,
        VariationIndex: 0);
}

/// <summary>
///     Builds a <see cref="SolreignFxRenderPlan"/> for one accepted, leased cue activation. See class
///     remarks on <see cref="SolreignFxRenderPlan"/> for the split rationale.
/// </summary>
public static class SolreignFxRenderRecipe
{
    /// <summary>
    ///     Repeating-flicker rate for FULL/LOW_VFX profiles — safely under the WCAG
    ///     <see cref="SolreignFxProfileGate.MaxFlashTransitionsPerSecond"/> (3/s) ceiling this value
    ///     is asserted against by every caller (spec §4.0, grk #4C).
    /// </summary>
    public const float RepeatingFlickerRatePerSecond = 2f;

    /// <summary>
    ///     A "single pulse" is modeled as one transition, reported here as a nominal 1/s rate purely
    ///     so <see cref="SolreignFxProfileGate.WithinFlashSafeRate"/> has a well-formed (and
    ///     trivially compliant) value to check — the renderer itself is responsible for actually
    ///     firing it once, not on a repeating timer.
    /// </summary>
    public const float SinglePulseNominalRatePerSecond = 1f;

    /// <summary>Deterministic small variety pool shared by every sprite-having primitive (seed-derived sprite/arc jitter — never a hardcoded index, per spec §1.2/cdx #18).</summary>
    public const uint VariationCount = 4;

    /// <summary>
    ///     Builds the render plan for one activation. <paramref name="effectId"/> is only
    ///     meaningfully different from <paramref name="category"/> for <c>transformation</c> vs
    ///     <c>transformation_generic</c> (both resolve to <see cref="SolreignFxCategory.Transformation"/>
    ///     — only the real, actor-received <c>transformation</c> id is ever eligible for the
    ///     actor-only screen sting, per spec §4's profile table and §5.2's broadcast-excludes-actor
    ///     rule: a bystander only ever receives <c>transformation_generic</c>, which must never sting
    ///     their screen).
    /// </summary>
    public static SolreignFxRenderPlan BuildPlan(
        SolreignFxCategory category,
        string effectId,
        SolreignFxProfile profile,
        SolreignFxProfilePolicy.ProfileBehavior behavior,
        bool noFlash,
        uint seed)
    {
        if (behavior.FullyDropped)
            return SolreignFxRenderPlan.None;

        var variation = SolreignFxSeedMixing.Index(seed, VariationCount);

        // Photosensitivity (spec §4.0, grk #4C): reduced_motion and cosmetic_minimal both convert
        // ANY flicker-capable primitive's light to a single non-repeating pulse, regardless of
        // category — full/low_vfx keep the (rate-capped) repeating flicker. no_flash zeroes the
        // flicker rate to 0 in every profile without disabling the light itself (the light can still
        // serve as a steady, non-flashing accent — spec's no_flash rule is "zeroes ALL light-PULSE
        // behavior," not "removes every light").
        var singlePulseProfile = profile is SolreignFxProfile.ReducedMotion or SolreignFxProfile.CosmeticMinimal;
        var flickerAllowed = SolreignFxProfileGate.FlickerAllowed(noFlash);
        var lightFlickers = behavior.LightAllowed && flickerAllowed && !singlePulseProfile;
        var flashRate = !behavior.LightAllowed || !flickerAllowed
            ? 0f
            : singlePulseProfile ? SinglePulseNominalRatePerSecond : RepeatingFlickerRatePerSecond;

        return category switch
        {
            SolreignFxCategory.ImpactLight or SolreignFxCategory.ImpactHeavy => new SolreignFxRenderPlan(
                SpriteEnabled: behavior.SpriteAllowed,
                SpriteIsIconFlash: profile == SolreignFxProfile.CosmeticMinimal,
                SpriteScaleMultiplier: category == SolreignFxCategory.ImpactHeavy ? 1.6f : 1f,
                LightEnabled: behavior.LightAllowed,
                LightFlickers: lightFlickers,
                LightFlashRatePerSecond: flashRate,
                OverlayEnabled: false,
                OverlayAnimated: false,
                // grk W3 round-2 review finding M1: SolreignFxProfilePolicy.CameraImpulseAllowed is
                // TRUE for BOTH ImpactLight and ImpactHeavy under `full` (that table only governs
                // WHETHER the category may touch a camera-impulse resource at all, same shape as
                // every other resource flag it exposes) — but spec §2's primitive table names camera
                // impulse specifically under `impact_heavy` ("camera impulse gated by
                // CCVars.ReducedMotion"), never impact_light. Narrowed here, at the render-decision
                // layer, rather than in the shared W2 policy table (which correctly stays a
                // per-category capability gate, not a per-primitive one — Light/Heavy share one
                // category row there by design).
                CameraImpulseEnabled: category == SolreignFxCategory.ImpactHeavy && behavior.CameraImpulseAllowed,
                AudioEnabled: true,
                ScreenStingEnabled: false,
                CountdownTextEnabled: false,
                VariationIndex: variation),

            SolreignFxCategory.Electrical => new SolreignFxRenderPlan(
                SpriteEnabled: behavior.SpriteAllowed,
                SpriteIsIconFlash: false,
                SpriteScaleMultiplier: 1f,
                LightEnabled: behavior.LightAllowed,
                LightFlickers: lightFlickers,
                LightFlashRatePerSecond: flashRate,
                OverlayEnabled: behavior.OverlayAllowed,
                OverlayAnimated: true,
                CameraImpulseEnabled: false,
                AudioEnabled: true,
                ScreenStingEnabled: false,
                CountdownTextEnabled: false,
                VariationIndex: variation),

            SolreignFxCategory.Dust or SolreignFxCategory.Smoke => new SolreignFxRenderPlan(
                SpriteEnabled: behavior.SpriteAllowed,
                SpriteIsIconFlash: false,
                SpriteScaleMultiplier: category == SolreignFxCategory.Smoke ? 1.5f : 1f,
                LightEnabled: false,
                LightFlickers: false,
                LightFlashRatePerSecond: 0f,
                OverlayEnabled: false,
                OverlayAnimated: false,
                CameraImpulseEnabled: false,
                AudioEnabled: false,
                ScreenStingEnabled: false,
                CountdownTextEnabled: false,
                VariationIndex: variation),

            SolreignFxCategory.CastRing => new SolreignFxRenderPlan(
                SpriteEnabled: false,
                SpriteIsIconFlash: false,
                SpriteScaleMultiplier: 1f,
                LightEnabled: behavior.LightAllowed,
                LightFlickers: false,
                LightFlashRatePerSecond: 0f,
                OverlayEnabled: behavior.OverlayAllowed,
                // full = animated (pulsing/rotating fill); reduced_motion = frozen slow linear fill;
                // low_vfx = simplified single-color fill (still animates its fill progress, just not
                // the extra pulse/rotation flourish) — reduced_motion is the one profile that freezes
                // fill progress itself (spec: "ring animation frozen").
                OverlayAnimated: behavior.OverlayAllowed && profile != SolreignFxProfile.ReducedMotion,
                CameraImpulseEnabled: false,
                AudioEnabled: false,
                ScreenStingEnabled: false,
                CountdownTextEnabled: profile == SolreignFxProfile.CosmeticMinimal,
                VariationIndex: variation),

            SolreignFxCategory.Transformation => new SolreignFxRenderPlan(
                SpriteEnabled: behavior.SpriteAllowed,
                SpriteIsIconFlash: false,
                SpriteScaleMultiplier: profile == SolreignFxProfile.LowVfx ? 0.75f : 1f,
                LightEnabled: false,
                LightFlickers: false,
                LightFlashRatePerSecond: 0f,
                OverlayEnabled: false,
                OverlayAnimated: false,
                CameraImpulseEnabled: false,
                AudioEnabled: behavior.SpriteAllowed,
                // Actor-only, full-profile-only (spec §4: "screen sting disabled for the actor" under
                // reduced_motion; low_vfx/cosmetic_minimal name only sprite simplification/dropping,
                // never re-affirm the sting, so it stays a full-profile-only flourish by the
                // conservative reading — never gameplay-critical either way, §4.0's own framing for
                // this primitive's ADDITIONAL cosmetic layer on top of the always-kept appearance swap).
                // Only the real `transformation` id (never `transformation_generic`) is eligible —
                // a bystander's redacted broadcast cue must never sting THEIR screen.
                ScreenStingEnabled: profile == SolreignFxProfile.Full && effectId == "transformation",
                CountdownTextEnabled: false,
                VariationIndex: variation),

            SolreignFxCategory.StaminaBreak or SolreignFxCategory.BodyShockGeneric => new SolreignFxRenderPlan(
                SpriteEnabled: behavior.SpriteAllowed,
                SpriteIsIconFlash: false,
                SpriteScaleMultiplier: 1f,
                LightEnabled: behavior.LightAllowed,
                LightFlickers: lightFlickers,
                LightFlashRatePerSecond: flashRate,
                OverlayEnabled: false,
                OverlayAnimated: false,
                CameraImpulseEnabled: false,
                // Mandatory in every profile, including cosmetic_minimal (spec §4's stamina_break
                // row: "+ mandatory audio") — never gated by noFlash (that toggle governs light
                // pulsing only) and never gated by profile.
                AudioEnabled: true,
                ScreenStingEnabled: false,
                CountdownTextEnabled: false,
                VariationIndex: variation),

            _ => SolreignFxRenderPlan.None,
        };
    }
}
