namespace Content.Shared._Solreign.FX;

/// <summary>
///     Per-<see cref="SolreignFxCategory"/>, per-<see cref="SolreignFxProfile"/> gate plumbing (spec
///     §4's table), pure and engine-free like every other math type in this directory. This is the
///     "which resource classes may a primitive touch under this profile" plumbing the mission asks
///     for — W2 has no primitives/renderers yet (W3), so this table governs whether
///     <c>SolreignFxLeaseManager</c> may even attempt a lease for a given (category, resource) pair,
///     not how any resource is actually drawn.
///
///     <see cref="ProfileBehavior.MustNeverFullyDrop"/> is spec §4.0's structural rule made
///     mechanical: <see cref="SolreignFxCategory.CastRing"/>, <see cref="SolreignFxCategory.Transformation"/>,
///     and <see cref="SolreignFxCategory.StaminaBreak"/> are the three primitives that can carry a
///     gameplay-critical telegraph — their row is <c>true</c> in EVERY profile, including
///     <see cref="SolreignFxProfile.CosmeticMinimal"/>, matching the spec's own language that they
///     are "never dropped, only re-rendered non-visually-dense." Every other category may
///     legitimately zero out at <see cref="SolreignFxProfile.CosmeticMinimal"/> because no gameplay
///     state is encoded in them exclusively.
/// </summary>
public static class SolreignFxProfilePolicy
{
    /// <summary>
    ///     One category's behavior under one profile. <see cref="ConcurrentCapMultiplier"/> scales
    ///     the category's spec §3 concurrent cap (e.g. <c>low_vfx</c>'s "budget halved" /
    ///     "40% of §3's concurrent cap" rows); 0 means the category is fully suppressed for THIS
    ///     profile (only ever legal when <see cref="MustNeverFullyDrop"/> is false).
    /// </summary>
    public readonly record struct ProfileBehavior(
        bool SpriteAllowed,
        bool OverlayAllowed,
        bool LightAllowed,
        bool CameraImpulseAllowed,
        float ConcurrentCapMultiplier,
        bool MustNeverFullyDrop)
    {
        /// <summary>Whether this category may acquire ANY lease at all under this profile (spec §4: cosmetic-only primitives "may legitimately degrade to nothing").</summary>
        public bool FullyDropped => !MustNeverFullyDrop && ConcurrentCapMultiplier <= 0f && !SpriteAllowed && !OverlayAllowed && !LightAllowed;
    }

