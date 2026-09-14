using Content.Shared._Solreign.PlayerDelight.Wingmates;
using Content.Client._Solreign.PlayerDelight.Wingmates;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using System.Collections.Generic;
using System.Linq;

namespace Content.Tests._Solreign;

[TestFixture]
public sealed class WingmatePresentationTests
{
    [Test]
    public void VolunteerCharterContainsEveryRequiredSafetyBoundary()
    {
        Assert.That(WingmateVolunteerCharter.TermKeys, Is.EqualTo(new[]
        {
            "wingmates-charter-teach-without-taking-over",
            "wingmates-charter-ask-before-spoilers",
            "wingmates-charter-keep-in-game",
            "wingmates-charter-no-private-contact",
            "wingmates-charter-no-hazing-pressure",
            "wingmates-charter-no-staff-authority",
            "wingmates-charter-respect-boundaries",
        }));
    }

    [TestCase(true, false, false, false)]
    [TestCase(true, false, true, true)]
    [TestCase(false, false, true, false)]
    [TestCase(false, true, false, true)]
    public void VolunteerOptInRequiresCharterAcceptanceButOptOutRemainsReachable(
        bool canVolunteer, bool isVolunteering, bool charterAccepted, bool expected)
    {
        Assert.That(WingmatePresentation.CanSubmitVolunteerChange(canVolunteer, isVolunteering, charterAccepted),
            Is.EqualTo(expected));
    }

    [TestCase(WingmateUiMode.Idle, nameof(WingmatePresentation.ShowRequest))]
    [TestCase(WingmateUiMode.Seeking, nameof(WingmatePresentation.ShowWaiting))]
    [TestCase(WingmateUiMode.OfferAvailable, nameof(WingmatePresentation.ShowOffer))]
    [TestCase(WingmateUiMode.OfferReceived, nameof(WingmatePresentation.ShowProposal))]
    [TestCase(WingmateUiMode.Paired, nameof(WingmatePresentation.ShowPair))]
    [TestCase(WingmateUiMode.Disabled, nameof(WingmatePresentation.ShowDisabled))]
    public void EachModeShowsExactlyOnePrimaryPanel(WingmateUiMode mode, string expectedPanel)
    {
        var presentation = WingmatePresentation.For(mode);
        var visiblePanels = new Dictionary<string, bool>
        {
            [nameof(presentation.ShowRequest)] = presentation.ShowRequest,
            [nameof(presentation.ShowWaiting)] = presentation.ShowWaiting,
            [nameof(presentation.ShowOffer)] = presentation.ShowOffer,
            [nameof(presentation.ShowProposal)] = presentation.ShowProposal,
            [nameof(presentation.ShowPair)] = presentation.ShowPair,
            [nameof(presentation.ShowDisabled)] = presentation.ShowDisabled,
        };

        Assert.Multiple(() =>
        {
            Assert.That(visiblePanels.Count(panel => panel.Value), Is.EqualTo(1));
            Assert.That(visiblePanels[expectedPanel], Is.True);
        });
    }

    [Test]
    public void PairedModeExposesConsentAndHelpControls()
    {
        var presentation = WingmatePresentation.For(WingmateUiMode.Paired);

        Assert.Multiple(() =>
        {
            Assert.That(presentation.ShowDissolve, Is.True);
            Assert.That(presentation.ShowBlock, Is.True);
            Assert.That(presentation.ShowModeratorHelp, Is.True);
        });
    }

    [Test]
    public void IdleModeExposesVolunteerConsentControl()
    {
        Assert.That(WingmatePresentation.For(WingmateUiMode.Idle).ShowVolunteer, Is.True);
    }

    [Test]
    public void AvailableOfferKeepsVolunteerOptOutReachable()
    {
        Assert.That(WingmatePresentation.For(WingmateUiMode.OfferAvailable).ShowVolunteer, Is.True);
    }

    [Test]
    public void PairedAndPausedModesExposeCorrectSafetyControls()
    {
        var paired = WingmatePresentation.For(WingmateUiMode.Paired);
        var paused = WingmatePresentation.For(WingmateUiMode.Paused);
        Assert.Multiple(() =>
        {
            Assert.That(paired.ShowPause, Is.True);
            Assert.That(paired.ShowResume, Is.False);
            Assert.That(paused.ShowPause, Is.False);
            Assert.That(paused.ShowResume, Is.True);
            Assert.That(paused.ShowDissolve, Is.True);
            Assert.That(paused.ShowBlock, Is.True);
            Assert.That(paused.ShowModeratorHelp, Is.True);
        });
    }

