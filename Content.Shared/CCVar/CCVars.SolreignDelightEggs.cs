using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
///     CVars for the delight-eggs batch (feat/delight-eggs): small reactive surprises layered onto
///     existing easter-egg items and the Providence voice, gated separately from
///     <c>CCVars.Solreign.cs</c> and <c>CCVars.SolreignProvidenceCommiseration.cs</c> per the fleet
///     workboard's collision-control guidance (own partial file per feature area rather than
///     appending to a hotspot everyone else edits). Every CVar here defaults to preserving the
///     existing house rule that a rare beat should read as occasional, not scripted.
/// </summary>
public sealed partial class CCVars
{
    /// <summary>
    ///     Kill switch for the wrist organizer's per-round use-count phrasing drift (layered on top of
    ///     the existing <c>Content.Server._Solreign.EasterEggs.SolreignWristOrganizerSystem</c> readout).
    ///     Default on. Off restores the original single-phrasing readout regardless of how many times
    ///     the same unit has been used this round.
    /// </summary>
    public static readonly CVarDef<bool> SolreignDelightWristDriftEnabled =
        CVarDef.Create("solreign.delight.wrist_drift_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Kill switch for the static receiver's proximity payoff
    ///     (<c>Content.Server._Solreign.EasterEggs.SolreignStaticReceiverSystem</c>): a use-in-hand,
    ///     60s-cooldown scan whose signal is deliberately ambiguous (several supernatural-anchor
    ///     source types, not just antags), probabilistic (misses near real sources, false-positives
    ///     with nothing around), and per-window seeded so it can't be re-rolled by spamming — see
    ///     <c>SolreignStaticReceiverRules</c> for the round-integrity model (orchestrator review,
    ///     2026-07-16). Default TRUE (the ambiguity+noise is what makes default-on safe).
    ///     Off makes the item inert on use (no popup at all) rather than silently lying —
    ///     same "no non-sequitur" reasoning as <c>ProvidenceCommiserationSystem</c>'s doc comment.
    /// </summary>
    public static readonly CVarDef<bool> SolreignDelightStaticReceiverEnabled =
        CVarDef.Create("solreign.delight.static_receiver_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Kill switch for Providence's "second condolence" — a rare, delayed follow-up line
    ///     (<c>Content.Server._Solreign.Providence.ProvidenceSecondCondolenceSystem</c>) that can only
    ///     ever be scheduled for a player who already received the base death-commiseration line this
    ///     round. Default on. Independent of both <see cref="SolreignProvidenceEnabled"/> and
    ///     <see cref="SolreignProvidenceCommiserationEnabled"/>, but the system no-ops whenever either of
    ///     those is off.
    /// </summary>
    public static readonly CVarDef<bool> SolreignProvidenceSecondCondolenceEnabled =
        CVarDef.Create("solreign.providence.second_condolence_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Probability (0-1) that a player's first commiseration this round gets a second-condolence
    ///     follow-up scheduled at all. Conservative default (30%, stricter than the base
    ///     commiseration's 50%) — a follow-up should be noticeably rarer than the beat that unlocked it,
    ///     so most players who see the first line never see a second. Clamped defensively at the call
    ///     site, same idiom as <see cref="CCVars.SolreignProvidenceCommiserationChance"/>.
    /// </summary>
    public static readonly CVarDef<float> SolreignProvidenceSecondCondolenceChance =
        CVarDef.Create("solreign.providence.second_condolence_chance", 0.3f, CVar.SERVERONLY);
}
