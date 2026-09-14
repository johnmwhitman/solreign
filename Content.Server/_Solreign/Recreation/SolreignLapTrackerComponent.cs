using Robust.Shared.Audio;

namespace Content.Server._Solreign.Recreation;

/// <summary>
/// Placed on a rideable recreation vehicle (e.g. <c>SolreignGoKart</c>,
/// Resources/Prototypes/_Solreign/Entities/recreation.yml) to track its progress around a course —
/// gap #2 in docs/specs/2026-07-11-recreation-spec.md. Lives on the kart rather than the driver
/// (the spec left this an open design choice: "driver survives kart destruction, kart is simpler");
/// the kart is what physically collides with checkpoint fixtures, so this keeps
/// <see cref="SolreignLapTrackerSystem"/>'s lookup a single same-entity TryComp instead of a
/// buckle-relationship traversal.
/// </summary>
[RegisterComponent]
[Access(typeof(SolreignLapTrackerSystem))]
public sealed partial class SolreignLapTrackerComponent : Component
{
    /// <summary>
    /// Which course this kart is currently racing. Must match a checkpoint's
    /// <see cref="SolreignLapCheckpointComponent.CourseId"/> for that checkpoint to count.
    /// </summary>
    [DataField]
    public string CourseId = "default";

    /// <summary>
    /// Number of checkpoints per lap (ordinals <c>0..CheckpointCount-1</c>). Needed to know when
    /// ordinal progress wraps back around to the start/finish line. Set to match the actual course
    /// this kart is placed on; the default is a spawnable placeholder, not a real track length.
    /// </summary>
    [DataField]
    public int CheckpointCount = 4;

    /// <summary>
    /// Laps required to finish the race.
    /// </summary>
    [DataField]
    public int TotalLaps = 3;

    /// <summary>
    /// Fanfare played (PVS, at the kart) on every completed lap — an arcade "win" jingle, since the
    /// per-lap popup used to fire with no sound at all (Phase2 A4 game-feel sweep). The final lap
    /// still gets its own global-announcement stinger on top of this, via
    /// <see cref="Content.Server.Chat.Systems.ChatSystem.DispatchGlobalAnnouncement"/>'s own sound.
    /// </summary>
    [DataField]
    public SoundSpecifier LapCompleteSound = new SoundPathSpecifier("/Audio/Effects/Arcade/win.ogg");

    /// <summary>
    /// The next checkpoint ordinal this kart needs to hit to make progress.
    /// </summary>
    [ViewVariables]
    public int NextCheckpointOrdinal;

    /// <summary>
    /// Full laps completed so far.
    /// </summary>
    [ViewVariables]
    public int LapsCompleted;

    /// <summary>
    /// True once <see cref="LapsCompleted"/> has reached <see cref="TotalLaps"/> and the finish
    /// transition has completed — stops re-announcing on every further checkpoint hit. The
    /// repeatable-heats kill switch may silently normalize an already-complete timed circuit to
    /// this state while discarding its unfinished timing sample.
    /// </summary>
    [ViewVariables]
    public bool Finished;

    /// <summary>
    /// Server game time when the current heat crossed its first valid start checkpoint.
    /// Null while the repeatable-heats CVar is disabled or before a heat starts.
    /// </summary>
    [ViewVariables]
    public TimeSpan? HeatStartedAt;

    /// <summary>
    /// Driver who crossed the valid start line for the active timed heat. A departure or swap
    /// invalidates the sample and resets course progress.
    /// </summary>
    [ViewVariables]
    public EntityUid? HeatDriver;

    /// <summary>
    /// Frozen authoritative duration for the completed heat, measured from the first valid
    /// ordinal-0 crossing through the final ordinal-0 finish-line crossing.
    /// </summary>
    [ViewVariables]
    public TimeSpan? FinishedElapsed;

    /// <summary>
    /// Driver credited when the current completed heat finished. A different driver may reset a
    /// finished heat when the repeatable-heats CVar is enabled.
    /// </summary>
    [ViewVariables]
    public EntityUid? FinishingDriver;
}
