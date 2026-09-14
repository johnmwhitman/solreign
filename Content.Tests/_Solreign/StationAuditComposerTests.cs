using System;
using Content.Server._Solreign.StationAudits;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Pure-unit coverage for <see cref="StationAuditComposer"/> (v14 wave-1 #3) — no ECS, no I/O.
///     Each section is exercised present and absent, and the empty-shift case is pinned to never
///     fabricate a number (spec: "empty shift must produce an honest minimal audit, never fake
///     numbers").
/// </summary>
[TestFixture]
[TestOf(typeof(StationAuditComposer))]
public sealed class StationAuditComposerTests
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

    [Test]
    public void EmptyShift_ProducesHonestMinimalAudit_NeverFakesNumbers()
    {
        var report = StationAuditComposer.Compose(Empty());

        Assert.Multiple(() =>
        {
            Assert.That(report.CrewCount, Is.EqualTo(0));
            Assert.That(report.HasDeaths, Is.False);
            Assert.That(report.DeathCount, Is.EqualTo(0));
            Assert.That(report.FirstDeathCommemorated, Is.False);
            Assert.That(report.HasDirective, Is.False);
            Assert.That(report.DirectiveTitle, Is.Null);
            Assert.That(report.DirectiveOutcomeReported, Is.False, "no directive means no outcome to report, regardless of input");
            Assert.That(report.StipendsEnabled, Is.False);
            Assert.That(report.StipendsProcessed, Is.EqualTo(0));
            Assert.That(report.BountiesEnabled, Is.False);
            Assert.That(report.BountyVerdicts, Is.EqualTo(0));
            Assert.That(report.NotableEventCount, Is.EqualTo(0));
            Assert.That(report.HasCommendation, Is.False);
            Assert.That(report.CommendationName, Is.Null);
            // Item of Concern is the one deliberately fictional flavor slot — it is ALWAYS present,
            // even on an empty shift, and that is by design (not a fabricated number).
            Assert.That(report.ItemOfConcernId, Is.Not.Empty);
        });
    }

    [Test]
    public void ShiftDuration_RoundsDownToWholeMinutes()
    {
        var inputs = Empty() with { ShiftDuration = TimeSpan.FromSeconds(125) };
        var report = StationAuditComposer.Compose(inputs);

        Assert.That(report.ShiftDurationMinutes, Is.EqualTo(2));
    }

    [Test]
    public void NegativeShiftDuration_ClampsToZero()
    {
        var inputs = Empty() with { ShiftDuration = TimeSpan.FromSeconds(-5) };
        var report = StationAuditComposer.Compose(inputs);

        Assert.That(report.ShiftDurationMinutes, Is.EqualTo(0));
    }

    [Test]
    public void Deaths_Present_CarriesCount()
    {
        var inputs = Empty() with { DeathCount = 3, FirstDeathCommemorated = true };
        var report = StationAuditComposer.Compose(inputs);

        Assert.Multiple(() =>
        {
            Assert.That(report.HasDeaths, Is.True);
            Assert.That(report.DeathCount, Is.EqualTo(3));
            Assert.That(report.FirstDeathCommemorated, Is.True);
        });
    }

    [Test]
    public void Commemorated_NeverClaimedOnADeathlessShift_EvenIfInputSaysTrue()
    {
        // Defensive: a caller bug that sets FirstDeathCommemorated=true with DeathCount=0 must not
        // produce a report that claims a commemoration on a shift with no deaths.
        var inputs = Empty() with { DeathCount = 0, FirstDeathCommemorated = true };
        var report = StationAuditComposer.Compose(inputs);

        Assert.That(report.FirstDeathCommemorated, Is.False);
    }

    [Test]
    public void Directive_Absent_MasksOutcomeEvenIfReported()
    {
        // Defensive: an outcome can't be reported for a directive that doesn't exist.
        var inputs = Empty() with { DirectiveTitle = null, DirectiveOutcomeReported = true, DirectiveOutcomeFulfilled = true };
        var report = StationAuditComposer.Compose(inputs);

        Assert.Multiple(() =>
        {
            Assert.That(report.HasDirective, Is.False);
            Assert.That(report.DirectiveOutcomeReported, Is.False);
        });
    }

    [Test]
    public void Directive_Present_WithUnreportedOutcome()
    {
        var inputs = Empty() with { DirectiveTitle = "Q3 Incident Quota", DirectiveOutcomeReported = false };
        var report = StationAuditComposer.Compose(inputs);

        Assert.Multiple(() =>
        {
            Assert.That(report.HasDirective, Is.True);
            Assert.That(report.DirectiveTitle, Is.EqualTo("Q3 Incident Quota"));
            Assert.That(report.DirectiveOutcomeReported, Is.False);
        });
    }

    [Test]
    public void Directive_Present_WithReportedFulfilledOutcome()
    {
        var inputs = Empty() with
        {
            DirectiveTitle = "Productivity Mandate",
            DirectiveOutcomeReported = true,
            DirectiveOutcomeFulfilled = true,
        };
        var report = StationAuditComposer.Compose(inputs);

        Assert.Multiple(() =>
        {
            Assert.That(report.DirectiveOutcomeReported, Is.True);
            Assert.That(report.DirectiveOutcomeFulfilled, Is.True);
        });
    }

    [Test]
    public void Stipends_ProcessedCount_ZeroedWhenDisabled_EvenIfInputCarriesANumber()
    {
        var inputs = Empty() with { StipendsEnabled = false, StipendsProcessed = 12 };
        var report = StationAuditComposer.Compose(inputs);

        Assert.Multiple(() =>
        {
            Assert.That(report.StipendsEnabled, Is.False);
            Assert.That(report.StipendsProcessed, Is.EqualTo(0), "never show a stale/fake stipend count for a disabled program");
        });
    }

    [Test]
    public void Stipends_ProcessedCount_PassesThroughWhenEnabled()
    {
        var inputs = Empty() with { StipendsEnabled = true, StipendsProcessed = 5 };
        var report = StationAuditComposer.Compose(inputs);

        Assert.That(report.StipendsProcessed, Is.EqualTo(5));
    }

    [Test]
    public void Bounties_VerdictsCount_ZeroedWhenDisabled()
    {
        var inputs = Empty() with { BountiesEnabled = false, BountyVerdicts = 8 };
        var report = StationAuditComposer.Compose(inputs);

        Assert.Multiple(() =>
        {
            Assert.That(report.BountiesEnabled, Is.False);
            Assert.That(report.BountyVerdicts, Is.EqualTo(0));
        });
    }

    [Test]
    public void Commendation_Present_WhenNameAndPositiveScore()
    {
        var inputs = Empty() with { CommendationName = "Juno Pike", CommendationScore = 4 };
        var report = StationAuditComposer.Compose(inputs);

        Assert.Multiple(() =>
        {
            Assert.That(report.HasCommendation, Is.True);
            Assert.That(report.CommendationName, Is.EqualTo("Juno Pike"));
            Assert.That(report.CommendationScore, Is.EqualTo(4));
        });
    }

    [Test]
    public void Commendation_Absent_WhenScoreIsZeroOrNegative()
    {
        var inputs = Empty() with { CommendationName = "Juno Pike", CommendationScore = 0 };
        var report = StationAuditComposer.Compose(inputs);

        Assert.That(report.HasCommendation, Is.False);
    }

    [Test]
    public void ItemOfConcern_IsAlwaysOneOfTheClosedSet()
    {
        for (var round = -5; round < 20; round++)
        {
            var id = ItemOfConcernPicker.Pick(round);
            Assert.That(ItemOfConcernPicker.Ids, Does.Contain(id));
        }
    }

    [Test]
    public void ItemOfConcern_IsDeterministicPerRoundId()
    {
        var first = ItemOfConcernPicker.Pick(42);
        var second = ItemOfConcernPicker.Pick(42);
        Assert.That(first, Is.EqualTo(second));
    }

    // --- Inspection layer (v14 gap-closure pass) ----------------------------------------------------

    [Test]
    public void AssignedCriteria_NullInInputs_ComposesToEmptyList_NeverNull()
    {
        // Empty()'s AssignedCriteria stays at StationAuditInputs's default (null) — the inspection
        // layer produced nothing this shift (disabled, or a misconfigured zero-count). The composed
        // Report must still expose a non-null empty list, never propagate the null.
        var report = StationAuditComposer.Compose(Empty());

        Assert.That(report.AssignedCriteria, Is.Not.Null);
        Assert.That(report.AssignedCriteria, Is.Empty);
        Assert.That(report.CheckpointConsequenceKind, Is.EqualTo(StationAuditConsequenceKind.None));
    }

    [Test]
    public void AssignedCriteria_Present_CarriesThroughUnmodified()
    {
        var criteria = new[]
        {
            new StationAuditCriterionResult("zero-fatalities", StationAuditCriterionVerdict.Pass),
            new StationAuditCriterionResult("event-responsiveness", StationAuditCriterionVerdict.Fail),
        };
        var inputs = Empty() with
        {
            AssignedCriteria = criteria,
            CheckpointConsequenceKind = StationAuditConsequenceKind.Escalation,
        };

        var report = StationAuditComposer.Compose(inputs);

        Assert.Multiple(() =>
        {
            Assert.That(report.AssignedCriteria, Is.EqualTo(criteria));
            Assert.That(report.CheckpointConsequenceKind, Is.EqualTo(StationAuditConsequenceKind.Escalation));
        });
    }
}
