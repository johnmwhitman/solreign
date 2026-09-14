using System;
using System.Collections.Generic;

namespace Content.Server._Solreign.DirectivesFax;

/// <summary>
///     One clause's compliance verdict, paired with the clause it came from so the renderer can pull
///     both the display text (<see cref="DirectivesFaxClauseFormatting"/>) and the pass/fail marker
///     without re-evaluating.
/// </summary>
public readonly record struct DirectivesFaxReportClauseResult(DirectivesFaxClauseSpec Clause, bool Passed);

/// <summary>
///     Everything <see cref="DirectivesFaxReportComposer.Compose"/> reads to assemble one shift's
///     end-of-shift compliance report paper. Deliberately flat and ECS-free — same "extract the pure
///     part, unit-test it directly" idiom as <c>StationAuditComposer</c> / <c>CorporateScoring</c> /
///     <c>DirectivesFaxClauseEvaluation</c> itself.
/// </summary>
public readonly record struct DirectivesFaxReportData(
    string DirectiveId,
    bool Met,
    IReadOnlyList<DirectivesFaxReportClauseResult> Clauses,
    int EligibleCrewCount,
    int CrewMetCount);

/// <summary>
///     Pure assembly of a <see cref="DirectivesFaxReportData"/> from a directive's clause list, the
///     shift's tracked state, and the count of present-eligible crew
///     (<c>DirectivesFaxRuleSystem.Tracking.cs</c>'s <c>SnapshotPresentEligibleAccounts</c>). No ECS,
///     no I/O, no localization — callers (<c>DirectivesFaxRuleSystem.Report.cs</c>) turn the returned
///     data into Loc-resolved paper text.
///
///     The station-wide "N of M crew met every clause" tally is intentionally coarse: the directive's
///     outcome is a single station-wide pass/fail (<see cref="DirectivesFaxClauseEvaluation.EvaluateAll"/>
///     — every clause is evaluated against shared station state, not per-account), so the tally is just
///     that single verdict broadcast across the eligible headcount (<c>met ? eligible : 0</c>), never a
///     fabricated per-account breakdown. Personal compliance streaks remain a per-account concept,
///     surfaced separately via the existing milestone popup/chat path
///     (<c>DirectivesFaxRuleSystem.Print.cs</c>'s <c>NotifyMilestone</c>) — no streak number belongs on
///     a single station-wide paper.
/// </summary>
public static class DirectivesFaxReportComposer
{
    public static DirectivesFaxReportData Compose(
        string directiveId,
        IReadOnlyList<DirectivesFaxClauseSpec> clauses,
        DirectivesFaxShiftState state,
        int eligibleCrewCount)
    {
        var results = new List<DirectivesFaxReportClauseResult>(clauses.Count);
        foreach (var clause in clauses)
            results.Add(new DirectivesFaxReportClauseResult(clause, DirectivesFaxClauseEvaluation.EvaluateClause(clause, state)));

        var met = DirectivesFaxClauseEvaluation.EvaluateAll(clauses, state);
        var eligible = Math.Max(eligibleCrewCount, 0);
        var crewMet = met ? eligible : 0;

        return new DirectivesFaxReportData(directiveId, met, results, eligible, crewMet);
    }
}
