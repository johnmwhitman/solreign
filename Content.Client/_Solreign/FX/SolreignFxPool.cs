using System;
using System.Collections.Generic;
using Content.Shared._Solreign.FX;

namespace Content.Client._Solreign.FX;

/// <summary>
///     The one genuinely new piece of client infrastructure spec §2.1 calls for: a fixed-capacity,
///     per-category slot table for client-only cosmetic FX activations. Deliberately pure and
///     engine-free (no Robust entity spawning, no IoC) — same "math half only" split as
///     <c>MovementBobMath</c>/<c>MovementBobSystem</c> and W1's own <c>SolreignFxProfileGate</c>, so
///     the spec §7 "Unit — budget pool" row ("pure logic, no client/server harness needed") is
///     literally true. A future consumer (W3, once real cosmetic sprite prototypes exist) owns the
///     actual <c>Spawn(SolreignFxCosmeticSpritePrototype, MapCoordinates.Nullspace)</c>/park/
///     reposition mechanics per slot index this type hands back — this type only tracks WHICH slot
///     index is doing WHAT, never touches an <c>EntityUid</c> itself.
///
///     Per-category behavior (spec §3): capacity is a hard ceiling; a same-(EffectId, anchor) cue on
///     an already-active slot MERGES (extends/refreshes) that slot rather than allocating a new one;
///     a cue that doesn't match any active slot and finds the category full RECYCLES the
///     least-recently-activated slot in that SAME category (never evicts another category — the
///     dictionary keying makes cross-category eviction structurally impossible, cdx #16).
/// </summary>
public sealed class SolreignFxPool
{
    /// <summary>
    ///     Anchor identity for merge-by-(EffectId, anchor) lives in
    ///     <see cref="Content.Shared._Solreign.FX.SolreignFxAnchorKey"/> — shared with the server's
    ///     egress-budget coalescing (spec §3.1), which needs the exact same "same anchor" notion but
    ///     cannot reference a <c>Content.Client</c> type. See that type's remarks for the full
    ///     rationale; this class just consumes it.
    /// </summary>
    public enum ActivationKind
    {
        /// <summary>A previously-idle slot was claimed.</summary>
        Fresh,

        /// <summary>An already-active slot with the same (EffectId, anchor) was extended/refreshed.</summary>
        Merged,

        /// <summary>The category was at capacity; the oldest active slot (by activation time) was recycled.</summary>
        Recycled,
    }

    private sealed class Slot
    {
        public bool Active;
        public string EffectId = string.Empty;
        public SolreignFxAnchorKey Anchor;
        public double ActivatedAtSeconds;
    }

    private readonly Dictionary<SolreignFxCategory, Slot[]> _slots = new();

    /// <summary>
    ///     Builds a pool with the given per-category capacities. A negative capacity clamps to 0
    ///     (a fully-disabled category, e.g. <see cref="SolreignFxCategory.Transformation"/>, which
    ///     never touches the pool at all per spec §3's "0 pooled entities" row) rather than throwing —
    ///     this constructor runs off CVar-derived values (spec §1.5: pool-cap CVars are clamped, but
    ///     never trusted blindly by their own consumer either).
    /// </summary>
    public SolreignFxPool(IReadOnlyDictionary<SolreignFxCategory, int> capacities)
    {
        foreach (var (category, rawCapacity) in capacities)
        {
            var capacity = Math.Max(0, rawCapacity);
            var slots = new Slot[capacity];
            for (var i = 0; i < capacity; i++)
                slots[i] = new Slot();

            _slots[category] = slots;
        }
    }

    public int Capacity(SolreignFxCategory category) => _slots.TryGetValue(category, out var slots) ? slots.Length : 0;

    public int ActiveCount(SolreignFxCategory category)
    {
        if (!_slots.TryGetValue(category, out var slots))
            return 0;

        var count = 0;
        foreach (var slot in slots)
        {
            if (slot.Active)
                count++;
        }

        return count;
    }

