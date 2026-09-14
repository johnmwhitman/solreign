using System.Collections.Generic;

namespace Content.Server._Solreign.StationAudits;

/// <summary>
///     The metrics a <see cref="StationAuditCriterion"/> can grade against. 1:1 with fields
///     <see cref="StationAuditInputs"/> already tracks — ZERO new instrumentation added for the
///     inspection layer.
/// </summary>
public enum StationAuditMetric
{
    CrewCount,
    DeathCount,
    NotableEventCount,
    StipendsProcessed,         // only eligible when StipendsEnabled
    BountyVerdicts,            // only eligible when BountiesEnabled
    DirectiveOutcomeFulfilled, // 0/1; only eligible when a directive was on file AND its outcome was reported
    FirstDeathCommemorated,    // 0/1; only meaningful when DeathCount > 0
}

public enum StationAuditComparison
{
    LessThanOrEqual,
    GreaterThanOrEqual,
    Equals,
}

/// <summary>The three-way shape of a fired PROVIDENCE consequence (council's Pope-test gate).</summary>
public enum StationAuditConsequenceKind
{
    None,
    Commendation,
    Escalation,
    QuietlyCorrectedError,
}

public enum StationAuditCriterionVerdict
{
    Pass,
    Fail,
    NotApplicable,
}

/// <summary>
///     One named inspection criterion PROVIDENCE can assign for self-graded shift inspections. A closed,
///     rarely-retuned catalog — same plain-C#-static-list idiom as <c>StationDirectiveCatalog</c> /
///     <c>ItemOfConcernPicker</c>, deliberately NOT a YAML-authored <c>[Prototype]</c>. The spec that
///     preceded this build (docs scratchpad SPEC-station-audits.md §2.1) proposed a real prototype for
///     designer-iterable tunability; this build downgrades that to the plain-catalog idiom already used
///     twice elsewhere in this exact system, because introducing a brand-new prototype type here would
///     also mean wiring prototype registration/validation (this fork runs orphan-reachability + RGA/RSI
///     YAML validators in CI — see .github/workflows/) for a closed, 6-entry, rarely-changed vocabulary
///     that doesn't need it. The grading/selection logic below is identical either way; a future pass can
///     promote this to a real prototype without touching <see cref="StationAuditCriterionGrading"/> or
///     <see cref="StationAuditInspectionSelection"/>.
/// </summary>
public sealed record StationAuditCriterion(
    string Id,
    string NameLocKey,
    string PassLocKey,
    string FailLocKey,
    StationAuditMetric Metric,
    StationAuditComparison Comparison,
    int Threshold,
    bool AlwaysEligible);

/// <summary>
///     Seed catalog — 6 entries, parity with <see cref="ItemOfConcernPicker"/>'s 6-entry closed
///     vocabulary. Three are always eligible (zero-fatalities, event-responsiveness, full-complement);
///     three are conditionally eligible on a subsystem/shift-state precondition
///     (<see cref="StationAuditCriterionGrading"/> enforces the same "null/false means genuinely nothing
///     here, never fabricate a verdict" honesty law <see cref="StationAuditComposer"/> already uses).
/// </summary>
public static class StationAuditCriterionCatalog
{
    public static readonly IReadOnlyList<StationAuditCriterion> Criteria = new[]
    {
        new StationAuditCriterion(
            Id: "zero-fatalities",
            NameLocKey: "solreign-station-audit-criterion-zero-fatalities-name",
            PassLocKey: "solreign-station-audit-criterion-zero-fatalities-pass",
            FailLocKey: "solreign-station-audit-criterion-zero-fatalities-fail",
            Metric: StationAuditMetric.DeathCount,
            Comparison: StationAuditComparison.LessThanOrEqual,
            Threshold: 0,
            AlwaysEligible: true),

        new StationAuditCriterion(
            Id: "event-responsiveness",
            NameLocKey: "solreign-station-audit-criterion-event-responsiveness-name",
            PassLocKey: "solreign-station-audit-criterion-event-responsiveness-pass",
            FailLocKey: "solreign-station-audit-criterion-event-responsiveness-fail",
            Metric: StationAuditMetric.NotableEventCount,
            Comparison: StationAuditComparison.GreaterThanOrEqual,
            Threshold: 1,
            AlwaysEligible: true),

        new StationAuditCriterion(
            Id: "full-complement",
            NameLocKey: "solreign-station-audit-criterion-full-complement-name",
            PassLocKey: "solreign-station-audit-criterion-full-complement-pass",
            FailLocKey: "solreign-station-audit-criterion-full-complement-fail",
            Metric: StationAuditMetric.CrewCount,
            Comparison: StationAuditComparison.GreaterThanOrEqual,
            Threshold: 1,
            AlwaysEligible: true),

        // Dynamic threshold: StationAuditCriterionGrading special-cases this one metric to compare
        // StipendsProcessed >= CrewCount rather than the literal Threshold below (which is unused for
        // this criterion) — the one non-generic rule in the grading model, documented rather than
        // over-generalizing the comparison shape for a single case.
        new StationAuditCriterion(
            Id: "payroll-discipline",
            NameLocKey: "solreign-station-audit-criterion-payroll-discipline-name",
            PassLocKey: "solreign-station-audit-criterion-payroll-discipline-pass",
            FailLocKey: "solreign-station-audit-criterion-payroll-discipline-fail",
            Metric: StationAuditMetric.StipendsProcessed,
            Comparison: StationAuditComparison.GreaterThanOrEqual,
            Threshold: 0,
            AlwaysEligible: false),

        new StationAuditCriterion(
            Id: "directive-compliance",
            NameLocKey: "solreign-station-audit-criterion-directive-compliance-name",
            PassLocKey: "solreign-station-audit-criterion-directive-compliance-pass",
            FailLocKey: "solreign-station-audit-criterion-directive-compliance-fail",
            Metric: StationAuditMetric.DirectiveOutcomeFulfilled,
            Comparison: StationAuditComparison.Equals,
            Threshold: 1,
            AlwaysEligible: false),

        new StationAuditCriterion(
            Id: "memorial-protocol",
            NameLocKey: "solreign-station-audit-criterion-memorial-protocol-name",
            PassLocKey: "solreign-station-audit-criterion-memorial-protocol-pass",
            FailLocKey: "solreign-station-audit-criterion-memorial-protocol-fail",
            Metric: StationAuditMetric.FirstDeathCommemorated,
            Comparison: StationAuditComparison.Equals,
            Threshold: 1,
            AlwaysEligible: false),
    };
}
