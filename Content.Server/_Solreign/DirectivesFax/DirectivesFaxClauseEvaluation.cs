using System;
using System.Collections.Generic;
using System.Linq;

namespace Content.Server._Solreign.DirectivesFax;

/// <summary>
///     Pure, unit-testable clause evaluation — no ECS, no I/O, no loc (same split as
///     <c>StationDirectiveSelection</c> / <c>Corporate.CorporateScoring</c>). The caller
///     (<c>DirectivesFaxRuleSystem</c>) gathers a cheap <see cref="DirectivesFaxShiftState"/> snapshot
///     ONCE at round end (a handful of already-tracked counters — no per-tick scan, see class docs on
///     <c>DirectivesFaxClauseCatalog</c>) and hands it here.
/// </summary>
public readonly record struct DirectivesFaxShiftState(int CrewDeaths, int CargoRevenueDelta, int SupplyOrdersDelta);

public static class DirectivesFaxClauseEvaluation
{
    /// <summary>Whether one clause is satisfied by the given shift state.</summary>
    public static bool EvaluateClause(DirectivesFaxClauseSpec clause, DirectivesFaxShiftState state)
    {
        return clause.Kind switch
        {
            DirectivesFaxClauseKind.ZeroCasualties => state.CrewDeaths == 0,
            DirectivesFaxClauseKind.CargoRevenueMin => state.CargoRevenueDelta >= clause.Threshold,
            DirectivesFaxClauseKind.SupplyOrdersMin => state.SupplyOrdersDelta >= clause.Threshold,
            _ => throw new ArgumentOutOfRangeException(nameof(clause), clause.Kind, "Unhandled Directives Fax clause kind."),
        };
    }

    /// <summary>
    ///     The directive's outcome: EVERY clause must be met (AND, not majority — a directive with two
    ///     clauses that only half-delivers is not "complied with"). An empty clause list (defensive —
    ///     an unrecognized directive id, see <c>DirectivesFaxClauseCatalog.GetClauses</c>) vacuously
    ///     evaluates to true; the caller never prints a fax for an unrecognized directive id in
    ///     practice, so this only matters for direct unit calls against malformed input.
    /// </summary>
    public static bool EvaluateAll(IReadOnlyList<DirectivesFaxClauseSpec> clauses, DirectivesFaxShiftState state)
    {
        return clauses.All(clause => EvaluateClause(clause, state));
    }
}

/// <summary>
///     Pure loc-key + argument selection for one clause's display text — kept separate from the
///     evaluator so it stays unit-testable without a live <c>ILocalizationManager</c> (the caller
///     applies <c>Loc.GetString(LocKey, Args)</c>). Three loc keys total, one per
///     <see cref="DirectivesFaxClauseKind"/> — closed vocabulary, see <c>directives_fax.ftl</c>.
/// </summary>
public static class DirectivesFaxClauseFormatting
{
    public static (string LocKey, (string, object)[] Args) Describe(DirectivesFaxClauseSpec clause)
    {
        return clause.Kind switch
        {
            DirectivesFaxClauseKind.ZeroCasualties =>
                ("solreign-directives-fax-clause-zero-casualties", Array.Empty<(string, object)>()),
            DirectivesFaxClauseKind.CargoRevenueMin =>
                ("solreign-directives-fax-clause-cargo-revenue-min", new (string, object)[] { ("amount", clause.Threshold) }),
            DirectivesFaxClauseKind.SupplyOrdersMin =>
                ("solreign-directives-fax-clause-supply-orders-min", new (string, object)[] { ("count", clause.Threshold) }),
            _ => throw new ArgumentOutOfRangeException(nameof(clause), clause.Kind, "Unhandled Directives Fax clause kind."),
        };
    }
}
