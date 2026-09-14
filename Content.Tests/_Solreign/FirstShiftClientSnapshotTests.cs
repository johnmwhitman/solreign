using System;
using System.Collections.Generic;
using Content.Client._Solreign.PlayerDelight.FirstShift;
using Content.Shared._Solreign.PlayerDelight.FirstShift;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.Tests._Solreign;

[TestFixture]
public sealed class FirstShiftClientSnapshotTests
{
    [Test]
    public void SnapshotIndexIsBeaconScopedAndStrictlyMonotonic()
    {
        var index = new FirstShiftSnapshotIndex();
        var first = new NetEntity(10);
        var second = new NetEntity(20);
        var idle = FirstShiftUiState.Idle(FirstShiftDepartment.Engineering);

        Assert.Multiple(() =>
        {
            Assert.That(index.TryUpdate(first, 7, idle), Is.True);
            Assert.That(index.TryUpdate(first, 7, idle), Is.False);
            Assert.That(index.TryUpdate(first, 6, idle), Is.False);
            Assert.That(index.TryUpdate(second, 1, idle), Is.True);
            Assert.That(index.TryGet(first, out var entry), Is.True);
            Assert.That(entry.Generation, Is.EqualTo(7));
            Assert.That(index.TryGet(second, out var other), Is.True);
            Assert.That(other.Generation, Is.EqualTo(1));
        });
    }

    [Test]
    public void DispatchUsesTransportGenerationForStartAndAssignmentGenerationForActiveIntents()
    {
        var dispatch = new FirstShiftIntentFactory(() => 91);

        Assert.Multiple(() =>
        {
            Assert.That(dispatch.Start(FirstShiftDepartment.Cargo),
                Is.EqualTo(new FirstShiftIntent.Start(FirstShiftDepartment.Cargo, 91)));
            Assert.That(dispatch.Advance(13), Is.EqualTo(new FirstShiftIntent.Advance(91, 13)));
            Assert.That(dispatch.Reroll(13), Is.EqualTo(new FirstShiftIntent.Reroll(91, 13)));
            Assert.That(dispatch.Complete(13), Is.EqualTo(new FirstShiftIntent.Complete(91, 13)));
            Assert.That(dispatch.End(13), Is.EqualTo(new FirstShiftIntent.End(91, 13)));
        });
    }

    [Test]
    public void CooldownRerollStillUsesCurrentAssignmentGeneration()
    {
        var dispatch = new FirstShiftIntentFactory(() => 500);

        Assert.That(dispatch.Reroll(27), Is.EqualTo(new FirstShiftIntent.Reroll(500, 27)));
    }

    [Test]
    public void ClosingBuiRemovesBeaconSnapshotSoReopenCannotApplyStaleCard()
    {
        var index = new FirstShiftSnapshotIndex();
        var beacon = new NetEntity(10);
        var active = State(active: true, enabled: true);

        Assert.That(index.TryUpdate(beacon, 8, active), Is.True);
        Assert.That(index.Remove(beacon), Is.True);
        Assert.That(index.TryGet(beacon, out _), Is.False);

        // A new BUI lifetime starts empty and can accept the next authoritative snapshot.
        Assert.That(index.TryUpdate(beacon, 9, FirstShiftUiState.Idle(FirstShiftDepartment.Engineering)), Is.True);
        Assert.That(index.TryGet(beacon, out var reopened), Is.True);
        Assert.That(reopened.State.Active, Is.False);
    }

    [TestCase(true, false)] // End / Complete returns an idle snapshot.
    [TestCase(false, false)] // CVar-off returns a disabled snapshot.
    public void NonActiveSnapshotRequiresPrivateMarkerDiscard(bool enabled, bool active)
    {
        Assert.That(FirstShiftPresentation.MustDiscardPrivateMarker(State(active, enabled)), Is.True);
    }

    [Test]
    public void BeaconShutdownClearsOpenPrivateViewBeforeInvalidatingCache()
    {
        var calls = new List<string>();
        var beacon = new NetEntity(44);

        FirstShiftClientLifecycle.BeaconShutdown(
            beacon,
            cleared => calls.Add($"clear:{cleared.Id}"),
            invalidated => calls.Add($"invalidate:{invalidated.Id}"));

        Assert.That(calls, Is.EqualTo(new[] { "clear:44", "invalidate:44" }));
    }

    private static FirstShiftUiState State(bool active, bool enabled) => new(
        enabled, active, FirstShiftDepartment.Engineering, FirstShiftDepartment.Engineering,
        active ? "Card" : "", "", "", "", "", "", "", "", "",
        FirstShiftAssignmentStage.Assigned, null, "", false, true, 1, true, TimeSpan.Zero,
        false, "", "");
}
