using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._Solreign.PlayerDelight.FirstShift;
using Content.Shared._Solreign.PlayerDelight.FirstShift;
using Content.Shared.Guidebook;
using Content.Shared.Roles;
using NUnit.Framework;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(FirstShiftRoundState))]
public sealed class FirstShiftRoundStateTests
{
    private static readonly NetUserId Alice = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly NetUserId Bob = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));
    private static readonly TimeSpan Now = TimeSpan.FromMinutes(10);

    private static readonly IReadOnlyList<FirstShiftCard> EngineeringCards = Catalog(
        Prototype("FirstShiftEngineeringA", FirstShiftDepartment.Engineering),
        Prototype("FirstShiftEngineeringB", FirstShiftDepartment.Engineering),
        Prototype("FirstShiftEngineeringDisabled", FirstShiftDepartment.Engineering, enabled: false),
        Prototype("FirstShiftMedicalA", FirstShiftDepartment.Medical));

    [Test]
    public void Selection_IsDeterministicAndHasNoUserIdentityInput()
    {
        var first = FirstShiftRoundState.SelectCard(42, FirstShiftDepartment.Engineering, EngineeringCards, null);
        var repeat = FirstShiftRoundState.SelectCard(42, FirstShiftDepartment.Engineering, EngineeringCards, null);

        var state = new FirstShiftRoundState();
        var alice = state.Request(Alice, FirstShiftDepartment.Engineering, EngineeringCards, 42, Now);
        state.Clear(Alice);
        var bob = state.Request(Bob, FirstShiftDepartment.Engineering, EngineeringCards, 42, Now);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(repeat));
            Assert.That(alice.Assignment?.CardId, Is.EqualTo(bob.Assignment?.CardId));
            Assert.That(alice.Assignment?.CardId, Is.EqualTo(first?.Id));
        });
    }

    [Test]
    public void Reroll_AvoidsImmediateRepeatWhenAlternativeExists()
    {
        var state = new FirstShiftRoundState();
        var first = state.Request(Alice, FirstShiftDepartment.Engineering, EngineeringCards, 9, Now);
        var reroll = state.Reroll(Alice, EngineeringCards, 9, Now + TimeSpan.FromSeconds(2));

        Assert.That(reroll.Assignment?.CardId, Is.Not.EqualTo(first.Assignment?.CardId));
    }

    [Test]
    public void Reroll_WithOnlyCardIsNoOp()
    {
        var cards = Catalog(Prototype("Only", FirstShiftDepartment.Service));
        var state = new FirstShiftRoundState();
        var first = state.Request(Alice, FirstShiftDepartment.Service, cards, 1, Now);
        var reroll = state.Reroll(Alice, cards, 1, Now + TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(reroll.Changed, Is.False);
            Assert.That(reroll.Reason, Is.EqualTo("no-alternative"));
            Assert.That(reroll.Assignment?.Generation, Is.EqualTo(first.Assignment?.Generation));
            Assert.That(state.Counters.Rerolled, Is.Zero);
        });
    }

    [Test]
    public void Request_RejectsDisabledAndWrongDepartmentCards()
    {
        var invalid = Catalog(
            Prototype("Disabled", FirstShiftDepartment.Engineering, enabled: false),
            Prototype("Wrong", FirstShiftDepartment.Medical));

        var result = new FirstShiftRoundState().Request(
            Alice, FirstShiftDepartment.Engineering, invalid, 1, Now);

        Assert.Multiple(() =>
        {
            Assert.That(result.Changed, Is.False);
            Assert.That(result.Reason, Is.EqualTo("no-eligible-assignment"));
        });
    }

    [Test]
    public void Advance_IsMonotonicSequentialAndIdempotent()
    {
        var state = AssignedState();

        Assert.Multiple(() =>
        {
            Assert.That(state.Advance(Alice, FirstShiftAssignmentStage.Orient).Changed, Is.True);
            Assert.That(state.Advance(Alice, FirstShiftAssignmentStage.Orient).Changed, Is.False);
            Assert.That(state.Advance(Alice, FirstShiftAssignmentStage.Assigned).Reason, Is.EqualTo("stale-stage"));
            Assert.That(state.Advance(Alice, FirstShiftAssignmentStage.Debrief).Reason, Is.EqualTo("stage-skip"));
            Assert.That(state.GetAssignment(Alice)?.Stage, Is.EqualTo(FirstShiftAssignmentStage.Orient));
        });
    }

    [Test]
    public void Reroll_ThrottleIncludesTwoSecondBoundary()
    {
        var state = AssignedState();
        var immediate = state.Reroll(Alice, EngineeringCards, 2, Now);
        var early = state.Reroll(Alice, EngineeringCards, 2, Now + TimeSpan.FromSeconds(1.999));
        var boundary = state.Reroll(Alice, EngineeringCards, 2, Now + TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(immediate.Changed, Is.True);
            Assert.That(early.Reason, Is.EqualTo("reroll-throttled"));
            Assert.That(boundary.Changed, Is.True);
            Assert.That(state.Counters.Rerolled, Is.EqualTo(2));
        });
    }

    [Test]
    public void ClearDisconnectAndRoundResetRemoveRoundLocalState()
    {
        var state = AssignedState();
        Assert.That(state.Clear(Alice).Changed, Is.True);
        Assert.That(state.GetAssignment(Alice), Is.Null);

        state.Request(Alice, FirstShiftDepartment.Engineering, EngineeringCards, 1, Now);
        state.Disconnect(Alice);
        Assert.That(state.GetAssignment(Alice), Is.Null);

        state.Request(Alice, FirstShiftDepartment.Engineering, EngineeringCards, 1, Now);
        state.Request(Bob, FirstShiftDepartment.Engineering, EngineeringCards, 1, Now);
        state.ClearRound();
        Assert.That(state.Counters.Active, Is.Zero);
    }

    [Test]
    public void CompletionAndCountersAreAggregateOnly()
    {
        var state = AssignedState();
        state.Request(Bob, FirstShiftDepartment.Engineering, EngineeringCards, 1, Now);
        Assert.That(state.Counters.Active, Is.EqualTo(2));

        state.Advance(Alice);
        state.Advance(Alice);
        state.Advance(Alice);
        state.Advance(Alice);

        var complete = state.Complete(Alice);

        Assert.Multiple(() =>
        {
            Assert.That(complete.Changed, Is.True);
            Assert.That(state.Counters, Is.EqualTo(new FirstShiftCounters(1, 1, 0)));
            Assert.That(state.GetAssignment(Alice), Is.Null);
            Assert.That(state.GetAssignment(Bob), Is.Not.Null);
        });
    }

    [TestCase(FirstShiftAssignmentStage.Assigned)]
    [TestCase(FirstShiftAssignmentStage.Orient)]
    [TestCase(FirstShiftAssignmentStage.Try)]
    [TestCase(FirstShiftAssignmentStage.Debrief)]
    public void CompleteBeforeMarkIsRejectedWithoutMutation(FirstShiftAssignmentStage stage)
    {
        var state = AssignedState();
        while (state.GetAssignment(Alice)!.Value.Stage < stage)
            state.Advance(Alice);
        var before = state.GetAssignment(Alice)!.Value;

        var result = state.Complete(Alice);

        Assert.Multiple(() =>
        {
            Assert.That(result.Changed, Is.False);
            Assert.That(result.Reason, Is.EqualTo("not-at-mark"));
            Assert.That(result.Assignment, Is.EqualTo(before));
            Assert.That(state.GetAssignment(Alice), Is.EqualTo(before));
            Assert.That(state.Counters, Is.EqualTo(new FirstShiftCounters(1, 0, 0)));
        });
    }

    [Test]
    public void Advance_ReachesMarkThenCompletes()
    {
        var state = AssignedState();

        state.Advance(Alice); // Assigned -> Orient
        state.Advance(Alice); // Orient -> Try
        state.Advance(Alice); // Try -> Debrief
        var atDebrief = state.Advance(Alice); // Debrief -> Mark, the MG-W3 append
        Assert.Multiple(() =>
        {
            Assert.That(atDebrief.Changed, Is.True);
            Assert.That(state.GetAssignment(Alice)!.Value.Stage, Is.EqualTo(FirstShiftAssignmentStage.Mark));
        });

        var overAdvance = state.Advance(Alice);
        Assert.Multiple(() =>
        {
            Assert.That(overAdvance.Changed, Is.False);
            Assert.That(overAdvance.Reason, Is.EqualTo("already-at-mark"));
        });

        var complete = state.Complete(Alice);
        Assert.Multiple(() =>
        {
            Assert.That(complete.Changed, Is.True);
            Assert.That(complete.Reason, Is.EqualTo("completed"));
            Assert.That(state.GetAssignment(Alice), Is.Null);
        });
    }

    [Test]
    public void GenerationChangesOnlyForEffectiveTransitions()
    {
        var state = AssignedState();
        var initial = state.GetAssignment(Alice)!.Value.Generation;

        state.Advance(Alice, FirstShiftAssignmentStage.Assigned);
        Assert.That(state.GetAssignment(Alice)!.Value.Generation, Is.EqualTo(initial));

        state.Advance(Alice, FirstShiftAssignmentStage.Orient);
        Assert.That(state.GetAssignment(Alice)!.Value.Generation, Is.EqualTo(initial + 1));
    }

    [TestCase("complete")]
    [TestCase("clear")]
    [TestCase("disconnect")]
    public void GenerationClockSurvivesUserStateRemoval(string removal)
    {
        var state = AssignedState();
        state.Advance(Alice, FirstShiftAssignmentStage.Orient);
        var staleGeneration = state.GetAssignment(Alice)!.Value.Generation;

        switch (removal)
        {
            case "complete":
                state.Advance(Alice); // Orient -> Try
                state.Advance(Alice); // Try -> Debrief
                state.Advance(Alice); // Debrief -> Mark
                state.Complete(Alice);
                break;
            case "clear": state.Clear(Alice); break;
            case "disconnect": state.Disconnect(Alice); break;
        }

        var replacement = state.Request(Alice, FirstShiftDepartment.Engineering, EngineeringCards, 1, Now);
        Assert.That(replacement.Assignment?.Generation, Is.GreaterThan(staleGeneration));
    }

    [Test]
    public void RoundResetClearsGenerationClock()
    {
        var state = AssignedState();
        state.Advance(Alice, FirstShiftAssignmentStage.Orient);
        state.ClearRound();

        var replacement = state.Request(Alice, FirstShiftDepartment.Engineering, EngineeringCards, 1, Now);

        Assert.That(replacement.Assignment?.Generation, Is.EqualTo(1));
    }

    [Test]
    public void SchemaValidatorRequiresSafeFiniteCardAndArrivalsFallback()
    {
        var valid = new FirstShiftAssignmentSchema(
            FirstShiftDepartment.Engineering,
            [new ProtoId<JobPrototype>("StationEngineer")],
            "title", "why", "orient", "try", "stuck", "stop",
            [new EntProtoId("DefaultStationBeaconEngineering"), new EntProtoId("DefaultStationBeaconArrivals")],
            new ProtoId<GuideEntryPrototype>("Engineering"),
            FirstShiftTaskCategory.Orientation,
            FirstShiftSafetyClassification.SafeOrientation);

        Assert.That(FirstShiftAssignmentValidator.Validate(valid), Is.Empty);

        var invalid = valid with
        {
            Department = (FirstShiftDepartment) 255,
            TaskCategory = (FirstShiftTaskCategory) 255,
            SafetyClassification = FirstShiftSafetyClassification.Restricted,
            SafetyStop = "",
            Anchors = [new EntProtoId("DefaultStationBeaconEngineering")],
        };
        var errors = FirstShiftAssignmentValidator.Validate(invalid);
        Assert.Multiple(() =>
        {
            Assert.That(errors, Does.Contain("invalid-department"));
            Assert.That(errors, Does.Contain("unsafe-classification"));
            Assert.That(errors, Does.Contain("forbidden-task-category"));
            Assert.That(errors, Does.Contain("missing-safety-stop"));
            Assert.That(errors, Does.Contain("missing-arrivals-fallback"));
        });
    }

    [Test]
    public void CatalogBuilderFailsClosedForUnsafePrototypeAndDuplicates()
    {
        var valid = Prototype("CardA", FirstShiftDepartment.Engineering);
        var duplicate = Prototype("CardA", FirstShiftDepartment.Engineering);
        var unsafeCard = Prototype("Unsafe", FirstShiftDepartment.Engineering);
        unsafeCard.SafetyClassification = FirstShiftSafetyClassification.Restricted;

        var unsafeResult = FirstShiftCatalogBuilder.Build([valid, unsafeCard]);
        var duplicateResult = FirstShiftCatalogBuilder.Build([valid, duplicate]);

        Assert.Multiple(() =>
        {
            Assert.That(unsafeResult.Cards, Is.Empty);
            Assert.That(unsafeResult.Errors, Does.Contain("Unsafe:unsafe-classification"));
            Assert.That(duplicateResult.Cards, Is.Empty);
            Assert.That(duplicateResult.Errors, Does.Contain("duplicate-id:CardA"));
        });
    }

    [Test]
    public void CatalogBuilderProjectsValidatedPrototypeAndAllowsUniversalWithoutJobs()
    {
        var department = Prototype("Engineering", FirstShiftDepartment.Engineering);
        var universal = Prototype("Universal", FirstShiftDepartment.Universal);
        universal.EligibleSafeJobs.Clear();

        var result = FirstShiftCatalogBuilder.Build([department, universal]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.Cards.Select(card => card.Id), Is.EqualTo(new[] { "Engineering", "Universal" }));
        });
    }

    private static FirstShiftRoundState AssignedState()
    {
        var state = new FirstShiftRoundState();
        state.Request(Alice, FirstShiftDepartment.Engineering, EngineeringCards, 1, Now);
        return state;
    }

    private static IReadOnlyList<FirstShiftCard> Catalog(params FirstShiftAssignmentPrototype[] prototypes)
    {
        var result = FirstShiftCatalogBuilder.Build(prototypes);
        Assert.That(result.Errors, Is.Empty);
        return result.Cards;
    }

    private static FirstShiftAssignmentPrototype Prototype(
        string id,
        FirstShiftDepartment department,
        bool enabled = true)
    {
#pragma warning disable RA0039 // Pure catalog projection test; no prototype manager is running here.
        var prototype = new FirstShiftAssignmentPrototype
        {
            Department = department,
            EligibleSafeJobs = [new ProtoId<JobPrototype>("StationEngineer")],
            Title = "title",
            Why = "why",
            Orient = "orient",
            Try = "try",
            IfStuck = "stuck",
            SafetyStop = "stop",
            Anchors = [new EntProtoId("DefaultStationBeaconArrivals")],
            GuideEntry = new ProtoId<GuideEntryPrototype>("Engineering"),
            SafetyClassification = FirstShiftSafetyClassification.SafeOrientation,
            TaskCategory = FirstShiftTaskCategory.Orientation,
            Enabled = enabled,
        };
#pragma warning restore RA0039
        typeof(FirstShiftAssignmentPrototype).GetProperty(nameof(FirstShiftAssignmentPrototype.ID))!
            .SetValue(prototype, id);
        return prototype;
    }
}
