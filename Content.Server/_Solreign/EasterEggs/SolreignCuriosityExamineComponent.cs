namespace Content.Server._Solreign.EasterEggs;

/// <summary>
///     Delight-eggs batch (feat/delight-eggs): a reusable "keep looking at it" examine escalation.
///     Attach to any entity that should reward repeated curiosity rather than a one-time find — the
///     text deepens across an examiner's own examine count, with an optional rare aside at the top
///     tier. Data-driven per prototype (see <see cref="TierLocKeys"/>/<see cref="TierThresholds"/>) so
///     the same mechanism can back several unrelated items with entirely different flavor, the same
///     way <c>SolreignWristOrganizerComponent</c> and <c>SolreignStaticReceiverComponent</c> are each a
///     thin data shell over one shared piece of server logic.
///
///     Server-only: the whole effect is examine markup text through the existing (server-authoritative)
///     examine pipeline, same reasoning as <c>SolreignWristOrganizerComponent</c>'s doc comment — no
///     networking of its own is needed. Pure flavor text with zero gameplay effect and no broadcast, so
///     unlike the other delight-eggs additions this one intentionally has no CVar — there is nothing for
///     an operator to need to kill.
/// </summary>
[RegisterComponent, Access(typeof(SolreignCuriosityExamineSystem))]
public sealed partial class SolreignCuriosityExamineComponent : Component
{
    /// <summary>
    ///     Ordered loc keys, one per tier, shown once an examiner's own examine count reaches the
    ///     matching entry in <see cref="TierThresholds"/>. Must be the same length as
    ///     <see cref="TierThresholds"/> — a length mismatch makes the system stop at the shorter list
    ///     rather than throw (YAML misconfiguration should degrade, not crash).
    /// </summary>
    [DataField]
    public List<string> TierLocKeys = new();

    /// <summary>
    ///     Ascending examine-count thresholds unlocking each entry in <see cref="TierLocKeys"/> at the
    ///     same index. E.g. <c>[1, 3, 6]</c> means tier 0 shows on the 1st examine, tier 1 on the 3rd,
    ///     tier 2 on the 6th and every examine after.
    /// </summary>
    [DataField]
    public List<int> TierThresholds = new();

    /// <summary>
    ///     Optional extra loc key that can additionally appear (on top of the top tier's own line) once
    ///     the examiner has reached the top tier — a rare, one-line PROVIDENCE-flavored aside rather
    ///     than a new tier of its own. Null disables this entirely.
    /// </summary>
    [DataField]
    public string? RareAsideLocKey;

    /// <summary>Per-examine chance (0-1) of showing <see cref="RareAsideLocKey"/> once eligible.</summary>
    [DataField]
    public float RareAsideChance = 0.15f;

    /// <summary>
    ///     Per-examiner examine counts, keyed by the examining entity. Server-side scheduling state
    ///     only — not saved, not networked, same as <c>SolreignWristOrganizerComponent.NextReadoutTime</c>.
    ///     Naturally resets across rounds because these are per-round map/spawn entities; no explicit
    ///     round-reset hook needed (same reasoning already relied on for the wrist organizer's cooldown).
    /// </summary>
    [ViewVariables]
    public Dictionary<EntityUid, int> ExamineCounts = new();
}