    /// <summary>
    ///     Spec §4's table, verbatim per row. <see cref="SolreignFxCategory.Transformation"/> always
    ///     keeps its appearance-layer swap "no matter what" per spec — modeled here as
    ///     <see cref="ProfileBehavior.MustNeverFullyDrop"/> true in every profile (the swap itself
    ///     rides <c>SharedAppearanceSystem</c>, outside the FX kill switch entirely per §4.0 — this
    ///     table only governs the ADDITIONAL burst-sprite/screen-sting cosmetic layer FX Language v1
    ///     contributes on top of it).
    /// </summary>
    public static ProfileBehavior GetBehavior(SolreignFxCategory category, SolreignFxProfile profile)
    {
        return (category, profile) switch
        {
            // --- impact_light / impact_heavy ---
            (SolreignFxCategory.ImpactLight or SolreignFxCategory.ImpactHeavy, SolreignFxProfile.Full) =>
                new ProfileBehavior(true, false, true, true, 1f, false),
            (SolreignFxCategory.ImpactLight or SolreignFxCategory.ImpactHeavy, SolreignFxProfile.ReducedMotion) =>
                new ProfileBehavior(true, false, true, false, 1f, false), // camera impulse forced off
            (SolreignFxCategory.ImpactLight or SolreignFxCategory.ImpactHeavy, SolreignFxProfile.LowVfx) =>
                new ProfileBehavior(true, false, true, false, 0.5f, false), // entity budget halved
            (SolreignFxCategory.ImpactLight or SolreignFxCategory.ImpactHeavy, SolreignFxProfile.CosmeticMinimal) =>
                new ProfileBehavior(true, false, true, false, 1f, false), // sprite -> single non-animated icon flash; light kept

            // --- electrical ---
            (SolreignFxCategory.Electrical, SolreignFxProfile.Full) =>
                new ProfileBehavior(true, true, true, false, 1f, false),
            (SolreignFxCategory.Electrical, SolreignFxProfile.ReducedMotion) =>
                new ProfileBehavior(true, false, true, false, 1f, false), // overlay distortion disabled (motion-coded)
            (SolreignFxCategory.Electrical, SolreignFxProfile.LowVfx) =>
                new ProfileBehavior(true, false, true, false, 0.5f, false), // overlay disabled, sprite budget halved
            (SolreignFxCategory.Electrical, SolreignFxProfile.CosmeticMinimal) =>
                new ProfileBehavior(false, false, true, false, 1f, false), // overlay+sprite dropped; light-only pulse + audio carries it

            // --- dust / smoke: pure cosmetic, may drop to zero at cosmetic_minimal ---
            (SolreignFxCategory.Dust or SolreignFxCategory.Smoke, SolreignFxProfile.Full) =>
                new ProfileBehavior(true, false, false, false, 1f, false),
            (SolreignFxCategory.Dust or SolreignFxCategory.Smoke, SolreignFxProfile.ReducedMotion) =>
                new ProfileBehavior(true, false, false, false, 1f, false), // neither is motion-heavy at the camera level
            (SolreignFxCategory.Dust or SolreignFxCategory.Smoke, SolreignFxProfile.LowVfx) =>
                new ProfileBehavior(true, false, false, false, 0.4f, false),
            (SolreignFxCategory.Dust or SolreignFxCategory.Smoke, SolreignFxProfile.CosmeticMinimal) =>
                new ProfileBehavior(false, false, false, false, 0f, false), // one static residue sprite OR dropped to zero; W2 takes the "dropped to zero" branch (no primitive/renderer exists yet to author the static variant)

            // --- cast_ring: gameplay-critical telegraph, never dropped ---
            (SolreignFxCategory.CastRing, SolreignFxProfile.Full) =>
                new ProfileBehavior(false, true, true, false, 1f, true),
            (SolreignFxCategory.CastRing, SolreignFxProfile.ReducedMotion) =>
                new ProfileBehavior(false, true, true, false, 1f, true), // animation frozen to slow linear fill, no pulsing/rotation
            (SolreignFxCategory.CastRing, SolreignFxProfile.LowVfx) =>
                new ProfileBehavior(false, true, true, false, 1f, true), // simplified single-color fill
            (SolreignFxCategory.CastRing, SolreignFxProfile.CosmeticMinimal) =>
                new ProfileBehavior(false, false, false, false, 1f, true), // ring -> static icon + countdown text above the caster

            // --- transformation: appearance-layer swap (outside the kill switch) always survives;
            //     this row only governs the extra burst-sprite/screen-sting cosmetic layer.
            (SolreignFxCategory.Transformation, SolreignFxProfile.Full) =>
                new ProfileBehavior(true, false, false, false, 1f, true),
            (SolreignFxCategory.Transformation, SolreignFxProfile.ReducedMotion) =>
                new ProfileBehavior(true, false, false, false, 1f, true), // screen sting disabled for the actor
            (SolreignFxCategory.Transformation, SolreignFxProfile.LowVfx) =>
                new ProfileBehavior(true, false, false, false, 1f, true), // burst sprite simplified
            (SolreignFxCategory.Transformation, SolreignFxProfile.CosmeticMinimal) =>
                new ProfileBehavior(false, false, false, false, 1f, true), // burst/sting dropped; appearance swap (outside this table) is what survives

            // --- stamina_break / body_shock_generic: gameplay-critical telegraph, never dropped ---
            (SolreignFxCategory.StaminaBreak or SolreignFxCategory.BodyShockGeneric, SolreignFxProfile.Full) =>
                new ProfileBehavior(true, false, true, false, 1f, true),
            (SolreignFxCategory.StaminaBreak or SolreignFxCategory.BodyShockGeneric, SolreignFxProfile.ReducedMotion) =>
                new ProfileBehavior(true, false, true, false, 1f, true), // it's a light, not camera motion
            (SolreignFxCategory.StaminaBreak or SolreignFxCategory.BodyShockGeneric, SolreignFxProfile.LowVfx) =>
                new ProfileBehavior(true, false, true, false, 1f, true),
            (SolreignFxCategory.StaminaBreak or SolreignFxCategory.BodyShockGeneric, SolreignFxProfile.CosmeticMinimal) =>
                new ProfileBehavior(false, false, true, false, 1f, true), // flicker (single pulse) + mandatory audio + status-icon HUD

            _ => new ProfileBehavior(false, false, false, false, 0f, false),
        };
    }
}
