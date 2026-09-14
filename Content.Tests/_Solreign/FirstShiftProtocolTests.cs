using System;
using System.Linq;
using System.Reflection;
using Content.Shared._Solreign.PlayerDelight.FirstShift;
using Content.Shared._Solreign.PlayerDelight.Wingmates;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Tests._Solreign;

[TestFixture]
public sealed class FirstShiftProtocolTests
{
    [Test]
    public void IntentsCarryOnlyDepartmentOrCurrentTransportAndAssignmentGenerations()
    {
        Assert.That(Declared(typeof(FirstShiftStartMessage)), Is.EquivalentTo(new[] { "Department", "SnapshotGeneration" }));
        foreach (var type in new[]
                 {
                     typeof(FirstShiftAdvanceMessage), typeof(FirstShiftRerollMessage),
                     typeof(FirstShiftCompleteMessage), typeof(FirstShiftEndMessage),
                 })
            Assert.That(Declared(type),
                Is.EquivalentTo(new[] { "SnapshotGeneration", "AssignmentGeneration" }), type.Name);
    }

    [Test]
    public void ProtocolIsSerializableAndPublicWingmateShellIsUnchanged()
    {
        var types = new[]
        {
            typeof(FirstShiftStartMessage), typeof(FirstShiftAdvanceMessage),
            typeof(FirstShiftRerollMessage), typeof(FirstShiftCompleteMessage),
            typeof(FirstShiftEndMessage), typeof(FirstShiftPrivateSnapshotEvent),
            typeof(FirstShiftUiState),
        };
        foreach (var type in types)
        {
            Assert.That(type.IsDefined(typeof(SerializableAttribute)), Is.True, type.Name);
            Assert.That(type.IsDefined(typeof(NetSerializableAttribute)), Is.True, type.Name);
        }

        Assert.That(typeof(WingmatePublicShellState).GetProperties().Select(p => p.Name),
            Is.EquivalentTo(new[] { nameof(WingmatePublicShellState.Enabled) }));
    }

    [Test]
    public void PrivateStateHasReviewedServerDerivedShape()
    {
        Assert.That(Declared(typeof(FirstShiftUiState)), Is.EquivalentTo(new[]
        {
            "Enabled", "Active", "SuggestedDepartment", "SelectedDepartment", "CardId",
            "Title", "Why", "Orient", "Try", "IfStuck", "SafetyStop", "GuideEntry",
            "Flavor", "Stage", "Anchor", "AnchorLabel", "UsedFallback", "AnchorUnavailable", "Generation",
            "CanReroll", "RerollRemaining", "MarkEnabled", "Task4RowKey", "Task4DeflectionKey",
        }));
    }

    [Test]
    public void EnabledIdleStateAllowsPrivateOptInWithoutPretendingAssignmentExists()
    {
        var state = FirstShiftUiState.Idle(FirstShiftDepartment.Engineering);
        Assert.Multiple(() =>
        {
            Assert.That(state.Enabled, Is.True);
            Assert.That(state.Active, Is.False);
            Assert.That(state.SuggestedDepartment, Is.EqualTo(FirstShiftDepartment.Engineering));
            Assert.That(state.CardId, Is.Empty);
        });
    }

    private static string[] Declared(Type type) => type
        .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
        .Select(p => p.Name).ToArray();
}
