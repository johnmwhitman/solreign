using System;
using System.Linq;
using Content.Server._Solreign.StationAudits;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Pure-unit coverage for <see cref="StationAuditCriterionGrading"/> and
///     <see cref="StationAuditCheckpointVerdict"/> (v14 gap-closure pass) — no ECS, no I/O. Exercises
///     every <see cref="StationAuditComparison"/>, boundary values, every metric's eligibility gate
///     (the anti-fabrication law: an ineligible criterion always grades NotApplicable, never a
///     fabricated Pass/Fail), and the <c>payroll-discipline</c> dynamic-threshold special case.
/// </summary>
[TestFixture]
[TestOf(typeof(StationAuditCriterionGrading))]
public sealed class StationAuditCriterionGradingTests
{
    private static StationAuditInputs Empty(int roundId = 1) => new(
        RoundId: roundId,
        ShiftDuration: TimeSpan.Zero,
        CrewCount: 0,
        DeathCount: 0,
        FirstDeathCommemorated: false,
        DirectiveTitle: null,
        DirectiveOutcomeReported: false,
        DirectiveOutcomeFulfilled: false,
        StipendsEnabled: false,
        StipendsProcessed: 0,
        BountiesEnabled: false,
        BountyVerdicts: 0,
        NotableEventCount: 0,
        CommendationName: null,
        CommendationScore: 0,
        ItemOfConcernId: ItemOfConcernPicker.Pick(roundId));

    private static StationAuditCriterion FindCriterion(string id) =>
        StationAuditCriterionCatalog.Criteria.FirstOrDefault(c => c.Id == id)
        ?? throw new InvalidOperationException($"seed catalog missing '{id}'");

    // --- Comparisons -------------------------------------------------------------------------------

    [Test]
    public void LessThanOrEqual_BoundaryExactlyMet_Passes()
    {
        // zero-fatalities: DeathCount <= 0
        var criterion = FindCriterion("zero-fatalities");
        var inputs = Empty() with { DeathCount = 0 };

        Assert.That(StationAuditCriterionGrading.Grade(criterion, inputs), Is.EqualTo(StationAuditCriterionVerdict.Pass));
    }

    [Test]
    public void LessThanOrEqual_OverThreshold_Fails()
    {
        var criterion = FindCriterion("zero-fatalities");
        var inputs = Empty() with { DeathCount = 1 };

        Assert.That(StationAuditCriterionGrading.Grade(criterion, inputs), Is.EqualTo(StationAuditCriterionVerdict.Fail));
    }

    [Test]
    public void GreaterThanOrEqual_BoundaryExactlyMet_Passes()
    {
        // event-responsiveness: NotableEventCount >= 1
        var criterion = FindCriterion("event-responsiveness");
        var inputs = Empty() with { NotableEventCount = 1 };

        Assert.That(StationAuditCriterionGrading.Grade(criterion, inputs), Is.EqualTo(StationAuditCriterionVerdict.Pass));
    }

    [Test]
    public void GreaterThanOrEqual_UnderThreshold_Fails()
    {
        var criterion = FindCriterion("event-responsiveness");
        var inputs = Empty() with { NotableEventCount = 0 };

        Assert.That(StationAuditCriterionGrading.Grade(criterion, inputs), Is.EqualTo(StationAuditCriterionVerdict.Fail));
    }

    [Test]
    public void Equals_ExactMatch_Passes()
    {
        // directive-compliance: DirectiveOutcomeFulfilled == 1 (only eligible w/ a directive+outcome)
        var criterion = FindCriterion("directive-compliance");
        var inputs = Empty() with
        {
            DirectiveTitle = "Q3 Incident Quota",
            DirectiveOutcomeReported = true,
            DirectiveOutcomeFulfilled = true,
        };

        Assert.That(StationAuditCriterionGrading.Grade(criterion, inputs), Is.EqualTo(StationAuditCriterionVerdict.Pass));
    }

