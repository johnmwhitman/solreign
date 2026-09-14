using System;
using System.Collections.Generic;
using Content.Shared._Solreign.FX;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.FX;

/// <summary>
///     The server-side egress budget spec §3.1 calls for (grk #1 / cdx #10, both reviewers' top DoS
///     finding): concurrency caps bound LIVE objects, not RATE — a buggy explosion-loop caller could
///     otherwise keep only a handful of client sprites alive while still paying full
///     serialize/fanout/deserialize cost on every single raise. Two independent gates, checked in
///     order inside <c>SolreignFxServerSystem</c>'s raise helpers, after <c>TryCreate</c> would
///     otherwise succeed:
///
///     1. A per-tick GLOBAL cap (spec default: 64/tick) — the coarse "nothing floods the whole FX
///        subsystem in one tick" backstop.
///     2. A per-category <see cref="SolreignFxTokenBucket"/> (spec default: refill = 2x the
///        category's concurrent cap/second, burst = the concurrent cap) — bounds sustained rate per
///        primitive family.
///
///     Within one tick, a repeat (category, EffectId, anchor) triple COALESCES: the first raise that
///     tick consumes budget normally, every subsequent identical triple that same tick is treated as
///     already-covered (spec §3.1: "the cue is coalesced by (EffectId, anchor) within the tick if
///     possible, otherwise dropped with an aggregate metric") — the caller does not raise a second
///     network event for it, but the call still reports success (the FIRST raise already carries the
///     up-to-date state for that anchor).
///
///     [grk review H-A/H-B fix] <see cref="TryConsumePair"/> exists because the naive approach of
///     calling <see cref="TryConsume"/> twice in a row (once for the detail half, once for the
///     generic half of a secret-role emission) has a real atomicity gap: if the FIRST call succeeds
///     (spending a real token and marking its key coalesced) and the SECOND then fails, the caller
///     must report overall failure — but the first key is now incorrectly marked "already covered"
///     for the rest of the tick, so a later identical call that tick would wrongly report success
///     via <see cref="ConsumeResult.CoalescedWithinTick"/> even though NEITHER half of THIS attempt
///     was ever raised. <see cref="TryConsumePair"/> peeks both categories' availability BEFORE
///     committing either, so a failure on either leaves NEITHER token spent and NEITHER key marked.
/// </summary>
public sealed class SolreignFxEgressBudget
{
    public enum ConsumeResult
    {
        /// <summary>Budget consumed; the caller should proceed to raise a real network event.</summary>
        Consumed,

        /// <summary>An identical (category, EffectId, anchor) triple already went out THIS tick — treat as already-covered, do not raise again.</summary>
        CoalescedWithinTick,

        /// <summary>The per-tick global cap is exhausted.</summary>
        DroppedGlobalCap,

        /// <summary>The category's own token bucket is exhausted.</summary>
        DroppedCategoryBudget,
    }

    /// <summary>
    ///     Spec §3's table marks <see cref="SolreignFxCategory.Transformation"/>'s concurrent cap
    ///     "n/a" (it never touches the client pool at all — §2.1). That is NOT the same axis as
    ///     "how many transformation cues may be RAISED per second" — <c>RaiseSecretRoleCue</c> must
    ///     still be able to get a changeling's transformation cue pair over the wire under normal
    ///     play. Rather than silently reusing a pool concept the category structurally has none of
    ///     (which would make its egress bucket permanently empty — burst 0, refill 0 — and drop
    ///     EVERY transformation cue outright), any category whose pool concurrent cap is 0 gets this
    ///     nominal egress-only burst instead. Ability-cooldown-gated primitives (transformation is
    ///     exactly this shape) don't need a large number here; 8 is the same order of magnitude as
    ///     <see cref="SolreignFxCategory.CastRing"/>'s own concurrent cap, the closest analogous
    ///     "inherently self-limiting via cooldowns" category spec §3 names. This is an EGRESS-ONLY
    ///     burst — it has no bearing on client pool capacity, which stays 0 for these categories.
    /// </summary>
    private const int MinimumEgressBurstForZeroCapCategories = 8;

