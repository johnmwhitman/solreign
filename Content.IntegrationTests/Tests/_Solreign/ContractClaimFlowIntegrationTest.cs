#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.Contracts;
using Content.Shared._Solreign.Contracts;
using Content.Shared._Solreign.SeasonLedger;
using Content.Shared.Station.Components;
using NUnit.Framework;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Phase-2 Track B2 (docs/plans/2026-07-11-ROADMAP-PHASE2.md, Track B, Wave B2): Contracts is
///     named as a money-path system with only Rules/Store/View-layer coverage
///     (<c>ContractRulesTests</c>, <c>ContractLedgerStoreTests</c>, <c>ContractBoardViewTests</c>) —
///     the actual ECS System (<see cref="ContractsSystem"/>) that runs <c>TryClaim</c>/<c>TryJoinRaid</c>
///     /<c>TryLaunchRaid</c>/<c>TrySkip</c> had zero coverage of its own guard clauses. This file
///     drives those private methods through their real, only public entry points — the Contracts
///     Board BUI messages (<see cref="SolreignContractClaimMessage"/> etc.) — exactly as a client
///     would trigger them, per the fork's own precedent for directed-event tests
///     (<c>SolreignZoneGateIntegrationTest</c> raising <c>ActivateInWorldEvent</c> directly instead of
///     simulating a click).
///
///     Station rig, chosen to avoid depending on a real map/grid (none of these tests care about
///     positions): <c>StationSystem.GetOwningStation</c> returns an entity that itself carries
///     <see cref="StationDataComponent"/> without needing a grid at all (see that method's own
///     "we are the station, just return ourselves" branch) — so the board entity below carries
///     <c>StationDataComponent</c> (left at its declared defaults, never touched — it is
///     <c>[Access(typeof(SharedStationSystem))]</c>-locked) alongside
///     <see cref="SolreignContractsBoardComponent"/> and <see cref="StationSolreignContractsComponent"/>
///     (NOT access-locked, so its <c>Contracts</c> list is populated directly with fixture data —
///     bypassing the random pool-filler entirely for deterministic test setup, the same idiom
///     <c>ContractBoardViewTests</c> uses for the Rules layer).
///
///     Second identities: rather than fabricate additional player sessions (Content.Server.Mind's
///     <c>MindSystem.SetUserId</c> refuses any <c>NetUserId</c> that isn't already registered
///     <c>IPlayerManager</c> player data, so a bare fabricated mind never actually attaches a UserId —
///     confirmed by reading <c>MindSystem.SetUserId</c> before relying on this), every "other
///     colleague" below is a bare <c>Guid</c> written straight into
///     <see cref="SolreignActiveContract.Claimant"/>/<c>.Participants</c> — exactly the shape
///     <c>ContractsSystem</c> itself stores and compares against, and exactly what
///     <c>ContractBoardViewTests</c> already does at the Rules layer. Only ONE real identity (the
///     pool's connected player) is needed to drive the System under test.
/// </summary>
[TestFixture]
public sealed class ContractClaimFlowIntegrationTest : GameTest
{
    // Dirty: every test spawns entities via entMan.SpawnEntity directly (not the SSpawn/Spawn proxy
    // methods GameTest tracks for automatic cleanup), matching WerewolfPolymorphTriggerTest's and
    // SolreignZoneGateIntegrationTest's own precedent for the same reason — the server must never be
    // handed back to the pool with untracked leftover board/station entities for another test to
    // trip over (e.g. an errant EntityQueryEnumerator<SolreignContractsBoardComponent> elsewhere).
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        // DummyTicker=false so the connected player session actually gets a spawned body attached
        // (HandTests.TestPickupDrop's precedent) — otherwise Sessions.First().AttachedEntity stays
        // null and every GetPlayer() call below throws InvalidOperationException.
        DummyTicker = false,
    };

    private const string PersonalContractProto = "SolContractColaAudit";
    private const string RankGatedContractProto = "SolContractExecutiveLunch"; // minRankIndex: 3
    private const string SalvageRaidProto = "SolRaidScrapReclamation"; // maxParticipants: 6, standing: 3

    private static EntityUid MakeStationBoard(IEntityManager entMan, out StationSolreignContractsComponent db)
    {
        var board = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
        entMan.EnsureComponent<StationDataComponent>(board); // default fields only; makes GetOwningStation(board) == board
        entMan.EnsureComponent<SolreignContractsBoardComponent>(board);
        db = entMan.EnsureComponent<StationSolreignContractsComponent>(board);
        return board;
    }

    private static SolreignActiveContract AddFixtureContract(
        StationSolreignContractsComponent db, string id, string prototype, SolreignContractScope scope)
    {
        var contract = new SolreignActiveContract
        {
            Id = id,
            Prototype = prototype,
            Scope = scope,
        };
        db.Contracts.Add(contract);
        return contract;
    }

    private static void AddFakeParticipants(SolreignActiveContract contract, int count, string label)
    {
        for (var i = 0; i < count; i++)
            contract.Participants[Guid.NewGuid()] = $"{label} {i}";
    }

    private static async Task<(EntityUid Player, Guid UserGuid)> GetPlayer(
        RobustIntegrationTest.ServerIntegrationInstance server, IPlayerManager playerMan)
    {
        EntityUid player = default;
        var guid = Guid.Empty;
        await server.WaitPost(() =>
        {
            var session = playerMan.Sessions.First();
            player = session.AttachedEntity!.Value;
            guid = session.UserId.UserId;
        });
        return (player, guid);
    }

    // --- double-claim ---

    [Test]
    public async Task Claim_OpenPersonalContract_Succeeds()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var (player, guid) = await GetPlayer(server, playerMan);

        EntityUid board = default;
        SolreignActiveContract contract = default!;
        await server.WaitPost(() =>
        {
            board = MakeStationBoard(entMan, out var db);
            contract = AddFixtureContract(db, "SOL-WO-TEST-001", PersonalContractProto, SolreignContractScope.Personal);
        });

        await server.WaitAssertion(() =>
        {
            entMan.EventBus.RaiseLocalEvent(board, new SolreignContractClaimMessage(contract.Id) { Actor = player });

            Assert.Multiple(() =>
            {
                Assert.That(contract.Claimant, Is.EqualTo(guid));
                Assert.That(contract.ClaimantName, Is.Not.Null.And.Not.Empty);
            });
        });
    }

    [Test]
    public async Task DoubleClaim_ContractAlreadyClaimedBySomeoneElse_IsDenied()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var (player, guid) = await GetPlayer(server, playerMan);

        var rival = Guid.NewGuid();
        EntityUid board = default;
        SolreignActiveContract contract = default!;
        await server.WaitPost(() =>
        {
            board = MakeStationBoard(entMan, out var db);
            contract = AddFixtureContract(db, "SOL-WO-TEST-002", PersonalContractProto, SolreignContractScope.Personal);
            contract.Claimant = rival;
            contract.ClaimantName = "Fixture Rival";
        });

        await server.WaitAssertion(() =>
        {
            entMan.EventBus.RaiseLocalEvent(board, new SolreignContractClaimMessage(contract.Id) { Actor = player });

            Assert.Multiple(() =>
            {
                Assert.That(contract.Claimant, Is.EqualTo(rival), "The second claim attempt must not steal the contract.");
                Assert.That(contract.Claimant, Is.Not.EqualTo(guid));
            });
        });
    }

    // --- claim cap (anti-grief rule 7, System-layer wiring of ContractRules.CanClaimAnother) ---

    [Test]
    public async Task ClaimCap_FourthPersonalClaim_IsDenied()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var (player, guid) = await GetPlayer(server, playerMan);

        EntityUid board = default;
        SolreignActiveContract fourth = default!;
        await server.WaitPost(() =>
        {
            board = MakeStationBoard(entMan, out var db);
            for (var i = 0; i < 3; i++)
            {
                var already = AddFixtureContract(db, $"SOL-WO-CAP-{i}", PersonalContractProto, SolreignContractScope.Personal);
                already.Claimant = guid;
                already.ClaimantName = "Fixture Self";
            }

            fourth = AddFixtureContract(db, "SOL-WO-CAP-3", PersonalContractProto, SolreignContractScope.Personal);
        });

        await server.WaitAssertion(() =>
        {
            entMan.EventBus.RaiseLocalEvent(board, new SolreignContractClaimMessage(fourth.Id) { Actor = player });

            Assert.That(fourth.Claimant, Is.Null,
                "A player already at the 3-claim cap (anti-grief rule 7) must be denied a 4th.");
        });
    }

    // --- rank change mid-contract ---

    [Test]
    public async Task RankGate_BelowThreshold_DeniedThenAllowedAfterRankBump()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var (player, guid) = await GetPlayer(server, playerMan);

        EntityUid board = default;
        SolreignActiveContract contract = default!;
        await server.WaitPost(() =>
        {
            board = MakeStationBoard(entMan, out var db);
            contract = AddFixtureContract(db, "SOL-WO-RANK-1", RankGatedContractProto, SolreignContractScope.Personal);
            entMan.EnsureComponent<SeasonTitleComponent>(player).RankIndex = 0;
        });

        await server.WaitAssertion(() =>
        {
            entMan.EventBus.RaiseLocalEvent(board, new SolreignContractClaimMessage(contract.Id) { Actor = player });
            Assert.That(contract.Claimant, Is.Null, "Probationary Asset (rank 0) must not clear the Manager+ gate.");
        });

        await server.WaitPost(() =>
        {
            entMan.GetComponent<SeasonTitleComponent>(player).RankIndex = 3; // promoted mid-round
        });

        await server.WaitAssertion(() =>
        {
            entMan.EventBus.RaiseLocalEvent(board, new SolreignContractClaimMessage(contract.Id) { Actor = player });
            Assert.That(contract.Claimant, Is.EqualTo(guid),
                "After a mid-contract rank bump to Manager+, the same still-open contract must now claim.");
        });
    }

    // --- salvage group join ---

    [Test]
    public async Task JoinRaid_Succeeds_RegistersParticipant()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var (player, guid) = await GetPlayer(server, playerMan);

        EntityUid board = default;
        SolreignActiveContract raid = default!;
        await server.WaitPost(() =>
        {
            board = MakeStationBoard(entMan, out var db);
            raid = AddFixtureContract(db, "SOL-RAID-JOIN-1", SalvageRaidProto, SolreignContractScope.SalvageRaid);
        });

        await server.WaitAssertion(() =>
        {
            entMan.EventBus.RaiseLocalEvent(board, new SolreignRaidJoinMessage(raid.Id) { Actor = player });

            Assert.Multiple(() =>
            {
                Assert.That(raid.Participants.ContainsKey(guid), Is.True);
                Assert.That(raid.Participants.Count, Is.EqualTo(1));
            });
        });
    }

    [Test]
    public async Task JoinRaid_SameUserTwice_DoesNotDuplicate()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var (player, _) = await GetPlayer(server, playerMan);

        EntityUid board = default;
        SolreignActiveContract raid = default!;
        await server.WaitPost(() =>
        {
            board = MakeStationBoard(entMan, out var db);
            raid = AddFixtureContract(db, "SOL-RAID-JOIN-2", SalvageRaidProto, SolreignContractScope.SalvageRaid);
        });

        await server.WaitAssertion(() =>
        {
            entMan.EventBus.RaiseLocalEvent(board, new SolreignRaidJoinMessage(raid.Id) { Actor = player });
            entMan.EventBus.RaiseLocalEvent(board, new SolreignRaidJoinMessage(raid.Id) { Actor = player });

            Assert.That(raid.Participants.Count, Is.EqualTo(1), "Re-joining must be idempotent, not duplicate the roster entry.");
        });
    }

    [Test]
    public async Task JoinRaid_AtRosterCap_IsDenied()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var (player, guid) = await GetPlayer(server, playerMan);

        EntityUid board = default;
        SolreignActiveContract raid = default!;
        await server.WaitPost(() =>
        {
            board = MakeStationBoard(entMan, out var db);
            raid = AddFixtureContract(db, "SOL-RAID-JOIN-3", SalvageRaidProto, SolreignContractScope.SalvageRaid);
            AddFakeParticipants(raid, 6, "Fixture Crewmate"); // SolRaidScrapReclamation's maxParticipants
        });

        await server.WaitAssertion(() =>
        {
            entMan.EventBus.RaiseLocalEvent(board, new SolreignRaidJoinMessage(raid.Id) { Actor = player });

            Assert.Multiple(() =>
            {
                Assert.That(raid.Participants.ContainsKey(guid), Is.False, "A full roster must refuse a new joiner.");
                Assert.That(raid.Participants.Count, Is.EqualTo(6));
            });
        });
    }

    [Test]
    public async Task JoinRaid_AfterLaunch_IsDenied()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var (player, guid) = await GetPlayer(server, playerMan);

        EntityUid board = default;
        SolreignActiveContract raid = default!;
        await server.WaitPost(() =>
        {
            board = MakeStationBoard(entMan, out var db);
            raid = AddFixtureContract(db, "SOL-RAID-JOIN-4", SalvageRaidProto, SolreignContractScope.SalvageRaid);
            AddFakeParticipants(raid, 2, "Fixture Crewmate");
            raid.Launched = true; // roster already locked
        });

        await server.WaitAssertion(() =>
        {
            entMan.EventBus.RaiseLocalEvent(board, new SolreignRaidJoinMessage(raid.Id) { Actor = player });

            Assert.Multiple(() =>
            {
                Assert.That(raid.Participants.ContainsKey(guid), Is.False, "A launched raid's roster must be locked.");
                Assert.That(raid.Participants.Count, Is.EqualTo(2));
            });
        });
    }

    [Test]
    public async Task JoinRaid_UnknownContractId_IsANoOp()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var (player, _) = await GetPlayer(server, playerMan);

        EntityUid board = default;
        await server.WaitPost(() =>
        {
            board = MakeStationBoard(entMan, out _);
        });

        await server.WaitAssertion(() =>
        {
            // TryGetContract fails for an id that was never issued; ContractsSystem must return
            // quietly rather than throw (defensive against a stale client-cached board id).
            Assert.DoesNotThrow(() =>
                entMan.EventBus.RaiseLocalEvent(board, new SolreignRaidJoinMessage("SOL-RAID-DOES-NOT-EXIST") { Actor = player }));
        });
    }

    // --- salvage group launch (accept-time scaling, wiring of ContractRules.SalvageScaledAmount/SalvageStanding) ---

    [Test]
    public async Task LaunchRaid_ScalesRequiredAndStanding_ToRegisteredCrew()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var (player, _) = await GetPlayer(server, playerMan);

        EntityUid board = default;
        SolreignActiveContract raid = default!;
        await server.WaitPost(() =>
        {
            board = MakeStationBoard(entMan, out var db);
            raid = AddFixtureContract(db, "SOL-RAID-LAUNCH-1", SalvageRaidProto, SolreignContractScope.SalvageRaid);
            raid.Required = new[] { 5 }; // SolRaidScrapReclamation's base per-head amount
            raid.StandingPerHead = 3; // base standing

            AddFakeParticipants(raid, 3, "Fixture Crewmate");
            entMan.EventBus.RaiseLocalEvent(board, new SolreignRaidJoinMessage(raid.Id) { Actor = player });
        });

        Assert.That(raid.Participants.Count, Is.EqualTo(4), "Setup failed: expected 3 fixture crewmates + the real player.");

        await server.WaitAssertion(() =>
        {
            entMan.EventBus.RaiseLocalEvent(board, new SolreignRaidLaunchMessage(raid.Id) { Actor = player });

            Assert.Multiple(() =>
            {
                Assert.That(raid.Launched, Is.True);
                Assert.That(raid.Required[0], Is.EqualTo(ContractRules.SalvageScaledAmount(5, 4, 6)),
                    "Objective must scale to the 4-strong crew registered at launch.");
                Assert.That(raid.StandingPerHead, Is.EqualTo(ContractRules.SalvageStanding(3, 4, 6)),
                    "Per-head reward must scale to the 4-strong crew registered at launch.");
            });
        });
    }

    [Test]
    public async Task LaunchRaid_ByNonParticipant_IsDenied()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var (player, _) = await GetPlayer(server, playerMan);

        EntityUid board = default;
        SolreignActiveContract raid = default!;
        await server.WaitPost(() =>
        {
            board = MakeStationBoard(entMan, out var db);
            raid = AddFixtureContract(db, "SOL-RAID-LAUNCH-2", SalvageRaidProto, SolreignContractScope.SalvageRaid);
            raid.Required = new[] { 5 };
            raid.StandingPerHead = 3;
            AddFakeParticipants(raid, 2, "Fixture Crewmate"); // player never joins this one
        });

        await server.WaitAssertion(() =>
        {
            entMan.EventBus.RaiseLocalEvent(board, new SolreignRaidLaunchMessage(raid.Id) { Actor = player });

            Assert.Multiple(() =>
            {
                Assert.That(raid.Launched, Is.False, "A non-member must not be able to launch someone else's raid.");
                Assert.That(raid.Required[0], Is.EqualTo(5), "Unlaunched objective must remain at its base amount.");
            });
        });
    }

    // --- salvage group "leave" (the system's real un-registration path is Skip, cancelling the whole raid) ---

    [Test]
    public async Task Skip_ByRaidParticipant_RemovesTheEntireRaidContract()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var (player, guid) = await GetPlayer(server, playerMan);

        EntityUid board = default;
        SolreignActiveContract raid = default!;
        await server.WaitPost(() =>
        {
            board = MakeStationBoard(entMan, out var db);
            raid = AddFixtureContract(db, "SOL-RAID-SKIP-1", SalvageRaidProto, SolreignContractScope.SalvageRaid);
            entMan.EventBus.RaiseLocalEvent(board, new SolreignRaidJoinMessage(raid.Id) { Actor = player });
        });

        Assert.That(raid.Participants.ContainsKey(guid), Is.True, "Setup failed: player did not register on the roster.");

        await server.WaitAssertion(() =>
        {
            entMan.EventBus.RaiseLocalEvent(board, new SolreignContractSkipMessage(raid.Id) { Actor = player });

            var db = entMan.GetComponent<StationSolreignContractsComponent>(board);
            Assert.That(db.Contracts.Any(c => c.Id == raid.Id), Is.False,
                "A registered participant skipping their own raid must cancel it for the whole roster (the only 'leave' surface Contracts exposes).");
        });
    }

    [Test]
    public async Task Skip_ByNonParticipant_IsDeniedForAnOwnedRaid()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var (player, _) = await GetPlayer(server, playerMan);

        EntityUid board = default;
        SolreignActiveContract raid = default!;
        await server.WaitPost(() =>
        {
            board = MakeStationBoard(entMan, out var db);
            raid = AddFixtureContract(db, "SOL-RAID-SKIP-2", SalvageRaidProto, SolreignContractScope.SalvageRaid);
            AddFakeParticipants(raid, 2, "Fixture Crewmate"); // player is not one of them
        });

        await server.WaitAssertion(() =>
        {
            entMan.EventBus.RaiseLocalEvent(board, new SolreignContractSkipMessage(raid.Id) { Actor = player });

            var db = entMan.GetComponent<StationSolreignContractsComponent>(board);
            Assert.That(db.Contracts.Any(c => c.Id == raid.Id), Is.True,
                "A non-member must not be able to cancel someone else's already-registered raid.");
        });
    }
}
