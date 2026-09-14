#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.PlayerDelight.Wingmates;
using Content.Server._Solreign.Providence;
using Content.Shared._Solreign.PlayerDelight.Wingmates;
using Content.Shared.CCVar;
using ClientPopupSystem = Content.Client.Popups.PopupSystem;
using NUnit.Framework;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Timing;
using Robust.UnitTesting;
using SharedTeachingMode = Content.Shared._Solreign.PlayerDelight.Wingmates.WingmateTeachingMode;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
/// Drives Wingmates through its real initialized ECS system and beacon event subscriptions. The
/// standard connected <see cref="GameTest"/> pool exposes one authenticated session, so the requester
/// uses the real BUI message path while the fabricated guide uses the same internal transition seams
/// invoked by the live handlers. Targeted delivery is tested with two opaque destination objects;
/// no second player session is fabricated and no private snapshot is put into entity-wide BUI state.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class WingmateSystemIntegrationTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        DummyTicker = false,
    };

    private static readonly NetUserId Guide =
        new(Guid.Parse("22222222-2222-2222-2222-222222222222"));

    private static readonly NetUserId Other =
        new(Guid.Parse("44444444-4444-4444-4444-444444444444"));

    /// <summary>
    ///     🔴 Cross-test isolation for the persistent-block writer. Three tests in this fixture stub
    ///     the durable writer with an always-fail double
    ///     (<c>SetPersistentBlockWriterForTests((_, _) => Task.FromResult(false))</c>) and the
    ///     override is process-lifetime on the pooled server — it survives pair recycling. Without
    ///     this restore, every test that runs after a stubbing sibling performs its "real SQLite"
    ///     block write against the stub: the write reports failure, the drain never dissolves the
    ///     pair, and PersistentBlockSurvivesRoundRestartAndPreventsOffer fails on
    ///     <c>Is.EqualTo(WingmateStatus.Dissolved)</c> — while passing in isolation. Diagnosed
    ///     2026-08-02 after three refuted hypotheses; the fix is at the leak, not in the victim test.
    /// </summary>
    [SetUp]
    public async Task RestorePersistentBlockWriter()
    {
        var system = Server.System<WingmateSystem>();
        await Server.WaitPost(system.RestorePersistentBlockWriterForTests);
    }

    private static async Task<(EntityUid Actor, NetUserId User)> GetPlayer(
        RobustIntegrationTest.ServerIntegrationInstance server,
        IPlayerManager players)
    {
        EntityUid actor = default;
        NetUserId user = default;
        await server.WaitPost(() =>
        {
            var session = players.Sessions.First();
            actor = session.AttachedEntity!.Value;
            user = session.UserId;
        });
        return (actor, user);
    }

    private static EntityUid SpawnBeacon(IEntityManager entMan)
    {
        var beacon = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
        entMan.EnsureComponent<WingmateBeaconComponent>(beacon);
        return beacon;
    }

    private static void Raise<T>(IEntityManager entMan, EntityUid beacon, EntityUid actor, T message)
        where T : BoundUserInterfaceMessage
    {
        message.Actor = actor;
        entMan.EventBus.RaiseLocalEvent(beacon, message);
    }

    [Test]
    public async Task AuthenticatedRequest_DuplicateAndCVarOff_AreSafe()
    {
        var server = Server;
        var players = server.ResolveDependency<IPlayerManager>();
        var system = server.System<WingmateSystem>();
        var (actor, requester) = await GetPlayer(server, players);
        EntityUid beacon = default;

        await server.WaitPost(() =>
        {
            beacon = SpawnBeacon(server.EntMan);
            server.CfgMan.SetCVar(CCVars.SolreignWingmatesEnabled, true);
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Raise(server.EntMan, beacon, actor,
                new WingmateRequestMessage("Engineering", SharedTeachingMode.LearnByDoing));
            Raise(server.EntMan, beacon, actor,
                new WingmateRequestMessage("Engineering", SharedTeachingMode.LearnByDoing));
            Assert.That(system.GetStatusForTests(requester), Is.EqualTo(WingmateStatus.Seeking));
        });

        server.CfgMan.SetCVar(CCVars.SolreignWingmatesEnabled, false);
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(system.GetStatusForTests(requester), Is.EqualTo(WingmateStatus.Idle));
            Raise(server.EntMan, beacon, actor,
                new WingmateRequestMessage("Medical", SharedTeachingMode.Tour));
            Assert.That(system.GetStatusForTests(requester), Is.EqualTo(WingmateStatus.Idle));
        });
    }

    [Test]
    public async Task ConsentFlow_StaleReplayDissolveDisconnectAndRoundCleanup_AreSymmetric()
    {
        var server = Server;
        var players = server.ResolveDependency<IPlayerManager>();
        var timing = server.ResolveDependency<IGameTiming>();
        var system = server.System<WingmateSystem>();
        var (actor, requester) = await GetPlayer(server, players);
        EntityUid beacon = default;

        await server.WaitPost(() =>
        {
            beacon = SpawnBeacon(server.EntMan);
            server.CfgMan.SetCVar(CCVars.SolreignWingmatesEnabled, true);

            // The pool's initial connected spawn (PoolSettings.Connected = true, DummyTicker = false)
            // already ran through ProvidenceWelcomeSystem's real OnPlayerSpawnComplete handler before
            // this test body runs, which — at the feature's default CVars (on) — schedules a delayed
            // personal-welcome beat ~8s of game time out (see ProvidenceWelcomeSystem's own doc comment
            // and ResetRoundStateForTests, the exact antidote its own integration tests use). This test
            // jumps IGameTiming.CurTick forward five minutes below to exercise the decline-cooldown
            // window, which trivially satisfies that beat's due-time and lets it fire, unrelated to
            // this system, on the very next real tick rolled forward in GameTest.DoTeardown. Firing
            // there creates a new audio entity in the same tick the manual CurTick jump desyncs PVS's
            // per-tick dirty-buffer bookkeeping, tripping an unrelated engine DebugAssert
            // (PvsSystem.OnEntityAdd) and dirty-disposing the pair. Clear the incidental beat before it
            // can collide with the clock jump.
            server.System<ProvidenceWelcomeSystem>().ResetRoundStateForTests();
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Raise(server.EntMan, beacon, actor,
                new WingmateRequestMessage("Engineering", SharedTeachingMode.Tour));
            system.SeedApprovedGuideForTests(Guide);
            Assert.That(system.SetVolunteeringForTests(Guide, true, true).Changed, Is.True);
            var offered = system.OfferForTests(Guide, requester);
            Assert.That(offered.Changed, Is.True);
            var nonce = system.GetIncomingOfferForTests(requester)!.Value.Nonce;

            Raise(server.EntMan, beacon, actor, new WingmateDeclineMessage(nonce));
            Assert.Multiple(() =>
            {
                Assert.That(system.GetStatusForTests(requester), Is.EqualTo(WingmateStatus.Seeking));
                Assert.That(system.GetStatusForTests(Guide), Is.EqualTo(WingmateStatus.Idle));
                Assert.That(system.GetIncomingOfferForTests(requester), Is.Null);
            });

            // The declining guide must not be able to immediately re-offer to the same requester.
            var immediateReoffer = system.OfferForTests(Guide, requester);
            Assert.Multiple(() =>
            {
                Assert.That(immediateReoffer.Changed, Is.False);
                Assert.That(immediateReoffer.Reason, Is.EqualTo("decline-cooldown"));
            });

            // Jump the server's simulation clock past the 5-minute decline cooldown. This mutates
            // IGameTiming.CurTick directly (settable by design) rather than running ~18k real ticks
            // via WaitRunTicks — the cooldown check only reads CurTime, so no other system needs to
            // observe every intervening tick. Safe here because PoolSettings.Dirty = true keeps this
            // server instance out of the shared pool afterwards.
            var cooldownTicks = (uint) Math.Ceiling(TimeSpan.FromMinutes(5).TotalSeconds * timing.TickRate) + 1;
            timing.CurTick = new GameTick(timing.CurTick.Value + cooldownTicks);

            var afterCooldown = system.OfferForTests(Guide, requester);
            Assert.That(afterCooldown.Changed, Is.True, "the same guide must be able to re-offer once the cooldown has elapsed");
            nonce = afterCooldown.OfferNonce!.Value;

            Raise(server.EntMan, beacon, actor, new WingmateAcceptMessage(Guid.NewGuid()));
            Assert.That(system.GetStatusForTests(requester), Is.EqualTo(WingmateStatus.OfferPending));
            Raise(server.EntMan, beacon, actor, new WingmateAcceptMessage(nonce));
            Raise(server.EntMan, beacon, actor, new WingmateAcceptMessage(nonce));
            Assert.Multiple(() =>
            {
                Assert.That(system.GetPartnerForTests(requester), Is.EqualTo(Guide));
                Assert.That(system.GetPartnerForTests(Guide), Is.EqualTo(requester));
            });

            Raise(server.EntMan, beacon, actor, new WingmatePauseMessage());
            Assert.That(system.GetStatusForTests(requester), Is.EqualTo(WingmateStatus.Paused));
            Assert.That(system.GetPartnerForTests(requester), Is.EqualTo(Guide));
            Raise(server.EntMan, beacon, actor, new WingmateResumeMessage());
            Assert.That(system.GetStatusForTests(requester), Is.EqualTo(WingmateStatus.Paired));

            Raise(server.EntMan, beacon, actor, new WingmateDissolveMessage());
            Assert.That(system.GetPartnerForTests(requester), Is.Null);

            Raise(server.EntMan, beacon, actor,
                new WingmateRequestMessage("Engineering", SharedTeachingMode.Tour));
            var second = system.OfferForTests(Guide, requester);
            Raise(server.EntMan, beacon, actor, new WingmateAcceptMessage(second.OfferNonce!.Value));
            Assert.That(system.GetPartnerForTests(requester), Is.EqualTo(Guide));
            Assert.That(system.HasOpenSnapshotMappingForTests(requester), Is.True);
            system.SessionUnavailableForTests(requester);
            Assert.That(system.GetPartnerForTests(Guide), Is.Null,
                "The same disconnect cleanup seam used by PlayerStatusChanged must dissolve both sides.");
            Assert.That(system.HasOpenSnapshotMappingForTests(requester), Is.False,
                "Disconnect cleanup must close the private snapshot destination before publishing counterparts.");

            Raise(server.EntMan, beacon, actor,
                new WingmateRequestMessage("Engineering", SharedTeachingMode.Tour));
            Assert.That(system.HasOpenSnapshotMappingForTests(requester), Is.True);
            system.RoundRestartForTests();
            Assert.That(system.GetStatusForTests(requester), Is.EqualTo(WingmateStatus.Idle));
            Assert.That(system.GetModeratorSnapshot(Guide).Approved, Is.False);
            Assert.That(system.HasOpenSnapshotMappingForTests(requester), Is.False);
        });
    }

    [Test]
    public async Task ForgedRequesterSelector_CannotActAsApprovedGuide()
    {
        var server = Server;
        var players = server.ResolveDependency<IPlayerManager>();
        var system = server.System<WingmateSystem>();
        var (actor, requester) = await GetPlayer(server, players);
        EntityUid beacon = default;

        await server.WaitPost(() =>
        {
            beacon = SpawnBeacon(server.EntMan);
            server.CfgMan.SetCVar(CCVars.SolreignWingmatesEnabled, true);
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Raise(server.EntMan, beacon, actor,
                new WingmateRequestMessage("Engineering", SharedTeachingMode.Tour));

            // The actor is unapproved and holds their own real, self-viewer-bound requester token —
            // even a genuine, self-resolvable token cannot be used to act as a guide. A forged/unknown
            // Guid (which is all a hostile client could ever construct, since tokens are opaque,
            // unpredictable, and never sent to non-owning clients) must fail identically.
            var ownToken = system.GetRequesterTokenForTests(requester, requester);
            Assert.That(ownToken, Is.Not.Null);
            Raise(server.EntMan, beacon, actor, new WingmateOfferMessage(ownToken!.Value));
            Raise(server.EntMan, beacon, actor, new WingmateOfferMessage(Guid.NewGuid()));

            Assert.Multiple(() =>
            {
                Assert.That(system.GetStatusForTests(requester), Is.EqualTo(WingmateStatus.Seeking));
                Assert.That(system.GetIncomingOfferForTests(requester), Is.Null);
            });
        });
    }

    [Test]
    public async Task TargetedSnapshots_TwoViewersReceiveOnlyTheirOwnStateAndNonce()
    {
        var server = Server;
        var players = server.ResolveDependency<IPlayerManager>();
        var system = server.System<WingmateSystem>();
        var (actor, requester) = await GetPlayer(server, players);
        EntityUid beacon = default;
        await server.WaitPost(() =>
        {
            beacon = SpawnBeacon(server.EntMan);
            server.CfgMan.SetCVar(CCVars.SolreignWingmatesEnabled, true);
        });
        await server.WaitRunTicks(1);

        var bystander = new NetUserId(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var sessions = new Dictionary<NetUserId, object>
        {
            [requester] = new object(),
            [bystander] = new object(),
        };
        var deliveries = new List<(object Session, WingmatePrivateSnapshotEvent Snapshot)>();
        var adapter = system.CreatePrivateSnapshotAdapterForTests<object>(
            (NetUserId user, out object session) => sessions.TryGetValue(user, out session!),
            (snapshot, session) => deliveries.Add((session, snapshot)));

        Guid requesterNonce = default;
        await server.WaitAssertion(() =>
        {
            Raise(server.EntMan, beacon, actor,
                new WingmateRequestMessage("Engineering", SharedTeachingMode.Tour));
            system.SeedApprovedGuideForTests(Guide);
            system.SetVolunteeringForTests(Guide, true, true);
            requesterNonce = system.OfferForTests(Guide, requester).OfferNonce!.Value;

            adapter.RememberOpen(requester, beacon);
            adapter.RememberOpen(bystander, beacon);
            adapter.Publish(new[] { requester, bystander });

            Assert.That(system.AcceptForTests(bystander, requesterNonce).Reason, Is.EqualTo("stale-offer"),
                "A bystander cannot use a nonce targeted to the requester account.");
            Assert.That(system.GetStatusForTests(requester), Is.EqualTo(WingmateStatus.OfferPending));
        });

        Assert.Multiple(() =>
        {
            Assert.That(deliveries, Has.Count.EqualTo(2));
            Assert.That(deliveries.Single(x => ReferenceEquals(x.Session, sessions[requester])).Snapshot.State.OfferNonce,
                Is.EqualTo(requesterNonce));
            Assert.That(deliveries.Single(x => ReferenceEquals(x.Session, sessions[bystander])).Snapshot.State.OfferNonce,
                Is.Null);
            Assert.That(deliveries.Select(x => x.Snapshot.Generation), Is.All.EqualTo(1UL));
        });
    }

    [Test]
    public async Task PrivateSnapshotContainsOpaqueRequesterTokenAndNoAccountId()
    {
        var server = Server;
        var players = server.ResolveDependency<IPlayerManager>();
        var system = server.System<WingmateSystem>();
        var metaData = server.System<MetaDataSystem>();
        var (actor, requester) = await GetPlayer(server, players);
        EntityUid beacon = default;

        await server.WaitPost(() =>
        {
            beacon = SpawnBeacon(server.EntMan);
            server.CfgMan.SetCVar(CCVars.SolreignWingmatesEnabled, true);
            // Give the requester's IC entity a name that is guaranteed to differ from the account
            // session name, so a display-name assertion actually distinguishes the two sources.
            metaData.SetEntityName(actor, "Captain Nova");
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var accountName = players.GetSessionById(requester).Name;

            Raise(server.EntMan, beacon, actor,
                new WingmateRequestMessage("Engineering", SharedTeachingMode.Tour));
            system.SeedApprovedGuideForTests(Guide);
            system.SetVolunteeringForTests(Guide, true, true);

            var snapshot = system.BuildPrivateStateForTests(Guide);
            var expectedToken = system.GetRequesterTokenForTests(Guide, requester);
            var bystanderToken = system.GetRequesterTokenForTests(Other, requester);

            Assert.Multiple(() =>
            {
                Assert.That(expectedToken, Is.Not.Null);
                Assert.That(snapshot.RequesterToken, Is.EqualTo(expectedToken));
                Assert.That(expectedToken, Is.Not.EqualTo(Guid.Empty));
                Assert.That(bystanderToken, Is.Not.EqualTo(expectedToken),
                    "the same requester must resolve to a different, viewer-bound token for a different viewer");
                Assert.That(typeof(WingmateUiState).GetProperties(),
                    Has.None.Matches<System.Reflection.PropertyInfo>(property =>
                        property.PropertyType == typeof(NetUserId) || property.PropertyType == typeof(NetUserId?)));
                Assert.That(snapshot.RequesterDisplayName, Is.EqualTo("Captain Nova"),
                    "the display name must come from the IC entity, not the account session");
                Assert.That(snapshot.RequesterDisplayName, Is.Not.EqualTo(accountName));
            });
        });
    }

    [Test]
    public async Task PersistentBlockSurvivesRoundRestartAndPreventsOffer()
    {
        var server = Server;
        var players = server.ResolveDependency<IPlayerManager>();
        var system = server.System<WingmateSystem>();
        var (actor, requester) = await GetPlayer(server, players);
        EntityUid beacon = default;

        await server.WaitPost(() =>
        {
            beacon = SpawnBeacon(server.EntMan);
            server.CfgMan.SetCVar(CCVars.SolreignWingmatesEnabled, true);
        });
        await server.WaitRunTicks(1);

        Task? writeTask = null;

        await server.WaitAssertion(() =>
        {
            Raise(server.EntMan, beacon, actor,
                new WingmateRequestMessage("Engineering", SharedTeachingMode.Tour));
            system.SeedApprovedGuideForTests(Guide);
            system.SetVolunteeringForTests(Guide, true, true);
            var offer = system.OfferForTests(Guide, requester);
            system.AcceptForTests(requester, offer.OfferNonce!.Value);
            Assert.That(system.GetPartnerForTests(requester), Is.EqualTo(Guide));

            // Requester blocks their current partner. Nothing takes effect synchronously — the durable
            // write against the REAL SQLite-backed season ledger (this is a live ECS system with its
            // own ledger file, not a test double) must be confirmed before the pair is dissolved or the
            // block is enforced.
            var pending = system.BlockCurrentPartnerForTests(requester);
            Assert.Multiple(() =>
            {
                Assert.That(pending.Reason, Is.EqualTo("block-pending"));
                Assert.That(system.GetStatusForTests(requester), Is.EqualTo(WingmateStatus.Paired),
                    "must remain paired until the durable write is confirmed — no optimistic mutation");
            });

            writeTask = system.GetPendingBlockWriteTaskForTests(requester, Guide);
            Assert.That(writeTask, Is.Not.Null);
        });

        // Await the real disk write completing — deterministic, no sleeping/polling.
        await writeTask!;

        await server.WaitAssertion(() =>
        {
            // Draining is what makes the confirmed write "take effect": the round-local dissolve/block
            // and the in-memory persistent-block set are both applied here, not at write time.
            system.DrainCompletedBlockWritesForTests();
            Assert.That(system.GetStatusForTests(requester), Is.EqualTo(WingmateStatus.Dissolved));
            Assert.That(system.IsPersistentlyBlockedForTests(requester, Guide), Is.True);

            // A round restart wipes ALL round-local state, including WingmateRoundState's own
            // round-scoped block set — proving that whatever still blocks the offer afterwards comes
            // from the persistent ledger-backed store, not leftover round state.
            system.RoundRestartForTests();
            Assert.That(system.GetStatusForTests(requester), Is.EqualTo(WingmateStatus.Idle));

            Raise(server.EntMan, beacon, actor,
                new WingmateRequestMessage("Engineering", SharedTeachingMode.Tour));
            system.SeedApprovedGuideForTests(Guide);
            system.SetVolunteeringForTests(Guide, true, true);

            var reoffer = system.OfferForTests(Guide, requester);

            Assert.Multiple(() =>
            {
                Assert.That(reoffer.Changed, Is.False);
                Assert.That(reoffer.Reason, Is.EqualTo("blocked"));
            });

            // The blocked requester must also not be surfaced to the guide as a seeker to offer to.
            var seekerSnapshot = system.BuildPrivateStateForTests(Guide);
            Assert.That(seekerSnapshot.RequesterToken, Is.Null);
        });
    }

    [Test]
    public async Task FailedBlockWriteNoticeSurvivesBeingUnattachedAndDeliversOnNextBeaconUiOpen()
    {
        // Regression for the lost failure notification: drain-time failure accumulation must not drop a
        // notice just because the blocker has no attached entity at drain time. This drives the real
        // ECS session-attach/detach lifecycle (Robust's own IPlayerManager.SetAttachedEntity, not a
        // fabricated NetUserId with no session at all) so both the "session exists but nothing to popup
        // against" failure mode and the real BoundUIOpenedEvent redelivery seam are exercised for real.
        var server = Server;
        var players = server.ResolveDependency<IPlayerManager>();
        var system = server.System<WingmateSystem>();
        var (actor, requester) = await GetPlayer(server, players);
        EntityUid beacon = default;

        await server.WaitPost(() =>
        {
            beacon = SpawnBeacon(server.EntMan);
            server.CfgMan.SetCVar(CCVars.SolreignWingmatesEnabled, true);
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Raise(server.EntMan, beacon, actor,
                new WingmateRequestMessage("Engineering", SharedTeachingMode.Tour));
            system.SeedApprovedGuideForTests(Guide);
            system.SetVolunteeringForTests(Guide, true, true);
            var offer = system.OfferForTests(Guide, requester);
            system.AcceptForTests(requester, offer.OfferNonce!.Value);
            Assert.That(system.GetPartnerForTests(requester), Is.EqualTo(Guide));

            // Force the durable write to fail — deterministic, no real SQLite involved for this
            // assertion, which is about notice delivery, not the write itself (already covered by
            // PersistentBlockSurvivesRoundRestartAndPreventsOffer above).
            system.SetPersistentBlockWriterForTests((_, _) => Task.FromResult(false));
            system.BlockCurrentPartnerForTests(requester);

            // Simulate the blocker being unattached at the moment the drain runs — the exact condition
            // that used to silently swallow the notice.
            var session = players.GetSessionById(requester);
            Assert.That(players.SetAttachedEntity(session, null), Is.True);

            system.DrainCompletedBlockWritesForTests();

            Assert.That(system.HasPendingBlockFailureNoticeForTests(requester), Is.True,
                "the notice must be queued, not dropped, while the blocker has no attached entity");

            // Reattach (the "next session attach") and reopen the beacon UI — the seam this system
            // already observes via its BoundUIOpenedEvent subscription.
            Assert.That(players.SetAttachedEntity(session, actor, force: true), Is.True);
            server.EntMan.EventBus.RaiseLocalEvent(beacon, new BoundUIOpenedEvent(WingmateUiKey.Beacon, beacon, actor));

            Assert.That(system.HasPendingBlockFailureNoticeForTests(requester), Is.False,
                "reopening the beacon UI after reattaching must deliver — and clear — the queued notice");
        });
    }

    [Test]
    public async Task BlockFailureNoticeTextIsCountAccurateAggregatedIdentityFreeAndEpochAware()
    {
        // Delivery-level coverage for the failure notice (cdx r3 finding 4): asserts the EXACT rendered
        // text a player would see — real initialized system, real Fluent localization — not just the
        // stored integer. Covers: singular wording, plural count=2 wording, one aggregated popup body
        // per user, absence of any blocked-party identity, and the previous-shift wording variant after
        // a round rollover.
        var server = Server;
        var players = server.ResolveDependency<IPlayerManager>();
        var system = server.System<WingmateSystem>();
        var (actor, requester) = await GetPlayer(server, players);
        EntityUid beacon = default;

        await server.WaitPost(() =>
        {
            beacon = SpawnBeacon(server.EntMan);
            server.CfgMan.SetCVar(CCVars.SolreignWingmatesEnabled, true);
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            system.SetPersistentBlockWriterForTests((_, _) => Task.FromResult(false));
            system.SeedApprovedGuideForTests(Guide);
            system.SetVolunteeringForTests(Guide, true, true);
            system.SeedApprovedGuideForTests(Other);
            system.SetVolunteeringForTests(Other, true, true);

            // Detach the requester so failures accumulate in the pending store instead of delivering
            // immediately at drain time — the aggregation path under test.
            var session = players.GetSessionById(requester);
            Assert.That(players.SetAttachedEntity(session, null), Is.True);

            // Failure #1: decline-and-block against Guide; the durable write fails.
            system.RequestForTests(requester, "Engineering", SharedTeachingMode.Tour);
            var firstOffer = system.OfferForTests(Guide, requester);
            system.DeclineForTests(requester, firstOffer.OfferNonce!.Value, blockGuide: true);
            system.DrainCompletedBlockWritesForTests();

            var singular = system.BuildPendingBlockFailureNoticeTextForTests(requester);
            Assert.That(singular,
                Is.EqualTo("Your block could not be saved, so no block was applied. Please try again."),
                "one failure this shift must render the exact singular wording");

            // Failure #2: decline-and-block against a DIFFERENT partner; must aggregate, not collapse.
            var secondOffer = system.OfferForTests(Other, requester);
            system.DeclineForTests(requester, secondOffer.OfferNonce!.Value, blockGuide: true);
            system.DrainCompletedBlockWritesForTests();

            var plural = system.BuildPendingBlockFailureNoticeTextForTests(requester);
            Assert.Multiple(() =>
            {
                Assert.That(plural,
                    Is.EqualTo("2 block confirmations failed this shift — re-check your blocks."),
                    "two same-shift failures must render one count-accurate line, not a generic one");
                Assert.That(plural, Does.Not.Contain("\n"),
                    "same-shift failures aggregate into ONE popup body, never one popup per failure");
                Assert.That(plural, Does.Not.Contain(session.Name),
                    "the notice must not carry the blocker's account name");
                Assert.That(plural, Does.Not.Contain(Guide.UserId.ToString()));
                Assert.That(plural, Does.Not.Contain(Other.UserId.ToString()),
                    "the notice must never identify who the player tried to block");
            });

            // Round rollover: the records survive (never lost) but the wording flips to previous-shift
            // so the notice is honest about when the attempts happened.
            system.RoundRestartForTests();
            Assert.That(system.BuildPendingBlockFailureNoticeTextForTests(requester),
                Is.EqualTo("2 block confirmations from previous shifts failed — re-check your blocks."),
                "failures that straddle a round boundary must re-word, not disappear");

            // Reattach and open the beacon: one delivery acknowledges and clears everything queued.
            Assert.That(players.SetAttachedEntity(session, actor, force: true), Is.True);
            server.EntMan.EventBus.RaiseLocalEvent(beacon,
                new BoundUIOpenedEvent(WingmateUiKey.Beacon, beacon, actor));
            Assert.That(system.HasPendingBlockFailureNoticeForTests(requester), Is.False,
                "a single beacon-open delivery is the acknowledgment — nothing may remain queued");
        });
    }

    [Test]
    public async Task TwoFailuresInOneDrainDeliverExactlyOneAggregatedPopupToTheClient()
    {
        // cdx r4 finding 3: REAL delivery-level aggregation coverage. The player stays ATTACHED, two
        // durable-block writes against two different partners fail before a single drain pass, and the
        // assertion reads the actual popup labels alive on the player's connected CLIENT after the
        // network flush — the rendered end of the delivery pipeline — not the text helper. Exactly ONE
        // aggregated popup must be shown, carrying the count-accurate wording. The failure mode this
        // guards against (two singular popups) is separately detectable: the client popup system stacks
        // identical popups into one label with Repeats > 1 and rewrapped text, so both the label count,
        // the Repeats counter, and the exact text are asserted.
        var server = Server;
        var players = server.ResolveDependency<IPlayerManager>();
        var system = server.System<WingmateSystem>();
        var clientPopups = Client.System<ClientPopupSystem>();
        var (actor, requester) = await GetPlayer(server, players);
        EntityUid beacon = default;

        await server.WaitPost(() =>
        {
            beacon = SpawnBeacon(server.EntMan);
            server.CfgMan.SetCVar(CCVars.SolreignWingmatesEnabled, true);
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            // The writer fails SYNCHRONOUSLY (an already-completed Task), so each decline-and-block
            // enqueues its failure result inline on the server thread — and since no game tick (hence
            // no Update()-driven drain) can run inside this WaitAssertion body, BOTH results are
            // deterministically sitting in the completed-write queue before the single drain below.
            system.SetPersistentBlockWriterForTests((_, _) => Task.FromResult(false));
            system.SeedApprovedGuideForTests(Guide);
            system.SetVolunteeringForTests(Guide, true, true);
            system.SeedApprovedGuideForTests(Other);
            system.SetVolunteeringForTests(Other, true, true);

            // Two decline-and-block actions against two different partners.
            system.RequestForTests(requester, "Engineering", SharedTeachingMode.Tour);
            var firstOffer = system.OfferForTests(Guide, requester);
            system.DeclineForTests(requester, firstOffer.OfferNonce!.Value, blockGuide: true);
            var secondOffer = system.OfferForTests(Other, requester);
            system.DeclineForTests(requester, secondOffer.OfferNonce!.Value, blockGuide: true);

            Assert.That(system.HasPendingBlockFailureNoticeForTests(requester), Is.False,
                "test precondition: nothing may have drained/tallied before the single drain pass");

            // One drain pass with the player attached — production Update()-equivalent.
            system.DrainCompletedBlockWritesForTests();
        });
        // Flush the popup network event through to the connected client so it renders.
        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            var blockLabels = clientPopups.WorldLabels
                .Where(label => label.Text.Contains("block"))
                .ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(blockLabels, Has.Length.EqualTo(1),
                    "two failures in one drain must render on the client as exactly ONE popup label");
                Assert.That(blockLabels[0].Text,
                    Is.EqualTo("2 block confirmations failed this shift — re-check your blocks."),
                    "the one popup must carry the aggregated, count-accurate wording");
                Assert.That(blockLabels[0].Repeats, Is.EqualTo(1),
                    "Repeats > 1 means two identical singular popups were sent and stacked client-side");
            });
        });

        await server.WaitAssertion(() =>
            Assert.That(system.HasPendingBlockFailureNoticeForTests(requester), Is.False,
                "the delivered popup is the acknowledgment — the tally must be cleared"));
    }
}
