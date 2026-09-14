using System.Collections.Generic;

namespace Content.Server._Solreign.StationDirective;

/// <summary>
///     One entry in the "Corporate Directive" rotation: a round-start PA keynote plus a small set of
///     flavor lines the PA ticker cycles through over the shift. Pure data — the darkly-funny HR framing
///     lives in <c>Resources/Locale/en-US/_solreign/station_directive.ftl</c>; this only names which loc
///     keys belong together.
///
///     <see cref="ScreenFxDurationSeconds"/> is the one optional mechanical hook: when set, the directive's
///     round-start announcement is paired with Solreign's existing brand-sting screen overlay
///     (<c>Content.Shared._Solreign.FX.SolreignScreenFxEvent</c> — the same already-shipped, engine-clamped,
///     self-clearing primitive <c>SolreignSolarFlareRule</c> and the Corporate Ladder's earnings call already
///     raise). It is purely cosmetic client-side overlay time — no gameplay state, no restore bookkeeping
///     needed. Every other directive is <c>null</c> here and is announcement/ticker text only (SR-W-081:
///     several flavors — Ration Audit, Quarterly Audit, Casual Friday, etc. — are intentionally
///     flavor-only; no existing primitive safely nudges hunger/thirst/Standing without risking the hard
///     constraints in the backlog item, so those stay atmosphere-only like the original three).
/// </summary>
public readonly record struct StationDirectiveDefinition(
    string Id,
    string AnnouncementLocKey,
    IReadOnlyList<string> TickerLocKeys,
    float? ScreenFxDurationSeconds = null);

/// <summary>
///     The rotating set of Corporate Directives ("random round modifiers", SR-W-081).
///     <see cref="StationDirectiveSelection.SelectDirectiveIndex"/> round-robins through this list by round
///     id so every directive gets its turn before any repeat — deterministic per round id, but from a
///     player's seat (nobody tracks round ids or catalog order) it reads as a fresh corporate mandate every
///     shift. Kept round-robin rather than switched to RNG-at-Started: it's the already-tested, already-shipped
///     selection primitive (see <c>StationDirectiveSelectionTests</c>), and round-robin gives the stronger
///     guarantee true randomness can't (every directive is guaranteed a turn before any repeat — no player
///     ever sees the same modifier twice in a row by bad luck).
/// </summary>
public static class StationDirectiveCatalog
{
    public static readonly IReadOnlyList<StationDirectiveDefinition> Directives = new[]
    {
        new StationDirectiveDefinition(
            Id: "q3-incident-quota",
            AnnouncementLocKey: "solreign-station-directive-announce-incident-quota",
            TickerLocKeys: new[]
            {
                "solreign-station-directive-ticker-incident-quota-1",
                "solreign-station-directive-ticker-incident-quota-2",
                "solreign-station-directive-ticker-incident-quota-3",
            }),
        new StationDirectiveDefinition(
            Id: "productivity-mandate",
            AnnouncementLocKey: "solreign-station-directive-announce-productivity-mandate",
            TickerLocKeys: new[]
            {
                "solreign-station-directive-ticker-productivity-mandate-1",
                "solreign-station-directive-ticker-productivity-mandate-2",
                "solreign-station-directive-ticker-productivity-mandate-3",
            }),
        new StationDirectiveDefinition(
            Id: "compliance-week",
            AnnouncementLocKey: "solreign-station-directive-announce-compliance-week",
            TickerLocKeys: new[]
            {
                "solreign-station-directive-ticker-compliance-week-1",
                "solreign-station-directive-ticker-compliance-week-2",
                "solreign-station-directive-ticker-compliance-week-3",
            }),
        new StationDirectiveDefinition(
            Id: "mandatory-overtime",
            AnnouncementLocKey: "solreign-station-directive-announce-mandatory-overtime",
            TickerLocKeys: new[]
            {
                "solreign-station-directive-ticker-mandatory-overtime-1",
                "solreign-station-directive-ticker-mandatory-overtime-2",
                "solreign-station-directive-ticker-mandatory-overtime-3",
            }),
        new StationDirectiveDefinition(
            Id: "budget-freeze",
            AnnouncementLocKey: "solreign-station-directive-announce-budget-freeze",
            TickerLocKeys: new[]
            {
                "solreign-station-directive-ticker-budget-freeze-1",
                "solreign-station-directive-ticker-budget-freeze-2",
                "solreign-station-directive-ticker-budget-freeze-3",
            }),
        new StationDirectiveDefinition(
            Id: "ration-audit",
            AnnouncementLocKey: "solreign-station-directive-announce-ration-audit",
            TickerLocKeys: new[]
            {
                "solreign-station-directive-ticker-ration-audit-1",
                "solreign-station-directive-ticker-ration-audit-2",
                "solreign-station-directive-ticker-ration-audit-3",
            }),
        new StationDirectiveDefinition(
            Id: "safety-inspection",
            AnnouncementLocKey: "solreign-station-directive-announce-safety-inspection",
            TickerLocKeys: new[]
            {
                "solreign-station-directive-ticker-safety-inspection-1",
                "solreign-station-directive-ticker-safety-inspection-2",
                "solreign-station-directive-ticker-safety-inspection-3",
            },
            ScreenFxDurationSeconds: 2.5f),
        new StationDirectiveDefinition(
            Id: "quarterly-audit",
            AnnouncementLocKey: "solreign-station-directive-announce-quarterly-audit",
            TickerLocKeys: new[]
            {
                "solreign-station-directive-ticker-quarterly-audit-1",
                "solreign-station-directive-ticker-quarterly-audit-2",
                "solreign-station-directive-ticker-quarterly-audit-3",
            }),
        new StationDirectiveDefinition(
            Id: "surveillance-sweep",
            AnnouncementLocKey: "solreign-station-directive-announce-surveillance-sweep",
            TickerLocKeys: new[]
            {
                "solreign-station-directive-ticker-surveillance-sweep-1",
                "solreign-station-directive-ticker-surveillance-sweep-2",
                "solreign-station-directive-ticker-surveillance-sweep-3",
            },
            ScreenFxDurationSeconds: 2f),
        new StationDirectiveDefinition(
            Id: "casual-friday",
            AnnouncementLocKey: "solreign-station-directive-announce-casual-friday",
            TickerLocKeys: new[]
            {
                "solreign-station-directive-ticker-casual-friday-1",
                "solreign-station-directive-ticker-casual-friday-2",
                "solreign-station-directive-ticker-casual-friday-3",
            }),
        new StationDirectiveDefinition(
            Id: "synergy-retreat",
            AnnouncementLocKey: "solreign-station-directive-announce-synergy-retreat",
            TickerLocKeys: new[]
            {
                "solreign-station-directive-ticker-synergy-retreat-1",
                "solreign-station-directive-ticker-synergy-retreat-2",
                "solreign-station-directive-ticker-synergy-retreat-3",
            }),
        new StationDirectiveDefinition(
            Id: "open-door-policy",
            AnnouncementLocKey: "solreign-station-directive-announce-open-door-policy",
            TickerLocKeys: new[]
            {
                "solreign-station-directive-ticker-open-door-policy-1",
                "solreign-station-directive-ticker-open-door-policy-2",
                "solreign-station-directive-ticker-open-door-policy-3",
            }),
    };
}
