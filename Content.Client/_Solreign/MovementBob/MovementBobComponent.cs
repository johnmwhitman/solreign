using System.Numerics;

namespace Content.Client._Solreign.MovementBob;

/// <summary>
/// SR-W-083: client-only bookkeeping for the procedural movement bob.
/// Registered only in Content.Client — never networked, never saved.
/// Added to humanoids by <see cref="MovementBobSystem"/> when they start moving
/// while the feature is enabled; removed when the feature turns off.
/// </summary>
[RegisterComponent]
public sealed partial class MovementBobComponent : Component
{
    /// <summary>Mirror of the latest SpriteMoveEvent for this entity.</summary>
    public bool IsMoving;

    /// <summary>Whether a bob offset is currently applied to the sprite.</summary>
    public bool Applied;

    /// <summary>
    /// Whether <see cref="BaseOffset"/> holds a valid pre-bob baseline. Deliberately
    /// separate from <see cref="Applied"/>: while another animation occupies the offset
    /// channel the bob suspends (Applied=false) but the baseline stays retained, so
    /// resuming never adopts an animation-poisoned offset as the new baseline (cdx r3).
    /// </summary>
    public bool HasBaseline;

    /// <summary>Sprite offset captured before the first bob frame, restored on stop.</summary>
    public Vector2 BaseOffset;

    /// <summary>Stable per-entity phase so crowds don't bounce in lockstep.</summary>
    public float Phase;

    /// <summary>
    /// The entity no longer qualifies (e.g. its humanoid profile shut down) but an offset
    /// animation currently owns the channel — retire on the first unoccupied frame instead
    /// of restoring mid-animation and letting the animation's end re-poison the offset.
    /// </summary>
    public bool Retiring;
}
