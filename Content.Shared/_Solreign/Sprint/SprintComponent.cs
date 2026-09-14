using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Solreign.Sprint;

/// <summary>
///     Lets an entity hold the Sprint keybind (default C) to move faster at the cost of
///     draining stamina. Auto-drops the moment stamina gets too low and enforces a brief
///     cooldown before sprint can be re-engaged. See <see cref="SprintMath"/> for the pure
///     drain/cooldown math and <see cref="SolreignSprintSystem"/> for the ECS wiring.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(SolreignSprintSystem))]
public sealed partial class SprintComponent : Component
{
    /// <summary>
    /// Movement speed multiplier applied while actively sprinting (1.35 = +35%).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float SpeedModifier = 1.35f;

    /// <summary>
    /// Stamina damage applied per second while actively sprinting.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float StaminaDrainPerSecond = 12f;

    /// <summary>
    /// Fraction (0-1) of the entity's stamina crit threshold at which sprint force-drops.
    /// e.g. 0.9 means sprint auto-drops once stamina damage reaches 90% of the crit threshold,
    /// leaving a buffer so sprinting alone can't push a mob into stamina crit.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float AutoDropDamageFraction = 0.9f;

    /// <summary>
    /// How long, in seconds, sprint is locked out after an auto-drop before it can restart.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Cooldown = 4f;

    /// <summary>
    /// Sound cue played (via PlayPredicted, so the sprinter hears it immediately and everyone
    /// else in PVS range hears it too - see SolreignSprintSystem.StartSprinting) the moment a
    /// sprint actually starts. Not networked: it's baked-in prototype data, identical on both
    /// client and server, same convention as CombatModeComponent.DisarmSuccessSound.
    /// </summary>
    [DataField]
    public SoundSpecifier SprintStartSound = new SoundPathSpecifier("/Audio/Magic/Cults/ClockCult/steam_whoosh.ogg");

    /// <summary>
    /// Whether the sprint key is currently held down by the controlling player.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public bool KeyHeld;

    /// <summary>
    /// Whether the sprint speed modifier is currently being applied. False while on cooldown or
    /// out of stamina, even if <see cref="KeyHeld"/> is still true.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public bool Sprinting;

    /// <summary>
    /// The time at which an auto-drop cooldown ends and sprint may be re-engaged.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField]
    [AutoPausedField]
    public TimeSpan CooldownEndTime = TimeSpan.Zero;

    /// <summary>
    /// The next time a lump of <see cref="StaminaDrainPerSecond"/> stamina damage is applied
    /// while sprinting. Drain is charged once per second (same idiom as
    /// <c>StaminaComponent.NextUpdate</c>'s decay ticks) rather than every frame, so it doesn't
    /// spam admin logs or the network with dozens of tiny damage ticks a second.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField]
    [AutoPausedField]
    public TimeSpan NextDrainTime = TimeSpan.Zero;
}
