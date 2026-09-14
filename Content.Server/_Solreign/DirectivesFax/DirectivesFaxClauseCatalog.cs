using System.Collections.Generic;

namespace Content.Server._Solreign.DirectivesFax;

/// <summary>
///     The CLOSED clause vocabulary for Directives Fax (v14 wave-1 item #1, council C1 "ship first").
///     Every clause a fax paper can print is one of exactly three machine-checkable kinds — never free
///     text, never player-authored, never something that needs a per-tick scan to evaluate (the
///     aliveness doctrine's "cheap to check" requirement, satisfied by only ever reading a handful of
///     already-tracked counters at shift end):
///       * <see cref="DirectivesFaxClauseKind.ZeroCasualties"/> — no crew death recorded this shift.
///       * <see cref="DirectivesFaxClauseKind.CargoRevenueMin"/> — the station's Cargo account balance
///         rose by at least <see cref="DirectivesFaxClauseSpec.Threshold"/> credits this shift.
///       * <see cref="DirectivesFaxClauseKind.SupplyOrdersMin"/> — at least
///         <see cref="DirectivesFaxClauseSpec.Threshold"/> supply orders were placed this shift.
///
///     All three are genuinely completable solo (the aliveness doctrine is law): a lone pop-1 player
///     can avoid dying, and can crew Cargo alone to place orders and move product. None of them require
///     another player, an antagonist, or any content beyond what every SOLREIGN station already ships
///     with (a cargo console + shuttle).
///
///     Each Station Directive (<c>Content.Server._Solreign.StationDirective.StationDirectiveCatalog</c>)
///     maps to a fixed 1-3 clause set, chosen for flavor coherence with the directive's HR framing (a
///     "Productivity Mandate" leans on throughput, "Safety Inspection" leans on zero casualties, an
///     "Audit" directive stacks two clauses). The mapping is pure data — no RNG, no per-round variance
///     beyond which directive the existing round-robin StationDirective selection already picked.
/// </summary>
public enum DirectivesFaxClauseKind
{
    ZeroCasualties,
    CargoRevenueMin,
    SupplyOrdersMin,
}

/// <summary>
///     One clause on the fax. <see cref="Id"/> is a short, stable slug (logging/testing only, never
///     shown to players — display text is resolved from <see cref="Kind"/> via
///     <see cref="DirectivesFaxClauseFormatting"/>). <see cref="Threshold"/> is unused
///     (conventionally 0) for <see cref="DirectivesFaxClauseKind.ZeroCasualties"/>.
/// </summary>
public readonly record struct DirectivesFaxClauseSpec(string Id, DirectivesFaxClauseKind Kind, int Threshold = 0);

public static class DirectivesFaxClauseCatalog
{
    private static readonly DirectivesFaxClauseSpec ZeroCasualties =
        new("zero-casualties", DirectivesFaxClauseKind.ZeroCasualties);

