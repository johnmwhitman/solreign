namespace Content.Server._Solreign.StationDirective.Components;

/// <summary>
///     Data bag for the "Station Directive" layerable game rule (Solreign, churn-insight follow-up to
///     Milestone 1's Corporate Ladder). Cosmetic only: on round start it picks one Corporate Directive
///     from a small rotating set and announces it over the PA in the Solreign HR voice; periodically it
///     tickers a flavor progress line for that directive. No mechanics are altered, nothing is tracked
///     or compelled — the point is a shared, present, station-wide "thing to care about" this shift, not
///     a scoreboard.
///
///     <c>StationAuditSystem</c> also reads <see cref="DirectiveIndex"/>, read-only (v14 wave-1 #3:
///     the audit's "directive on file" line names whichever directive was selected this round — real,
///     already-shipped, local state; no new tracking added here). No <c>Access</c> grant needed for
///     that: the default "Other" permission on this attribute is already Read-only, which is all a
///     second reader needs — see <c>Robust.Shared.Analyzers.AccessAttribute.OtherDefaultPermissions</c>.
/// </summary>
[RegisterComponent, Access(typeof(StationDirectiveRuleSystem))]
public sealed partial class StationDirectiveRuleComponent : Component
{
    /// <summary>
    ///     Index into <see cref="StationDirectiveCatalog.Directives"/> chosen for this round (see
    ///     <see cref="StationDirectiveSelection.SelectDirectiveIndex"/>). Set once in
    ///     <c>StationDirectiveRuleSystem.Started</c> and not changed for the rest of the round.
    /// </summary>
    [DataField]
    public int DirectiveIndex;

    /// <summary>
    ///     Index into the chosen directive's ticker flavor lines that will be read out next. Advances
    ///     cyclically (see <see cref="StationDirectiveSelection.NextTickerIndex"/>) each time the PA
    ///     ticker fires, so consecutive tickers don't repeat the same line back-to-back.
    /// </summary>
    [DataField]
    public int TickerLineIndex;

    /// <summary>
    ///     Accumulates <c>frameTime</c> since the last ticker line. Reset to 0 each broadcast. Not
    ///     persisted, not networked — pure server-side throttle state (same idiom as
    ///     <c>SolreignCorporateRuleComponent.SinceLastAudit</c>).
    /// </summary>
    [ViewVariables]
    public float SinceLastTicker;
}