    /// <summary>
    ///     Rebuilds a category's slot array to <paramref name="newCapacity"/> (e.g. on a pool-cap-scale
    ///     CVar change). Slots beyond the new capacity are dropped outright — the caller (the lease
    ///     manager) is responsible for releasing whatever client-side resource those dropped slots
    ///     were tracking; this method only reshapes the table, it never itself calls back out.
    /// </summary>
    public void Resize(SolreignFxCategory category, int newCapacity)
    {
        var capacity = Math.Max(0, newCapacity);
        var existing = _slots.TryGetValue(category, out var slots) ? slots : Array.Empty<Slot>();
        var next = new Slot[capacity];

        for (var i = 0; i < capacity; i++)
            next[i] = i < existing.Length ? existing[i] : new Slot();

        _slots[category] = next;
    }

    /// <summary>
    ///     Peek-only check: does an ACTIVE slot in <paramref name="category"/> already carry this
    ///     exact (<paramref name="effectId"/>, <paramref name="anchor"/>) pair? Never mutates —
    ///     callers that need to know "would this be a merge" before deciding whether to spend a
    ///     budget/intake token (e.g. <c>SolreignFxLeaseManager</c>) use this instead of
    ///     <see cref="TryActivate"/> itself.
    /// </summary>
    public bool HasActiveMatch(SolreignFxCategory category, string effectId, SolreignFxAnchorKey anchor)
    {
        if (!_slots.TryGetValue(category, out var slots))
            return false;

        foreach (var slot in slots)
        {
            if (slot.Active && slot.EffectId == effectId && slot.Anchor.Equals(anchor))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Attempts to activate a slot for (<paramref name="category"/>, <paramref name="effectId"/>,
    ///     <paramref name="anchor"/>). Never fails outright for a nonzero-capacity category — merge,
    ///     fresh-claim, and recycle between them cover every case; only a zero-capacity category (or
    ///     an unregistered one) returns <c>false</c>.
    /// </summary>
    public bool TryActivate(
        SolreignFxCategory category,
        string effectId,
        SolreignFxAnchorKey anchor,
        double nowSeconds,
        out int slotIndex,
        out ActivationKind kind)
    {
        slotIndex = -1;
        kind = default;

        if (!_slots.TryGetValue(category, out var slots) || slots.Length == 0)
            return false;

        // 1. Merge: an active slot already carries this exact (EffectId, anchor) pair.
        for (var i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot.Active && slot.EffectId == effectId && slot.Anchor.Equals(anchor))
            {
                slot.ActivatedAtSeconds = nowSeconds;
                slotIndex = i;
                kind = ActivationKind.Merged;
                return true;
            }
        }

        // 2. Fresh: an idle slot exists.
        for (var i = 0; i < slots.Length; i++)
        {
            if (!slots[i].Active)
            {
                Activate(slots[i], effectId, anchor, nowSeconds);
                slotIndex = i;
                kind = ActivationKind.Fresh;
                return true;
            }
        }

        // 3. Recycle: category is full — reclaim the least-recently-activated slot in THIS category
        //    only (cdx #16: caps are per-category, so cosmetic spam can never evict another
        //    category's active cue by construction — there is no cross-category slot array to reach
        //    into in the first place).
        var oldestIndex = 0;
        var oldestTime = slots[0].ActivatedAtSeconds;
        for (var i = 1; i < slots.Length; i++)
        {
            if (slots[i].ActivatedAtSeconds < oldestTime)
            {
                oldestTime = slots[i].ActivatedAtSeconds;
                oldestIndex = i;
            }
        }

        Activate(slots[oldestIndex], effectId, anchor, nowSeconds);
        slotIndex = oldestIndex;
        kind = ActivationKind.Recycled;
        return true;
    }

    /// <summary>Marks a slot idle again (its duration elapsed, or its anchor became invalid mid-effect).</summary>
    public void Release(SolreignFxCategory category, int slotIndex)
    {
        if (!_slots.TryGetValue(category, out var slots) || slotIndex < 0 || slotIndex >= slots.Length)
            return;

        var slot = slots[slotIndex];
        slot.Active = false;
        slot.EffectId = string.Empty;
        slot.Anchor = default;
    }

    private static void Activate(Slot slot, string effectId, SolreignFxAnchorKey anchor, double nowSeconds)
    {
        slot.Active = true;
        slot.EffectId = effectId;
        slot.Anchor = anchor;
        slot.ActivatedAtSeconds = nowSeconds;
    }
}