    /// <summary>Fixed 1-3 clause set per Station Directive id. Keys must match
    /// <c>StationDirectiveCatalog.Directives[*].Id</c> exactly — verified in
    /// <c>DirectivesFaxClauseCatalogTests</c> against the real catalog, not duplicated here, so the
    /// two catalogs can never silently drift apart.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<DirectivesFaxClauseSpec>> ClausesByDirective =
        new Dictionary<string, IReadOnlyList<DirectivesFaxClauseSpec>>
        {
            // Q3 Incident Quota: keep reportable incidents at zero — a death is the least ambiguous one.
            ["q3-incident-quota"] = new[] { ZeroCasualties },

            // Productivity Mandate: stay busy — cargo throughput is the cleanest "busy" signal we can
            // check cheaply.
            ["productivity-mandate"] = new[]
            {
                new DirectivesFaxClauseSpec("supply-orders-3", DirectivesFaxClauseKind.SupplyOrdersMin, 3),
            },

            // Compliance Week: minimize contraband/incidents — folds to the same zero-casualties signal.
            ["compliance-week"] = new[] { ZeroCasualties },

            // Mandatory Overtime: more hours, more orders.
            ["mandatory-overtime"] = new[]
            {
                new DirectivesFaxClauseSpec("supply-orders-4", DirectivesFaxClauseKind.SupplyOrdersMin, 4),
            },

            // Budget Freeze: the department still has to make its numbers, freeze or not.
            ["budget-freeze"] = new[]
            {
                new DirectivesFaxClauseSpec("cargo-revenue-1500", DirectivesFaxClauseKind.CargoRevenueMin, 1500),
            },

            // Ration Audit: tally what moved AND what it was worth — two clauses.
            ["ration-audit"] = new[]
            {
                new DirectivesFaxClauseSpec("supply-orders-2", DirectivesFaxClauseKind.SupplyOrdersMin, 2),
                new DirectivesFaxClauseSpec("cargo-revenue-1000", DirectivesFaxClauseKind.CargoRevenueMin, 1000),
            },

            // Safety Inspection: the flagship zero-casualties directive.
            ["safety-inspection"] = new[] { ZeroCasualties },

            // Quarterly Audit: the full audit — revenue AND throughput, two clauses (flavor-only in the
            // Station Directive catalog itself; Directives Fax layers its own mechanical clauses on top
            // without touching that catalog's "no mechanical hook" guarantee).
            ["quarterly-audit"] = new[]
            {
                new DirectivesFaxClauseSpec("cargo-revenue-3000", DirectivesFaxClauseKind.CargoRevenueMin, 3000),
                new DirectivesFaxClauseSpec("supply-orders-2", DirectivesFaxClauseKind.SupplyOrdersMin, 2),
            },

            // Surveillance Sweep: watching for incidents, i.e. zero casualties on watch.
            ["surveillance-sweep"] = new[] { ZeroCasualties },

            // Casual Friday: deliberately the lowest bar in the catalog.
            ["casual-friday"] = new[]
            {
                new DirectivesFaxClauseSpec("cargo-revenue-1000", DirectivesFaxClauseKind.CargoRevenueMin, 1000),
            },

            // Synergy Retreat: collaboration (throughput) + safety, two clauses.
            ["synergy-retreat"] = new[]
            {
                new DirectivesFaxClauseSpec("supply-orders-2", DirectivesFaxClauseKind.SupplyOrdersMin, 2),
                ZeroCasualties,
            },

            // Open Door Policy: harmony (zero casualties) + modest productivity, two clauses.
            ["open-door-policy"] = new[]
            {
                ZeroCasualties,
                new DirectivesFaxClauseSpec("cargo-revenue-1000", DirectivesFaxClauseKind.CargoRevenueMin, 1000),
            },
        };

    /// <summary>
    ///     Clauses for a directive id, or an empty list if the id is unknown (fails closed — an
    ///     unrecognized directive id prints a fax with no clauses rather than throwing mid-round-start).
    /// </summary>
    public static IReadOnlyList<DirectivesFaxClauseSpec> GetClauses(string directiveId)
    {
        return ClausesByDirective.TryGetValue(directiveId, out var clauses)
            ? clauses
            : System.Array.Empty<DirectivesFaxClauseSpec>();
    }

    /// <summary>Short human-readable directive names for the fax header + round-end line — kept
    /// separate from the Station Directive catalog's own (paragraph-length) announcement text rather
    /// than parsing/truncating that Fluent string at runtime.</summary>
    private static readonly IReadOnlyDictionary<string, string> DisplayNames = new Dictionary<string, string>
    {
        ["q3-incident-quota"] = "Q3 Incident Quota",
        ["productivity-mandate"] = "Productivity Mandate",
        ["compliance-week"] = "Compliance Week",
        ["mandatory-overtime"] = "Mandatory Overtime",
        ["budget-freeze"] = "Budget Freeze",
        ["ration-audit"] = "Ration Audit",
        ["safety-inspection"] = "Safety Inspection",
        ["quarterly-audit"] = "Quarterly Audit",
        ["surveillance-sweep"] = "Surveillance Sweep",
        ["casual-friday"] = "Casual Friday",
        ["synergy-retreat"] = "Synergy Retreat",
        ["open-door-policy"] = "Open Door Policy",
    };

    /// <summary>Short display name for a directive id, or the raw id itself if unrecognized (fails
    /// closed — never throws).</summary>
    public static string GetDisplayName(string directiveId)
    {
        return DisplayNames.TryGetValue(directiveId, out var name) ? name : directiveId;
    }
}
