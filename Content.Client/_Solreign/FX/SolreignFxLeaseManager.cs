using System;
using System.Collections.Generic;
using Content.Shared._Solreign.FX;

namespace Content.Client._Solreign.FX;

/// <summary>
///     The unified atomic client-side lease spec §3.1 calls for (grk #1 / cdx #9-#10, the top DoS
///     findings from both external reviewers): one lease per accepted cue covering EVERY resource
///     class it will touch — pooled sprite slot, lights, overlay instance, audio plays, timers — as
///     a single all-or-nothing acquisition, including zero-sprite primitives
///     (<see cref="SolreignFxCategory.StaminaBreak"/>) whose light/audio a sprite-only cap would
///     miss entirely (cdx #9). <see cref="SolreignFxPool"/> is the authoritative per-category
///     occupancy/recycle/merge tracker this type composes over — a granted lease's slot represents
///     the WHOLE cue activation (sprite + light + overlay + audio + timer bundled under one cap),
///     not a sprite-only reservation; which of those sub-resources a slot conceptually carries is
///     read from <see cref="SolreignFxCategoryTable.GetDefaults"/> metadata for the caller (W3/W4's
///     real renderer) to act on — W2 ships the cap/atomicity plumbing, not the renderer.
///
///     Also owns the client per-frame intake cap (spec §3.1 default: 32 cue-activations/frame,
///     cdx #10): bounds recycle-churn CPU regardless of what a hostile or buggy server sends, by
///     capping how many BRAND-NEW (fresh-claim or recycle) activations may happen in one frame.
///     A cue that MERGES into an already-active slot for the same (EffectId, anchor) is never
///     throttled by the intake cap — extending an existing effect costs no new churn, matching
///     spec §3's own "cues merge... rather than being dropped or crashing the pool" framing.
/// </summary>
public sealed class SolreignFxLeaseManager
{
    /// <summary>An acquired, atomic bundle of resources for one accepted cue activation.</summary>
    public readonly record struct Lease(
        SolreignFxCategory Category,
        int SlotIndex,
        SolreignFxPool.ActivationKind Kind,
        int EntitiesGranted,
        int LightsGranted,
        bool OverlayGranted);

    /// <summary>Why <see cref="TryAcquire"/> failed — never a per-cue log line, only an aggregate counter (spec §1.3b's "rate-limited, aggregated diagnostics" rule applies here too).</summary>
    public enum AcquireFailureReason
    {
        None = 0,
        IntakeCapExceeded,
        CategoryHasNoCapacity,
    }

    private readonly SolreignFxPool _pool;
    private int _intakeUsedThisFrame;

    /// <summary>Default 32 (spec §3.1); settable so the owning system can wire it to the <c>solreign.fx.client_intake_per_frame</c> CVar.</summary>
    public int IntakeCapPerFrame { get; set; } = 32;

    public SolreignFxLeaseManager(SolreignFxPool pool)
    {
        _pool = pool;
    }

    /// <summary>Call once per client frame before processing that frame's received cues — resets the intake counter to zero.</summary>
    public void BeginFrame()
    {
        _intakeUsedThisFrame = 0;
    }

    /// <summary>
    ///     Attempts to atomically acquire everything one cue activation needs. Either returns a
    ///     complete <see cref="Lease"/> or fails outright with a reason — never a partial grant
    ///     (spec §3.1's "atomic lease... no lease → coalesce-or-drop, never partial setup").
    /// </summary>
    public bool TryAcquire(
        SolreignFxCategory category,
        string effectId,
        SolreignFxAnchorKey anchor,
        double nowSeconds,
        out Lease lease,
        out AcquireFailureReason failureReason)
    {
        lease = default;

        if (_pool.Capacity(category) == 0)
        {
            failureReason = AcquireFailureReason.CategoryHasNoCapacity;
            return false;
        }

        // Merging into an existing activation for the same (EffectId, anchor) never costs intake
        // budget — it's an extension of an already-accounted-for activation, not new churn.
        var isMerge = _pool.HasActiveMatch(category, effectId, anchor);

        if (!isMerge && _intakeUsedThisFrame >= IntakeCapPerFrame)
        {
            failureReason = AcquireFailureReason.IntakeCapExceeded;
            return false;
        }

        if (!_pool.TryActivate(category, effectId, anchor, nowSeconds, out var slotIndex, out var kind))
        {
            // Capacity was nonzero (checked above) so this should be unreachable, but TryAcquire's
            // contract is "never partial" — fail closed rather than assume.
            failureReason = AcquireFailureReason.CategoryHasNoCapacity;
            return false;
        }

        if (!isMerge)
            _intakeUsedThisFrame++;

        var defaults = SolreignFxCategoryTable.GetDefaults(category);
        lease = new Lease(
            category,
            slotIndex,
            kind,
            defaults.EntitiesPerCue,
            defaults.LightsPerCue,
            defaults.HasOverlayInstance);
        failureReason = AcquireFailureReason.None;
        return true;
    }

    /// <summary>Releases a previously-acquired lease's slot (its duration elapsed, or its anchor became invalid mid-effect per spec §1.3b.4).</summary>
    public void Release(Lease lease)
    {
        _pool.Release(lease.Category, lease.SlotIndex);
    }

    /// <summary>Test/diagnostics visibility only — how many fresh/recycled (non-merge) activations this frame has consumed so far.</summary>
    internal int IntakeUsedThisFrameForTests => _intakeUsedThisFrame;
}