    [Test]
    public void Equals_Mismatch_Fails()
    {
        var criterion = FindCriterion("directive-compliance");
        var inputs = Empty() with
        {
            DirectiveTitle = "Q3 Incident Quota",
            DirectiveOutcomeReported = true,
            DirectiveOutcomeFulfilled = false,
        };

        Assert.That(StationAuditCriterionGrading.Grade(criterion, inputs), Is.EqualTo(StationAuditCriterionVerdict.Fail));
    }

    // --- Eligibility gates (anti-fabrication law) --------------------------------------------------

    [Test]
    public void PayrollDiscipline_StipendsDisabled_IsNotApplicable_NeverFabricatesAVerdict()
    {
        var criterion = FindCriterion("payroll-discipline");
        var inputs = Empty() with { StipendsEnabled = false, StipendsProcessed = 999, CrewCount = 1 };

        Assert.That(StationAuditCriterionGrading.Grade(criterion, inputs), Is.EqualTo(StationAuditCriterionVerdict.NotApplicable));
    }

    [Test]
    public void BountyMetric_BountiesDisabled_IsNotApplicable()
    {
        // No seed criterion currently grades BountyVerdicts, but the eligibility gate itself must
        // still honor the "disabled subsystem -> NotApplicable" law for that metric in isolation.
        var criterion = FindCriterion("payroll-discipline") with
        {
            Metric = StationAuditMetric.BountyVerdicts,
        };
        var inputs = Empty() with { BountiesEnabled = false, BountyVerdicts = 5 };

        Assert.That(StationAuditCriterionGrading.Grade(criterion, inputs), Is.EqualTo(StationAuditCriterionVerdict.NotApplicable));
    }

    [Test]
    public void DirectiveCompliance_NoDirectiveOnFile_IsNotApplicable()
    {
        var criterion = FindCriterion("directive-compliance");
        var inputs = Empty() with { DirectiveTitle = null, DirectiveOutcomeFulfilled = true };

        Assert.That(StationAuditCriterionGrading.Grade(criterion, inputs), Is.EqualTo(StationAuditCriterionVerdict.NotApplicable));
    }

    [Test]
    public void DirectiveCompliance_DirectiveOnFileButOutcomeUnreported_IsNotApplicable()
    {
        var criterion = FindCriterion("directive-compliance");
        var inputs = Empty() with
        {
            DirectiveTitle = "Q3 Incident Quota",
            DirectiveOutcomeReported = false,
            DirectiveOutcomeFulfilled = true, // must never be trusted while unreported
        };

        Assert.That(StationAuditCriterionGrading.Grade(criterion, inputs), Is.EqualTo(StationAuditCriterionVerdict.NotApplicable));
    }

    [Test]
    public void MemorialProtocol_NoDeathsThisShift_IsNotApplicable()
    {
        var criterion = FindCriterion("memorial-protocol");
        var inputs = Empty() with { DeathCount = 0, FirstDeathCommemorated = true };

        Assert.That(StationAuditCriterionGrading.Grade(criterion, inputs), Is.EqualTo(StationAuditCriterionVerdict.NotApplicable));
    }

    [Test]
    public void MemorialProtocol_DeathsAndCommemorated_Passes()
    {
        var criterion = FindCriterion("memorial-protocol");
        var inputs = Empty() with { DeathCount = 1, FirstDeathCommemorated = true };

        Assert.That(StationAuditCriterionGrading.Grade(criterion, inputs), Is.EqualTo(StationAuditCriterionVerdict.Pass));
    }

    [Test]
    public void MemorialProtocol_DeathsButUncommemorated_Fails()
    {
        var criterion = FindCriterion("memorial-protocol");
        var inputs = Empty() with { DeathCount = 1, FirstDeathCommemorated = false };

        Assert.That(StationAuditCriterionGrading.Grade(criterion, inputs), Is.EqualTo(StationAuditCriterionVerdict.Fail));
    }

    // --- payroll-discipline's dynamic-threshold special case ---------------------------------------

