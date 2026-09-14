using Content.Client._Solreign.PlayerDelight.FirstShift;
using Content.Shared._Solreign.PlayerDelight.FirstShift;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
public sealed class FirstShiftClientPresentationTests
{
    // markEnabled=true: the full 5-task kishōtenketsu path, Debrief now advances INTO Mark.
    [TestCase(FirstShiftAssignmentStage.Assigned, true, "first-shift-stage-next-orient", true)]
    [TestCase(FirstShiftAssignmentStage.Orient, true, "first-shift-stage-next-try", true)]
    [TestCase(FirstShiftAssignmentStage.Try, true, "first-shift-stage-next-debrief", true)]
    [TestCase(FirstShiftAssignmentStage.Debrief, true, "first-shift-stage-next-mark", true)]
    [TestCase(FirstShiftAssignmentStage.Mark, true, "first-shift-stage-complete", false)]
    // markEnabled=false: the dormant ship posture — the classic 3-task list, Debrief is terminal
    // exactly as it was before the wave (spec §3.6's "renders the classic 3-task list" rule).
    [TestCase(FirstShiftAssignmentStage.Assigned, false, "first-shift-stage-next-orient", true)]
    [TestCase(FirstShiftAssignmentStage.Orient, false, "first-shift-stage-next-try", true)]
    [TestCase(FirstShiftAssignmentStage.Try, false, "first-shift-stage-next-debrief", true)]
    [TestCase(FirstShiftAssignmentStage.Debrief, false, "first-shift-stage-complete", false)]
    public void StagePresentationHasExpectedPrimaryActionAndSkip(
        FirstShiftAssignmentStage stage,
        bool markEnabled,
        string primaryKey,
        bool showSkip)
    {
        var view = FirstShiftPresentation.For(stage, markEnabled, usedFallback: false, anchorUnavailable: false,
            hasGuide: true, showFlavor: true, hasFlavor: true);

        Assert.Multiple(() =>
        {
            Assert.That(view.PrimaryLocKey, Is.EqualTo(primaryKey));
            Assert.That(view.ShowSkip, Is.EqualTo(showSkip));
            Assert.That(view.ShowFlavor, Is.True);
            Assert.That(view.CanOpenGuide, Is.True);
            Assert.That(view.CanOpenMap, Is.True);
        });
    }

    [Test]
    public void MarkStageAnchorStatusNamesTheContinuityGarden()
    {
        var live = FirstShiftPresentation.For(FirstShiftAssignmentStage.Mark, markEnabled: true,
            usedFallback: false, anchorUnavailable: false, hasGuide: true, showFlavor: true, hasFlavor: true);
        var unavailable = FirstShiftPresentation.For(FirstShiftAssignmentStage.Mark, markEnabled: true,
            usedFallback: false, anchorUnavailable: true, hasGuide: true, showFlavor: true, hasFlavor: true);

        Assert.Multiple(() =>
        {
            Assert.That(live.AnchorStatusLocKey, Is.EqualTo("first-shift-marker-continuity-garden"));
            Assert.That(unavailable.AnchorStatusLocKey, Is.EqualTo("first-shift-marker-unavailable"));
        });
    }

    [Test]
    public void PlainInstructionsHidesOnlyFlavorAndFallbackNamesArrivals()
    {
        var plain = FirstShiftPresentation.For(FirstShiftAssignmentStage.Orient, markEnabled: true,
            usedFallback: true, anchorUnavailable: false, hasGuide: true,
            showFlavor: false, hasFlavor: true);
        var unavailable = FirstShiftPresentation.For(FirstShiftAssignmentStage.Orient, markEnabled: true,
            usedFallback: false, anchorUnavailable: true, hasGuide: false,
            showFlavor: true, hasFlavor: false);

        Assert.Multiple(() =>
        {
            Assert.That(plain.ShowFlavor, Is.False);
            Assert.That(plain.AnchorStatusLocKey, Is.EqualTo("first-shift-marker-arrivals-fallback"));
            Assert.That(plain.CanOpenMap, Is.True);
            Assert.That(unavailable.AnchorStatusLocKey, Is.EqualTo("first-shift-marker-unavailable"));
            Assert.That(unavailable.CanOpenMap, Is.False);
            Assert.That(unavailable.CanOpenGuide, Is.False);
        });
    }

    [Test]
    public void RerollRemainsReachableDuringServerCooldown()
    {
        var view = FirstShiftPresentation.For(FirstShiftAssignmentStage.Try, markEnabled: true,
            usedFallback: false, anchorUnavailable: false, hasGuide: true,
            showFlavor: true, hasFlavor: true);

        Assert.That(view.RerollReachable, Is.True,
            "The server remains authoritative, but the Another Card control must not disappear or disable during cooldown.");
    }

    [TestCase(FirstShiftDepartment.Universal)]
    [TestCase((FirstShiftDepartment) 255)]
    public void UniversalOrUnknownSuggestionUsesNeutralCopy(FirstShiftDepartment department)
    {
        Assert.That(FirstShiftPresentation.SuggestionLocKey(department),
            Is.EqualTo("first-shift-department-universal"));
    }

    [Test]
    public void TaskListRowsAreThreeDormantFiveLive()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FirstShiftTaskListPresentation.RowKeys(false), Has.Count.EqualTo(3));
            Assert.That(FirstShiftTaskListPresentation.RowKeys(false),
                Has.None.EqualTo("first-shift-task-4-list").And.None.EqualTo("first-shift-task-5-mark"));
            Assert.That(FirstShiftTaskListPresentation.RowKeys(true), Has.Count.EqualTo(5));
            Assert.That(FirstShiftTaskListPresentation.RowKeys(true),
                Does.Contain("first-shift-task-4-list").And.Contain("first-shift-task-5-mark"));
        });
    }
}
