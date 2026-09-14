#nullable enable
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.Contracts;
using Content.Server._Solreign.Corporate;
using Content.Server._Solreign.SeasonLedger;
using Content.Shared._Solreign.Contracts;
using Content.Shared.CCVar;
using Content.Shared.Interaction;
using Content.Shared.Station.Components;
using NUnit.Framework;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     v14 low-pop quest-board extension (spec §2): drives <see cref="ContractsSystem.FillContracts"/>'s
///     guaranteed-easy-contract wiring end-to-end (not just the pure <c>ContractRulesTests</c> layer),
///     confirming both that the guarantee actually fires with the extension's shipped enabled-by-default
///     CVar, AND that its runtime kill switch genuinely prevents a forced issue when explicitly disabled
///     — the same "CVar-off regression" discipline
///     <c>ContractsIntegrationTest</c>/<c>ContractClaimFlowIntegrationTest</c> already rely on for the
///     always-on core. Isolates the guarantee from the normal random top-up by capping
///     MaxPersonal/MaxSalvage at 0, so any issued contract can only have come from the guarantee path.
/// </summary>
[TestFixture]
public sealed class ContractsLowPopIntegrationTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true, // this test mutates CVars — never hand the server back to the pool.
        DummyTicker = false, // the real claim-path test needs the connected session's attached body.
    };

    // The four EasyTier-tagged prototypes (quest-board spec §2.2) — every other Personal prototype
    // stays EasyTier: false, so any contract issued here must be one of these.
    private static readonly string[] EasyTierProtoIds =
    {
        "SolContractColaAudit",
        "SolContractPenRecovery",
        "SolContractMandatoryFun",
        "SolContractIncidentReport",
    };

    [Test]
    public async Task LowPopExtension_DefaultEnabled_AtOrUnderThreshold_ForcesAnEasyTierContract()
    {
        var server = Server;
        var entMan = server.EntMan;

        // Deliberately do NOT touch the CVar: this is the behavioral regression guard for the
        // shipped default. A false default must leave this board unfilled when both caps are zero.
        // Comfortably above the pool's connected-player count so the guarantee is guaranteed to fire
        // regardless of how many sessions this particular pooled server happens to carry.
        server.CfgMan.SetCVar(CCVars.SolreignContractsLowPopThreshold, 50);

        await server.WaitPost(() =>
        {
            var stationUid = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            var sys = entMan.System<ContractsSystem>();
            var comp = entMan.EnsureComponent<StationSolreignContractsComponent>(stationUid);

            // Isolate the guarantee: with both caps at 0, the normal FillScope top-up issues nothing at
            // all, so any contract present after FillContracts can only be the guarantee's own force-issue.
            comp.MaxPersonal = 0;
            comp.MaxSalvage = 0;

            sys.FillContracts(stationUid, comp);

            Assert.That(comp.Contracts, Has.Count.EqualTo(1),
                "the low-pop guarantee should have force-issued exactly one contract despite MaxPersonal=0");
            Assert.That(EasyTierProtoIds, Does.Contain(comp.Contracts[0].Prototype),
                "the force-issued contract must be drawn from the EasyTier-tagged pool");
        });
    }

    [Test]
    public async Task LowPopExtension_ExplicitlyDisabled_DoesNotForceAnyContract()
    {
        var server = Server;
        var entMan = server.EntMan;

        server.CfgMan.SetCVar(CCVars.SolreignContractsQuestBoardEnabled, false);

        await server.WaitPost(() =>
        {
            var stationUid = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            var sys = entMan.System<ContractsSystem>();
            var comp = entMan.EnsureComponent<StationSolreignContractsComponent>(stationUid);
            comp.MaxPersonal = 0;
            comp.MaxSalvage = 0;
            comp.Contracts.Add(new SolreignActiveContract
            {
                Id = "SOL-WO-DISABLED",
                Prototype = "SolContractOnboarding1",
                Scope = SolreignContractScope.Personal,
            });
            var skipBefore = comp.NextSkipTime;

            sys.FillContracts(stationUid, comp);

            Assert.Multiple(() =>
            {
                Assert.That(comp.Contracts, Has.Count.EqualTo(1));
                Assert.That(comp.Contracts[0].Id, Is.EqualTo("SOL-WO-DISABLED"),
                    "CVar-off must not issue or expire for the guarantee");
                Assert.That(comp.TotalIssued, Is.Zero);
                Assert.That(comp.NextSkipTime, Is.EqualTo(skipBefore));
            });
        });
    }

    [Test]
    public async Task LowPopExtension_Enabled_AboveThreshold_DoesNotForceAnyContract()
    {
        var server = Server;
        var entMan = server.EntMan;

        server.CfgMan.SetCVar(CCVars.SolreignContractsQuestBoardEnabled, true);
        // Below the pool's connected-player count (a GameTest server always has >= 1 session) so the
        // guarantee's threshold check must fail even though the extension itself is on.
        server.CfgMan.SetCVar(CCVars.SolreignContractsLowPopThreshold, 0);

        await server.WaitPost(() =>
        {
            var stationUid = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            var sys = entMan.System<ContractsSystem>();
            var comp = entMan.EnsureComponent<StationSolreignContractsComponent>(stationUid);
            comp.MaxPersonal = 0;
            comp.MaxSalvage = 0;
            comp.Contracts.Add(new SolreignActiveContract
            {
                Id = "SOL-WO-ABOVE-THRESHOLD",
                Prototype = "SolContractOnboarding1",
                Scope = SolreignContractScope.Personal,
            });
            var skipBefore = comp.NextSkipTime;

            sys.FillContracts(stationUid, comp);

            Assert.Multiple(() =>
            {
                Assert.That(comp.Contracts, Has.Count.EqualTo(1));
                Assert.That(comp.Contracts[0].Id, Is.EqualTo("SOL-WO-ABOVE-THRESHOLD"),
                    "above-threshold Ensure must not expire an existing non-easy row");
                Assert.That(comp.TotalIssued, Is.Zero);
                Assert.That(comp.NextSkipTime, Is.EqualTo(skipBefore));
            });
        });
    }

    [Test]
    public async Task LowPopExtension_RealClaims_RearmOnceAndNeverExceedMaxPlusTwo()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        EntityUid player = default;

        server.CfgMan.SetCVar(CCVars.SolreignContractsQuestBoardEnabled, true);
        server.CfgMan.SetCVar(CCVars.SolreignContractsLowPopThreshold, 50);

        await server.WaitPost(() =>
        {
            player = playerMan.Sessions.First().AttachedEntity!.Value;
        });

        EntityUid board = default;
        StationSolreignContractsComponent comp = default!;
        await server.WaitPost(() =>
        {
            // The board is also its owning station, matching the production BUI routing contract.
            board = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            entMan.EnsureComponent<StationDataComponent>(board);
            entMan.EnsureComponent<SolreignContractsBoardComponent>(board);
            comp = entMan.EnsureComponent<StationSolreignContractsComponent>(board);
            comp.MaxPersonal = 0;
            comp.MaxSalvage = 0;

            entMan.System<ContractsSystem>().FillContracts(board, comp);
            Assert.That(comp.Contracts, Has.Count.EqualTo(1));
        });

        await server.WaitAssertion(() =>
        {
            var first = comp.Contracts.Single(c => c.Claimant is null);
            entMan.EventBus.RaiseLocalEvent(
                board,
                new SolreignContractClaimMessage(first.Id) { Actor = player });

            Assert.Multiple(() =>
            {
                Assert.That(comp.Contracts, Has.Count.EqualTo(2),
                    "the first real claim re-arms one open easy at the bounded MaxPersonal+2 ceiling");
                Assert.That(comp.Contracts.Count(c => c.Claimant is not null), Is.EqualTo(1));
                Assert.That(comp.Contracts.Count(c => c.Claimant is null
                    && EasyTierProtoIds.Contains(c.Prototype)), Is.EqualTo(1));
            });
        });

        await server.WaitAssertion(() =>
        {
            var second = comp.Contracts.Single(c => c.Claimant is null);
            entMan.EventBus.RaiseLocalEvent(
                board,
                new SolreignContractClaimMessage(second.Id) { Actor = player });

            Assert.Multiple(() =>
            {
                Assert.That(comp.Contracts, Has.Count.EqualTo(2),
                    "the second claim reaches the hard bound and must not create a third contract");
                Assert.That(comp.Contracts.Count(c => c.Claimant is not null), Is.EqualTo(2));
                Assert.That(comp.Contracts.Count(c => c.Claimant is null
                    && EasyTierProtoIds.Contains(c.Prototype)), Is.Zero);
                Assert.That(comp.TotalIssued, Is.EqualTo(2));
            });
        });
    }

    [Test]
    public async Task LowPopExtension_ClaimAtSoftBound_ReplacesOldestOpenNonEasyWithoutPayout()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        EntityUid player = default;
        NetUserId userId = default;

        server.CfgMan.SetCVar(CCVars.SolreignContractsQuestBoardEnabled, true);
        server.CfgMan.SetCVar(CCVars.SolreignContractsLowPopThreshold, 50);

        await server.WaitPost(() =>
        {
            var session = playerMan.Sessions.First();
            player = session.AttachedEntity!.Value;
            userId = session.UserId;
        });

        EntityUid board = default;
        StationSolreignContractsComponent comp = default!;
        await server.WaitPost(() =>
        {
            board = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            entMan.EnsureComponent<StationDataComponent>(board);
            entMan.EnsureComponent<SolreignContractsBoardComponent>(board);
            comp = entMan.EnsureComponent<StationSolreignContractsComponent>(board);
            comp.MaxPersonal = 2;
            comp.MaxSalvage = 0;

            // List order is the issue-order truth. IDs are deliberately reverse-lexical so an
            // implementation that sorts by ID removes the wrong contract.
            comp.Contracts.Add(new SolreignActiveContract
            {
                Id = "SOL-WO-Z-OLDEST",
                Prototype = "SolContractOnboarding1",
                Scope = SolreignContractScope.Personal,
            });
            comp.Contracts.Add(new SolreignActiveContract
            {
                Id = "SOL-WO-A-NEWER",
                Prototype = "SolContractOnboarding2",
                Scope = SolreignContractScope.Personal,
            });

            entMan.System<ContractsSystem>().FillContracts(board, comp);
            Assert.That(comp.Contracts, Has.Count.EqualTo(3));
        });

        await server.WaitAssertion(() =>
        {
            var firstEasy = comp.Contracts.Single(c => c.Claimant is null
                && EasyTierProtoIds.Contains(c.Prototype));
            var skipBefore = comp.NextSkipTime;
            var corporate = entMan.System<SolreignCorporateRuleSystem>();
            var ledger = entMan.System<SeasonLedgerSystem>();
            var standingBefore = corporate.CurrentStanding(userId);
            var totalsBefore = ledger.CurrentContractTotals(userId.UserId);
            var logCountBefore = ledger.CurrentContractLogCount(userId.UserId);

            entMan.EventBus.RaiseLocalEvent(
                board,
                new SolreignContractClaimMessage(firstEasy.Id) { Actor = player });

            Assert.Multiple(() =>
            {
                Assert.That(comp.Contracts, Has.Count.EqualTo(3),
                    "replacement must preserve the preferred MaxPersonal+1 active count");
                Assert.That(comp.Contracts.Any(c => c.Id == "SOL-WO-Z-OLDEST"), Is.False,
                    "maintenance removes the insertion-oldest open non-easy, not lexical-lowest ID");
                Assert.That(comp.Contracts.Any(c => c.Id == "SOL-WO-A-NEWER"), Is.True);
                Assert.That(comp.Contracts.Count(c => c.Claimant is not null), Is.EqualTo(1));
                Assert.That(comp.Contracts.Count(c => c.Claimant is null
                    && EasyTierProtoIds.Contains(c.Prototype)), Is.EqualTo(1));
                Assert.That(comp.TotalIssued, Is.EqualTo(2),
                    "maintenance removal is not a completion; only the two generated easies count as issued");
                Assert.That(comp.NextSkipTime, Is.EqualTo(skipBefore),
                    "maintenance removal must not consume or reset the player skip cooldown");
                Assert.That(corporate.CurrentStanding(userId), Is.EqualTo(standingBefore),
                    "maintenance replacement must not award Corporate Standing");
                Assert.That(ledger.CurrentContractTotals(userId.UserId), Is.EqualTo(totalsBefore),
                    "maintenance replacement must not credit contract completion or score");
                Assert.That(ledger.CurrentContractLogCount(userId.UserId), Is.EqualTo(logCountBefore),
                    "maintenance replacement must not append a contract-log row");
            });
        });
    }

    [Test]
    public async Task LowPopExtension_RepeatedClaims_RetireAtMostOneNormalContract()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        EntityUid player = default;

        server.CfgMan.SetCVar(CCVars.SolreignContractsQuestBoardEnabled, true);
        server.CfgMan.SetCVar(CCVars.SolreignContractsLowPopThreshold, 50);

        await server.WaitPost(() =>
        {
            player = playerMan.Sessions.First().AttachedEntity!.Value;
        });

        EntityUid board = default;
        StationSolreignContractsComponent comp = default!;
        await server.WaitPost(() =>
        {
            board = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            entMan.EnsureComponent<StationDataComponent>(board);
            entMan.EnsureComponent<SolreignContractsBoardComponent>(board);
            comp = entMan.EnsureComponent<StationSolreignContractsComponent>(board);
            comp.MaxPersonal = 2;
            comp.MaxSalvage = 0;

            comp.Contracts.Add(new SolreignActiveContract
            {
                Id = "SOL-WO-NORMAL-1",
                Prototype = "SolContractOnboarding1",
                Scope = SolreignContractScope.Personal,
            });
            comp.Contracts.Add(new SolreignActiveContract
            {
                Id = "SOL-WO-NORMAL-2",
                Prototype = "SolContractOnboarding2",
                Scope = SolreignContractScope.Personal,
            });

            entMan.System<ContractsSystem>().FillContracts(board, comp);
        });

        await server.WaitAssertion(() =>
        {
            for (var claim = 0; claim < 3; claim++)
            {
                var openEasy = comp.Contracts.Single(c => c.Claimant is null
                    && EasyTierProtoIds.Contains(c.Prototype));
                entMan.EventBus.RaiseLocalEvent(
                    board,
                    new SolreignContractClaimMessage(openEasy.Id) { Actor = player });
            }

            Assert.Multiple(() =>
            {
                Assert.That(comp.Contracts, Has.Count.EqualTo(comp.MaxPersonal + 2),
                    "the third claim must stop at the MaxPersonal+2 hard bound");
                Assert.That(comp.Contracts.Count(c => c.Claimant is not null), Is.EqualTo(3));
                Assert.That(comp.Contracts.Count(c => c.Id is "SOL-WO-NORMAL-1" or "SOL-WO-NORMAL-2"),
                    Is.EqualTo(1),
                    "one guarantee chain may retire at most one normal contract before using the hard-cap slot");
                Assert.That(comp.Contracts.Count(c => c.Claimant is null
                    && EasyTierProtoIds.Contains(c.Prototype)), Is.Zero,
                    "after the third bounded claim, no fourth easy may be issued");
            });
        });
    }

    [Test]
    public async Task LowPopExtension_OtherPlayersClaim_DoesNotBlockRealSecondPlayer()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        EntityUid player = default;
        Guid playerGuid = default;

        server.CfgMan.SetCVar(CCVars.SolreignContractsQuestBoardEnabled, true);
        server.CfgMan.SetCVar(CCVars.SolreignContractsLowPopThreshold, 50);

        await server.WaitPost(() =>
        {
            var session = playerMan.Sessions.First();
            player = session.AttachedEntity!.Value;
            playerGuid = session.UserId.UserId;
        });

        EntityUid board = default;
        StationSolreignContractsComponent comp = default!;
        var firstClaimant = Guid.NewGuid();
        await server.WaitPost(() =>
        {
            board = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            entMan.EnsureComponent<StationDataComponent>(board);
            entMan.EnsureComponent<SolreignContractsBoardComponent>(board);
            comp = entMan.EnsureComponent<StationSolreignContractsComponent>(board);
            comp.MaxPersonal = 0;
            comp.MaxSalvage = 0;
            comp.Contracts.Add(new SolreignActiveContract
            {
                Id = "SOL-WO-OTHER-CLAIM",
                Prototype = "SolContractIncidentReport",
                Scope = SolreignContractScope.Personal,
                Claimant = firstClaimant,
                ClaimantName = "First Player",
            });

            entMan.System<ContractsSystem>().FillContracts(board, comp);
            Assert.That(comp.Contracts.Count(c => c.Claimant is null
                && EasyTierProtoIds.Contains(c.Prototype)), Is.EqualTo(1),
                "another player's unresolved easy must not hide the re-armed open easy");
        });

        await server.WaitAssertion(() =>
        {
            var openEasy = comp.Contracts.Single(c => c.Claimant is null
                && EasyTierProtoIds.Contains(c.Prototype));
            entMan.EventBus.RaiseLocalEvent(
                board,
                new SolreignContractClaimMessage(openEasy.Id) { Actor = player });

            Assert.Multiple(() =>
            {
                Assert.That(comp.Contracts, Has.Count.EqualTo(2));
                Assert.That(comp.Contracts.Any(c => c.Claimant == firstClaimant), Is.True);
                Assert.That(comp.Contracts.Any(c => c.Claimant == playerGuid), Is.True,
                    "the real second player must be able to claim the re-armed easy");
                Assert.That(comp.Contracts.Count(c => c.Claimant is null
                    && EasyTierProtoIds.Contains(c.Prototype)), Is.Zero,
                    "the MaxPersonal+2 hard bound stops a third issue");
            });
        });
    }

    [Test]
    public async Task LowPopExtension_LiveEnable_ReconcilesExistingStationWithoutFill()
    {
        var server = Server;
        var entMan = server.EntMan;

        server.CfgMan.SetCVar(CCVars.SolreignContractsQuestBoardEnabled, false);
        server.CfgMan.SetCVar(CCVars.SolreignContractsLowPopThreshold, 50);

        StationSolreignContractsComponent comp = default!;
        await server.WaitPost(() =>
        {
            var station = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            comp = entMan.EnsureComponent<StationSolreignContractsComponent>(station);
            comp.MaxPersonal = 0;
            comp.MaxSalvage = 0;

            entMan.System<ContractsSystem>().FillContracts(station, comp);
            Assert.That(comp.Contracts, Is.Empty);
        });

        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(CCVars.SolreignContractsQuestBoardEnabled, true);

            Assert.Multiple(() =>
            {
                Assert.That(comp.Contracts, Has.Count.EqualTo(1),
                    "live enable must restore the invariant without a restart or unrelated Fill call");
                Assert.That(EasyTierProtoIds, Does.Contain(comp.Contracts[0].Prototype));
                Assert.That(comp.TotalIssued, Is.EqualTo(1));
            });
        });
    }

    [Test]
    public async Task LowPopExtension_RealCompletion_PaysAndLogsExactlyOnceThenRearms()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        EntityUid player = default;
        NetUserId userId = default;

        server.CfgMan.SetCVar(CCVars.SolreignContractsQuestBoardEnabled, true);
        server.CfgMan.SetCVar(CCVars.SolreignContractsLowPopThreshold, 50);

        await server.WaitPost(() =>
        {
            var session = playerMan.Sessions.First();
            player = session.AttachedEntity!.Value;
            userId = session.UserId;
        });

        EntityUid board = default;
        StationSolreignContractsComponent comp = default!;
        SolreignActiveContract contract = default!;
        await server.WaitPost(() =>
        {
            board = entMan.SpawnEntity("SolreignFulfillmentDropbox", MapCoordinates.Nullspace);
            entMan.EnsureComponent<StationDataComponent>(board);
            entMan.EnsureComponent<SolreignContractsBoardComponent>(board);
            comp = entMan.EnsureComponent<StationSolreignContractsComponent>(board);
            comp.MaxPersonal = 0;
            comp.MaxSalvage = 0;
            contract = new SolreignActiveContract
            {
                Id = "SOL-WO-COMPLETE-ONCE",
                Prototype = "SolContractIncidentReport",
                Scope = SolreignContractScope.Personal,
                Required = new[] { 1 },
                Progress = new[] { 0 },
                StandingPerHead = 3,
            };
            comp.Contracts.Add(contract);

            entMan.EventBus.RaiseLocalEvent(
                board,
                new SolreignContractClaimMessage(contract.Id) { Actor = player });
        });

        await server.WaitAssertion(() =>
        {
            var corporate = entMan.System<SolreignCorporateRuleSystem>();
            var ledger = entMan.System<SeasonLedgerSystem>();
            var standingBefore = corporate.CurrentStanding(userId);
            var totalsBefore = ledger.CurrentContractTotals(userId.UserId);
            var logCountBefore = ledger.CurrentContractLogCount(userId.UserId);

            var paper = entMan.SpawnEntity("Paper", MapCoordinates.Nullspace);
            var deposit = new InteractUsingEvent(
                player,
                paper,
                board,
                new EntityCoordinates(board, Vector2.Zero));
            entMan.EventBus.RaiseLocalEvent(board, deposit);

            Assert.Multiple(() =>
            {
                Assert.That(corporate.CurrentStanding(userId), Is.EqualTo(standingBefore + 3));
                Assert.That(ledger.CurrentContractTotals(userId.UserId),
                    Is.EqualTo((totalsBefore.Completed + 1, totalsBefore.Score + 2)));
                Assert.That(ledger.CurrentContractLogCount(userId.UserId), Is.EqualTo(logCountBefore + 1));
                Assert.That(comp.Contracts.Any(c => c.Id == contract.Id), Is.False);
                Assert.That(comp.Contracts.Count(c => c.Claimant is null
                    && EasyTierProtoIds.Contains(c.Prototype)), Is.EqualTo(1),
                    "completion must refill/re-arm one open easy");
            });

            var duplicatePaper = entMan.SpawnEntity("Paper", MapCoordinates.Nullspace);
            var duplicate = new InteractUsingEvent(
                player,
                duplicatePaper,
                board,
                new EntityCoordinates(board, Vector2.Zero));
            entMan.EventBus.RaiseLocalEvent(board, duplicate);

            Assert.Multiple(() =>
            {
                Assert.That(corporate.CurrentStanding(userId), Is.EqualTo(standingBefore + 3),
                    "a stale duplicate interaction must not pay twice");
                Assert.That(ledger.CurrentContractTotals(userId.UserId),
                    Is.EqualTo((totalsBefore.Completed + 1, totalsBefore.Score + 2)));
                Assert.That(ledger.CurrentContractLogCount(userId.UserId), Is.EqualTo(logCountBefore + 1));
            });
        });
    }
}
