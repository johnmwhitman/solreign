using Content.Shared._Solreign.Ninjitsu;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._Solreign.Ninjitsu;

/// <summary>
///     Marks a Space Ninja as carrying their innate ninjitsu training — granted automatically
///     alongside <see cref="Content.Shared.Ninja.Components.SpaceNinjaComponent"/>
///     (<see cref="NinjitsuSystem"/> subscribes <c>ComponentStartup</c>), independent of gear:
///     unlike Smoke Vanish (<see cref="NinjitsuGearComponent"/>, suit battery tech), Silent Step and
///     the Sleeping-Carp-style takedown are the ninja's OWN body skill and work whether or not the
///     suit or boots are still on — see the Silent Step doc comment on why that matters.
///
///     Server-only on purpose, same reasoning as <c>SolreignMartialArtistComponent</c>: every effect
///     here is applied through existing shared systems (footstep tag/component, stamina/stun via
///     the takedown), so nothing needs its own networking.
/// </summary>
[RegisterComponent, Access(typeof(NinjitsuSystem))]
public sealed partial class NinjitsuTrainingComponent : Component
{
    // --- Silent Step ---

    /// <summary>The action id for the innate Silent Step toggle.</summary>
    [DataField]
    public EntProtoId SilentStepAction = "ActionNinjitsuSilentStep";

    [DataField]
    public EntityUid? SilentStepActionEntity;

    /// <summary>
    ///     Minimum time between toggles, gated by <see cref="NinjitsuRules.CooldownReady"/> — stops
    ///     spam-clicking the action from thrashing the networked <c>FootstepModifierComponent</c>
    ///     add/remove every tick.
    /// </summary>
    [DataField]
    public TimeSpan SilentStepToggleDebounce = TimeSpan.FromSeconds(1);

    /// <summary>Game time at which the toggle may next be used.</summary>
    [ViewVariables]
    public TimeSpan NextSilentStepToggleAt;

    /// <summary>Sound played on each toggle (on AND off — a ninja notices either way).</summary>
    [DataField]
    public SoundSpecifier SilentStepSound = new SoundPathSpecifier("/Audio/Effects/thudswoosh.ogg");

    // --- Carp-style nonlethal takedown ---
    // Reuses the Way of the Ornamental Carp's combo core directly (CarpComboRules.WithinWindow for
    // the chain window, ComboReady/NextComboTime for the finisher cooldown — see
    // Content.Server._Solreign.MartialArts.CarpComboRules) rather than re-deriving the same pure
    // math under a new name. Ninjitsu's chain is simpler than Carp's: a single input (a landed
    // unarmed strike), two in a row on the same target within the window silently drops them —
    // nonlethal, no damage, same "existing interactions only" philosophy as the Carp style.

    /// <summary>How long after a strike the follow-up may land and still chain into a takedown.</summary>
    [DataField]
    public TimeSpan TakedownWindow = TimeSpan.FromSeconds(1.5);

    /// <summary>Seconds between takedowns, so strike-spam can't chain-drop a whole corridor.</summary>
    [DataField]
    public TimeSpan TakedownCooldown = TimeSpan.FromSeconds(4);

    /// <summary>How long the takedown keeps the target on the floor.</summary>
    [DataField]
    public TimeSpan TakedownKnockdownDuration = TimeSpan.FromSeconds(4);

    /// <summary>Distinct sound for the takedown finisher.</summary>
    [DataField]
    public SoundSpecifier TakedownSound = new SoundPathSpecifier("/Audio/Effects/metal_thud1.ogg");

    /// <summary>Who the previous unarmed strike landed on; the chain must stay on one target.</summary>
    [ViewVariables]
    public EntityUid? LastStrikeTarget;

    /// <summary>Game time of the previous strike.</summary>
    [ViewVariables]
    public TimeSpan LastStrikeTime;

    /// <summary>Game time at which the next takedown finisher may fire.</summary>
    [ViewVariables]
    public TimeSpan NextTakedownAt;
}
