namespace Content.Server._Solreign.Corporate.Components;

/// <summary>
///     Data bag for "The Corporate Ladder" layerable game rule (Solreign creative bible, Milestone 1).
///     Cosmetic only: it tracks a per-account "Corporate Standing" score and periodically broadcasts a
///     top-3 "quarterly earnings" scoreboard over the PA. No mechanics are altered, nothing is compelled.
/// </summary>
[RegisterComponent, Access(typeof(SolreignCorporateRuleSystem))]
public sealed partial class SolreignCorporateRuleComponent : Component
{
    /// <summary>
    ///     Seconds between "quarterly earnings call" scoreboard broadcasts over the PA. Default ~3 minutes so
    ///     the fiscal quarter passes at a brisk, unpaid pace.
    /// </summary>
    [DataField]
    public float AuditIntervalSeconds = 180f;

    /// <summary>
    ///     Accumulates <c>frameTime</c> since the last earnings call. Reset to 0 each broadcast. Not persisted,
    ///     not networked — pure server-side throttle state.
    /// </summary>
    [ViewVariables]
    public float SinceLastAudit;

    /// <summary>
    ///     The randomized corporate modifier applied this round.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public Content.Shared._Solreign.Corporate.CorporateModifierType ActiveModifier;

    /// <summary>How many top performers the quarterly earnings call names over the PA.</summary>
    [DataField]
    public int EarningsCallTopCount = 3;

    /// <summary>How many names the final FY standings block lists at round end.</summary>
    [DataField]
    public int FinalStandingsCount = 5;
}
