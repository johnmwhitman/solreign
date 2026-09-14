using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._Solreign.PlayerDelight.FirstShift;
using Content.Shared._Solreign.PlayerDelight.FirstShift;
using Content.Shared.CCVar;
using Content.Shared.Administration;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;

namespace Content.Tests._Solreign;

[TestFixture]
public sealed class FirstShiftServerRulesTests
{
    private static readonly NetUserId Alice = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly NetUserId Bob = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));

    [Test]
    public void SnapshotAdapterTargetsOnlyMappedLiveSessionAndBeacon()
    {
        var beacon = new EntityUid(42);
        var live = true;
        var sessions = new Dictionary<NetUserId, string> { [Alice] = "alice", [Bob] = "bob" };
        var sends = new List<(FirstShiftPrivateSnapshotEvent Snapshot, string Session)>();
        var adapter = new FirstShiftSnapshotAdapter<string>(
            (NetUserId id, out string session) => sessions.TryGetValue(id, out session!),
            uid => live && uid == beacon,
            uid => new NetEntity(uid.Id),
            id => FirstShiftUiState.Disabled(FirstShiftDepartment.Universal),
            (snapshot, session) => sends.Add((snapshot, session)));

        adapter.RememberOpen(Alice, beacon);
        adapter.Publish(new[] { Alice, Bob });
        Assert.That(sends, Has.Count.EqualTo(1));
        Assert.That(sends[0].Session, Is.EqualTo("alice"));

        live = false;
        adapter.Publish(new[] { Alice });
        Assert.That(sends, Has.Count.EqualTo(1));
        Assert.That(adapter.HasOpenMapping(Alice), Is.False);
    }

    [Test]
    public void ClosedOrDeletedBeaconSuppressesFutureSnapshots()
    {
        var beacon = new EntityUid(7);
        var sends = 0;
        var adapter = new FirstShiftSnapshotAdapter<string>(
            (NetUserId _, out string session) => { session = "session"; return true; },
            _ => true, uid => new NetEntity(uid.Id),
            _ => FirstShiftUiState.Disabled(FirstShiftDepartment.Universal), (_, _) => sends++);
        adapter.RememberOpen(Alice, beacon);
        adapter.Close(Alice, beacon);
        adapter.Publish(new[] { Alice });
        adapter.RememberOpen(Alice, beacon);
        adapter.RemoveBeacon(beacon);
        adapter.Publish(new[] { Alice });
        Assert.That(sends, Is.Zero);
    }

    [Test]
    public void AnchorResolverUsesDeclaredPriorityThenArrivalsAndNeverCrossesStation()
    {
        var candidates = new[]
        {
            new FirstShiftAnchorCandidate(9, "Engineering", 1, true, true),
            new FirstShiftAnchorCandidate(3, "Engineering", 1, true, true),
            new FirstShiftAnchorCandidate(1, "Engineering", 2, true, true),
            new FirstShiftAnchorCandidate(2, "Arrivals", 1, true, true),
            new FirstShiftAnchorCandidate(0, "Engineering", 1, true, false),
        };
        Assert.That(FirstShiftAnchorResolver.Select(1, new[] { "Engineering", "Arrivals" }, candidates)?.EntityId,
            Is.EqualTo(3));
        Assert.That(FirstShiftAnchorResolver.Select(1, new[] { "Missing", "Arrivals" }, candidates)?.EntityId,
            Is.EqualTo(2));
        Assert.That(FirstShiftAnchorResolver.Select(4, new[] { "Engineering", "Arrivals" }, candidates), Is.Null);
    }

    [Test]
    public void SnapshotGenerationSurvivesDisableEnableForSameOpenViewer()
    {
        var beacon = new EntityUid(12);
        var enabled = true;
        var sends = new List<FirstShiftPrivateSnapshotEvent>();
        var adapter = new FirstShiftSnapshotAdapter<string>(
            (NetUserId _, out string session) => { session = "session"; return true; },
            _ => true, uid => new NetEntity(uid.Id),
            _ => enabled ? FirstShiftUiState.Idle(FirstShiftDepartment.Service) : FirstShiftUiState.Disabled(FirstShiftDepartment.Service),
            (snapshot, _) => sends.Add(snapshot));
        adapter.RememberOpen(Alice, beacon);

        adapter.Publish([Alice]);
        enabled = false;
        adapter.Publish([Alice]);
        enabled = true;
        adapter.Publish([Alice]);

        Assert.Multiple(() =>
        {
            Assert.That(sends.Select(s => s.Generation), Is.EqualTo(new ulong[] { 1, 2, 3 }));
            Assert.That(sends.Select(s => s.State.Enabled), Is.EqualTo(new[] { true, false, true }));
            Assert.That(adapter.HasOpenMapping(Alice), Is.True);
            Assert.That(adapter.IsCurrent(Alice, beacon, 3), Is.True);
            Assert.That(adapter.IsCurrent(Alice, beacon, 2), Is.False);
        });
    }

    [TestCase("Engineering", FirstShiftDepartment.Engineering)]
    [TestCase("Medical", FirstShiftDepartment.Medical)]
    [TestCase("Science", FirstShiftDepartment.Science)]
    [TestCase("Cargo", FirstShiftDepartment.Cargo)]
    [TestCase("Service", FirstShiftDepartment.Service)]
    [TestCase("Civilian", FirstShiftDepartment.Service)]
    [TestCase("Security", FirstShiftDepartment.Universal)]
    [TestCase("Command", FirstShiftDepartment.Universal)]
    public void DepartmentMappingIsExplicitAndCivilianMapsToService(string departmentId, FirstShiftDepartment expected)
    {
        Assert.That(FirstShiftSystem.MapDepartmentForTests(departmentId), Is.EqualTo(expected));
    }

    /// <summary>
    ///     OBSOLETE (v15.0.1 hotfix): <c>FirstShiftSystem.OnStart</c> no longer routes through
    ///     <c>IsStartAuthorized</c> — the snapshot-generation check was removed from Start because
    ///     a stale transport generation must not silently eat a first-assignment press (UI gates
    ///     on <c>state.Active == false</c> and the handler's own <c>!active</c> branch covers
    ///     the duplicate case). The new contract is pinned end-to-end by
    ///     <c>FirstShiftSystemIntegrationTest.AuthenticatedLifecycle_StartAcceptsStaleTransportButAdvanceEtcRejectStaleGenerations</c>.
    ///     <c>IsStartAuthorized</c> / <c>IsStartAuthorizedForTests</c> remain as test-only
    ///     accessors; no production caller wires them today.
    /// </summary>
    [Test]
    [Ignore("v15.0.1 hotfix: OnStart no longer routes through IsStartAuthorized; see XML doc above.")]
    public void StartRequiresCurrentPrivateSnapshotAndNoActiveAssignment()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FirstShiftSystem.IsStartAuthorizedForTests(true, false, true, FirstShiftDepartment.Service), Is.True);
            Assert.That(FirstShiftSystem.IsStartAuthorizedForTests(true, false, false, FirstShiftDepartment.Service), Is.False);
            Assert.That(FirstShiftSystem.IsStartAuthorizedForTests(true, true, true, FirstShiftDepartment.Service), Is.False);
            Assert.That(FirstShiftSystem.IsStartAuthorizedForTests(false, false, true, FirstShiftDepartment.Service), Is.False);
            Assert.That(FirstShiftSystem.IsStartAuthorizedForTests(true, false, true, FirstShiftDepartment.Universal), Is.False,
                "Universal is a server fallback/suggestion, never a client-selectable deck.");
            Assert.That(FirstShiftSystem.IsStartAuthorizedForTests(true, false, true, (FirstShiftDepartment) 255), Is.False);
        });
    }

    /// <summary>
    ///     The first-shift CVar is its own SERVERONLY switch, separate from wingmates.
    /// </summary>
    /// <remarks>
    ///     Was <c>CanaryCVarIsSeparateServerOnlyAndDefaultOff</c> and asserted a default of
    ///     FALSE. That default was a canary-rollout stance, and the canary is over: the
    ///     first-shift tutorial was deliberately enabled for the v14.2 release, which is the
    ///     onboarding players had asked for. The assertion outlived the decision and left the
    ///     suite red, which is how a suite stops being read.
    ///
    ///     What is still worth pinning is pinned: the exact CVar name (it is referenced from
    ///     box config), SERVERONLY (a client must not be able to flip its own tutorial), and
    ///     that it is a DIFFERENT switch from wingmates so either can be turned off alone.
    ///     The default now asserts TRUE deliberately — if someone turns onboarding off again,
    ///     that should be a visible decision, not a silent drift.
    /// </remarks>
    [Test]
    public void FirstShiftCVarIsSeparateServerOnlyAndDefaultOn()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CCVars.SolreignFirstShiftAssignmentsEnabled.Name,
                Is.EqualTo("solreign.first_shift_assignments_enabled"));
            Assert.That(CCVars.SolreignFirstShiftAssignmentsEnabled.DefaultValue, Is.True,
                "The first-shift tutorial ships ON as of v14.2. If this is being turned off, "
                + "change it here too so the decision is recorded rather than inferred.");
            Assert.That(CCVars.SolreignFirstShiftAssignmentsEnabled.Flags.HasFlag(Robust.Shared.Configuration.CVar.SERVERONLY), Is.True);
            Assert.That(CCVars.SolreignFirstShiftAssignmentsEnabled.Name,
                Is.Not.EqualTo(CCVars.SolreignWingmatesEnabled.Name));
        });
    }

    [Test]
    public void ModeratorCommandsExposeOnlyCoarseAggregateContract()
    {
        foreach (var type in new[] { typeof(FirstShiftStatusCommand), typeof(FirstShiftClearCommand) })
        {
            var attribute = type.GetCustomAttributesData()
                .Single(a => a.AttributeType.Name == "AdminCommandAttribute");
            Assert.That(Convert.ToInt64(attribute.ConstructorArguments.Single().Value),
                Is.EqualTo(Convert.ToInt64(AdminFlags.Moderator)));
        }

        Assert.That(new FirstShiftStatusCommand().Command, Is.EqualTo("firstshiftstatus"));
        Assert.That(new FirstShiftClearCommand().Command, Is.EqualTo("firstshiftclear"));
        Assert.That(typeof(FirstShiftCounters).GetProperties().Select(p => p.Name),
            Is.EquivalentTo(new[] { "Active", "Completed", "Rerolled" }));
        Assert.That(typeof(FirstShiftCounters).GetProperties().Select(p => p.Name),
            Has.None.Contains("Card").And.None.Contains("Job").And.None.Contains("Stage"));
    }
}
