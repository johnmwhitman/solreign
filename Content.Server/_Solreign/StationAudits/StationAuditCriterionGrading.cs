namespace Content.Server._Solreign.StationAudits;

/// <summary>
///     Pure, ECS-free self-grading: <c>(StationAuditCriterion, StationAuditInputs) -&gt; verdict</c>. Same
///     "pure function, unit-tested without a server" idiom as <see cref="StationAuditComposer"/> /
///     <c>CorporateScoring</c>.
///
///     Anti-fabrication law (carried over from <see cref="StationAuditComposer"/>): an assigned-but-
///     ineligible criterion (e.g. Stipend Program off, no directive on file, no deaths this shift) grades
///     <see cref="StationAuditCriterionVerdict.NotApplicable"/> — never a fabricated Pass or Fail.
/// </summary>
public static class StationAuditCriterionGrading
{
    public static StationAuditCriterionVerdict Grade(StationAuditCriterion criterion, StationAuditInputs inputs)
    {
        if (!IsEligible(criterion.Metric, inputs))
            return StationAuditCriterionVerdict.NotApplicable;

        var (value, threshold) = ReadMetric(criterion, inputs);

        var passed = criterion.Comparison switch
        {
            StationAuditComparison.LessThanOrEqual => value <= threshold,
            StationAuditComparison.GreaterThanOrEqual => value >= threshold,
            StationAuditComparison.Equals => value == threshold,
            _ => false,
        };

        return passed ? StationAuditCriterionVerdict.Pass : StationAuditCriterionVerdict.Fail;
    }

    private static bool IsEligible(StationAuditMetric metric, StationAuditInputs inputs) => metric switch
    {
        StationAuditMetric.StipendsProcessed => inputs.StipendsEnabled,
        StationAuditMetric.BountyVerdicts => inputs.BountiesEnabled,
        StationAuditMetric.DirectiveOutcomeFulfilled => inputs.DirectiveTitle is { Length: > 0 } && inputs.DirectiveOutcomeReported,
        StationAuditMetric.FirstDeathCommemorated => inputs.DeathCount > 0,
        _ => true,
    };

    /// <summary>Resolves (observed value, threshold-to-compare-against) for one criterion. The single
    /// dynamic-threshold special case (<c>payroll-discipline</c>) compares
    /// <see cref="StationAuditInputs.StipendsProcessed"/> against <see cref="StationAuditInputs.CrewCount"/>
    /// rather than the criterion's literal <see cref="StationAuditCriterion.Threshold"/> — see the
    /// catalog's doc comment on that entry.</summary>
    private static (int Value, int Threshold) ReadMetric(StationAuditCriterion criterion, StationAuditInputs inputs) =>
        criterion.Metric switch
        {
            StationAuditMetric.CrewCount => (inputs.CrewCount, criterion.Threshold),
            StationAuditMetric.DeathCount => (inputs.DeathCount, criterion.Threshold),
            StationAuditMetric.NotableEventCount => (inputs.NotableEventCount, criterion.Threshold),
            StationAuditMetric.StipendsProcessed => (inputs.StipendsProcessed, inputs.CrewCount),
            StationAuditMetric.BountyVerdicts => (inputs.BountyVerdicts, criterion.Threshold),
            StationAuditMetric.DirectiveOutcomeFulfilled => (inputs.DirectiveOutcomeFulfilled ? 1 : 0, criterion.Threshold),
            StationAuditMetric.FirstDeathCommemorated => (inputs.FirstDeathCommemorated ? 1 : 0, criterion.Threshold),
            _ => (0, criterion.Threshold),
        };
}
