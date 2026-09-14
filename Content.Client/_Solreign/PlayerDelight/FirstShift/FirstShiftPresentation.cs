using System.Collections.Generic;
using Content.Shared._Solreign.PlayerDelight.FirstShift;

namespace Content.Client._Solreign.PlayerDelight.FirstShift;

public readonly record struct FirstShiftPresentation(
    string PrimaryLocKey,
    bool ShowSkip,
    bool ShowFlavor,
    bool CanOpenGuide,
    bool CanOpenMap,
    string AnchorStatusLocKey,
    bool RerollReachable)
{
    /// <summary>
    ///     <paramref name="markEnabled"/> is the MG-W3 addition (spec §3.6): Debrief's primary
    ///     action only advances into the appended Mark stage when the feature is live; while
    ///     dormant, Debrief presents exactly as it did before the wave (terminal, "complete") — the
    ///     server-side <c>FirstShiftSystem.OnComplete</c> adapter shim makes that presentation
    ///     truthful by walking the pure reducer through the unseen Mark stage in one call, so Mark
    ///     itself is never published to the client while off.
    /// </summary>
    public static FirstShiftPresentation For(FirstShiftAssignmentStage stage, bool markEnabled, bool usedFallback,
        bool anchorUnavailable, bool hasGuide, bool showFlavor, bool hasFlavor)
    {
        var (primary, skip) = stage switch
        {
            FirstShiftAssignmentStage.Assigned => ("first-shift-stage-next-orient", true),
            FirstShiftAssignmentStage.Orient => ("first-shift-stage-next-try", true),
            FirstShiftAssignmentStage.Try => ("first-shift-stage-next-debrief", true),
            FirstShiftAssignmentStage.Debrief => markEnabled
                ? ("first-shift-stage-next-mark", true)
                : ("first-shift-stage-complete", false),
            FirstShiftAssignmentStage.Mark => ("first-shift-stage-complete", false),
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
        };

        // The Task 5 marker repurposes the SAME MarkerStatus/MapButton controls the card anchors
        // already use (spec §3.6 edit #4) — only the label text and status key change; no new
        // widgets. Mark stage is only ever reached when the feature is live, so this branch never
        // needs its own markEnabled check.
        var anchorStatus = stage == FirstShiftAssignmentStage.Mark
            ? (anchorUnavailable ? "first-shift-marker-unavailable" : "first-shift-marker-continuity-garden")
            : anchorUnavailable
                ? "first-shift-marker-unavailable"
                : usedFallback
                    ? "first-shift-marker-arrivals-fallback"
                    : "first-shift-marker-department";

        return new(primary, skip, showFlavor && hasFlavor, hasGuide,
            !anchorUnavailable, anchorStatus, true);
    }

    public static string SuggestionLocKey(FirstShiftDepartment department) => department switch
    {
        FirstShiftDepartment.Engineering => "first-shift-department-engineering",
        FirstShiftDepartment.Medical => "first-shift-department-medical",
        FirstShiftDepartment.Science => "first-shift-department-science",
        FirstShiftDepartment.Cargo => "first-shift-department-cargo",
        FirstShiftDepartment.Service => "first-shift-department-service",
        _ => "first-shift-department-universal",
    };

    public static bool MustDiscardPrivateMarker(FirstShiftUiState state) => !state.Enabled || !state.Active;
}

/// <summary>
///     The numbered kishōtenketsu task-list header (spec §3.6): 3 rows dormant (the classic list,
///     Task 4 absent), 5 once the Mark ships live. Presentation-only — picks WHICH row loc keys to
///     show, never their content; the panel just joins and localizes them.
/// </summary>
public static class FirstShiftTaskListPresentation
{
    private static readonly string[] ThreeTaskRows =
    {
        "first-shift-task-1-orient",
        "first-shift-task-2-try",
        "first-shift-task-3-debrief",
    };

    private static readonly string[] FiveTaskRows =
    {
        "first-shift-task-1-orient",
        "first-shift-task-2-try",
        "first-shift-task-3-debrief",
        "first-shift-task-4-list",
        "first-shift-task-5-mark",
    };

    public static IReadOnlyList<string> RowKeys(bool markEnabled) => markEnabled ? FiveTaskRows : ThreeTaskRows;
}

public abstract record FirstShiftIntent
{
    public sealed record Start(FirstShiftDepartment Department, ulong SnapshotGeneration) : FirstShiftIntent;
    public sealed record Advance(ulong SnapshotGeneration, uint Generation) : FirstShiftIntent;
    public sealed record Reroll(ulong SnapshotGeneration, uint Generation) : FirstShiftIntent;
    public sealed record Complete(ulong SnapshotGeneration, uint Generation) : FirstShiftIntent;
    public sealed record End(ulong SnapshotGeneration, uint Generation) : FirstShiftIntent;
}

public sealed class FirstShiftIntentFactory(Func<ulong> snapshotGeneration)
{
    public FirstShiftIntent.Start Start(FirstShiftDepartment department) => new(department, snapshotGeneration());
    public FirstShiftIntent.Advance Advance(uint generation) => new(snapshotGeneration(), generation);
    public FirstShiftIntent.Reroll Reroll(uint generation) => new(snapshotGeneration(), generation);
    public FirstShiftIntent.Complete Complete(uint generation) => new(snapshotGeneration(), generation);
    public FirstShiftIntent.End End(uint generation) => new(snapshotGeneration(), generation);
}
