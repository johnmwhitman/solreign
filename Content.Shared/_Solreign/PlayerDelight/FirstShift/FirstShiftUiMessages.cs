using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Solreign.PlayerDelight.FirstShift;

[Serializable, NetSerializable]
public sealed class FirstShiftStartMessage(FirstShiftDepartment department, ulong snapshotGeneration) : BoundUserInterfaceMessage
{
    public FirstShiftDepartment Department { get; } = department;
    public ulong SnapshotGeneration { get; } = snapshotGeneration;
}

[Serializable, NetSerializable]
public sealed class FirstShiftAdvanceMessage(ulong snapshotGeneration, uint assignmentGeneration) : BoundUserInterfaceMessage
{
    public ulong SnapshotGeneration { get; } = snapshotGeneration;
    public uint AssignmentGeneration { get; } = assignmentGeneration;
}

[Serializable, NetSerializable]
public sealed class FirstShiftRerollMessage(ulong snapshotGeneration, uint assignmentGeneration) : BoundUserInterfaceMessage
{
    public ulong SnapshotGeneration { get; } = snapshotGeneration;
    public uint AssignmentGeneration { get; } = assignmentGeneration;
}

[Serializable, NetSerializable]
public sealed class FirstShiftCompleteMessage(ulong snapshotGeneration, uint assignmentGeneration) : BoundUserInterfaceMessage
{
    public ulong SnapshotGeneration { get; } = snapshotGeneration;
    public uint AssignmentGeneration { get; } = assignmentGeneration;
}

[Serializable, NetSerializable]
public sealed class FirstShiftEndMessage(ulong snapshotGeneration, uint assignmentGeneration) : BoundUserInterfaceMessage
{
    public ulong SnapshotGeneration { get; } = snapshotGeneration;
    public uint AssignmentGeneration { get; } = assignmentGeneration;
}

[Serializable, NetSerializable]
public sealed class FirstShiftUiState : BoundUserInterfaceState
{
    public bool Enabled { get; }
    public bool Active { get; }
    public FirstShiftDepartment SuggestedDepartment { get; }
    public FirstShiftDepartment SelectedDepartment { get; }
    public string CardId { get; }
    public string Title { get; }
    public string Why { get; }
    public string Orient { get; }
    public string Try { get; }
    public string IfStuck { get; }
    public string SafetyStop { get; }
    public string GuideEntry { get; }
    public string Flavor { get; }
    public FirstShiftAssignmentStage Stage { get; }
    public NetCoordinates? Anchor { get; }
    public string AnchorLabel { get; }
    public bool UsedFallback { get; }
    public bool AnchorUnavailable { get; }
    public uint Generation { get; }
    public bool CanReroll { get; }
    public TimeSpan RerollRemaining { get; }

    /// <summary>
    ///     MG-W3 addition (spec §3.6): mirrors <c>CCVars.SolreignMarkEnabled</c> so the client's
    ///     presentation can render the classic 3-task list (dormant) vs. the full kishōtenketsu
    ///     5-task list (live) without needing its own CVar access. Always false on
    ///     <see cref="Disabled"/>/<see cref="Idle"/> — those panels never show the task list.
    /// </summary>
    public bool MarkEnabled { get; }

    /// <summary>
    ///     MG-W3 addition: the server-picked, round-seed-deterministic Task 4 row line (spec §8B's
    ///     three "reassigned" variants — the same player, the same round, always sees the same
    ///     pick). Empty when <see cref="MarkEnabled"/> is false.
    /// </summary>
    public string Task4RowKey { get; }

    /// <summary>MG-W3 addition: the matching Task 4 deflection line, same picking law.</summary>
    public string Task4DeflectionKey { get; }

    public FirstShiftUiState(bool enabled, bool active, FirstShiftDepartment suggestedDepartment,
        FirstShiftDepartment selectedDepartment, string cardId, string title, string why, string orient,
        string @try, string ifStuck, string safetyStop, string guideEntry, string flavor,
        FirstShiftAssignmentStage stage, NetCoordinates? anchor, string anchorLabel, bool usedFallback, bool anchorUnavailable,
        uint generation, bool canReroll, TimeSpan rerollRemaining,
        bool markEnabled, string task4RowKey, string task4DeflectionKey)
    {
        Enabled = enabled; Active = active; SuggestedDepartment = suggestedDepartment;
        SelectedDepartment = selectedDepartment; CardId = cardId; Title = title; Why = why;
        Orient = orient; Try = @try; IfStuck = ifStuck; SafetyStop = safetyStop;
        GuideEntry = guideEntry; Flavor = flavor; Stage = stage; Anchor = anchor; AnchorLabel = anchorLabel;
        UsedFallback = usedFallback; AnchorUnavailable = anchorUnavailable; Generation = generation;
        CanReroll = canReroll; RerollRemaining = rerollRemaining;
        MarkEnabled = markEnabled; Task4RowKey = task4RowKey; Task4DeflectionKey = task4DeflectionKey;
    }

    public static FirstShiftUiState Disabled(FirstShiftDepartment suggested) =>
        new(false, false, suggested, FirstShiftDepartment.Universal, "", "", "", "", "", "", "", "", "",
            FirstShiftAssignmentStage.Assigned, null, "", false, true, 0, false, TimeSpan.Zero,
            false, "", "");

    public static FirstShiftUiState Idle(FirstShiftDepartment suggested) =>
        new(true, false, suggested, FirstShiftDepartment.Universal, "", "", "", "", "", "", "", "", "",
            FirstShiftAssignmentStage.Assigned, null, "", false, true, 0, false, TimeSpan.Zero,
            false, "", "");
}

[Serializable, NetSerializable]
public sealed class FirstShiftPrivateSnapshotEvent(NetEntity beacon, ulong generation, FirstShiftUiState state) : EntityEventArgs
{
    public NetEntity Beacon { get; } = beacon;
    public ulong Generation { get; } = generation;
    public FirstShiftUiState State { get; } = state;
}
