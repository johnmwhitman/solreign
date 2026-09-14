namespace Content.Server._Solreign.Contracts;

/// <summary>
///     Rank-gates a UI-activatable entity (spec M4, "Executive Vendor"): additive analogue of the upstream
///     <c>ActivatableUIRequiresAccessComponent</c>/<c>ActivatableUIRequiresAccessSystem</c>
///     (<c>Content.Shared/Access/Systems</c>), but gating on Solreign career rank
///     (<see cref="Content.Shared._Solreign.SeasonLedger.SeasonTitleComponent.RankIndex"/>) instead of an
///     ID-card access tag. See <see cref="SolreignExecutiveAccessSystem"/> for the check itself and why it
///     is server-only.
///
///     Server-only component (not networked) — mirrors <c>SeasonTitleComponent</c>'s own server-authoritative
///     idiom, since the rank it reads is itself server-only data.
/// </summary>
[RegisterComponent]
public sealed partial class SolreignExecutiveAccessComponent : Component
{
    /// <summary>
    ///     Minimum career rank index (see <c>RankRules.CorporateRank</c>) required to open this entity's UI.
    ///     0 = everyone. Checked with the same <see cref="ContractRules.MeetsRankGate"/> predicate the
    ///     Contracts board and salvage-raid roster already use, so one formula backs every rank gate in the
    ///     fork.
    /// </summary>
    [DataField(required: true)]
    public int MinRankIndex;

    /// <summary>The corporate-voice denial popup shown to a sub-rank asset who tries to open the UI.</summary>
    [DataField]
    public LocId PopupMessage = "solreign-executive-vendor-popup-denied";
}
