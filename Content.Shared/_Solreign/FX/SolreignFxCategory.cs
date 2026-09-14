namespace Content.Shared._Solreign.FX;

/// <summary>
///     The 9 budget/pool categories W2 plumbs (spec §3's table has 8 primitive rows;
///     <c>body_shock_generic</c> is the 9th — the non-secret redacted-vocabulary cover id spec §2.2
///     introduces to solve grk #2B — and is given its own row here since it is a first-class wire
///     allowlist member with its own budget shape, not a re-use of <see cref="StaminaBreak"/>'s
///     literal enum value even though it shares that primitive's budget numbers per spec §2.2's own
///     framing ("body convulsion/shock... electrocution recovery, stamina collapse recoveries").
///
///     W3 has not shipped <c>effects.yml</c> yet, so there is no real per-<c>EffectId</c> "category"
///     field on a loaded prototype to read — <see cref="SolreignFxCategoryTable.TryResolveCategory"/>
///     is W2's own deterministic effect-id-to-category mapping, built directly off the sealed
///     <see cref="SolreignFxWireAllowlist"/> W1 shipped, so pool/budget/lease plumbing has something
///     concrete to key off before any real prototype exists to consult instead.
/// </summary>
public enum SolreignFxCategory : byte
{
    ImpactLight = 0,
    ImpactHeavy = 1,
    Electrical = 2,
    Dust = 3,
    Smoke = 4,
    CastRing = 5,

    /// <summary>0 pooled entities, 0 lights, n/a duration (spec §3: "rides GenericVisualizer, no extra entity... state, not timed"). Carried here only so lookups/tables stay total over every wire id — never leases anything.</summary>
    Transformation = 6,

    StaminaBreak = 7,
    BodyShockGeneric = 8,
}

/// <summary>
///     Per-category provisional engineering defaults (spec §3's table — explicitly "provisional,"
///     revalidated once the Lab's effect profiler exists) and the effect-id-to-category lookup W2's
///     pool/lease/budget/profile-policy code shares.
/// </summary>
public static class SolreignFxCategoryTable
{
    /// <summary>One row of spec §3's per-primitive budget table.</summary>
    public readonly record struct Defaults(
        int EntitiesPerCue,
        int ConcurrentCap,
        int LightsPerCue,
        float DurationCapSeconds,
        bool HasOverlayInstance);

    /// <summary>
    ///     Spec §3's table, verbatim. <see cref="SolreignFxCategory.Transformation"/> carries all-zero
    ///     defaults (n/a everywhere) since it never touches the pool/lease machinery at all.
    /// </summary>
    public static Defaults GetDefaults(SolreignFxCategory category) => category switch
    {
        SolreignFxCategory.ImpactLight => new Defaults(1, 24, 0, 0.4f, false),
        SolreignFxCategory.ImpactHeavy => new Defaults(1, 16, 1, 0.8f, false),
        SolreignFxCategory.Electrical => new Defaults(1, 12, 1, 1.2f, true),
        SolreignFxCategory.Dust => new Defaults(6, 60, 0, 3.0f, false),
        SolreignFxCategory.Smoke => new Defaults(3, 30, 0, 4.0f, false),
        SolreignFxCategory.CastRing => new Defaults(1, 8, 1, 2.5f, true),
        SolreignFxCategory.Transformation => new Defaults(0, 0, 0, 0f, false),
        SolreignFxCategory.StaminaBreak => new Defaults(1, 24, 1, 0.6f, false),
        SolreignFxCategory.BodyShockGeneric => new Defaults(1, 24, 1, 0.6f, false),
        _ => new Defaults(0, 0, 0, 0f, false),
    };

    /// <summary>
    ///     Deterministic effect-id → category mapping over the exact 10 ids in
    ///     <see cref="SolreignFxWireAllowlist.V1"/>. Total and exception-free: an unrecognized id
    ///     (which should never reach here — callers are expected to have already run it through
    ///     <see cref="SolreignFxWireAllowlist.IsAllowlisted"/>) returns <c>false</c> rather than
    ///     throwing or guessing.
    /// </summary>
    public static bool TryResolveCategory(string? effectId, out SolreignFxCategory category)
    {
        switch (effectId)
        {
            case "impact_light":
                category = SolreignFxCategory.ImpactLight;
                return true;
            case "impact_heavy":
                category = SolreignFxCategory.ImpactHeavy;
                return true;
            case "electrical":
                category = SolreignFxCategory.Electrical;
                return true;
            case "dust":
                category = SolreignFxCategory.Dust;
                return true;
            case "smoke":
                category = SolreignFxCategory.Smoke;
                return true;
            case "cast_ring":
                category = SolreignFxCategory.CastRing;
                return true;
            case "transformation":
            case "transformation_generic":
                category = SolreignFxCategory.Transformation;
                return true;
            case "stamina_break":
                category = SolreignFxCategory.StaminaBreak;
                return true;
            case "body_shock_generic":
                category = SolreignFxCategory.BodyShockGeneric;
                return true;
            default:
                category = default;
                return false;
        }
    }
}
