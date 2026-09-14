using System;

namespace Content.Server._Solreign.StationAudits;

/// <summary>
///     Pure assembly of a <see cref="StationAuditReport"/> from <see cref="StationAuditInputs"/>. No
///     ECS, no I/O, no localization — the same "extract the pure part, unit-test it directly" idiom
///     as <c>CorporateScoring</c> / <c>BountyClaimRules</c> / <c>FirstDeathEpitaphPicker</c>. Callers
///     (<see cref="StationAuditSystem"/>) turn the returned report into Loc-resolved text; this class
///     only ever decides WHICH closed-vocabulary section shows and what numbers/names go in it.
/// </summary>
public static class StationAuditComposer
{
    public static StationAuditReport Compose(StationAuditInputs inputs)
    {
        var minutes = (int) inputs.ShiftDuration.TotalMinutes;
        if (minutes < 0)
            minutes = 0;

        return new StationAuditReport(
            RoundId: inputs.RoundId,
            ShiftDurationMinutes: minutes,
            CrewCount: inputs.CrewCount,
            HasDeaths: inputs.DeathCount > 0,
            DeathCount: inputs.DeathCount,
            // Commemoration is only ever meaningful when there was at least one death this shift —
            // never claimed on an empty/deathless shift, regardless of what the input carries.
            FirstDeathCommemorated: inputs.DeathCount > 0 && inputs.FirstDeathCommemorated,
            HasDirective: inputs.DirectiveTitle is { Length: > 0 },
            DirectiveTitle: inputs.DirectiveTitle,
            // Same discipline: an outcome can only be "reported" when there was a directive to
            // report an outcome FOR. A dormant/absent directive system never gets to claim a verdict.
            DirectiveOutcomeReported: inputs.DirectiveTitle is { Length: > 0 } && inputs.DirectiveOutcomeReported,
            DirectiveOutcomeFulfilled: inputs.DirectiveOutcomeFulfilled,
            StipendsEnabled: inputs.StipendsEnabled,
            StipendsProcessed: inputs.StipendsEnabled ? inputs.StipendsProcessed : 0,
            BountiesEnabled: inputs.BountiesEnabled,
            BountyVerdicts: inputs.BountiesEnabled ? inputs.BountyVerdicts : 0,
            NotableEventCount: inputs.NotableEventCount,
            HasCommendation: inputs.CommendationName is { Length: > 0 } && inputs.CommendationScore > 0,
            CommendationName: inputs.CommendationName,
            CommendationScore: inputs.CommendationScore,
            ItemOfConcernId: inputs.ItemOfConcernId,
            AssignedCriteria: inputs.AssignedCriteria ?? Array.Empty<StationAuditCriterionResult>(),
            CheckpointConsequenceKind: inputs.CheckpointConsequenceKind);
    }
}
