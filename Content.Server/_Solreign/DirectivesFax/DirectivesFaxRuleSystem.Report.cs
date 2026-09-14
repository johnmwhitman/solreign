using System.Text;
using Content.Server._Solreign.DirectivesFax.Components;
using Content.Server._Solreign.StationAudits;
using Content.Shared.Fax.Components;
using Content.Shared.Paper;

namespace Content.Server._Solreign.DirectivesFax;

/// <summary>
///     The stamped/shareable end-of-shift compliance report artifact (v14 gap-closure — see the
///     gap-analysis spec: the round-start fax and round-end text/ledger already shipped, but nothing
///     produced a discrete, screenshottable, held-in-hand document the way
///     <c>StationAudits.StationAuditSystem.SpawnAuditPaper</c> already does for its own, separate,
///     still-dormant feature).
///
///     Two responsibilities live in this partial:
///       1. <see cref="SpawnComplianceReportPaper"/> — mirrors the Station Audit keepsake-paper
///          pattern (a dedicated <c>PaperOffice</c>-derived prototype, content assembled and stamped at
///          print time) but reuses THIS feature's own existing fax-print infrastructure
///          (<c>FindFaxPrintTarget</c> from <c>DirectivesFaxRuleSystem.Print.cs</c>, same partial class
///          so no extraction needed — private members are shared across all files of one partial
///          class) rather than Station Audits' comms-console spawn point, so the compliance report
///          physically arrives at the same desk the round-start directive fax did ("PROVIDENCE faxes
///          back the results").
///       2. <see cref="OnDirectiveOutcomeQuery"/> — fills the previously-unfilled
///          <see cref="SolreignDirectiveOutcomeQueryEvent"/> seam Station Audits already defines and
///          raises once at round end. Zero coupling in the other direction: Station Audits has no
///          reference to any Directives Fax type; this file reaches OUT to Station Audits' event type,
///          which is exactly the seam's documented contract ("a future directive-outcome system
///          participates by subscribing... and calling Report... exactly once").
///
///     Ordering-race discipline (the one genuinely tricky bit — see
///     <see cref="DirectivesFaxRuleComponent.OutcomeComputed"/>'s doc comment): two independent
///     <c>RoundEndTextAppendEvent</c> subscribers (Station Audits' own, and this rule's
///     <c>AppendRoundEndText</c>) both may need this round's outcome, with no guaranteed relative
///     order. <see cref="ComputeOrGetOutcome"/> is the single compute-once-cache-idempotently choke
///     point both paths funnel through — same discipline the round-start directive-id re-derivation
///     already established (see <see cref="DirectivesFaxRuleSystem"/>'s class doc comment).
/// </summary>
public sealed partial class DirectivesFaxRuleSystem
{
    private const string ReportPaperPrototype = "SolreignPaperDirectivesFaxReport";

    private void InitializeReport()
    {
        SubscribeLocalEvent<SolreignDirectiveOutcomeQueryEvent>(OnDirectiveOutcomeQuery);
    }

    /// <summary>
    ///     Answers Station Audits' outcome query for whichever Directives Fax rule is currently running
    ///     (in practice at most one at a time). Computes-and-caches via <see cref="ComputeOrGetOutcome"/>
    ///     if nothing has asked yet this round; reuses the cached value otherwise. If no
    ///     <see cref="DirectivesFaxRuleComponent"/> exists at all (feature dormant, or not yet started),
    ///     the query is left un-<c>Report</c>ed — Station Audits' own default ("outcome not on record")
    ///     stands, which is the correct, honest answer.
    /// </summary>
    private void OnDirectiveOutcomeQuery(SolreignDirectiveOutcomeQueryEvent ev)
    {
        var query = EntityQueryEnumerator<DirectivesFaxRuleComponent>();
        while (query.MoveNext(out _, out var component))
        {
            if (component.DirectiveId is not { } directiveId)
                continue;

            ev.Report(ComputeOrGetOutcome(component, directiveId));
            return;
        }
    }

    /// <summary>
    ///     The single choke point for "was this shift's directive fulfilled" — returns the cached
    ///     <see cref="DirectivesFaxRuleComponent.OutcomeMet"/> if already computed, otherwise computes
    ///     it (the same <c>GatherShiftState</c> + <c>DirectivesFaxClauseEvaluation.EvaluateAll</c> call
    ///     <c>AppendRoundEndText</c> always made) and caches it before returning. Callable from either
    ///     <c>AppendRoundEndText</c> or <see cref="OnDirectiveOutcomeQuery"/>, in either order, any
    ///     number of times, with an identical result — see the class doc comment's ordering-race note.
    /// </summary>
    private bool ComputeOrGetOutcome(DirectivesFaxRuleComponent component, string directiveId)
    {
        if (component.OutcomeComputed)
            return component.OutcomeMet;

        var clauses = DirectivesFaxClauseCatalog.GetClauses(directiveId);
        var state = GatherShiftState(component);

        component.OutcomeMet = DirectivesFaxClauseEvaluation.EvaluateAll(clauses, state);
        component.OutcomeComputed = true;

        return component.OutcomeMet;
    }

