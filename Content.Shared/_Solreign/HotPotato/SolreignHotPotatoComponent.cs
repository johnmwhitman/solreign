using Content.Shared.Explosion;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Solreign.HotPotato;

/// <summary>
///     The "Mandatory Team-Building Exercise": a live morale device that arms the first time it is
///     picked up, cannot be put down afterwards, and changes owners only by bumping into a living
///     colleague. Detonates (small, mostly-knockdown, PG) when the fuse runs out.
///
///     Distinct from upstream <see cref="Content.Shared.HotPotato.HotPotatoComponent"/> (which
///     transfers on melee hit): this one transfers on physical collision with a 1s anti-ping-pong
///     cooldown, and carries its own fixed fuse with an accelerating beep.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class SolreignHotPotatoComponent : Component
{
    /// <summary>
    /// Fixed fuse length, started the first time the exercise is picked up. Never resets.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan FuseDuration = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Whether the exercise has been armed (first pickup happened and the fuse is running).
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Armed;

    /// <summary>
    /// Transient gate mirroring upstream HotPotatoComponent.CanTransfer: while false an armed
    /// exercise cannot leave its container (no dropping, no stripping); the transfer code flips it
    /// on for the duration of a forced hand-off only.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool CanTransfer;

    /// <summary>
    /// The time at which the exercise detonates.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoNetworkedField, AutoPausedField]
    public TimeSpan DetonateAt = TimeSpan.Zero;

    /// <summary>
    /// Time of the next beep. Server-side only, so not networked (same as TimerTriggerComponent).
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoPausedField]
    public TimeSpan NextBeep = TimeSpan.Zero;

    /// <summary>
    /// Minimum time between collision hand-offs so the exercise can't ping-pong every tick
    /// between two colleagues standing in the same doorway.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan TransferCooldown = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The earliest time the next collision hand-off is allowed.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoPausedField]
    public TimeSpan NextTransferAllowed = TimeSpan.Zero;

    /// <summary>
    /// The beep. Interval shrinks linearly from <see cref="MaxBeepInterval"/> to
    /// <see cref="MinBeepInterval"/> as the fuse runs down (see HotPotatoFuseMath).
    /// </summary>
    [DataField, AutoNetworkedField]
    public SoundSpecifier? BeepSound = new SoundPathSpecifier("/Audio/Machines/Nuke/general_beep.ogg");

    /// <summary>
    /// Beep interval when the fuse is full.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan MaxBeepInterval = TimeSpan.FromSeconds(1.25);

    /// <summary>
    /// Beep interval just before detonation.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan MinBeepInterval = TimeSpan.FromSeconds(0.15);

    /// <summary>
    /// Explosion type used on detonation. Kept small and PG: mostly a loud knockdown.
    /// </summary>
    [DataField]
    public ProtoId<ExplosionPrototype> ExplosionType = "Default";

    /// <summary>
    /// Total explosion intensity. Deliberately tiny — this is a team-building exercise, not a bomb. Officially.
    /// </summary>
    [DataField]
    public float TotalIntensity = 5f;

    [DataField]
    public float IntensitySlope = 5f;

    [DataField]
    public float MaxTileIntensity = 2f;

    /// <summary>
    /// Radius of the accompanying flash — the bulk of the "explosion" is light and noise.
    /// </summary>
    [DataField]
    public float FlashRange = 3f;

    /// <summary>
    /// How long bystanders see spots after the exercise concludes.
    /// </summary>
    [DataField]
    public TimeSpan FlashDuration = TimeSpan.FromSeconds(4);
}