    [Test]
    public void PayrollDiscipline_StipendsKeepPaceWithCrew_Passes()
    {
        var criterion = FindCriterion("payroll-discipline");
        var inputs = Empty() with { StipendsEnabled = true, CrewCount = 5, StipendsProcessed = 5 };

        Assert.That(StationAuditCriterionGrading.Grade(criterion, inputs), Is.EqualTo(StationAuditCriterionVerdict.Pass));
    }

    [Test]
    public void PayrollDiscipline_StipendsFallBehindCrew_Fails()
    {
        var criterion = FindCriterion("payroll-discipline");
        var inputs = Empty() with { StipendsEnabled = true, CrewCount = 5, StipendsProcessed = 3 };

        Assert.That(StationAuditCriterionGrading.Grade(criterion, inputs), Is.EqualTo(StationAuditCriterionVerdict.Fail));
    }

    [Test]
    public void PayrollDiscipline_IgnoresLiteralThresholdField_UsesCrewCountInstead()
    {
        // The catalog entry's own Threshold field is 0 and is deliberately unused for this metric —
        // pin that CrewCount (not the literal Threshold) is what's actually compared against.
        var criterion = FindCriterion("payroll-discipline");
        Assert.That(criterion.Threshold, Is.EqualTo(0), "catalog's literal threshold for this entry is a documented no-op");

        var inputs = Empty() with { StipendsEnabled = true, CrewCount = 10, StipendsProcessed = 0 };
        Assert.That(StationAuditCriterionGrading.Grade(criterion, inputs), Is.EqualTo(StationAuditCriterionVerdict.Fail),
            "0 processed against a literal threshold of 0 would wrongly pass if the dynamic special-case weren't applied");
    }

    // --- StationAuditCheckpointVerdict reduction ----------------------------------------------------

    [Test]
    public void CheckpointVerdict_AllPass_IsCommendation()
    {
        var verdicts = new[] { StationAuditCriterionVerdict.Pass, StationAuditCriterionVerdict.Pass };
        Assert.That(StationAuditCheckpointVerdict.Determine(verdicts), Is.EqualTo(StationAuditConsequenceKind.Commendation));
    }

    [Test]
    public void CheckpointVerdict_AnyFail_IsEscalation()
    {
        var verdicts = new[] { StationAuditCriterionVerdict.Pass, StationAuditCriterionVerdict.Fail };
        Assert.That(StationAuditCheckpointVerdict.Determine(verdicts), Is.EqualTo(StationAuditConsequenceKind.Escalation));
    }

    [Test]
    public void CheckpointVerdict_MixedPassAndNotApplicable_NoFail_IsQuietlyCorrectedError()
    {
        var verdicts = new[] { StationAuditCriterionVerdict.Pass, StationAuditCriterionVerdict.NotApplicable };
        Assert.That(StationAuditCheckpointVerdict.Determine(verdicts), Is.EqualTo(StationAuditConsequenceKind.QuietlyCorrectedError));
    }

    [Test]
    public void CheckpointVerdict_AllNotApplicable_IsQuietlyCorrectedError()
    {
        var verdicts = new[] { StationAuditCriterionVerdict.NotApplicable, StationAuditCriterionVerdict.NotApplicable };
        Assert.That(StationAuditCheckpointVerdict.Determine(verdicts), Is.EqualTo(StationAuditConsequenceKind.QuietlyCorrectedError));
    }

    [Test]
    public void CheckpointVerdict_EmptySet_IsNone()
    {
        Assert.That(StationAuditCheckpointVerdict.Determine(Array.Empty<StationAuditCriterionVerdict>()), Is.EqualTo(StationAuditConsequenceKind.None));
    }

    [Test]
    public void CheckpointVerdict_FailAndNotApplicable_StillEscalation()
    {
        var verdicts = new[] { StationAuditCriterionVerdict.Fail, StationAuditCriterionVerdict.NotApplicable };
        Assert.That(StationAuditCheckpointVerdict.Determine(verdicts), Is.EqualTo(StationAuditConsequenceKind.Escalation));
    }
}
