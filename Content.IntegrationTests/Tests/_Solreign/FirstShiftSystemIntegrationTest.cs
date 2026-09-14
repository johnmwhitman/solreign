#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Solreign.PlayerDelight.FirstShift;
using Content.Server._Solreign.SeasonLedger;
using Content.Server._Solreign.Social;
using Content.Shared._Solreign.PlayerDelight.FirstShift;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using NUnit.Framework;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.UnitTesting;
using Content.Shared.Preferences;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
/// Engine-backed First Shift coverage. GameTest exposes one authenticated player, so real ECS handlers
/// cover that actor while two opaque destinations exercise the production targeted snapshot boundary.
/// This deliberately makes no real-wire or second-connected-session claim.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class FirstShiftSystemIntegrationTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        DummyTicker = false,
    };

    private static readonly NetUserId Bystander =
        new(Guid.Parse("33333333-3333-3333-3333-333333333333"));

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
        var beacon = entMan.SpawnEntity("SolreignWingmateBeacon", MapCoordinates.Nullspace);
        Assert.That(entMan.HasComponent<FirstShiftBeaconComponent>(beacon), Is.True);
        return beacon;
    }

    private static void Raise<T>(IEntityManager entMan, EntityUid beacon, EntityUid actor, T message)
        where T : BoundUserInterfaceMessage
    {
        message.Actor = actor;
        entMan.EventBus.RaiseLocalEvent(beacon, message);
    }

    [Test]
    public async Task AuthenticatedLifecycle_StartAcceptsStaleTransportButAdvanceEtcRejectStaleGenerations()
    {
        var server = Server;
        var players = server.ResolveDependency<IPlayerManager>();
        var system = server.System<FirstShiftSystem>();
        var (actor, user) = await GetPlayer(server, players);
        EntityUid beacon = default;

        await server.WaitPost(() =>
        {
            beacon = SpawnBeacon(server.EntMan);
            server.CfgMan.SetCVar(CCVars.SolreignFirstShiftAssignmentsEnabled, false);
            system.OpenForTests(user, beacon);
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.CfgMan.GetCVar(CCVars.SolreignMarkEnabled), Is.True,
                "The activated default must exercise the visible Mark stage.");
            var disabledTransport = system.GetTransportGenerationForTests(user);
            Raise(server.EntMan, beacon, actor,
                new FirstShiftStartMessage(FirstShiftDepartment.Engineering, disabledTransport));
            Assert.That(system.GetAssignmentForTests(user), Is.Null, "CVar-off must reject Start.");

            server.CfgMan.SetCVar(CCVars.SolreignFirstShiftAssignmentsEnabled, true);
            var idleTransport = system.GetTransportGenerationForTests(user);
            Assert.That(idleTransport, Is.GreaterThan(disabledTransport));

            // v15.0.1 hotfix: Start accepts stale transport; only mutating intents reject it. See assert msg.
            Raise(server.EntMan, beacon, actor,
                new FirstShiftStartMessage(FirstShiftDepartment.Medical, disabledTransport));
            Assert.That(system.GetAssignmentForTests(user), Is.Not.Null,
                "v15.0.1 hotfix: Start accepts stale transport generation — the snapshot check is "
                + "applied only to mutating intents (Advance/Reroll/Complete/End).");
            var assigned = system.GetAssignmentForTests(user)!.Value;

            Raise(server.EntMan, beacon, actor,
                new FirstShiftStartMessage(FirstShiftDepartment.Medical, system.GetTransportGenerationForTests(user)));
            Assert.That(system.GetAssignmentForTests(user)!.Value.CardId, Is.EqualTo(assigned.CardId),
                "Delayed/duplicate Start cannot replace an active card.");

            var assignedTransport = system.GetTransportGenerationForTests(user);
            Raise(server.EntMan, beacon, actor, new FirstShiftAdvanceMessage(assignedTransport, assigned.Generation));
            var oriented = system.GetAssignmentForTests(user)!.Value;
            Assert.That(oriented.Stage, Is.EqualTo(FirstShiftAssignmentStage.Orient));
            Assert.That(oriented.Generation, Is.GreaterThan(assigned.Generation));
            Raise(server.EntMan, beacon, actor, new FirstShiftAdvanceMessage(assignedTransport, assigned.Generation));
            Assert.That(system.GetAssignmentForTests(user)!.Value, Is.EqualTo(oriented));

            var orientedTransport = system.GetTransportGenerationForTests(user);
            Raise(server.EntMan, beacon, actor, new FirstShiftRerollMessage(orientedTransport, oriented.Generation));
            var rerolled = system.GetAssignmentForTests(user)!.Value;
            Assert.That(rerolled.CardId, Is.Not.EqualTo(oriented.CardId));
            Assert.That(rerolled.Generation, Is.GreaterThan(oriented.Generation));
            Raise(server.EntMan, beacon, actor,
                new FirstShiftRerollMessage(system.GetTransportGenerationForTests(user), rerolled.Generation));
            Assert.That(system.GetAssignmentForTests(user)!.Value, Is.EqualTo(rerolled),
                "Immediate reroll is throttled.");
            Raise(server.EntMan, beacon, actor,
                new FirstShiftAdvanceMessage(system.GetTransportGenerationForTests(user), oriented.Generation));
            Assert.That(system.GetAssignmentForTests(user)!.Value, Is.EqualTo(rerolled),
                "Pre-reroll assignment generation is stale.");

            Raise(server.EntMan, beacon, actor,
                new FirstShiftEndMessage(system.GetTransportGenerationForTests(user), rerolled.Generation));
            Assert.That(system.GetAssignmentForTests(user), Is.Null);

            Raise(server.EntMan, beacon, actor,
                new FirstShiftStartMessage(FirstShiftDepartment.Engineering,
                    system.GetTransportGenerationForTests(user)));
            var early = system.GetAssignmentForTests(user)!.Value;
            Raise(server.EntMan, beacon, actor,
                new FirstShiftCompleteMessage(system.GetTransportGenerationForTests(user), early.Generation));
            Assert.That(system.GetAssignmentForTests(user), Is.EqualTo(early),
                "Generation-valid Complete must not bypass the Debrief stage.");
            Assert.That(system.Counters.Completed, Is.Zero);
            for (var stage = FirstShiftAssignmentStage.Orient;
                 stage <= FirstShiftAssignmentStage.Debrief;
                 stage++)
            {
                var current = system.GetAssignmentForTests(user)!.Value;
                Raise(server.EntMan, beacon, actor,
                    new FirstShiftAdvanceMessage(system.GetTransportGenerationForTests(user), current.Generation));
                Assert.That(system.GetAssignmentForTests(user)!.Value.Stage, Is.EqualTo(stage));
            }
            var debrief = system.GetAssignmentForTests(user)!.Value;
            Assert.That(system.BuildStateForTests(user).MarkEnabled, Is.True);
            Raise(server.EntMan, beacon, actor,
                new FirstShiftCompleteMessage(system.GetTransportGenerationForTests(user), debrief.Generation));
            Assert.That(system.GetAssignmentForTests(user), Is.EqualTo(debrief),
                "With Mark enabled, Complete cannot bypass the Mark stage.");
            Assert.That(system.Counters.Completed, Is.Zero);

            Raise(server.EntMan, beacon, actor,
                new FirstShiftAdvanceMessage(system.GetTransportGenerationForTests(user), debrief.Generation));
            var mark = system.GetAssignmentForTests(user)!.Value;
            Assert.That(mark.Stage, Is.EqualTo(FirstShiftAssignmentStage.Mark));
            Assert.That(mark.Generation, Is.GreaterThan(debrief.Generation));
            Raise(server.EntMan, beacon, actor,
                new FirstShiftCompleteMessage(system.GetTransportGenerationForTests(user), mark.Generation));
            Assert.That(system.GetAssignmentForTests(user), Is.Null);
            Assert.That(system.Counters.Completed, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ClosedOrSupersededBeaconIntent_CannotMutateOrResurrectOpenMapping()
    {
        var server = Server;
        var players = server.ResolveDependency<IPlayerManager>();
        var system = server.System<FirstShiftSystem>();
        var (actor, user) = await GetPlayer(server, players);
        EntityUid first = default;
        EntityUid second = default;

        await server.WaitPost(() =>
        {
            first = SpawnBeacon(server.EntMan);
            second = SpawnBeacon(server.EntMan);
            server.CfgMan.SetCVar(CCVars.SolreignFirstShiftAssignmentsEnabled, true);
            system.OpenForTests(user, first);
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Raise(server.EntMan, first, actor,
                new FirstShiftStartMessage(FirstShiftDepartment.Engineering, system.GetTransportGenerationForTests(user)));
            var assignment = system.GetAssignmentForTests(user)!.Value;
            var firstTransport = system.GetTransportGenerationForTests(user);

            system.CloseForTests(user, first);
            Raise(server.EntMan, first, actor, new FirstShiftAdvanceMessage(firstTransport, assignment.Generation));
            Assert.That(system.GetAssignmentForTests(user)!.Value, Is.EqualTo(assignment));
            Assert.That(system.HasOpenSnapshotMappingForTests(user), Is.False,
                "An intent must never recreate a closed mapping.");

            system.OpenForTests(user, second);
            var secondTransport = system.GetTransportGenerationForTests(user);
            Raise(server.EntMan, first, actor, new FirstShiftAdvanceMessage(secondTransport, assignment.Generation));
            Assert.That(system.GetAssignmentForTests(user)!.Value, Is.EqualTo(assignment));
            Assert.That(system.GetOpenBeaconForTests(user), Is.EqualTo(second),
                "A superseded beacon cannot steal the active destination.");

            Raise(server.EntMan, second, actor, new FirstShiftAdvanceMessage(firstTransport, assignment.Generation));
            Assert.That(system.GetAssignmentForTests(user)!.Value, Is.EqualTo(assignment),
                "A stale transport generation is rejected even from the current beacon.");
            Raise(server.EntMan, second, actor, new FirstShiftAdvanceMessage(secondTransport, assignment.Generation));
            Assert.That(system.GetAssignmentForTests(user)!.Value.Stage, Is.EqualTo(FirstShiftAssignmentStage.Orient));
        });
    }

    [Test]
    public async Task DisableDisconnectRoundRestartAndReenable_UseExactCleanupPaths()
    {
        var server = Server;
        var players = server.ResolveDependency<IPlayerManager>();
        var system = server.System<FirstShiftSystem>();
        var (actor, user) = await GetPlayer(server, players);
        EntityUid beacon = default;

        await server.WaitPost(() =>
        {
            beacon = SpawnBeacon(server.EntMan);
            server.CfgMan.SetCVar(CCVars.SolreignFirstShiftAssignmentsEnabled, true);
            system.OpenForTests(user, beacon);
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Raise(server.EntMan, beacon, actor,
                new FirstShiftStartMessage(FirstShiftDepartment.Cargo, system.GetTransportGenerationForTests(user)));
            Assert.That(system.GetAssignmentForTests(user), Is.Not.Null);
            var beforeDisable = system.GetTransportGenerationForTests(user);

            server.CfgMan.SetCVar(CCVars.SolreignFirstShiftAssignmentsEnabled, false);
            Assert.That(system.GetAssignmentForTests(user), Is.Null);
            Assert.That(system.GetTransportGenerationForTests(user), Is.GreaterThan(beforeDisable),
                "Disable publishes a disabled snapshot without rewinding transport generation.");
            Assert.That(system.BuildStateForTests(user).Enabled, Is.False);

            server.CfgMan.SetCVar(CCVars.SolreignFirstShiftAssignmentsEnabled, true);
            Assert.That(system.BuildStateForTests(user).Enabled, Is.True);
            Assert.That(system.BuildStateForTests(user).Active, Is.False);
            Raise(server.EntMan, beacon, actor,
                new FirstShiftStartMessage(FirstShiftDepartment.Service, system.GetTransportGenerationForTests(user)));
            Assert.That(system.ClearForModerator(user), Is.True);
            Assert.That(system.GetAssignmentForTests(user), Is.Null,
                "Moderator clear must invoke the same round-state clear path and publish idle.");
            Raise(server.EntMan, beacon, actor,
                new FirstShiftStartMessage(FirstShiftDepartment.Service, system.GetTransportGenerationForTests(user)));
            system.SessionUnavailableForTests(user);
            Assert.That(system.GetAssignmentForTests(user), Is.Null);
            Assert.That(system.HasOpenSnapshotMappingForTests(user), Is.False);

            system.OpenForTests(user, beacon);
            Raise(server.EntMan, beacon, actor,
                new FirstShiftStartMessage(FirstShiftDepartment.Science, system.GetTransportGenerationForTests(user)));
            system.RoundRestartForTests();
            Assert.That(system.GetAssignmentForTests(user), Is.Null);
            Assert.That(system.HasOpenSnapshotMappingForTests(user), Is.False);
            Assert.That(system.Counters, Is.EqualTo(new FirstShiftCounters(0, 0, 0)));
        });
    }

    [Test]
    public async Task TargetedSnapshots_TwoDestinationsReceiveOnlyTheirOwnServerBuiltState()
    {
        var server = Server;
        var players = server.ResolveDependency<IPlayerManager>();
        var system = server.System<FirstShiftSystem>();
        var (actor, user) = await GetPlayer(server, players);
        EntityUid beacon = default;
        await server.WaitPost(() =>
        {
            beacon = SpawnBeacon(server.EntMan);
            server.CfgMan.SetCVar(CCVars.SolreignFirstShiftAssignmentsEnabled, true);
            system.OpenForTests(user, beacon);
        });
        await server.WaitRunTicks(1);

        var destinations = new Dictionary<NetUserId, object>
        {
            [user] = new object(),
            [Bystander] = new object(),
        };
        var deliveries = new List<(object Destination, FirstShiftPrivateSnapshotEvent Snapshot)>();

        await server.WaitAssertion(() =>
        {
            Raise(server.EntMan, beacon, actor,
                new FirstShiftStartMessage(FirstShiftDepartment.Engineering, system.GetTransportGenerationForTests(user)));
            system.AssignForTests(Bystander, FirstShiftDepartment.Medical);
            var bystanderAssigned = system.GetAssignmentForTests(Bystander)!.Value;
            Assert.That(system.AdvanceForTests(Bystander, bystanderAssigned.Generation).Changed, Is.True);
            var adapter = system.CreatePrivateSnapshotAdapterForTests<object>(
                (NetUserId id, out object destination) => destinations.TryGetValue(id, out destination!),
                (snapshot, destination) => deliveries.Add((destination, snapshot)));
            adapter.RememberOpen(user, beacon);
            adapter.RememberOpen(Bystander, beacon);
            adapter.Publish([user, Bystander]);

            var own = deliveries.Single(x => ReferenceEquals(x.Destination, destinations[user])).Snapshot;
            var other = deliveries.Single(x => ReferenceEquals(x.Destination, destinations[Bystander])).Snapshot;
            Assert.Multiple(() =>
            {
                Assert.That(own.State.SelectedDepartment, Is.EqualTo(FirstShiftDepartment.Engineering));
                Assert.That(other.State.SelectedDepartment, Is.EqualTo(FirstShiftDepartment.Medical));
                Assert.That(own.State.CardId, Is.Not.EqualTo(other.State.CardId));
                Assert.That(own.State.Generation, Is.EqualTo(system.GetAssignmentForTests(user)!.Value.Generation));
                Assert.That(other.State.Generation, Is.EqualTo(system.GetAssignmentForTests(Bystander)!.Value.Generation));
                Assert.That(system.AdvanceForTests(Bystander, own.State.Generation).Changed, Is.False,
                    "A stale generation observed in another user's private card cannot act on this user's assignment.");
                Assert.That(own.Generation, Is.EqualTo(1UL));
                Assert.That(other.Generation, Is.EqualTo(1UL));
            });
        });
    }

    [Test]
    public async Task ExplicitJobSuggestionMapping_IsSafeAndDeterministic()
    {
        await Server.WaitAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(FirstShiftSystem.MapDepartmentForTests("Engineering"), Is.EqualTo(FirstShiftDepartment.Engineering));
            Assert.That(FirstShiftSystem.MapDepartmentForTests("Medical"), Is.EqualTo(FirstShiftDepartment.Medical));
            Assert.That(FirstShiftSystem.MapDepartmentForTests("Science"), Is.EqualTo(FirstShiftDepartment.Science));
            Assert.That(FirstShiftSystem.MapDepartmentForTests("Cargo"), Is.EqualTo(FirstShiftDepartment.Cargo));
            Assert.That(FirstShiftSystem.MapDepartmentForTests("Service"), Is.EqualTo(FirstShiftDepartment.Service));
            Assert.That(FirstShiftSystem.MapDepartmentForTests("Civilian"), Is.EqualTo(FirstShiftDepartment.Service));
            Assert.That(FirstShiftSystem.MapDepartmentForTests("Security"), Is.EqualTo(FirstShiftDepartment.Universal));
            Assert.That(FirstShiftSystem.MapDepartmentForTests("Command"), Is.EqualTo(FirstShiftDepartment.Universal));
        }));
    }

    // --- First-spawn push prompt (FirstShiftSpawnPromptSystem) --------------------------------
    //
    // 🔴 FIXTURE FACT the two tests below are designed AROUND, not against: pool setup performs a
    // REAL spawn for the connected account with the prompt CVars at their production defaults
    // (on) against this pair's fresh temp ledger DB — so the once-ever
    // first_shift_spawn_prompt claim for the CONNECTED account is legitimately burned before any
    // test body runs. Test 1 turns that into the real-wire positive proof; deconfounded
    // eligibility assertions use synthetic account guids via LoadPromptForTests (scheduling is
    // guid-keyed; fire-time session resolution silently drops guids with no live session).

    private string ResolveLedgerDbPath()
    {
        return SeasonLedgerDbPath.Resolve(
            Server.ResolveDependency<IConfigurationManager>(),
            Server.ResolveDependency<IResourceManager>());
    }

    private async Task PollUntilAsync(Func<Task<bool>> predicate, string failureMessage, int maxAttempts = 150)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (await predicate())
                return;

            await Server.WaitRunTicks(1);
        }

        Assert.Fail(failureMessage);
    }

    private static PlayerSpawnCompleteEvent MakeSpawnEvent(EntityUid mob, ICommonSession session, bool silent = false)
    {
        return new PlayerSpawnCompleteEvent(
            mob,
            session,
            jobId: "Passenger",
            lateJoin: false,
            silent: silent,
            joinOrder: 1,
            station: EntityUid.Invalid,
            profile: new HumanoidCharacterProfile());
    }

    [Test]
    public async Task SpawnPrompt_RealSpawnClaims_ThenOncePerAccountEver()
    {
        var server = Server;
        var players = server.ResolveDependency<IPlayerManager>();
        var (mob, user) = await GetPlayer(server, players);
        var account = user.UserId;
        var prompts = server.System<FirstShiftSpawnPromptSystem>();
        var dbPath = ResolveLedgerDbPath();

        // 1) The REAL PlayerSpawnCompleteEvent path (pool setup's own spawn) must have claimed the
        //    once-ever flag end-to-end — this is the positive real-wire proof, not a synthetic one.
        await PollUntilAsync(
            async () =>
            {
                var store = new SeasonLedgerStore(dbPath);
                var flags = await store.GetSocialFirstFlagsAsync(account);
                return flags.Contains(SolreignSocialFirstFlags.FirstShiftSpawnPrompt);
            },
            $"the pool-setup spawn never landed the first_shift_spawn_prompt claim row for {account:N} " +
            $"in {dbPath} — the real spawn path did not run.");

        // 2) Drain whatever is pending far in the future — a delivery exception fails the test.
        await server.WaitPost(() => prompts.FireDuePromptsForTests(TimeSpan.FromHours(1)));

        // 3) Once-ever across a simulated fresh round: reset round state, re-raise a real
        //    non-silent spawn event — the burned claim must keep the pending queue empty.
        ICommonSession session = default!;
        await server.WaitPost(() => session = players.Sessions.First());
        await server.WaitPost(() =>
        {
            prompts.ResetRoundStateForTests();
            server.EntMan.EventBus.RaiseLocalEvent(mob, MakeSpawnEvent(mob, session), broadcast: true);
        });
        await server.WaitRunTicks(15);

        await server.WaitAssertion(() =>
        {
            Assert.That(prompts.PendingPromptCountForTests, Is.Zero,
                "the once-ever claim must hold across round boundaries — a second eligible spawn " +
                "for the same account must never schedule a second prompt");
        });
    }

    [Test]
    public async Task SpawnPrompt_SuppressedOnSilent_CVarOff_AndVeteran()
    {
        var server = Server;
        var players = server.ResolveDependency<IPlayerManager>();
        var (mob, _) = await GetPlayer(server, players);
        var prompts = server.System<FirstShiftSpawnPromptSystem>();
        var dbPath = ResolveLedgerDbPath();

        ICommonSession session = default!;
        await server.WaitPost(() => session = players.Sessions.First());

        // Silent spawn: opted out before any async work — pending stays empty.
        await server.WaitPost(() =>
        {
            prompts.ResetRoundStateForTests();
            server.EntMan.EventBus.RaiseLocalEvent(mob, MakeSpawnEvent(mob, session, silent: true), broadcast: true);
        });
        await server.WaitRunTicks(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(prompts.PendingPromptCountForTests, Is.Zero, "a Silent spawn must never prompt");
            // 🔴 The load-bearing assertion. The pool account's once-ever claim is already burned,
            // so pending == 0 would hold even WITHOUT the Silent guard (the claim refusal masks
            // it) — watched happen when the Silent-deletion mutation survived the line above.
            // The _checkedThisRound mark is set only AFTER the Silent guard passes, so it sees
            // the guard itself.
            Assert.That(prompts.WasCheckedThisRoundForTests(session.UserId.UserId), Is.False,
                "a Silent spawn must return BEFORE the per-round mark — the guard itself, not a " +
                "downstream refusal, must be what suppresses it");
        });

        // CVar off: the layered kill switch suppresses even a non-silent spawn.
        await OverrideCVar(Side.Server, CCVars.SolreignFirstShiftSpawnPrompt, false);
        await server.WaitPost(() =>
        {
            prompts.ResetRoundStateForTests();
            server.EntMan.EventBus.RaiseLocalEvent(mob, MakeSpawnEvent(mob, session), broadcast: true);
        });
        await server.WaitRunTicks(10);
        await server.WaitAssertion(() =>
            Assert.That(prompts.PendingPromptCountForTests, Is.Zero, "CVar off must suppress the prompt"));
        await OverrideCVar(Side.Server, CCVars.SolreignFirstShiftSpawnPrompt, true);

        // Veteran (synthetic guid A, 1 completed tour): ineligible AND — the guard-order law — the
        // once-ever claim must NOT be burned by the ineligible visit.
        var veteran = Guid.NewGuid();
        var seedStore = new SeasonLedgerStore(dbPath);
        await seedStore.AddRoundRecordAsync(
            veteran,
            new RoundContribution(
                WasCaptainClean: false,
                AntagWin: false,
                EarlyDeath: false,
                RoundId: UniqueRoundId()));

        await server.WaitPost(() => prompts.LoadPromptForTests(veteran));
        await server.WaitRunTicks(15);
        await server.WaitAssertion(() =>
            Assert.That(prompts.PendingPromptCountForTests, Is.Zero, "a Tours >= 1 account must never prompt"));
        var veteranFlags = await new SeasonLedgerStore(dbPath).GetSocialFirstFlagsAsync(veteran);
        Assert.That(veteranFlags, Does.Not.Contain(SolreignSocialFirstFlags.FirstShiftSpawnPrompt),
            "an ineligible visit must never burn the once-ever claim (Tours check runs BEFORE the claim)");

        // Fresh synthetic guid B, 0 tours: eligibility + claim really work when deconfounded from
        // the pool account's already-burned claim. (Fire-time session resolution will silently
        // drop the guid — scheduling is the assertion surface here.)
        var fresh = Guid.NewGuid();
        await server.WaitPost(() => prompts.LoadPromptForTests(fresh));
        await PollUntilAsync(
            async () =>
            {
                var store = new SeasonLedgerStore(dbPath);
                var flags = await store.GetSocialFirstFlagsAsync(fresh);
                return flags.Contains(SolreignSocialFirstFlags.FirstShiftSpawnPrompt);
            },
            "a fresh Tours == 0 account must claim and schedule");
        await server.WaitAssertion(() =>
            Assert.That(prompts.PendingPromptCountForTests, Is.EqualTo(1),
                "a fresh eligible account must schedule exactly one pending prompt"));
    }

    /// <summary>Fresh round ids per run — the ledger's round-envelope replay protection rejects a
    /// repeated (round id, account) pair the moment the same db sees a second run.</summary>
    private static int UniqueRoundId()
    {
        return Random.Shared.Next(100_000, int.MaxValue - 16);
    }
}