    /// <summary>
    ///     Prints the stamped compliance report paper. Same 4-tier fallback ladder as the round-start
    ///     fax (<c>PrintDirectiveFax</c>/<c>FindFaxPrintTarget</c> in <c>.Print.cs</c>): Bridge → HoP →
    ///     any station fax → a loose paper at the station's own transform if no fax machine resolves at
    ///     all. Uses <c>FaxSystem.Receive</c> (not a bare <c>Spawn</c>) on the fax path so the report
    ///     physically prints on a fax roll the same way the round-start directive did — reinforces the
    ///     "PROVIDENCE fax exchange" framing bookending the shift — and passes
    ///     <see cref="ReportPaperPrototype"/> through <c>FaxPrintout.PrototypeId</c> so the printed
    ///     entity is the dedicated keepsake prototype (stamped/shareable framing in its own
    ///     name/description), not a generic "Paper". The loose-paper fallback spawns that same
    ///     prototype directly, so even the defensive path prints a properly-named/described artifact
    ///     rather than a bare "Paper" object.
    /// </summary>
    private void SpawnComplianceReportPaper(
        EntityUid station,
        string directiveId,
        string directiveDisplayName,
        IReadOnlyList<DirectivesFaxClauseSpec> clauses,
        DirectivesFaxShiftState state)
    {
        var eligibleCrewCount = SnapshotPresentEligibleAccounts().Count;
        var report = DirectivesFaxReportComposer.Compose(directiveId, clauses, state, eligibleCrewCount);
        var body = BuildComplianceReportBody(directiveDisplayName, report);
        var name = Loc.GetString("solreign-directives-fax-report-paper-name");

        if (FindFaxPrintTarget(station) is { } faxUid)
        {
            var printout = new FaxPrintout(
                body,
                name,
                prototypeId: ReportPaperPrototype,
                senderFaxName: Loc.GetString("solreign-directives-fax-sender"));
            _fax.Receive(faxUid, printout);
            return;
        }

        // Defensive fallback — see class doc, path 4. Spawns the keepsake prototype directly (its YAML
        // already carries the right name/description, unlike the round-start fax's generic-"Paper"
        // fallback, which has to set the name manually).
        var loose = Spawn(ReportPaperPrototype, Transform(station).Coordinates);
        if (TryComp<PaperComponent>(loose, out var paper))
            _paper.SetContent((loose, paper), body);
    }

    /// <summary>Loc-resolves a composed <see cref="DirectivesFaxReportData"/> into the paper's printed
    /// text. Reuses the SAME round-end-header/met/unmet/clause-met/clause-unmet loc keys
    /// <c>AppendRoundEndText</c> already renders to the round-end summary screen (per the gap spec:
    /// "same content already computed... just rendered to the paper... in addition to the round-end
    /// text lines") plus two paper-only additions: a station-wide tally line and a closing stamp
    /// line.</summary>
    private string BuildComplianceReportBody(string directiveDisplayName, DirectivesFaxReportData report)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Loc.GetString("solreign-directives-fax-round-end-header"));
        sb.AppendLine(Loc.GetString(
            report.Met ? "solreign-directives-fax-round-end-met" : "solreign-directives-fax-round-end-unmet",
            ("directive", directiveDisplayName)));
        sb.AppendLine();

        foreach (var result in report.Clauses)
        {
            var (locKey, locArgs) = DirectivesFaxClauseFormatting.Describe(result.Clause);
            var clauseText = Loc.GetString(locKey, locArgs);
            sb.AppendLine(Loc.GetString(
                result.Passed ? "solreign-directives-fax-round-end-clause-met" : "solreign-directives-fax-round-end-clause-unmet",
                ("clause", clauseText)));
        }

        sb.AppendLine();
        sb.AppendLine(Loc.GetString(
            "solreign-directives-fax-report-tally",
            ("met", report.CrewMetCount),
            ("total", report.EligibleCrewCount)));
        sb.AppendLine();
        sb.AppendLine(Loc.GetString("solreign-directives-fax-report-stamp"));

        return sb.ToString();
    }
}