    [Test]
    public void DisabledModeExposesNoMutationControls()
    {
        var presentation = WingmatePresentation.For(WingmateUiMode.Disabled);

        Assert.Multiple(() =>
        {
            Assert.That(presentation.ShowRequest, Is.False);
            Assert.That(presentation.ShowWaiting, Is.False);
            Assert.That(presentation.ShowOffer, Is.False);
            Assert.That(presentation.ShowProposal, Is.False);
            Assert.That(presentation.ShowPair, Is.False);
            Assert.That(presentation.ShowDissolve, Is.False);
            Assert.That(presentation.ShowBlock, Is.False);
            Assert.That(presentation.ShowModeratorHelp, Is.False);
        });
    }

    [Test]
    public void SnapshotIndexKeysStateByBeaconAndRejectsStaleGenerations()
    {
        var firstBeacon = new NetEntity(101);
        var secondBeacon = new NetEntity(202);
        var index = new WingmateSnapshotIndex();
        var idle = State(WingmateUiMode.Idle, "idle");
        var seeking = State(WingmateUiMode.Seeking, "seeking");
        var paired = State(WingmateUiMode.Paired, "paired");

        Assert.Multiple(() =>
        {
            Assert.That(index.TryUpdate(firstBeacon, 2, seeking), Is.True);
            Assert.That(index.TryUpdate(firstBeacon, 1, idle), Is.False);
            Assert.That(index.TryUpdate(firstBeacon, 2, paired), Is.False);
            Assert.That(index.TryUpdate(secondBeacon, 1, paired), Is.True);
        });

        Assert.Multiple(() =>
        {
            Assert.That(index.TryGet(firstBeacon, out var first), Is.True);
            Assert.That(first.Generation, Is.EqualTo(2));
            Assert.That(first.State, Is.SameAs(seeking));
            Assert.That(index.TryGet(secondBeacon, out var second), Is.True);
            Assert.That(second.State, Is.SameAs(paired));
        });
    }

    [Test]
    public void SnapshotIndexClearAllowsNewRoundGeneration()
    {
        var beacon = new NetEntity(303);
        var index = new WingmateSnapshotIndex();
        var oldRound = State(WingmateUiMode.Paired, "old round");
        var newRound = State(WingmateUiMode.Idle, "new round");

        Assert.That(index.TryUpdate(beacon, 9, oldRound), Is.True);
        index.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(index.TryUpdate(beacon, 1, newRound), Is.True);
            Assert.That(index.TryGet(beacon, out var current), Is.True);
            Assert.That(current.State, Is.SameAs(newRound));
        });
    }

    [Test]
    public void TransitionReceiptRejectsBuiReplaysAndClearsForNewRound()
    {
        var beacon = new NetEntity(404);
        var otherBeacon = new NetEntity(405);
        var index = new WingmateSnapshotIndex();

        Assert.Multiple(() =>
        {
            Assert.That(index.TryConsumeTransition(beacon, 7), Is.True,
                "the first BUI instance must surface a new rejection");
            Assert.That(index.TryConsumeTransition(beacon, 7), Is.False,
                "a replacement BUI must not replay the cached rejection");
            Assert.That(index.TryConsumeTransition(beacon, 6), Is.False,
                "an older cached snapshot must not re-arm the popup");
            Assert.That(index.TryConsumeTransition(otherBeacon, 7), Is.True,
                "transition receipts are beacon-scoped");
            Assert.That(index.TryConsumeTransition(beacon, 8), Is.True,
                "a genuinely newer rejection must still surface");
        });

        index.Clear();

        Assert.That(index.TryConsumeTransition(beacon, 7), Is.True,
            "round cleanup must clear client-side transition receipts");
    }

    private static WingmateUiState State(WingmateUiMode mode, string status)
    {
        return new WingmateUiState(
            mode,
            string.Empty,
            WingmateTeachingMode.LearnByDoing,
            null,
            null,
            null,
            null,
            mode == WingmateUiMode.Idle,
            false,
            status);
    }
}
