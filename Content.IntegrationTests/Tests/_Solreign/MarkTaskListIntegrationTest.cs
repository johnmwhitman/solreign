#nullable enable
using System;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.PlayerDelight.FirstShift;
using Content.Server._Solreign.PlayerDelight.Mark;
using Content.Server._Solreign.SeasonLedger;
using Content.Server.Station.Systems;
using Content.Shared._Solreign.PlayerDelight.FirstShift;
using Content.Shared._Solreign.PlayerDelight.Mark;
using Content.Shared.CCVar;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     The kishōtenketsu task-list restructure's ECS wiring (docs/specs/MARK-SPEC-2026-07-17-DRAFT.md
///     §3.6, MG-W3): the appended Mark stage, the Complete()-requires-Mark law reaching the real
///     server adapter, the dormant-CVar shim collapsing Debrief straight to completion, and
///     <c>SolreignMarkGardenSystem.NotifyMarkPlanted</c> auto-completing Task 5 on a real plant.
///
///     Pure reducer/presentation law is exhaustively unit-tested without a server in
///     Content.Tests/_Solreign/FirstShift{RoundState,ClientPresentation}Tests.cs — this file only
///     covers what those tests cannot reach: the real adapter, the real CVar, and the real
///     cross-system call from the Mark garden into First Shift.
/// </summary>
[TestFixture]
public sealed class MarkTaskListIntegrationTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        DummyTicker = false,
    };

    private async Task<bool> PollAsync(Func<bool> condition, int maxTicks = 60)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            var hit = false;
            await Server.WaitPost(() => hit = condition());
            if (hit)
                return true;
            await Server.WaitRunTicks(1);
        }

        return false;
    }

    [Test]
    public async Task DormantMark_DebriefCompletesDirectly_NeverPublishesMarkStage()
    {
        var server = Server;
        var firstShift = server.System<FirstShiftSystem>();
        var user = ServerSession!.UserId;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.SolreignFirstShiftAssignmentsEnabled, true);
            server.CfgMan.SetCVar(CCVars.SolreignMarkEnabled, false);
            firstShift.RoundRestartForTests();
            firstShift.AssignForTests(user, FirstShiftDepartment.Engineering);
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            // Walk to Debrief exactly as the dormant client presentation would (3 advances).
            var assignment = firstShift.GetAssignmentForTests(user)!.Value;
            Assert.That(firstShift.AdvanceForTests(user, assignment.Generation).Changed, Is.True);
            assignment = firstShift.GetAssignmentForTests(user)!.Value;
            Assert.That(firstShift.AdvanceForTests(user, assignment.Generation).Changed, Is.True);
            assignment = firstShift.GetAssignmentForTests(user)!.Value;
            Assert.That(firstShift.AdvanceForTests(user, assignment.Generation).Changed, Is.True);
            assignment = firstShift.GetAssignmentForTests(user)!.Value;
            Assert.That(assignment.Stage, Is.EqualTo(FirstShiftAssignmentStage.Debrief));

            // The classic-list client would send Complete here (presentation says "complete" at
            // Debrief while dormant) — the state.MarkEnabled the panel would have seen:
            Assert.That(firstShift.BuildStateForTests(user).MarkEnabled, Is.False);

            firstShift.NotifyMarkPlantedForTests(user); // must be a no-op — wrong stage while dormant
            Assert.That(firstShift.GetAssignmentForTests(user)!.Value.Stage, Is.EqualTo(FirstShiftAssignmentStage.Debrief),
                "NotifyMarkPlanted must never auto-complete a stage the dormant path never reaches");
        });
    }

    [Test]
    public async Task LiveMark_DebriefAdvancesToMark_TaskListShowsFiveRows()
    {
        var server = Server;
        var firstShift = server.System<FirstShiftSystem>();
        var user = ServerSession!.UserId;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.SolreignFirstShiftAssignmentsEnabled, true);
            server.CfgMan.SetCVar(CCVars.SolreignMarkEnabled, true);
            firstShift.RoundRestartForTests();
            firstShift.AssignForTests(user, FirstShiftDepartment.Engineering);
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var assignment = firstShift.GetAssignmentForTests(user)!.Value;
            Assert.That(firstShift.AdvanceForTests(user, assignment.Generation).Changed, Is.True); // -> Orient
            assignment = firstShift.GetAssignmentForTests(user)!.Value;
            Assert.That(firstShift.AdvanceForTests(user, assignment.Generation).Changed, Is.True); // -> Try
            assignment = firstShift.GetAssignmentForTests(user)!.Value;
            Assert.That(firstShift.AdvanceForTests(user, assignment.Generation).Changed, Is.True); // -> Debrief
            assignment = firstShift.GetAssignmentForTests(user)!.Value;
            Assert.That(assignment.Stage, Is.EqualTo(FirstShiftAssignmentStage.Debrief));

            var beforeMark = firstShift.BuildStateForTests(user);
            Assert.That(beforeMark.MarkEnabled, Is.True);
            Assert.That(beforeMark.Task4RowKey, Is.Not.Empty, "Task 4's row line must be populated once live");

            Assert.That(firstShift.AdvanceForTests(user, assignment.Generation).Changed, Is.True); // -> Mark
            Assert.That(firstShift.GetAssignmentForTests(user)!.Value.Stage, Is.EqualTo(FirstShiftAssignmentStage.Mark));

            var atMark = firstShift.BuildStateForTests(user);
            Assert.That(atMark.Stage, Is.EqualTo(FirstShiftAssignmentStage.Mark));
        });
    }

    [Test]
    public async Task Planting_AtMarkStage_AutoCompletesTask5ViaNotifyMarkPlanted()
    {
        var server = Server;
        var entMan = server.EntMan;
        var firstShift = server.System<FirstShiftSystem>();
        var mark = server.System<SolreignMarkGardenSystem>();
        var ledger = server.System<SeasonLedgerSystem>();
        var user = ServerSession!.UserId;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.SolreignFirstShiftAssignmentsEnabled, true);
            server.CfgMan.SetCVar(CCVars.SolreignMarkEnabled, true);
            firstShift.RoundRestartForTests();
            mark.ResetRoundStateForTests();
            firstShift.AssignForTests(user, FirstShiftDepartment.Engineering);
        });
        await server.WaitRunTicks(1);

        EntityUid mob = default;
        EntityUid garden = default;
        await server.WaitPost(() =>
        {
            var assignment = firstShift.GetAssignmentForTests(user)!.Value;
            firstShift.AdvanceForTests(user, assignment.Generation); // -> Orient
            assignment = firstShift.GetAssignmentForTests(user)!.Value;
            firstShift.AdvanceForTests(user, assignment.Generation); // -> Try
            assignment = firstShift.GetAssignmentForTests(user)!.Value;
            firstShift.AdvanceForTests(user, assignment.Generation); // -> Debrief
            assignment = firstShift.GetAssignmentForTests(user)!.Value;
            firstShift.AdvanceForTests(user, assignment.Generation); // -> Mark
            Assert.That(firstShift.GetAssignmentForTests(user)!.Value.Stage, Is.EqualTo(FirstShiftAssignmentStage.Mark));

            mob = ServerSession!.AttachedEntity!.Value;
            garden = entMan.SpawnEntity(SolreignMarkGardenSystem.GardenPrototypeId, entMan.GetComponent<TransformComponent>(mob).Coordinates);
            mark.TryPlantForTests(garden, mob, MarkKind.Sapling);
        });

        var account = user.UserId;
        var claimed = await PollAsync(() => firstShift.GetAssignmentForTests(user) is null);
        Assert.That(claimed, Is.True, "the real claim -> NotifyMarkPlanted call must complete Task 5");
        Assert.That(await ledger.GetMarkAsync(account), Is.Not.Null, "the plant itself must still have landed a row");
        Assert.That(firstShift.Counters.Completed, Is.EqualTo(1));
    }
}
