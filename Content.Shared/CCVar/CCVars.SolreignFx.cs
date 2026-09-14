using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

// FX Language v1 (docs/specs/FX-LANGUAGE-V1-SPEC-2026-07-16.md).
// Own partial file per Solreign convention so parallel worktree waves don't collide.
public sealed partial class CCVars
{
    /// <summary>
    ///     Master kill switch for FX Language v1's <c>SolreignFxCueV1</c> wire contract (spec §1,
    ///     §9). Enabled 2026-07-25 (activation pass) — every W1-W5 worktree's code is inert until this flips.
    ///     Same replicated-server-owned shape as <see cref="SolreignMovementBobEnabled"/>: flipping it
    ///     live applies to every connected client, and flipping it back to <c>false</c> is the
    ///     universal rollback for the whole FX language at once, independent of any per-worktree
    ///     revert (spec §9).
    /// </summary>
    public static readonly CVarDef<bool> SolreignFxCueV1Enabled =
        CVarDef.Create("solreign.fx.cue_v1", true, CVar.REPLICATED | CVar.SERVER);

    /// <summary>
    ///     Client-local accessibility profile selection (spec §4): one of <c>full</c> |
    ///     <c>reduced_motion</c> | <c>low_vfx</c> | <c>cosmetic_minimal</c>. An unrecognized value
    ///     is treated as <c>full</c> by <c>SolreignFxProfileGate.ParseProfile</c> — never throws.
    ///     Composes with, but never overrides, the engine's own <c>CCVars.ReducedMotion</c>
    ///     (<c>SolreignFxProfileGate.EffectiveProfile</c>).
    /// </summary>
    public static readonly CVarDef<string> SolreignFxProfile =
        CVarDef.Create("solreign.fx.profile", "full", CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Dedicated photosensitivity toggle (spec §4.0, grk #4C): zeroes ALL light-pulse/flicker
    ///     behavior for FX cues independently of <see cref="SolreignFxProfile"/> — every profile,
    ///     including <c>full</c>, respects this switch.
    /// </summary>
    public static readonly CVarDef<bool> SolreignFxNoFlash =
        CVarDef.Create("solreign.fx.no_flash", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    // --- W2 additions: egress budget (spec §3.1, grk #1/cdx #9-#10) ---

    /// <summary>
    ///     Server-side global per-tick cue cap (spec §3.1 default: 64/tick), enforced by
    ///     <c>SolreignFxEgressBudget</c> before any per-category token bucket is even consulted.
    ///     Clamped to a hardcoded ceiling at read time (spec §1.5: "Pool-cap CVars are likewise
    ///     clamped to hardcoded ceilings") — no CVar edit can turn this into an unbounded flood gate.
    /// </summary>
    public static readonly CVarDef<int> SolreignFxEgressTickCap =
        CVarDef.Create("solreign.fx.egress_tick_cap", 64, CVar.SERVER);

    /// <summary>
    ///     Per-category token-bucket refill multiplier (spec §3.1 default: "2x the category's
    ///     concurrent cap per second"). Burst capacity is always exactly the category's concurrent
    ///     cap (spec, non-configurable — burst above the cap would defeat the cap's own purpose).
    /// </summary>
    public static readonly CVarDef<float> SolreignFxEgressRefillMultiplier =
        CVarDef.Create("solreign.fx.egress_refill_multiplier", 2f, CVar.SERVER);

    /// <summary>
    ///     Client-side per-frame intake cap (spec §3.1 default: 32 cue-activations/frame) —
    ///     <c>SolreignFxLeaseManager</c>'s bound on recycle-churn CPU regardless of what a hostile or
    ///     buggy server sends (cdx #10). Excess cues this frame are coalesced-or-dropped by category,
    ///     never processed.
    /// </summary>
    public static readonly CVarDef<int> SolreignFxClientIntakePerFrame =
        CVarDef.Create("solreign.fx.client_intake_per_frame", 32, CVar.CLIENTONLY);

    /// <summary>
    ///     A single global scale applied to every <see cref="SolreignFxCategoryTable"/> concurrent
    ///     cap (spec §3: "Each is a CVar default so it is a canary/tuning knob, not a hardcoded
    ///     constant"). W2 ships one governing knob rather than ~9 categories × several per-category
    ///     CVars each — the per-category NUMBERS themselves are still the spec §3 table's provisional
    ///     engineering defaults (revalidation requirement per spec §3's own caveat); this CVar is the
    ///     coarse admin dial for "everything's too much/too little" without a CVar-sprawl per
    ///     primitive. Clamped to <c>[0.1, 4.0]</c> — never zero (which would silently disable every
    ///     category with no signal) and never unboundedly large.
    /// </summary>
    public static readonly CVarDef<float> SolreignFxPoolCapScale =
        CVarDef.Create("solreign.fx.pool_cap_scale", 1f, CVar.SERVER | CVar.REPLICATED);
}