    private Dictionary<SolreignFxCategory, SolreignFxTokenBucket> _buckets = new();
    private readonly HashSet<(SolreignFxCategory Category, string EffectId, SolreignFxAnchorKey Anchor)> _coalescedThisTick = new();
    private GameTick? _currentTick;
    private int _globalUsedThisTick;

    /// <summary>Spec default 64/tick; settable so the owning system can wire it to <c>solreign.fx.egress_tick_cap</c>.</summary>
    public int GlobalTickCap { get; set; } = 64;

    /// <summary>Spec default 2.0 (refill = 2x the category's concurrent cap/second); settable so the owning system can wire it to <c>solreign.fx.egress_refill_multiplier</c>.</summary>
    public float RefillMultiplier { get; set; } = 2f;

    /// <summary>
    ///     [grk review M-C fix] Drops every existing bucket so the NEXT consume for each category
    ///     rebuilds it off the current <see cref="RefillMultiplier"/>. Call this whenever
    ///     <see cref="RefillMultiplier"/> changes — a bucket already constructed with the OLD
    ///     multiplier baked its refill rate in at construction time and would otherwise never pick
    ///     up a live CVar change. Safe: a freshly (re)built bucket starts topped up to full burst
    ///     (<c>SolreignFxTokenBucket</c>'s own constructor), so this never punishes anyone mid-round.
    /// </summary>
    public void ResetBuckets()
    {
        _buckets = new Dictionary<SolreignFxCategory, SolreignFxTokenBucket>();
    }

    private void AdvanceTickIfNeeded(GameTick tick)
    {
        if (_currentTick == tick)
            return;

        _currentTick = tick;
        _globalUsedThisTick = 0;
        _coalescedThisTick.Clear();
    }

    private SolreignFxTokenBucket GetOrCreateBucket(SolreignFxCategory category, double nowSeconds)
    {
        if (_buckets.TryGetValue(category, out var bucket))
            return bucket;

        var defaults = SolreignFxCategoryTable.GetDefaults(category);
        var burst = defaults.ConcurrentCap > 0 ? defaults.ConcurrentCap : MinimumEgressBurstForZeroCapCategories;
        return new SolreignFxTokenBucket(burst * Math.Max(0f, RefillMultiplier), burst, nowSeconds);
    }

    /// <summary>Peek-only: refills (if due) and reports whether ≥<paramref name="cost"/> tokens are currently available, WITHOUT consuming any. Persists the refill state either way (refilling is idempotent/non-destructive).</summary>
    private bool PeekBucketAvailable(SolreignFxCategory category, double nowSeconds, float cost = 1f)
    {
        var bucket = GetOrCreateBucket(category, nowSeconds);
        var available = bucket.Available(nowSeconds) >= cost;
        _buckets[category] = bucket;
        return available;
    }

    /// <summary>Actually consumes <paramref name="cost"/> tokens from a category bucket already confirmed available. Never call this without a prior successful <see cref="PeekBucketAvailable"/> for the SAME cost in the same atomic decision.</summary>
    private void ConsumeBucket(SolreignFxCategory category, double nowSeconds, float cost = 1f)
    {
        var bucket = GetOrCreateBucket(category, nowSeconds);
        bucket.TryConsume(nowSeconds, cost);
        _buckets[category] = bucket;
    }

