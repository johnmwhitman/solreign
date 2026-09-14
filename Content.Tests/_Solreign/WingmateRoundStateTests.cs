using System;
using System.Linq;
using Content.Server._Solreign.PlayerDelight.Wingmates;
using NUnit.Framework;
using Robust.Shared.Network;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(WingmateRoundState))]
public sealed class WingmateRoundStateTests
{
    private static readonly NetUserId Requester = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly NetUserId Guide = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));
    private static readonly NetUserId Other = new(Guid.Parse("33333333-3333-3333-3333-333333333333"));
    private static readonly TimeSpan Now = TimeSpan.FromMinutes(10);

    [Test]
    public void RequestOfferAccept_PairsBothUsers()
    {
        var state = NewState();
        Assert.That(state.Request(Requester, "Engineering", WingmateTeachingMode.LearnByDoing, Now).Changed, Is.True);
        var offer = state.Offer(Guide, Requester, Now);
        Assert.That(offer.Changed, Is.True);
        Assert.That(state.Accept(Requester, offer.OfferNonce!.Value, Now).Changed, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(state.GetPartner(Requester), Is.EqualTo(Guide));
            Assert.That(state.GetPartner(Guide), Is.EqualTo(Requester));
        });
    }

    [Test]
    public void DuplicateRequest_IsIdempotent()
    {
        var state = NewState();
        Assert.That(state.Request(Requester, "Engineering", WingmateTeachingMode.Tour, Now).Changed, Is.True);
        Assert.That(state.Request(Requester, "Engineering", WingmateTeachingMode.Tour, Now).Changed, Is.False);
    }

    [Test]
    public void Offer_RejectsSelfAndNonSeekingTarget()
    {
        var state = NewState();
        state.Request(Requester, "Engineering", WingmateTeachingMode.Tour, Now);
        Assert.Multiple(() =>
        {
            Assert.That(state.Offer(Requester, Requester, Now).Reason, Is.EqualTo("self-offer"));
            Assert.That(state.Offer(Guide, Other, Now).Reason, Is.EqualTo("not-seeking"));
        });
    }

    [Test]
    public void Accept_RejectsStaleNonceAndReplay()
    {
        var state = NewState();
        state.Request(Requester, "Engineering", WingmateTeachingMode.Tour, Now);
        var offer = state.Offer(Guide, Requester, Now);
        Assert.That(state.Accept(Requester, Guid.NewGuid(), Now).Reason, Is.EqualTo("stale-offer"));
        Assert.That(state.Accept(Requester, offer.OfferNonce!.Value, Now).Changed, Is.True);
        Assert.That(state.Accept(Requester, offer.OfferNonce.Value, Now).Changed, Is.False);
    }

    [Test]
    public void Decline_LeavesRequesterSeeking()
    {
        var state = NewState();
        state.Request(Requester, "Medical", WingmateTeachingMode.ShadowMe, Now);
        var offer = state.Offer(Guide, Requester, Now);
        Assert.That(state.Decline(Requester, offer.OfferNonce!.Value, Now).Reason, Is.EqualTo("declined"));
        Assert.That(state.GetStatus(Requester), Is.EqualTo(WingmateStatus.Seeking));
    }

    [Test]
    public void Dissolve_IsSymmetric()
    {
        var state = PairedState();
        Assert.That(state.Dissolve(Guide).Reason, Is.EqualTo("dissolved"));
        Assert.Multiple(() =>
        {
            Assert.That(state.GetPartner(Requester), Is.Null);
            Assert.That(state.GetPartner(Guide), Is.Null);
            Assert.That(state.GetStatus(Requester), Is.EqualTo(WingmateStatus.Dissolved));
            Assert.That(state.GetStatus(Guide), Is.EqualTo(WingmateStatus.Dissolved));
        });
    }

    [Test]
    public void BlockForRound_IsBidirectional()
    {
        var state = PairedState();
        Assert.That(state.BlockForRound(Requester, Guide).Reason, Is.EqualTo("blocked"));
        state.Request(Requester, "Cargo", WingmateTeachingMode.Tour, Now);
        state.Request(Guide, "Cargo", WingmateTeachingMode.Tour, Now);
        Assert.Multiple(() =>
        {
            Assert.That(state.Offer(Guide, Requester, Now).Reason, Is.EqualTo("blocked"));
            Assert.That(state.Offer(Requester, Guide, Now).Reason, Is.EqualTo("blocked"));
        });
    }

    [Test]
    public void Expire_RemovesOfferAndReturnsRequesterToSeeking()
    {
        var state = NewState();
        state.Request(Requester, "Science", WingmateTeachingMode.Tour, Now);
        state.Offer(Guide, Requester, Now);
        var affected = state.Expire(Now + TimeSpan.FromMinutes(5));
        Assert.Multiple(() =>
        {
            Assert.That(state.GetIncomingOffer(Requester), Is.Null);
            Assert.That(state.GetStatus(Requester), Is.EqualTo(WingmateStatus.Seeking));
            Assert.That(state.GetStatus(Guide), Is.EqualTo(WingmateStatus.Expired));
            Assert.That(affected, Is.EquivalentTo(new[] { Requester, Guide }));
        });
    }

    [Test]
    public void ExpiredOfferCannotBeDeclinedOrDisplayed()
    {
        var state = NewState();
        state.Request(Requester, "Science", WingmateTeachingMode.Tour, Now);
        var offer = state.Offer(Guide, Requester, Now);

        state.Expire(Now + TimeSpan.FromMinutes(6));

        Assert.Multiple(() =>
        {
            Assert.That(state.GetIncomingOffer(Requester), Is.Null);
            Assert.That(state.Decline(Requester, offer.OfferNonce!.Value, Now + TimeSpan.FromMinutes(6)).Reason,
                Is.EqualTo("stale-offer"));
        });
    }

    [Test]
    public void SeekingSnapshotExposesValidatedRequestContext()
    {
        var state = NewState();
        state.Request(Requester, "Engineering", WingmateTeachingMode.LearnByDoing, Now);

        var details = state.GetRequestDetails(Requester);
        var seekers = state.GetSeekingRequests(Guide);

        Assert.Multiple(() =>
        {
            Assert.That(details?.Department, Is.EqualTo("Engineering"));
            Assert.That(details?.TeachingMode, Is.EqualTo(WingmateTeachingMode.LearnByDoing));
            Assert.That(seekers, Has.Count.EqualTo(1));
            Assert.That(seekers[0].Requester, Is.EqualTo(Requester));
            Assert.That(seekers[0].Department, Is.EqualTo("Engineering"));
            Assert.That(seekers[0].RequesterToken, Is.Not.EqualTo(Guid.Empty));
        });
    }

    [Test]
    public void RequesterTokens_AreRandomViewerBoundStableWithinRoundAndInvalidatedOnClear()
    {
        var state = NewState();
        state.Request(Requester, "Engineering", WingmateTeachingMode.Tour, Now);

        var guideToken = state.GetSeekingRequests(Guide).Single().RequesterToken;
        var otherViewerToken = state.GetSeekingRequests(Other).Single().RequesterToken;

        // Stable within the round: re-requesting (department change) does not mint a new token.
        state.Request(Requester, "Medical", WingmateTeachingMode.Tour, Now);
        var guideTokenAfterReRequest = state.GetSeekingRequests(Guide).Single().RequesterToken;

        Assert.Multiple(() =>
        {
            Assert.That(guideToken, Is.Not.EqualTo(Guid.Empty));
            Assert.That(otherViewerToken, Is.Not.EqualTo(Guid.Empty));
            // Viewer-bound: the same requester resolves to a *different* token for a different viewer,
            // so a token intercepted for one guide cannot be replayed by another.
            Assert.That(otherViewerToken, Is.Not.EqualTo(guideToken));
            Assert.That(guideTokenAfterReRequest, Is.EqualTo(guideToken));
            Assert.That(state.TryResolveRequesterToken(Guide, guideToken, out var resolved) && resolved == Requester,
                Is.True);
            Assert.That(state.TryResolveRequesterToken(Other, guideToken, out _), Is.False,
                "a token minted for one viewer must never resolve for a different viewer");
        });

        state.Clear();

        // The security property that matters is that the *mapping* is wiped — Clear() invalidates
        // every previously-issued (viewer, token) pair.
        Assert.That(state.TryResolveRequesterToken(Guide, guideToken, out _), Is.False,
            "a pre-Clear() token must not resolve to anyone after the round resets");

        state.Request(Requester, "Engineering", WingmateTeachingMode.Tour, Now);
        var freshToken = state.GetSeekingRequests(Guide).Single().RequesterToken;
        Assert.That(freshToken, Is.Not.EqualTo(guideToken),
            "a freshly-minted random token in the new round must not coincide with the invalidated one");
    }

    [Test]
    public void DeclinedGuideCannotReofferUntilCooldownExpires()
    {
        var state = NewState();
        state.Request(Requester, "Engineering", WingmateTeachingMode.Tour, Now);
        var offer = state.Offer(Guide, Requester, Now);
        state.Decline(Requester, offer.OfferNonce!.Value, Now);

        var immediateReoffer = state.Offer(Guide, Requester, Now);
        var justBeforeExpiry = state.Offer(Guide, Requester, Now + TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(immediateReoffer.Changed, Is.False);
            Assert.That(immediateReoffer.Reason, Is.EqualTo("decline-cooldown"));
            Assert.That(justBeforeExpiry.Changed, Is.False);
            Assert.That(justBeforeExpiry.Reason, Is.EqualTo("decline-cooldown"));
        });

        var afterCooldown = state.Offer(Guide, Requester, Now + TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        Assert.That(afterCooldown.Changed, Is.True);
    }

    [Test]
    public void DifferentGuideCanOfferDuringDeclineCooldown()
    {
        var state = NewState();
        state.Request(Requester, "Engineering", WingmateTeachingMode.Tour, Now);
        var offer = state.Offer(Guide, Requester, Now);
        state.Decline(Requester, offer.OfferNonce!.Value, Now);

        var fromDifferentGuide = state.Offer(Other, Requester, Now);

        Assert.That(fromDifferentGuide.Changed, Is.True);
    }

    [Test]
    public void DeclineTimeBlockOutlastsTheOrdinaryDeclineCooldown()
    {
        // Once a decline-time persistent block is confirmed durable, WingmateSystem applies it here as
        // a round-local BlockForRound — this proves that path is a hard block, not merely the 5-minute
        // decline cooldown every decline already gets: it still rejects the same guide a full day later,
        // while leaving a different guide free to offer immediately.
        var state = NewState();
        state.Request(Requester, "Engineering", WingmateTeachingMode.Tour, Now);
        state.Offer(Guide, Requester, Now);

        var block = state.BlockForRound(Requester, Guide);

        Assert.Multiple(() =>
        {
            Assert.That(block.Changed, Is.True);
            Assert.That(state.GetIncomingOffer(Requester), Is.Null);
            Assert.That(state.GetStatus(Requester), Is.EqualTo(WingmateStatus.Seeking));
            Assert.That(state.Offer(Guide, Requester, Now + TimeSpan.FromDays(1)).Reason, Is.EqualTo("blocked"));
            Assert.That(state.Offer(Other, Requester, Now).Changed, Is.True);
        });
    }

    [Test]
    public void PairMutationsIdentifyEveryAffectedParticipant()
    {
        var state = NewState();
        state.Request(Requester, "Engineering", WingmateTeachingMode.Tour, Now);
        var offer = state.Offer(Guide, Requester, Now);
        Assert.That(offer.AffectedUsers, Is.EquivalentTo(new[] { Guide, Requester }));

        var accept = state.Accept(Requester, offer.OfferNonce!.Value, Now);
        Assert.That(accept.AffectedUsers, Is.EquivalentTo(new[] { Guide, Requester }));

        var dissolve = state.Dissolve(Guide);
        Assert.That(dissolve.AffectedUsers, Is.EquivalentTo(new[] { Guide, Requester }));
    }

    [Test]
    public void OfferRemovalMutationsIdentifyEveryAffectedParticipant()
    {
        var state = NewState();
        state.Request(Requester, "Engineering", WingmateTeachingMode.Tour, Now);
        var offer = state.Offer(Guide, Requester, Now);
        var decline = state.Decline(Requester, offer.OfferNonce!.Value, Now);
        Assert.That(decline.AffectedUsers, Is.EquivalentTo(new[] { Guide, Requester }));

        // Re-offering from the same guide is subject to the decline cooldown; advance past it so this
        // re-exercises a genuine offer rather than being rejected with "decline-cooldown".
        var afterCooldown = Now + TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1);
        offer = state.Offer(Guide, Requester, afterCooldown);
        Assert.That(offer.Changed, Is.True);
        var block = state.BlockForRound(Requester, Guide);
        Assert.That(block.AffectedUsers, Is.EquivalentTo(new[] { Guide, Requester }));
    }

    [Test]
    public void ReplacementOfferRefreshesDisplacedGuideToo()
    {
        var state = NewState();
        state.Request(Requester, "Engineering", WingmateTeachingMode.Tour, Now);
        state.Offer(Guide, Requester, Now);

        var replacement = state.Offer(Other, Requester, Now);

        Assert.That(replacement.AffectedUsers, Is.EquivalentTo(new[] { Guide, Other, Requester }));
    }

    [Test]
    public void RetargetedOfferRefreshesPreviousRequesterToo()
    {
        var state = NewState();
        state.Request(Requester, "Engineering", WingmateTeachingMode.Tour, Now);
        state.Request(Other, "Medical", WingmateTeachingMode.Tour, Now);
        state.Offer(Guide, Requester, Now);

        var replacement = state.Offer(Guide, Other, Now);

        Assert.That(replacement.AffectedUsers, Is.EquivalentTo(new[] { Guide, Requester, Other }));
    }

    [Test]
    public void Clear_RemovesAllStateIncludingBlocks()
    {
        var state = PairedState();
        state.BlockForRound(Requester, Guide);
        state.Clear();
        Assert.Multiple(() =>
        {
            Assert.That(state.GetStatus(Requester), Is.EqualTo(WingmateStatus.Idle));
            Assert.That(state.GetPartner(Requester), Is.Null);
        });
        state.Request(Requester, "Service", WingmateTeachingMode.Tour, Now);
        Assert.That(state.Offer(Guide, Requester, Now).Changed, Is.True);
    }

    [Test]
    public void ConflictingRole_IsRejectedBeforePairingCanBecomeAsymmetric()
    {
        var state = NewState();
        state.Request(Requester, "Engineering", WingmateTeachingMode.Tour, Now);
        state.Request(Guide, "Medical", WingmateTeachingMode.ShadowMe, Now);

        var conflictingOffer = state.Offer(Guide, Requester, Now);
        var othersOffer = state.Offer(Other, Guide, Now);
        Assert.That(state.Accept(Guide, othersOffer.OfferNonce!.Value, Now).Changed, Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(conflictingOffer.Reason, Is.EqualTo("role-conflict"));
            Assert.That(state.GetPartner(Guide), Is.EqualTo(Other));
            Assert.That(state.GetPartner(Other), Is.EqualTo(Guide));
            Assert.That(state.GetPartner(Requester), Is.Null);
            Assert.That(state.GetStatus(Requester), Is.EqualTo(WingmateStatus.Seeking));
            Assert.That(state.GetIncomingOffer(Requester), Is.Null);
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void BlockPendingOffer_ClearsOfferAndIsIdempotent(bool requesterBlocks)
    {
        var state = NewState();
        state.Request(Requester, "Cargo", WingmateTeachingMode.LearnByDoing, Now);
        state.Offer(Guide, Requester, Now);

        var actor = requesterBlocks ? Requester : Guide;
        var other = requesterBlocks ? Guide : Requester;
        var first = state.BlockForRound(actor, other);
        var repeat = state.BlockForRound(actor, other);

        Assert.Multiple(() =>
        {
            Assert.That(first.Changed, Is.True);
            Assert.That(first.Reason, Is.EqualTo("blocked"));
            Assert.That(repeat.Changed, Is.False);
            Assert.That(repeat.Reason, Is.EqualTo("blocked"));
            Assert.That(state.GetIncomingOffer(Requester), Is.Null);
            Assert.That(state.GetStatus(Requester), Is.EqualTo(WingmateStatus.Seeking));
            Assert.That(state.GetStatus(Guide), Is.EqualTo(WingmateStatus.Idle));
        });
    }

    [Test]
    public void GuideWithOwnRequest_CannotOffer()
    {
        var state = NewState();
        state.Request(Guide, "Medical", WingmateTeachingMode.ShadowMe, Now);
        state.Request(Requester, "Engineering", WingmateTeachingMode.Tour, Now);

        var result = state.Offer(Guide, Requester, Now);

        Assert.Multiple(() =>
        {
            Assert.That(result.Changed, Is.False);
            Assert.That(result.Reason, Is.EqualTo("role-conflict"));
            Assert.That(state.GetStatus(Guide), Is.EqualTo(WingmateStatus.Seeking));
            Assert.That(state.GetStatus(Requester), Is.EqualTo(WingmateStatus.Seeking));
            Assert.That(state.GetIncomingOffer(Requester), Is.Null);
        });
    }

    [Test]
    public void GuideWithIncomingOffer_CannotOffer()
    {
        var state = NewState();
        state.Request(Guide, "Medical", WingmateTeachingMode.ShadowMe, Now);
        var incoming = state.Offer(Other, Guide, Now);
        state.Request(Requester, "Engineering", WingmateTeachingMode.Tour, Now);

        var result = state.Offer(Guide, Requester, Now);

        Assert.Multiple(() =>
        {
            Assert.That(result.Changed, Is.False);
            Assert.That(result.Reason, Is.EqualTo("role-conflict"));
            Assert.That(state.GetIncomingOffer(Guide)?.Nonce, Is.EqualTo(incoming.OfferNonce));
            Assert.That(state.GetStatus(Guide), Is.EqualTo(WingmateStatus.OfferPending));
            Assert.That(state.GetIncomingOffer(Requester), Is.Null);
        });
    }

    [Test]
    public void RequesterWithOutgoingOffer_CannotReceiveAnotherOffer()
    {
        var state = NewState();
        state.Request(Requester, "Engineering", WingmateTeachingMode.Tour, Now);
        var outgoing = state.Offer(Guide, Requester, Now);

        var result = state.Offer(Other, Guide, Now);

        Assert.Multiple(() =>
        {
            Assert.That(outgoing.Changed, Is.True);
            Assert.That(result.Changed, Is.False);
            Assert.That(result.Reason, Is.EqualTo("role-conflict"));
            Assert.That(state.GetIncomingOffer(Requester)?.Nonce, Is.EqualTo(outgoing.OfferNonce));
            Assert.That(state.GetIncomingOffer(Guide), Is.Null);
        });
    }

    [Test]
    public void ChangedRequestWhileGuiding_IsRejectedWithoutMutatingOffer()
    {
        var state = NewState();
        state.Request(Requester, "Engineering", WingmateTeachingMode.Tour, Now);
        var outgoing = state.Offer(Guide, Requester, Now);

        var result = state.Request(Guide, "Medical", WingmateTeachingMode.ShadowMe, Now);

        Assert.Multiple(() =>
        {
            Assert.That(result.Changed, Is.False);
            Assert.That(result.Reason, Is.EqualTo("role-conflict"));
            Assert.That(state.GetStatus(Guide), Is.EqualTo(WingmateStatus.OfferPending));
            Assert.That(state.GetIncomingOffer(Requester)?.Nonce, Is.EqualTo(outgoing.OfferNonce));
        });
    }

    [Test]
    public void PauseAndResume_AreSymmetricIdempotentAndPreservePair()
    {
        var state = PairedState();
        Assert.That(state.Pause(Requester).Changed, Is.True);
        Assert.That(state.Pause(Guide).Changed, Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(state.GetPartner(Requester), Is.EqualTo(Guide));
            Assert.That(state.GetPartner(Guide), Is.EqualTo(Requester));
            Assert.That(state.GetStatus(Requester), Is.EqualTo(WingmateStatus.Paused));
            Assert.That(state.GetStatus(Guide), Is.EqualTo(WingmateStatus.Paused));
        });
        Assert.That(state.Resume(Guide).Changed, Is.True);
        Assert.That(state.Resume(Requester).Changed, Is.False);
        Assert.That(state.GetStatus(Requester), Is.EqualTo(WingmateStatus.Paired));
    }

    [Test]
    public void DiscoveryFiltersBidirectionalBlocksWithoutStarvingNextSeeker()
    {
        var state = NewState();
        state.Request(Requester, "Engineering", WingmateTeachingMode.Tour, Now);
        state.Request(Other, "Medical", WingmateTeachingMode.LearnByDoing, Now);
        state.BlockForRound(Guide, Requester);

        var visible = state.GetSeekingRequests(Guide);

        Assert.That(visible.Select(request => request.Requester), Is.EqualTo(new[] { Other }));
        Assert.That(state.GetSeekingRequests(Requester).Select(request => request.Requester),
            Does.Not.Contain(Guide));
    }

    [Test]
    public void WithdrawOutgoingOfferInvalidatesNonceAndRefreshesBothSides()
    {
        var state = NewState();
        state.Request(Requester, "Engineering", WingmateTeachingMode.Tour, Now);
        var offer = state.Offer(Guide, Requester, Now);

        var withdrawn = state.WithdrawOutgoingOffer(Guide);

        Assert.Multiple(() =>
        {
            Assert.That(withdrawn.AffectedUsers, Is.EquivalentTo(new[] { Guide, Requester }));
            Assert.That(state.GetIncomingOffer(Requester), Is.Null);
            Assert.That(state.GetStatus(Requester), Is.EqualTo(WingmateStatus.Seeking));
            Assert.That(state.GetStatus(Guide), Is.EqualTo(WingmateStatus.Idle));
            Assert.That(state.Accept(Requester, offer.OfferNonce!.Value, Now).Reason, Is.EqualTo("stale-offer"));
        });
    }

    private static WingmateRoundState NewState() => new(TimeSpan.FromMinutes(5));

    private static WingmateRoundState PairedState()
    {
        var state = NewState();
        state.Request(Requester, "Engineering", WingmateTeachingMode.LearnByDoing, Now);
        var offer = state.Offer(Guide, Requester, Now);
        state.Accept(Requester, offer.OfferNonce!.Value, Now);
        return state;
    }
}
