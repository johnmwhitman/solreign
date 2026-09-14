using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared.Xenoarchaeology.Artifact.Components;

/// <summary>
/// This is used for tracking the nodes which have been triggered during a particular unlocking state.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class XenoArtifactUnlockingComponent : Component
{
    /// <summary>
    /// Indexes corresponding to all of the nodes that have been triggered
    /// </summary>
    [DataField, AutoNetworkedField]
    public HashSet<int> TriggeredNodeIndexes = new();

    /// <summary>
    /// The time at which the unlocking state ends.
    /// </summary>
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan EndTime;

    /// <summary>
    /// Tracks if artifexium has been applied, which changes the unlock behavior slightly.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool ArtifexiumApplied;

    /// <summary>
    /// The sound that plays when an artifact finishes unlocking successfully (with node unlocked).
    /// </summary>
    [DataField]
    // SOLREIGN 2026-07-22 (player report): this is the "artifact stopped pulsing" sound. At
    // +3dB it was the loudest thing in xenoarch and grating in a room where artifacts unlock
    // repeatedly -- note the sibling failure sound below carries no boost at all. -2dB puts it
    // just under unity so it still reads as a resolution cue without dominating the room.
    public SoundSpecifier UnlockActivationSuccessfulSound = new SoundCollectionSpecifier("ArtifactUnlockingActivationSuccess")
    {
        Params = new()
        {
            Variation = 0.1f,
            Volume = -6f
        }
    };

    /// <summary>
    /// The sound that plays when artifact finishes unlocking non-successfully.
    /// </summary>
    [DataField]
    public SoundSpecifier? UnlockActivationFailedSound = new SoundCollectionSpecifier("ArtifactUnlockActivationFailure")
    {
        Params = new()
        {
            Variation = 0.1f
        }
    };
}