    /// <summary>
    ///     Attempts to atomically consume this tick/category's budget for one (EffectId, anchor)
    ///     raise. Advances the tick window (resetting the global counter and the coalescing set)
    ///     whenever <paramref name="tick"/> differs from the last call's tick.
    /// </summary>
    public ConsumeResult TryConsume(SolreignFxCategory category, string effectId, SolreignFxAnchorKey anchor, GameTick tick, double nowSeconds)
    {
        AdvanceTickIfNeeded(tick);

        var key = (category, effectId, anchor);
        if (_coalescedThisTick.Contains(key))
            return ConsumeResult.CoalescedWithinTick;

        if (_globalUsedThisTick >= Math.Max(0, GlobalTickCap))
            return ConsumeResult.DroppedGlobalCap;

        if (!PeekBucketAvailable(category, nowSeconds))
            return ConsumeResult.DroppedCategoryBudget;

        ConsumeBucket(category, nowSeconds);
        _globalUsedThisTick++;
        _coalescedThisTick.Add(key);
        return ConsumeResult.Consumed;
    }

    /// <summary>
    ///     [grk review H-A/H-B fix] Atomic paired consumption for <c>RaiseSecretRoleCue</c>'s
    ///     generic+detail emission: peeks (never consumes) BOTH categories' bucket availability and
    ///     the 2-unit global-tick cost BEFORE committing either — a failure on either side leaves
    ///     NEITHER bucket touched and NEITHER key marked coalesced, so a later same-tick retry is
    ///     never falsely told "already covered" for an attempt that never actually went out.
    ///     Coalescing is keyed off <paramref name="effectIdA"/> (the DETAIL half) — a repeat trigger
    ///     on the same detail (id, anchor) this tick means the whole pair already went out.
    ///
    ///     [grk review round-2 finding M-new-1] <paramref name="categoryA"/> and
    ///     <paramref name="categoryB"/> can be the SAME category (e.g. <c>transformation</c> and
    ///     <c>transformation_generic</c> both map to <see cref="SolreignFxCategory.Transformation"/>).
    ///     Peeking the same bucket twice independently for "≥1 available" is wrong when it holds
    ///     exactly 1 token: both peeks would individually pass, but only one unit actually exists —
    ///     the second commit would then silently under-consume (or, with a naive two-cost-1 commit,
    ///     over-spend by one). When both categories match, this checks/consumes a single COST-2
    ///     unit against that one bucket instead of two independent cost-1 units.
    /// </summary>
    public ConsumeResult TryConsumePair(
        SolreignFxCategory categoryA, string effectIdA,
        SolreignFxCategory categoryB, string effectIdB,
        SolreignFxAnchorKey anchor, GameTick tick, double nowSeconds)
    {
        AdvanceTickIfNeeded(tick);

        var keyA = (categoryA, effectIdA, anchor);
        if (_coalescedThisTick.Contains(keyA))
            return ConsumeResult.CoalescedWithinTick;

        if (_globalUsedThisTick + 2 > Math.Max(0, GlobalTickCap))
            return ConsumeResult.DroppedGlobalCap;

        var sameCategory = categoryA == categoryB;

        var bucketsAvailable = sameCategory
            ? PeekBucketAvailable(categoryA, nowSeconds, cost: 2f)
            : PeekBucketAvailable(categoryA, nowSeconds) && PeekBucketAvailable(categoryB, nowSeconds);

        if (!bucketsAvailable)
            return ConsumeResult.DroppedCategoryBudget;

        // Confirmed available — NOW actually commit. Neither bucket nor the coalesce set was
        // touched by either peek above, so this section cannot itself partially fail.
        if (sameCategory)
            ConsumeBucket(categoryA, nowSeconds, cost: 2f);
        else
        {
            ConsumeBucket(categoryA, nowSeconds);
            ConsumeBucket(categoryB, nowSeconds);
        }

        _globalUsedThisTick += 2;
        _coalescedThisTick.Add(keyA);
        _coalescedThisTick.Add((categoryB, effectIdB, anchor));
        return ConsumeResult.Consumed;
    }

    /// <summary>Test visibility only.</summary>
    internal int GlobalUsedThisTickForTests => _globalUsedThisTick;
}
