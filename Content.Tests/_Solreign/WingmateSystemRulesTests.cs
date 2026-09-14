using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._Solreign.PlayerDelight.Wingmates;
using Content.Shared._Solreign.PlayerDelight.Wingmates;
using NUnit.Framework;
using Robust.Shared.Network;
using Robust.Shared.GameObjects;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(WingmateSystem))]
public sealed class WingmateSystemRulesTests
{
    private static readonly NetUserId Requester = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly NetUserId Guide = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));

    [TestCase("Engineering")]
    [TestCase("Medical")]
    [TestCase("Service")]
    [TestCase("Science")]
    [TestCase("Security")]
    [TestCase("Cargo")]
    [TestCase("Command")]
    public void DepartmentAllowlistAcceptsOnlyNamedDepartments(string department)
    {
        Assert.That(WingmateSystem.IsAllowedDepartmentForTests(department), Is.True);
    }

    [TestCase("")]
    [TestCase("engineering")]
    [TestCase("Syndicate")]
    [TestCase("Engineering ")]
    public void DepartmentAllowlistRejectsEverythingElse(string department)
    {
        Assert.That(WingmateSystem.IsAllowedDepartmentForTests(department), Is.False);
    }

    [Test]
    public void SharedTeachingModeMappingIsExplicitAndExhaustive()
    {
        foreach (var mode in Enum.GetValues<Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode>())
        {
            var mapped = WingmateSystem.MapTeachingModeForTests(mode);
            Assert.That((byte) mapped, Is.EqualTo((byte) mode));
        }
    }

    [Test]
    public void MalformedTeachingModeIsRejectedWithoutThrowing()
    {
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);

        WingmateTransitionResult result = default;
        Assert.DoesNotThrow(() => result = system.RequestForTests(Requester, "Engineering",
            (Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode) byte.MaxValue));
        Assert.Multiple(() =>
        {
            Assert.That(result.Changed, Is.False);
            Assert.That(result.Reason, Is.EqualTo("invalid-teaching-mode"));
        });
    }

    [Test]
    public void DisabledFeatureRejectsMutationAndClearsTransientState()
    {
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);
        Assert.That(system.RequestForTests(Requester, "Engineering", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.LearnByDoing).Changed, Is.True);

        system.SetEnabledForTests(false);

        Assert.Multiple(() =>
        {
            Assert.That(system.GetStatusForTests(Requester), Is.EqualTo(WingmateStatus.Idle));
            Assert.That(system.RequestForTests(Requester, "Medical", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour).Changed, Is.False);
        });
    }

    [Test]
    public void GuideMustBeApprovedAndOptedInBeforeOffering()
    {
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);
        system.RequestForTests(Requester, "Engineering", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour);

        Assert.That(system.OfferForTests(Guide, Requester).Changed, Is.False);

        system.SeedApprovedGuideForTests(Guide);
        Assert.That(system.OfferForTests(Guide, Requester).Reason, Is.EqualTo("not-volunteering"));

        Assert.That(system.SetVolunteeringForTests(Guide, volunteering: true, charterAccepted: false).Changed, Is.False);
        Assert.That(system.SetVolunteeringForTests(Guide, volunteering: true, charterAccepted: true).Changed, Is.True);
        Assert.That(system.OfferForTests(Guide, Requester).Changed, Is.True);
    }

    [Test]
    public void VolunteerCanOptOutAndRoundDisableAndDisconnectClearConsent()
    {
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);
        system.SeedApprovedGuideForTests(Guide);

        system.SetVolunteeringForTests(Guide, true, true);
        Assert.That(system.IsVolunteeringForTests(Guide), Is.True);
        system.SetVolunteeringForTests(Guide, false, false);
        Assert.That(system.IsVolunteeringForTests(Guide), Is.False);

        system.SetVolunteeringForTests(Guide, true, true);
        system.SessionUnavailableForTests(Guide);
        Assert.That(system.IsVolunteeringForTests(Guide), Is.False);

        system.SetVolunteeringForTests(Guide, true, true);
        system.SetEnabledForTests(false);
        Assert.That(system.IsVolunteeringForTests(Guide), Is.False);
    }

    [Test]
    public void VolunteerWithdrawalAtomicallyCancelsPendingOffer()
    {
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);
        system.SeedApprovedGuideForTests(Guide);
        system.SetVolunteeringForTests(Guide, true, true);
        system.RequestForTests(Requester, "Engineering", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour);
        var offer = system.OfferForTests(Guide, Requester);

        var withdrawal = system.SetVolunteeringForTests(Guide, false, false);

        Assert.Multiple(() =>
        {
            Assert.That(withdrawal.AffectedUsers, Is.EquivalentTo(new[] { Guide, Requester }));
            Assert.That(system.GetIncomingOfferForTests(Requester), Is.Null);
            Assert.That(system.AcceptForTests(Requester, offer.OfferNonce!.Value).Reason, Is.EqualTo("stale-offer"));
        });
    }

    [Test]
    public void BlockedFirstSeekerIsNotDisclosedAndNextEligibleSeekerIsShown()
    {
        var other = new NetUserId(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);
        system.SeedApprovedGuideForTests(Guide);
        system.SetVolunteeringForTests(Guide, true, true);
        system.RequestForTests(Requester, "Engineering", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour);
        system.RequestForTests(other, "Medical", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.LearnByDoing);
        system.BlockUsersForTests(Guide, Requester);

        var snapshot = system.BuildPrivateStateForTests(Guide);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.RequesterToken, Is.EqualTo(system.GetRequesterTokenForTests(Guide, other)));
            Assert.That(snapshot.Department, Is.EqualTo("Medical"));
            Assert.That(snapshot.RequesterToken, Is.Not.EqualTo(system.GetRequesterTokenForTests(Guide, Requester)));
        });
    }

    [Test]
    public void BlockCurrentPartnerWaitsForDurableConfirmationBeforeTakingEffect()
    {
        var system = CreatePairedSystem();
        system.SetPersistentBlockWriterForTests((_, _) => Task.FromResult(true));

        var pending = system.BlockCurrentPartnerForTests(Requester);

        // Nothing observable happens synchronously: no async void, no optimistic mutation. The pair
        // stays paired and the block is not yet enforceable until the write is confirmed and drained.
        Assert.Multiple(() =>
        {
            Assert.That(pending.Changed, Is.False);
            Assert.That(pending.Reason, Is.EqualTo("block-pending"));
            Assert.That(system.GetStatusForTests(Requester), Is.EqualTo(WingmateStatus.Paired));
            Assert.That(system.GetStatusForTests(Guide), Is.EqualTo(WingmateStatus.Paired));
            Assert.That(system.IsPersistentlyBlockedForTests(Requester, Guide), Is.False);
        });

        system.DrainCompletedBlockWritesForTests();

        Assert.Multiple(() =>
        {
            Assert.That(system.GetStatusForTests(Requester), Is.EqualTo(WingmateStatus.Dissolved));
            Assert.That(system.GetStatusForTests(Guide), Is.EqualTo(WingmateStatus.Dissolved));
            Assert.That(system.IsPersistentlyBlockedForTests(Requester, Guide), Is.True);
        });

        // Idempotent once confirmed: a second attempt resolves immediately (no new write) and reports
        // no further change.
        Assert.That(system.BlockCurrentPartnerForTests(Requester).Changed, Is.False);
    }

    [Test]
    public void FailedDurableBlockWriteLeavesStateUnchangedAndNotifiesTheBlocker()
    {
        var system = CreatePairedSystem();
        var failures = new List<NetUserId>();
        system.SetPersistentBlockWriterForTests((_, _) => Task.FromResult(false));

        var pending = system.BlockCurrentPartnerForTests(Requester);
        system.DrainCompletedBlockWritesForTests(failures.Add);

        Assert.Multiple(() =>
        {
            Assert.That(pending.Reason, Is.EqualTo("block-pending"));
            Assert.That(system.GetStatusForTests(Requester), Is.EqualTo(WingmateStatus.Paired),
                "a failed durable write must leave the pair exactly as it was — no partial effect");
            Assert.That(system.GetStatusForTests(Guide), Is.EqualTo(WingmateStatus.Paired));
            Assert.That(system.IsPersistentlyBlockedForTests(Requester, Guide), Is.False);
            Assert.That(failures, Is.EqualTo(new[] { Requester }));
        });
    }

    [Test]
    public void DurableBlockWriteExceptionIsCaughtNotUnobservedAndStillDrains()
    {
        // The write delegate throwing must not crash the drain or leak an unobserved task exception —
        // it must be caught and reported through the same failure path as an ordinary false result.
        var system = CreatePairedSystem();
        var failures = new List<NetUserId>();
        system.SetPersistentBlockWriterForTests((_, _) => throw new InvalidOperationException("ledger unavailable"));

        system.BlockCurrentPartnerForTests(Requester);
        system.DrainCompletedBlockWritesForTests(failures.Add);

        Assert.Multiple(() =>
        {
            Assert.That(system.GetStatusForTests(Requester), Is.EqualTo(WingmateStatus.Paired));
            Assert.That(system.IsPersistentlyBlockedForTests(Requester, Guide), Is.False);
            Assert.That(failures, Is.EqualTo(new[] { Requester }));
        });
    }

    [Test]
    public void ConcurrentDuplicateBlockAttemptsWhileAWriteIsPendingAreCoalesced()
    {
        var system = CreatePairedSystem();
        system.SetPersistentBlockWriterForTests((_, _) => Task.FromResult(true));

        var first = system.BlockCurrentPartnerForTests(Requester);
        var second = system.BlockCurrentPartnerForTests(Requester);

        Assert.Multiple(() =>
        {
            Assert.That(first.Reason, Is.EqualTo("block-pending"));
            Assert.That(second.Reason, Is.EqualTo("block-pending"));
            Assert.That(system.GetStatusForTests(Requester), Is.EqualTo(WingmateStatus.Paired));
        });

        system.DrainCompletedBlockWritesForTests();

        Assert.That(system.GetStatusForTests(Requester), Is.EqualTo(WingmateStatus.Dissolved));
    }

    [Test]
    public void PendingDurableBlockRejectsReofferAcrossARoundBoundaryUntilDrained()
    {
        // Regression for the pending-write re-offer gap: WingmateRoundState.Clear() (run by a round
        // restart or feature toggle) wipes the ordinary decline-cooldown along with everything else
        // round-local, but a decline-time block whose SQLite write is still in flight must keep
        // rejecting offers between that account pair regardless — the durable-block bookkeeping is
        // deliberately round-independent (see BeginPersistentBlock/_pendingPersistentBlocks).
        var system = CreatePairedSystem();
        var pendingWrite = new TaskCompletionSource<bool>();
        system.SetPersistentBlockWriterForTests((_, _) => pendingWrite.Task);

        var pending = system.BlockCurrentPartnerForTests(Requester);
        Assert.Multiple(() =>
        {
            Assert.That(pending.Reason, Is.EqualTo("block-pending"));
            Assert.That(system.IsPersistentlyBlockedForTests(Requester, Guide), Is.False,
                "not yet confirmed durable");
            Assert.That(system.IsBlockedOrPendingForTests(Requester, Guide), Is.True,
                "but a write is in flight, so the pending-inclusive gate must already treat this pair as blocked");
        });

        // A round boundary clears decline cooldowns, approvals, and pairing state — but must not open a
        // re-offer window while the durable write for the earlier block is still unresolved.
        system.RoundRestartForTests();
        system.SeedApprovedGuideForTests(Guide);
        system.SetVolunteeringForTests(Guide, true, true);
        system.RequestForTests(Requester, "Engineering", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour);

        var reoffer = system.OfferForTests(Guide, Requester);

        Assert.Multiple(() =>
        {
            Assert.That(reoffer.Changed, Is.False);
            Assert.That(reoffer.Reason, Is.EqualTo("blocked"));
        });

        // Resolution, confirmed side: once the write lands successfully and drains, the block becomes
        // durably confirmed and the pending entry is gone — both gates now agree.
        pendingWrite.SetResult(true);
        system.DrainCompletedBlockWritesForTests();

        Assert.Multiple(() =>
        {
            Assert.That(system.IsPersistentlyBlockedForTests(Requester, Guide), Is.True);
            Assert.That(system.IsBlockedOrPendingForTests(Requester, Guide), Is.True);
        });
    }

    [Test]
    public void PendingDurableBlockThatFailsReleasesTheOfferGateOnceDrained()
    {
        // Resolution, removed side: a write that ultimately fails must not leave the pending-inclusive
        // gate stuck "blocked" forever — once DrainCompletedBlockWrites processes the failure, the pair
        // is exactly as unblocked as before the attempt.
        var system = CreatePairedSystem();
        var pendingWrite = new TaskCompletionSource<bool>();
        system.SetPersistentBlockWriterForTests((_, _) => pendingWrite.Task);

        system.BlockCurrentPartnerForTests(Requester);
        Assert.That(system.IsBlockedOrPendingForTests(Requester, Guide), Is.True,
            "in-flight write must close the offer window even before confirmation");

        pendingWrite.SetResult(false);
        system.DrainCompletedBlockWritesForTests();

        Assert.Multiple(() =>
        {
            Assert.That(system.IsPersistentlyBlockedForTests(Requester, Guide), Is.False);
            Assert.That(system.IsBlockedOrPendingForTests(Requester, Guide), Is.False,
                "a failed write must release the pending-inclusive gate too, not just the confirmed set");
        });
    }

    [Test]
    public void StaleEpochFailedBlockWriteIsRetainedAndClassifiedAsPreviousShift()
    {
        // Round-epoch handling for failures (cdx r3 finding 1): a write kicked off pre-restart that
        // resolves with failure only after ClearRound() has bumped the epoch must NOT be presented as a
        // current-round event — but it must never be silently lost either. It records a failed
        // cross-round safety action; the epoch changes its WORDING (previous-shift phrasing at
        // delivery), never its existence.
        var system = CreatePairedSystem();
        var epochAtWriteStart = system.GetRoundEpochForTests();
        var pendingWrite = new TaskCompletionSource<bool>();
        system.SetPersistentBlockWriterForTests((_, _) => pendingWrite.Task);

        system.BlockCurrentPartnerForTests(Requester);
        system.RoundRestartForTests();
        pendingWrite.SetResult(false);
        // Default failure path (no override): the record must be queued, not dropped, even though its
        // epoch is stale. (Delivery can't land in a rules harness — no IoC session — which is exactly
        // the retention case.)
        system.DrainCompletedBlockWritesForTests();

        Assert.Multiple(() =>
        {
            Assert.That(system.HasPendingBlockFailureNoticeForTests(Requester), Is.True,
                "a stale-epoch failure must be retained until delivered, never silently discarded");
            Assert.That(system.GetPendingBlockFailureCountForTests(Requester), Is.EqualTo(1));
            Assert.That(system.IsPersistentlyBlockedForTests(Requester, Guide), Is.False);
            Assert.That(system.GetRoundEpochForTests(), Is.Not.EqualTo(epochAtWriteStart),
                "test setup sanity: the round restart must actually move the epoch forward");
            Assert.That(system.GetPendingBlockFailureTallyForTests(Requester), Is.EqualTo((0, 1)),
                "a retained stale failure must classify as previous-shift, not as a current-round event");
        });
    }

    [Test]
    public void UndeliveredBlockFailureNoticeSurvivesRoundRollover()
    {
        // cdx r3 finding 1, ClearRound leg: a failure already queued (player unattached at drain time)
        // must survive the round boundary — ClearRound() clears every round-local set but must NOT
        // delete undelivered failure records. Only actual delivery (or operator feature-disable)
        // removes them.
        var system = CreatePairedSystem();
        system.SetPersistentBlockWriterForTests((_, _) => Task.FromResult(false));

        system.BlockCurrentPartnerForTests(Requester);
        system.DrainCompletedBlockWritesForTests();
        Assert.That(system.HasPendingBlockFailureNoticeForTests(Requester), Is.True);

        system.RoundRestartForTests();

        Assert.Multiple(() =>
        {
            Assert.That(system.HasPendingBlockFailureNoticeForTests(Requester), Is.True,
                "round rollover must never lose the fact that a durable safety action failed");
            Assert.That(system.GetPendingBlockFailureCountForTests(Requester), Is.EqualTo(1));
        });
    }

    [Test]
    public void FeatureDisableDropsUndeliveredBlockFailureNotices()
    {
        // cdx r3 finding 3: unlike a round rollover, an operator feature-disable IS allowed to drop
        // undelivered failure notices — a disable/re-enable cycle must not replay a notice from before
        // the operator reset the service. This is the one sanctioned loss path, by design.
        var system = CreatePairedSystem();
        system.SetPersistentBlockWriterForTests((_, _) => Task.FromResult(false));

        system.BlockCurrentPartnerForTests(Requester);
        system.DrainCompletedBlockWritesForTests();
        Assert.That(system.HasPendingBlockFailureNoticeForTests(Requester), Is.True);

        system.SetEnabledForTests(false);
        system.SetEnabledForTests(true);

        Assert.That(system.HasPendingBlockFailureNoticeForTests(Requester), Is.False,
            "a disable/re-enable cycle must not carry a stale queued notice back into service");
    }

    [Test]
    public void BlockFailureNoticePartsAggregateIntoOnePopupWithOnlyCounts()
    {
        // Aggregation/identity leg (pure seam — wording itself is asserted at the delivery level in
        // integration where Loc is live): however many failures are tallied, the notice is at most TWO
        // lines in ONE popup (current-shift line + previous-shift line), each carrying only a count —
        // no blocked-party identity can even reach the text builder.
        Assert.Multiple(() =>
        {
            // Singular, current shift.
            Assert.That(WingmateSystem.BuildBlockFailureNoticeParts(1, 0),
                Is.EqualTo(new[] { ("wingmates-block-persistence-failed-count", 1) }));
            // Two failures, same shift: one part, count-accurate — never two popups.
            Assert.That(WingmateSystem.BuildBlockFailureNoticeParts(2, 0),
                Is.EqualTo(new[] { ("wingmates-block-persistence-failed-count", 2) }));
            // Stale only: previous-shift wording.
            Assert.That(WingmateSystem.BuildBlockFailureNoticeParts(0, 2),
                Is.EqualTo(new[] { ("wingmates-block-persistence-failed-previous-shift", 2) }));
            // Mixed: both lines, still a single aggregated delivery.
            Assert.That(WingmateSystem.BuildBlockFailureNoticeParts(1, 1),
                Is.EqualTo(new[]
                {
                    ("wingmates-block-persistence-failed-count", 1),
                    ("wingmates-block-persistence-failed-previous-shift", 1),
                }));
            // Nothing tallied: nothing to say.
            Assert.That(WingmateSystem.BuildBlockFailureNoticeParts(0, 0), Is.Empty);
        });
    }

    [Test]
    public void FailureTallyCoalescesToTwoBoundedCountersAcrossManyFailuresAndRounds()
    {
        // cdx r4 finding 2: per-user failure state must be O(1) — a bounded tally of (this-shift,
        // previous-shift) counts — not one record per failure, so a ledger outage can't grow memory
        // without bound. This drives many failures across two rounds through the REAL drain path and
        // asserts the delivery-facing tally stays exactly two counters with accurate totals.
        var otherA = new NetUserId(Guid.Parse("55555555-5555-5555-5555-555555555555"));
        var otherB = new NetUserId(Guid.Parse("66666666-6666-6666-6666-666666666666"));
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);
        system.SetPersistentBlockWriterForTests((_, _) => Task.FromResult(false));

        // Three failed block writes against three different partners in round N.
        system.BeginPersistentBlockForTests(Requester, Guide);
        system.BeginPersistentBlockForTests(Requester, otherA);
        system.BeginPersistentBlockForTests(Requester, otherB);
        system.DrainCompletedBlockWritesForTests();
        Assert.That(system.GetPendingBlockFailureTallyForTests(Requester), Is.EqualTo((3, 0)));

        // Round rolls over: all three re-classify as previous-shift; two more fail in round N+1.
        system.RoundRestartForTests();
        Assert.That(system.GetPendingBlockFailureTallyForTests(Requester), Is.EqualTo((0, 3)));

        system.BeginPersistentBlockForTests(Requester, Guide);
        system.BeginPersistentBlockForTests(Requester, otherA);
        system.DrainCompletedBlockWritesForTests();

        Assert.Multiple(() =>
        {
            Assert.That(system.GetPendingBlockFailureTallyForTests(Requester), Is.EqualTo((2, 3)),
                "five failures across two rounds must coalesce into exactly two accurate counters");
            Assert.That(system.GetPendingBlockFailureCountForTests(Requester), Is.EqualTo(5));
        });
    }

    [Test]
    public void WriteInFlightAcrossFeatureDisableNeverProducesANotice()
    {
        // cdx r4 finding 1: a write in flight when the operator disables the feature must be fully
        // suppressed — its failure result must produce NO notice, neither during the maintenance
        // window nor after re-enable, and must not be mislabeled "previous shift" off the disable's
        // epoch bump. (Round rollover retains failures; operator disable is the one sanctioned drop.)
        var system = CreatePairedSystem();
        var pendingWrite = new TaskCompletionSource<bool>();
        system.SetPersistentBlockWriterForTests((_, _) => pendingWrite.Task);

        system.BlockCurrentPartnerForTests(Requester);

        // Operator disables while the write is in flight, then re-enables.
        system.SetEnabledForTests(false);
        system.SetEnabledForTests(true);

        // The write fails only after re-enable; the first drain afterwards must discard the notice.
        pendingWrite.SetResult(false);
        system.DrainCompletedBlockWritesForTests();

        Assert.Multiple(() =>
        {
            Assert.That(system.HasPendingBlockFailureNoticeForTests(Requester), Is.False,
                "a maintenance-straddling failure must never surface as a popup, before or after re-enable");
            Assert.That(system.IsPersistentlyBlockedForTests(Requester, Guide), Is.False);
            Assert.That(system.IsBlockedOrPendingForTests(Requester, Guide), Is.False,
                "the drained failure must still release the pending gate as usual");
        });
    }

    [Test]
    public void FailureCompletedButUndrainedWhenDisableHitsIsAlsoSuppressed()
    {
        // cdx r4 finding 1, completed-but-undrained leg: a failure result already sitting in the
        // completed-write queue when the operator disables (i.e. it completed but no Update() drained
        // it yet) must be equally suppressed at the next enabled drain.
        var system = CreatePairedSystem();
        system.SetPersistentBlockWriterForTests((_, _) => Task.FromResult(false));

        system.BlockCurrentPartnerForTests(Requester);
        // No drain here — the result is enqueued but unprocessed when the disable lands.
        system.SetEnabledForTests(false);
        system.SetEnabledForTests(true);
        system.DrainCompletedBlockWritesForTests();

        Assert.That(system.HasPendingBlockFailureNoticeForTests(Requester), Is.False,
            "a failure that completed before the disable but drained after must not become a popup");
    }

    [Test]
    public void SuccessStraddlingFeatureDisableStillConfirmsTheDurableBlock()
    {
        // Companion guard for the service-epoch discard: only failure NOTICES are suppressed by a
        // disable. A write that SUCCEEDS across the maintenance window still durably confirmed in
        // SQLite, and the in-memory confirmed set must agree with the ledger or offers after re-enable
        // would diverge from what the next boot reloads.
        var system = CreatePairedSystem();
        var pendingWrite = new TaskCompletionSource<bool>();
        system.SetPersistentBlockWriterForTests((_, _) => pendingWrite.Task);

        system.BlockCurrentPartnerForTests(Requester);
        system.SetEnabledForTests(false);
        system.SetEnabledForTests(true);
        pendingWrite.SetResult(true);
        system.DrainCompletedBlockWritesForTests();

        Assert.That(system.IsPersistentlyBlockedForTests(Requester, Guide), Is.True,
            "a maintenance-straddling SUCCESS must still confirm — memory must not diverge from SQLite");
    }

    [Test]
    public void StaleEpochSuccessfulBlockWriteStillConfirmsTheDurableBlockUnlikeAFailure()
    {
        // The success variant of the round-epoch fix: unlike a failure, a write that ultimately
        // SUCCEEDS but resolves after a round boundary is still applied — the durable block it confirms
        // is deliberately round-independent (see BeginPersistentBlock/_pendingPersistentBlocks and
        // DrainCompletedBlockWrites), and the pending-inclusive gate already prevented this exact pair
        // from re-pairing with each other while the write was in flight, so applying it late cannot
        // touch an unrelated new-round pairing. Only a stale FAILURE's notice is dropped (see
        // StaleEpochFailedBlockWriteIsDroppedAndProducesNoNotice) — epoch-staleness is not a blanket
        // "ignore this write" rule.
        var system = CreatePairedSystem();
        var pendingWrite = new TaskCompletionSource<bool>();
        system.SetPersistentBlockWriterForTests((_, _) => pendingWrite.Task);

        system.BlockCurrentPartnerForTests(Requester);
        var epochAtWriteStart = system.GetRoundEpochForTests();
        system.RoundRestartForTests();

        Assert.That(system.GetRoundEpochForTests(), Is.Not.EqualTo(epochAtWriteStart),
            "test setup sanity: the round restart must actually move the epoch forward");

        pendingWrite.SetResult(true);
        system.DrainCompletedBlockWritesForTests();

        Assert.Multiple(() =>
        {
            Assert.That(system.IsPersistentlyBlockedForTests(Requester, Guide), Is.True,
                "a stale-epoch SUCCESS must still confirm — only failure notices are epoch-gated");
            Assert.That(system.HasPendingBlockFailureNoticeForTests(Requester), Is.False,
                "a success must never be routed through the failure-notice path regardless of epoch");
        });
    }

    [Test]
    public void TwoFailedBlockWritesAgainstDifferentPartnersProduceACountAccurateNotice()
    {
        // Regression for the failure-collapse bug: the old HashSet<NetUserId> store meant a second
        // failed block write (against a DIFFERENT partner) was indistinguishable from the first — the
        // player could not know both safety actions failed. Two distinct failures for the same blocker
        // must now be counted, not collapsed.
        var otherGuide = new NetUserId(Guid.Parse("55555555-5555-5555-5555-555555555555"));
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);
        system.SeedApprovedGuideForTests(Guide);
        system.SeedApprovedGuideForTests(otherGuide);
        system.SetVolunteeringForTests(Guide, true, true);
        system.SetVolunteeringForTests(otherGuide, true, true);
        system.SetPersistentBlockWriterForTests((_, _) => Task.FromResult(false));

        system.RequestForTests(Requester, "Engineering", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour);
        var firstOffer = system.OfferForTests(Guide, Requester);
        system.DeclineForTests(Requester, firstOffer.OfferNonce!.Value, blockGuide: true);

        system.RequestForTests(Requester, "Medical", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour);
        var secondOffer = system.OfferForTests(otherGuide, Requester);
        system.DeclineForTests(Requester, secondOffer.OfferNonce!.Value, blockGuide: true);

        system.DrainCompletedBlockWritesForTests();

        Assert.Multiple(() =>
        {
            Assert.That(system.HasPendingBlockFailureNoticeForTests(Requester), Is.True);
            Assert.That(system.GetPendingBlockFailureCountForTests(Requester), Is.EqualTo(2),
                "two failures against two different partners must both be counted, not collapsed into one");
        });
    }

    [Test]
    public void PendingUnconfirmedBlockSuppressesRequesterFromGuidesSeekerView()
    {
        // Regression for the pending-suppression seeker-visibility path (WingmateSystem.BuildPrivateState
        // filtering via IsBlockedOrPending, not the confirmed-only IsPersistentlyBlocked): reverting that
        // filter to confirmed-only would stay green on every other existing test while still exposing a
        // requester with an in-flight, unconfirmed block as a seeker to the very guide who blocked them.
        var system = CreatePairedSystem();
        var pendingWrite = new TaskCompletionSource<bool>();
        system.SetPersistentBlockWriterForTests((_, _) => pendingWrite.Task);

        // Requester begins a durable block against Guide; the write never resolves in this test, so it
        // stays pending (unconfirmed) — IsPersistentlyBlockedForTests must stay false throughout.
        system.BlockCurrentPartnerForTests(Requester);
        Assert.That(system.IsPersistentlyBlockedForTests(Requester, Guide), Is.False);

        // A round boundary clears pairing/status but not the round-independent pending-block bookkeeping.
        system.RoundRestartForTests();
        system.SeedApprovedGuideForTests(Guide);
        system.SetVolunteeringForTests(Guide, true, true);
        system.RequestForTests(Requester, "Engineering", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour);
        Assert.That(system.GetStatusForTests(Requester), Is.EqualTo(WingmateStatus.Seeking));

        var snapshot = system.BuildPrivateStateForTests(Guide);

        Assert.That(snapshot.RequesterToken, Is.Null,
            "a requester with an in-flight, unconfirmed block against this guide must not be surfaced as a seeker");
    }

    [Test]
    public void FailedDurableBlockWriteQueuesNoticeWhenSessionCannotReceiveItImmediately()
    {
        // Regression for the lost failure notification: a rules-test WingmateSystem never runs
        // Initialize(), so it has no IoC-wired IPlayerManager — exactly the "blocker disconnected/
        // unattached at drain time" condition the drain-time failure accumulation must not silently drop.
        // This exercises the real production default (no onFailure override), not a test double.
        var system = CreatePairedSystem();
        system.SetPersistentBlockWriterForTests((_, _) => Task.FromResult(false));

        system.BlockCurrentPartnerForTests(Requester);
        system.DrainCompletedBlockWritesForTests();

        Assert.That(system.HasPendingBlockFailureNoticeForTests(Requester), Is.True);
    }

    [Test]
    public void DeclineAndBlockDeclinesImmediatelyButOnlyBlocksAfterDurableConfirmation()
    {
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);
        system.SeedApprovedGuideForTests(Guide);
        system.SetVolunteeringForTests(Guide, true, true);
        system.SetPersistentBlockWriterForTests((_, _) => Task.FromResult(true));
        system.RequestForTests(Requester, "Engineering", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour);
        var offer = system.OfferForTests(Guide, Requester);

        var declined = system.DeclineForTests(Requester, offer.OfferNonce!.Value, blockGuide: true);

        Assert.Multiple(() =>
        {
            Assert.That(declined.Reason, Is.EqualTo("declined"));
            Assert.That(system.GetIncomingOfferForTests(Requester), Is.Null);
            Assert.That(system.GetStatusForTests(Requester), Is.EqualTo(WingmateStatus.Seeking),
                "declining is immediate and unconditional, independent of the durable write");
            Assert.That(system.IsPersistentlyBlockedForTests(Requester, Guide), Is.False,
                "the block has not been confirmed durable yet");
        });

        system.DrainCompletedBlockWritesForTests();

        Assert.Multiple(() =>
        {
            Assert.That(system.IsPersistentlyBlockedForTests(Requester, Guide), Is.True);
            Assert.That(system.OfferForTests(Guide, Requester).Reason, Is.EqualTo("blocked"));
        });
    }

    [Test]
    public void DeclineWithoutBlockGuideNeverStartsADurableWrite()
    {
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);
        system.SeedApprovedGuideForTests(Guide);
        system.SetVolunteeringForTests(Guide, true, true);
        system.RequestForTests(Requester, "Engineering", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour);
        var offer = system.OfferForTests(Guide, Requester);

        system.DeclineForTests(Requester, offer.OfferNonce!.Value, blockGuide: false);

        Assert.That(system.GetPendingBlockWriteTaskForTests(Requester, Guide), Is.Null);
    }

    [Test]
    public void DeclineOfStaleNonceWithBlockGuideDoesNotBlockAnyone()
    {
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);
        system.SeedApprovedGuideForTests(Guide);
        system.SetVolunteeringForTests(Guide, true, true);
        system.RequestForTests(Requester, "Engineering", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour);
        system.OfferForTests(Guide, Requester);

        var declined = system.DeclineForTests(Requester, Guid.NewGuid(), blockGuide: true);

        Assert.Multiple(() =>
        {
            Assert.That(declined.Reason, Is.EqualTo("stale-offer"));
            Assert.That(system.GetPendingBlockWriteTaskForTests(Requester, Guide), Is.Null);
        });
    }

    [Test]
    public void RequesterTokenIsRejectedWhenReplayedByADifferentGuide()
    {
        var otherGuide = new NetUserId(Guid.Parse("44444444-4444-4444-4444-444444444444"));
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);
        system.SeedApprovedGuideForTests(Guide);
        system.SeedApprovedGuideForTests(otherGuide);
        system.SetVolunteeringForTests(Guide, true, true);
        system.SetVolunteeringForTests(otherGuide, true, true);
        system.RequestForTests(Requester, "Engineering", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour);

        var guideToken = system.GetRequesterTokenForTests(Guide, Requester)!.Value;
        var otherGuideToken = system.GetRequesterTokenForTests(otherGuide, Requester)!.Value;

        Assert.Multiple(() =>
        {
            Assert.That(guideToken, Is.Not.EqualTo(otherGuideToken));
            Assert.That(system.OfferByTokenForTests(otherGuide, guideToken).Reason, Is.EqualTo("invalid-requester-token"));
            Assert.That(system.OfferByTokenForTests(Guide, guideToken).Changed, Is.True);
        });
    }

    [Test]
    public void RequestRateLimitWindowSurvivesReconnectForTheSameAccount()
    {
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);

        // Three requests fit within the window (department alternates so each call is a genuine new
        // attempt rather than an idempotent re-request).
        Assert.That(system.RequestForTests(Requester, "Engineering", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour).Changed, Is.True);
        Assert.That(system.RequestForTests(Requester, "Medical", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour).Changed, Is.True);
        Assert.That(system.RequestForTests(Requester, "Service", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour).Changed, Is.True);

        system.SessionUnavailableForTests(Requester);

        var afterReconnect = system.RequestForTests(Requester, "Science", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour);

        Assert.That(afterReconnect.Reason, Is.EqualTo("rate-limited"),
            "disconnect cleanup must not reset the request rate-limit window — otherwise reconnecting bypasses it");
    }

    [Test]
    public void OfferRateLimitWindowSurvivesReconnectForTheSameAccount()
    {
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);
        system.SeedApprovedGuideForTests(Guide);
        system.SetVolunteeringForTests(Guide, true, true);
        system.RequestForTests(Requester, "Engineering", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour);

        for (var i = 0; i < OfferAttemptsPerWindowForTests; i++)
            Assert.That(system.OfferForTests(Guide, Requester).Reason, Is.Not.EqualTo("rate-limited"));

        system.SessionUnavailableForTests(Guide);
        Assert.That(system.SetVolunteeringForTests(Guide, true, true).Changed, Is.True);

        var afterReconnect = system.OfferForTests(Guide, Requester);

        Assert.That(afterReconnect.Reason, Is.EqualTo("rate-limited"),
            "disconnect cleanup must not reset the offer rate-limit window — otherwise reconnecting bypasses it");
    }

    [Test]
    public void RoundRestartDoesResetRateLimitWindows()
    {
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);

        Assert.That(system.RequestForTests(Requester, "Engineering", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour).Changed, Is.True);
        Assert.That(system.RequestForTests(Requester, "Medical", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour).Changed, Is.True);
        Assert.That(system.RequestForTests(Requester, "Service", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour).Changed, Is.True);
        Assert.That(system.RequestForTests(Requester, "Science", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour).Reason,
            Is.EqualTo("rate-limited"));

        system.RoundRestartForTests();

        Assert.That(system.RequestForTests(Requester, "Engineering", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour).Changed, Is.True,
            "unlike a disconnect, a round boundary is exactly when rate-limit windows should reset");
    }

    private const int OfferAttemptsPerWindowForTests = 6;

    private static WingmateSystem CreatePairedSystem()
    {
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);
        system.SeedApprovedGuideForTests(Guide);
        system.SetVolunteeringForTests(Guide, true, true);
        system.RequestForTests(Requester, "Engineering", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour);
        var offer = system.OfferForTests(Guide, Requester);
        system.AcceptForTests(Requester, offer.OfferNonce!.Value);
        return system;
    }

    [Test]
    public void RequestAttemptsAreFixedWindowLimited()
    {
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);

        // Three requests fit within the window (department alternates so each call is a genuine new
        // attempt rather than an idempotent re-request).
        Assert.That(system.RequestForTests(Requester, "Engineering", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour).Changed, Is.True);
        Assert.That(system.RequestForTests(Requester, "Medical", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour).Changed, Is.True);
        Assert.That(system.RequestForTests(Requester, "Service", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour).Changed, Is.True);

        var fourth = system.RequestForTests(Requester, "Science", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour);

        Assert.Multiple(() =>
        {
            Assert.That(fourth.Changed, Is.False);
            Assert.That(fourth.Reason, Is.EqualTo("rate-limited"));
        });

        // A different user is unaffected by another user's window.
        var other = new NetUserId(Guid.Parse("44444444-4444-4444-4444-444444444444"));
        Assert.That(system.RequestForTests(other, "Engineering", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour).Changed, Is.True);
    }

    [Test]
    public void OfferAttemptsIncludingInvalidTokensAreFixedWindowLimited()
    {
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);
        system.SeedApprovedGuideForTests(Guide);
        system.SetVolunteeringForTests(Guide, true, true);

        // Malformed/forged tokens never resolve to a real requester, but they must still be metered —
        // otherwise an attacker could brute-force tokens through an unmetered rejection path.
        for (var i = 0; i < 6; i++)
        {
            var attempt = system.OfferByTokenForTests(Guide, Guid.NewGuid());
            Assert.That(attempt.Reason, Is.EqualTo("invalid-requester-token"));
        }

        var seventh = system.OfferByTokenForTests(Guide, Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(seventh.Changed, Is.False);
            Assert.That(seventh.Reason, Is.EqualTo("rate-limited"));
        });
    }

    [Test]
    public void PersistentBlockSuppressesSeekerAndRejectsOffer()
    {
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);
        system.SeedApprovedGuideForTests(Guide);
        system.SetVolunteeringForTests(Guide, true, true);
        system.RequestForTests(Requester, "Engineering", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour);
        system.SeedPersistentBlockForTests(Guide, Requester);

        var snapshot = system.BuildPrivateStateForTests(Guide);
        var offer = system.OfferForTests(Guide, Requester);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.RequesterToken, Is.Null, "a persistently-blocked requester must not be surfaced as a seeker");
            Assert.That(offer.Changed, Is.False);
            Assert.That(offer.Reason, Is.EqualTo("blocked"));
        });
    }

    [Test]
    public void OffersFailClosedUntilPersistentBlocksAreLoaded()
    {
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);
        system.SeedApprovedGuideForTests(Guide);
        system.SetVolunteeringForTests(Guide, true, true);
        system.RequestForTests(Requester, "Engineering", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour);

        // Simulate the window between Initialize() firing the async ledger load and it completing.
        system.SetPersistentBlocksReadyForTests(false);

        var duringLoad = system.OfferForTests(Guide, Requester);

        Assert.Multiple(() =>
        {
            Assert.That(duringLoad.Changed, Is.False);
            Assert.That(duringLoad.Reason, Is.EqualTo("blocks-loading"));
        });

        system.SetPersistentBlocksReadyForTests(true);
        Assert.That(system.OfferForTests(Guide, Requester).Changed, Is.True);
    }

    [Test]
    public void DisplayNameSeamUsesEntityNameNotAccountName()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WingmateSystem.ResolveDisplayNameForTests("AccountName123", "Captain Nova", hasAttachedEntity: true),
                Is.EqualTo("Captain Nova"));
            Assert.That(WingmateSystem.ResolveDisplayNameForTests("AccountName123", "Captain Nova", hasAttachedEntity: true),
                Is.Not.EqualTo("AccountName123"));
            Assert.That(WingmateSystem.ResolveDisplayNameForTests("AccountName123", null, hasAttachedEntity: false),
                Is.Null);
        });
    }

    [Test]
    public void ModeratorApprovalApiIsRoundLocalAndRevocationEndsParticipation()
    {
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);

        Assert.That(system.ApproveGuide(Guide).Changed, Is.True);
        Assert.That(system.SetVolunteeringForTests(Guide, true, true).Changed, Is.True);
        var before = system.GetModeratorSnapshot(Guide);

        var revoked = system.RevokeGuide(Guide);
        var after = system.GetModeratorSnapshot(Guide);

        Assert.Multiple(() =>
        {
            Assert.That(before.Approved, Is.True);
            Assert.That(before.Volunteering, Is.True);
            Assert.That(revoked.Changed, Is.True);
            Assert.That(after.Approved, Is.False);
            Assert.That(after.Volunteering, Is.False);
        });
    }

    [Test]
    public void RevokeGuideRecordsAHardDenyAndApproveGuideClearsIt()
    {
        // grk code review finding (HIGH), the pure-logic slice testable without a live session:
        // RevokeGuide must record a real denylist entry (_deniedGuides), not just remove the
        // approval — otherwise an account that also qualifies via auto-eligibility (untestable here
        // without IoC, see the integration suite for that half) would simply re-qualify on its next
        // check. A subsequent ApproveGuide must clear the denial (the "either way" override
        // guarantee working in reverse).
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);
        system.ApproveGuide(Guide);

        Assert.That(system.IsDeniedGuideForTests(Guide), Is.False, "test precondition: not yet denied");

        system.RevokeGuide(Guide);
        Assert.Multiple(() =>
        {
            Assert.That(system.IsDeniedGuideForTests(Guide), Is.True);
            Assert.That(system.IsGuideEligibleForTests(Guide), Is.False);
        });

        system.ApproveGuide(Guide);
        Assert.Multiple(() =>
        {
            Assert.That(system.IsDeniedGuideForTests(Guide), Is.False, "re-approval must clear the denial");
            Assert.That(system.IsGuideEligibleForTests(Guide), Is.True);
        });
    }

    [Test]
    public void DeniedGuideStatusIsRoundLocalLikeApprovedStatus()
    {
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);
        system.ApproveGuide(Guide);
        system.RevokeGuide(Guide);
        Assert.That(system.IsDeniedGuideForTests(Guide), Is.True);

        system.RoundRestartForTests();

        Assert.That(system.IsDeniedGuideForTests(Guide), Is.False,
            "a moderator's deny is a THIS-ROUND decision, mirroring _approvedGuides' own round-local lifetime");
    }

    [Test]
    public void ModeratorDissolveApiEndsBothSidesWithoutExposingPrivateState()
    {
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);
        system.ApproveGuide(Guide);
        system.SetVolunteeringForTests(Guide, true, true);
        system.RequestForTests(Requester, "Engineering", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour);
        var offer = system.OfferForTests(Guide, Requester);
        system.AcceptForTests(Requester, offer.OfferNonce!.Value);

        var result = system.DissolveForModerator(Requester);
        var snapshot = system.GetModeratorSnapshot(Requester);

        Assert.Multiple(() =>
        {
            Assert.That(result.Changed, Is.True);
            Assert.That(system.GetStatusForTests(Requester), Is.EqualTo(WingmateStatus.Dissolved));
            Assert.That(system.GetStatusForTests(Guide), Is.EqualTo(WingmateStatus.Dissolved));
            Assert.That(typeof(WingmateModeratorSnapshot).GetProperties().Select(property => property.Name),
                Is.EquivalentTo(new[] { "Status", "Approved", "Volunteering" }));
            Assert.That(snapshot.Status, Is.EqualTo(WingmateStatus.Dissolved));
        });
    }

    [Test]
    public void DisconnectOrZombieDissolvesPairImmediately()
    {
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);
        system.SeedApprovedGuideForTests(Guide);
        system.SetVolunteeringForTests(Guide, true, true);
        system.RequestForTests(Requester, "Engineering", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour);
        var offer = system.OfferForTests(Guide, Requester);
        system.AcceptForTests(Requester, offer.OfferNonce!.Value);

        system.SessionUnavailableForTests(Guide);

        Assert.Multiple(() =>
        {
            Assert.That(system.GetStatusForTests(Requester), Is.EqualTo(WingmateStatus.Dissolved));
            Assert.That(system.GetStatusForTests(Guide), Is.EqualTo(WingmateStatus.Dissolved));
        });
    }

    [Test]
    public void TestHooksAreInternalOnly()
    {
        var hooks = typeof(WingmateSystem).GetMethods(System.Reflection.BindingFlags.Instance |
                                                      System.Reflection.BindingFlags.Static |
                                                      System.Reflection.BindingFlags.NonPublic)
            .Select(method => method.Name)
            .ToHashSet();

        Assert.That(hooks, Does.Contain("GetStatusForTests"));
        Assert.That(hooks, Does.Contain("SeedApprovedGuideForTests"));
        Assert.That(hooks, Does.Contain("ClearForTests"));
        Assert.That(typeof(WingmateSystem).GetMethods().Select(method => method.Name),
            Does.Not.Contain("SeedApprovedGuideForTests"));
    }

    [Test]
    public void SnapshotAdapterTargetsResolvedSessionAndIncrementsGeneration()
    {
        var beacon = new EntityUid(42);
        var destinations = new Dictionary<NetUserId, string>
        {
            [Requester] = "requester-session",
            [Guide] = "guide-session",
        };
        var sends = new List<(WingmatePrivateSnapshotEvent Snapshot, string Session)>();
        var adapter = new WingmateSnapshotAdapter<string>(
            (NetUserId user, out string session) => destinations.TryGetValue(user, out session!),
            uid => uid == beacon,
            uid => new NetEntity(uid.Id),
            _ => new WingmateUiState(WingmateUiMode.Idle, string.Empty,
                Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour,
                null, null, null, null, true, false, "Idle"),
            (snapshot, session) => sends.Add((snapshot, session)));
        adapter.RememberOpen(Requester, beacon);
        adapter.RememberOpen(Guide, beacon);

        adapter.Publish(new[] { Requester, Guide });
        adapter.Publish(new[] { Requester });
        adapter.Close(Requester, beacon);
        adapter.RememberOpen(Requester, beacon);
        adapter.Publish(new[] { Requester });

        Assert.Multiple(() =>
        {
            Assert.That(sends.Select(send => send.Session),
                Is.EqualTo(new[] { "requester-session", "guide-session", "requester-session", "requester-session" }));
            Assert.That(sends.Select(send => send.Snapshot.Beacon),
                Is.All.EqualTo(new NetEntity(beacon.Id)));
            Assert.That(sends.Select(send => send.Snapshot.Generation), Is.EqualTo(new ulong[] { 1, 1, 2, 3 }));
        });
    }

    [Test]
    public void SnapshotAdapterNeverUsesEntityWideUiStateAndRefreshesBothAffectedUsers()
    {
        var beacon = new EntityUid(42);
        var builtFor = new List<NetUserId>();
        var sentTo = new List<NetUserId>();
        var adapter = new WingmateSnapshotAdapter<NetUserId>(
            (NetUserId user, out NetUserId session) => { session = user; return true; },
            uid => uid == beacon,
            uid => new NetEntity(uid.Id),
            user =>
            {
                builtFor.Add(user);
                return new WingmateUiState(WingmateUiMode.Idle, string.Empty,
                    Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour,
                    null, null, null, null, true, false, "Idle");
            },
            (_, session) => sentTo.Add(session));
        adapter.RememberOpen(Requester, beacon);
        adapter.RememberOpen(Guide, beacon);

        adapter.Publish(new[] { Requester, Guide });

        Assert.Multiple(() =>
        {
            Assert.That(builtFor, Is.EquivalentTo(new[] { Requester, Guide }));
            Assert.That(sentTo, Is.EquivalentTo(new[] { Requester, Guide }));
            Assert.That(typeof(WingmateSnapshotAdapter<NetUserId>).GetFields(
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Select(field => field.FieldType),
                Has.None.EqualTo(typeof(SharedUserInterfaceSystem)));
        });
    }

    [Test]
    public void ClosedOrDeletedBeaconCannotBuildOrSendSnapshot()
    {
        var beacon = new EntityUid(42);
        var alive = true;
        var buildCount = 0;
        var sendCount = 0;
        var adapter = new WingmateSnapshotAdapter<string>(
            (NetUserId _, out string session) => { session = "session"; return true; },
            _ => alive,
            uid => new NetEntity(uid.Id),
            _ =>
            {
                buildCount++;
                return new WingmateUiState(WingmateUiMode.Idle, string.Empty,
                    Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour,
                    null, null, null, null, true, false, "Idle");
            },
            (_, _) => sendCount++);
        adapter.RememberOpen(Requester, beacon);

        alive = false;
        adapter.Publish(new[] { Requester });
        alive = true;
        adapter.RememberOpen(Requester, beacon);
        adapter.Close(Requester, beacon);
        adapter.Publish(new[] { Requester });

        Assert.Multiple(() =>
        {
            Assert.That(buildCount, Is.Zero);
            Assert.That(sendCount, Is.Zero);
            Assert.That(adapter.HasOpenMapping(Requester), Is.False);
        });
    }

    [Test]
    public void BeaconShutdownRemovesEveryOpenMappingForThatEntity()
    {
        var beacon = new EntityUid(42);
        var other = new EntityUid(43);
        var adapter = new WingmateSnapshotAdapter<string>(
            (NetUserId _, out string session) => { session = "session"; return true; },
            _ => true,
            uid => new NetEntity(uid.Id),
            _ => new WingmateUiState(WingmateUiMode.Idle, string.Empty,
                Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour,
                null, null, null, null, true, false, "Idle"),
            (_, _) => { });
        adapter.RememberOpen(Requester, beacon);
        adapter.RememberOpen(Guide, beacon);
        var third = new NetUserId(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        adapter.RememberOpen(third, other);

        adapter.RemoveBeacon(beacon);

        Assert.Multiple(() =>
        {
            Assert.That(adapter.HasOpenMapping(Requester), Is.False);
            Assert.That(adapter.HasOpenMapping(Guide), Is.False);
            Assert.That(adapter.HasOpenMapping(third), Is.True);
        });
    }

    [Test]
    public void SnapshotPublicationExpiresOnceBeforeBuildAndRefreshesExpiryAffectedUsers()
    {
        var beacon = new EntityUid(42);
        var order = new List<string>();
        var sentTo = new List<NetUserId>();
        var adapter = new WingmateSnapshotAdapter<NetUserId>(
            (NetUserId user, out NetUserId session) => { session = user; return true; },
            _ => true,
            uid => new NetEntity(uid.Id),
            user =>
            {
                order.Add($"build:{user}");
                return new WingmateUiState(WingmateUiMode.Idle, string.Empty,
                    Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour,
                    null, null, null, null, true, false, "Idle");
            },
            (_, session) => sentTo.Add(session),
            () =>
            {
                order.Add("expire");
                return new[] { Guide };
            });
        adapter.RememberOpen(Requester, beacon);
        adapter.RememberOpen(Guide, beacon);

        adapter.Publish(new[] { Requester });

        Assert.Multiple(() =>
        {
            Assert.That(order[0], Is.EqualTo("expire"));
            Assert.That(order.Count(entry => entry == "expire"), Is.EqualTo(1));
            Assert.That(sentTo, Is.EquivalentTo(new[] { Requester, Guide }));
        });
    }

    // -----------------------------------------------------------------------------------------
    // UX-SIMPLE FIX 1: auto guide eligibility (CCVars.SolreignWingmatesAutoGuideEligibility).
    //
    // A plain `new WingmateSystem()` rules harness never runs Initialize(), so _players/_cfg are
    // never IoC-wired (null) — MeetsAutoEligibilityCriteria therefore ALWAYS fails closed here
    // regardless of the CVar override, exactly like every other _players-dependent seam in this
    // file (GetDisplayName, TryDeliverBlockFailureNotice, etc). That is deliberately what these
    // tests assert: the auto path is inert without a live session/entity to check, and the
    // moderator-approval override keeps working in EITHER posture. Full live-entity coverage
    // (ghost/mobstate/tenure-cache criteria) lives in the integration suite, where a real
    // IPlayerManager/EntityManager exist.
    // -----------------------------------------------------------------------------------------

    [Test]
    public void PureBasicCriteriaHelperRejectsGhostsAndTheDead()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WingmateSystem.IsBasicGuideCriteriaMet(isGhost: false, isDead: false), Is.True);
            Assert.That(WingmateSystem.IsBasicGuideCriteriaMet(isGhost: true, isDead: false), Is.False);
            Assert.That(WingmateSystem.IsBasicGuideCriteriaMet(isGhost: false, isDead: true), Is.False);
            Assert.That(WingmateSystem.IsBasicGuideCriteriaMet(isGhost: true, isDead: true), Is.False);
        });
    }

    [Test]
    public void PureEligiblePeerSeamRequiresAnotherAttachedLivingNonGhostPlayer()
    {
        // ALIVENESS P0 #3: the Seeking view's "is anyone plausibly pairable connected" bit.
        Assert.Multiple(() =>
        {
            // The seeker themselves never counts as their own peer.
            Assert.That(WingmateSystem.IsEligiblePeer(isSelf: true, hasAttachedEntity: true, isGhost: false, isDead: false), Is.False);
            // A connected-but-unattached session (lobby/limbo) is not a pairable peer.
            Assert.That(WingmateSystem.IsEligiblePeer(isSelf: false, hasAttachedEntity: false, isGhost: false, isDead: false), Is.False);
            // Ghosts/observers and the dead are held to the same basic floor guides are.
            Assert.That(WingmateSystem.IsEligiblePeer(isSelf: false, hasAttachedEntity: true, isGhost: true, isDead: false), Is.False);
            Assert.That(WingmateSystem.IsEligiblePeer(isSelf: false, hasAttachedEntity: true, isGhost: false, isDead: true), Is.False);
            // A second attached, alive, non-ghost player IS a peer — the normal seek copy stays.
            Assert.That(WingmateSystem.IsEligiblePeer(isSelf: false, hasAttachedEntity: true, isGhost: false, isDead: false), Is.True);
        });
    }

    [Test]
    public void AutoEligibilityCannotResolveWithoutALiveSessionSoItFailsClosedInARulesHarness()
    {
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);
        system.SetAutoGuideEligibilityForTests(true);

        // No IoC wiring exists in this harness (no _players/_cfg), so the auto path can never
        // resolve a session/entity to check — it must fail closed, not throw and not optimistically
        // allow. Only the explicit moderator-approval path (SeedApprovedGuideForTests /
        // ApproveGuide) can grant eligibility here.
        Assert.Multiple(() =>
        {
            Assert.That(system.IsGuideEligibleForTests(Guide), Is.False);
            Assert.That(system.SetVolunteeringForTests(Guide, volunteering: true, charterAccepted: true).Changed,
                Is.False, "auto-on alone must not grant eligibility without a resolvable live entity");
        });

        system.SeedApprovedGuideForTests(Guide);
        Assert.That(system.IsGuideEligibleForTests(Guide), Is.True,
            "an explicit moderator grant must still work even though the auto path can never resolve here");
    }

    [Test]
    public void ModeratorApprovalOverridesBothAutoPosturesRegardlessOfCVarState()
    {
        // FIX 1 spec: "the wingmateapprove admin command KEEPS working as an override/revoke path
        // either way" — assert this holds with the auto CVar explicitly ON and explicitly OFF.
        foreach (var autoEligibility in new[] { true, false })
        {
            var system = new WingmateSystem();
            system.SetEnabledForTests(true);
            system.SetAutoGuideEligibilityForTests(autoEligibility);

            system.SeedApprovedGuideForTests(Guide);

            Assert.That(system.IsGuideEligibleForTests(Guide), Is.True,
                $"moderator approval must grant eligibility with auto_guide_eligibility={autoEligibility}");
            Assert.That(system.SetVolunteeringForTests(Guide, volunteering: true, charterAccepted: true).Changed,
                Is.True);
        }
    }

    [Test]
    public void BlockedReasonIsManualPendingApprovalWhenAutoEligibilityIsOff()
    {
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);
        system.SetAutoGuideEligibilityForTests(false);

        Assert.That(system.ComputeVolunteerIneligibleReasonForTests(Guide),
            Is.EqualTo("wingmates-guide-ineligible-manual"),
            "the manual posture must give a specific, non-null reason — never a silently disabled button");
    }

    [Test]
    public void BlockedReasonIsNullOnceApprovedRegardlessOfPosture()
    {
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);
        system.SetAutoGuideEligibilityForTests(false);
        system.SeedApprovedGuideForTests(Guide);

        Assert.That(system.ComputeVolunteerIneligibleReasonForTests(Guide), Is.Null);
    }

    [Test]
    public void BlockedReasonIsNullWhileAlreadyVolunteering()
    {
        // Opt-out must stay reachable even if eligibility changes after consent (mirrors
        // WingmatePresentation.CanSubmitVolunteerChange's own "opt-out stays reachable" contract) —
        // the blocked-reason label must not reappear and confuse an already-volunteering guide.
        // Note: a MODERATOR revoke (RevokeGuide) always clears _volunteeringGuides together with
        // _approvedGuides in the same call — by design, a revoked guide can never be left "stuck
        // volunteering while ineligible" through that path (see
        // RevokeGuideWhilePairedDissolvesTheActivePairImmediately). The state this test targets —
        // already-volunteering, eligibility lost some other way (e.g. the auto-posture account died
        // mid-round with nothing continuously re-polling _volunteeringGuides) — is reached directly
        // via SeedVolunteeringForTests rather than the approve/revoke dance, which cannot produce it.
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);
        system.SeedVolunteeringForTests(Guide);

        Assert.That(system.ComputeVolunteerIneligibleReasonForTests(Guide), Is.Null);
    }

    [Test]
    public void NoAutoGuideEligibilityOverrideMeansCvarIsUnreachableWithoutIoCAndDefaultsClosed()
    {
        // Sanity check for the test-seam itself: with no override set at all (the CVar default is
        // TRUE in production, but _cfg is never wired here), AutoGuideEligibilityEnabled must not
        // throw and must resolve to a safe, non-eligible-granting state.
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);

        Assert.DoesNotThrow(() => system.IsGuideEligibleForTests(Guide));
        Assert.That(system.IsGuideEligibleForTests(Guide), Is.False);
    }

    [Test]
    public void RevokeGuideWhilePairedDissolvesTheActivePairImmediately()
    {
        // grk design-sanity pass (FIX 1, pre-implementation review): "revoke while paired" was
        // flagged as needing an explicit, verified answer rather than an inferred one. RevokeGuide
        // already routes through the same WingmateRoundState.RemoveUser primitive that disconnect
        // cleanup uses (see DisconnectOrZombieDissolvesPairImmediately above) — this pins that a
        // moderator's wingmaterevoke hard-ends an active pairing on the spot, for both sides, and
        // does not leave an orphaned "bad buddy" pairing running under a revoked guide.
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);
        system.ApproveGuide(Guide);
        system.SetVolunteeringForTests(Guide, volunteering: true, charterAccepted: true);
        system.RequestForTests(Requester, "Engineering", Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.Tour);
        var offer = system.OfferForTests(Guide, Requester);
        system.AcceptForTests(Requester, offer.OfferNonce!.Value);
        Assert.That(system.GetPartnerForTests(Requester), Is.EqualTo(Guide), "test precondition: actually paired");

        var revoked = system.RevokeGuide(Guide);

        Assert.Multiple(() =>
        {
            Assert.That(revoked.Changed, Is.True);
            Assert.That(system.GetStatusForTests(Requester), Is.EqualTo(WingmateStatus.Dissolved),
                "the newcomer must not be left stranded with a revoked guide — the pair ends, not just the flag");
            Assert.That(system.GetStatusForTests(Guide), Is.EqualTo(WingmateStatus.Dissolved));
            Assert.That(system.GetPartnerForTests(Requester), Is.Null);
            Assert.That(system.IsGuideEligibleForTests(Guide), Is.False,
                "revocation must remove eligibility, not just end this one pairing");
        });
    }

    // P1.4 SILENT-DROP BATCH FIX: the silent-drop fix relies on ExpireApplyAndPublish recording a
    // per-actor failure (loc key + seq) on a non-success transition, then clearing it on the next
    // success. Tests below pin both halves of that contract — without them, a refactor could
    // regress the silent-drop behavior without breaking any other test.
    [Test]
    public void RejectedTransitionQueuesFailureStatusForClientPopup()
    {
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);

        // Try to Offer with no eligibility, no token, etc. — every gate trips in turn until one
        // returns a non-success result. Whichever reason is first, the failure must be queued.
        var result = system.OfferForTests(Guide, Requester);

        Assert.Multiple(() =>
        {
            Assert.That(result.Changed, Is.False);
            Assert.That(system.GetLastTransitionForTests(Guide), Is.Not.Null);
            Assert.That(system.GetLastTransitionForTests(Guide)!.Value.Status, Is.Not.Null.And.Not.Empty);
            Assert.That(system.GetLastTransitionForTests(Guide)!.Value.Seq, Is.GreaterThan(0u));
        });

        // The queued failure MUST surface in the next private snapshot, with the same reason and
        // the same seq — the client popup depends on a single source of truth, not a recompute.
        var snapshot = system.BuildPrivateStateForTests(Guide);
        Assert.That(snapshot.LastTransitionStatus, Is.EqualTo(system.GetLastTransitionForTests(Guide)!.Value.Status));
        Assert.That(snapshot.LastTransitionSeq, Is.EqualTo(system.GetLastTransitionForTests(Guide)!.Value.Seq));
    }

    [Test]
    public void SuccessfulTransitionClearsStaleFailure()
    {
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);
        system.SeedApprovedGuideForTests(Guide);
        system.SeedVolunteeringForTests(Guide);
        system.SetPersistentBlocksReadyForTests(true);

        // First offer attempt: Requester has no open request yet, so Offer returns "not-seeking"
        // — queues a failure trace on the Guide's per-actor slot.
        var rejected = system.OfferForTests(Guide, Requester);
        Assert.That(rejected.Changed, Is.False);
        Assert.That(rejected.Reason, Is.EqualTo("not-seeking"));
        Assert.That(system.GetLastTransitionForTests(Guide), Is.Not.Null);
        var staleSeq = system.GetLastTransitionForTests(Guide)!.Value.Seq;

        // Open a request from Requester (direct state mutation, not via ExpireApplyAndPublish) so
        // the next Offer has a valid peer, then issue a successful Offer — this second Offer IS
        // routed through ExpireApplyAndPublish and is the path that clears the stale trace.
        system.RequestForTests(Requester, "Engineering",
            Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode.LearnByDoing);
        var success = system.OfferForTests(Guide, Requester);
        Assert.That(success.Changed, Is.True);

        // After a successful transition, the Guide's stale failure trace must be cleared so a
        // later, unrelated snapshot does not pop the old reason.
        Assert.That(system.GetLastTransitionForTests(Guide), Is.Null);

        // A subsequent rejection on a DIFFERENT action bumps the seq strictly past the stale one,
        // so the client popup fires exactly once per distinct failure (not replayed from stale).
        system.SetVolunteeringForTests(Guide, false, true);
        var secondReject = system.OfferForTests(Guide, Requester);
        Assert.That(secondReject.Changed, Is.False);
        Assert.That(system.GetLastTransitionForTests(Guide), Is.Not.Null);
        Assert.That(system.GetLastTransitionForTests(Guide)!.Value.Seq, Is.GreaterThan(staleSeq),
            "a fresh failure must bump seq strictly past the cleared stale one");
    }

    [Test]
    public void DisabledFeatureRejectionIsSurfacedAsPopup()
    {
        var system = new WingmateSystem();
        system.SetEnabledForTests(true);
        system.SeedApprovedGuideForTests(Guide);
        system.SeedVolunteeringForTests(Guide);
        system.SetPersistentBlocksReadyForTests(true);

        // Disable mid-test, then try an Offer. The "disabled" rejection must surface as a popup —
        // otherwise a player walking up to a beacon during a maintenance window gets the exact
        // silent drop the audit flagged as HIGH impact.
        system.SetEnabledForTests(false);
        var rejected = system.OfferForTests(Guide, Requester);
        Assert.Multiple(() =>
        {
            Assert.That(rejected.Changed, Is.False);
            Assert.That(rejected.Reason, Is.EqualTo("disabled"));
            Assert.That(system.GetLastTransitionForTests(Guide), Is.Not.Null);
            Assert.That(system.GetLastTransitionForTests(Guide)!.Value.Status, Is.EqualTo("disabled"));
        });
    }

}
