using Content.Shared.Actions;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Solreign.Ninjitsu;

/// <summary>
///     Solreign ninjitsu flavor, wave 7+ ninja pass: "Smoke Vanish", a suit-tech gadget ability
///     alongside the upstream EMP/throwing-star/recall-katana suit actions
///     (<see cref="Content.Shared.Ninja.Components.NinjaSuitComponent"/>). Attached to the ninja
///     suit prototype (Resources/Prototypes/Entities/Clothing/OuterClothing/suits.yml,
///     <c>ClothingOuterSuitSpaceNinja</c>) the exact same way <c>DashAbility</c> is attached to the
///     Energy Katana — a standalone component + system, no edits to <c>NinjaSuitComponent</c> itself.
///
///     Spends suit battery charge (like EMP) to pop a smoke charge at the wearer's feet: a real
///     <c>Smoke</c> cloud entity (<see cref="Content.Server.Fluids.EntitySystems.SmokeSystem"/>,
///     empty solution — visual concealment only, no reagent effects) plus a brief personal
///     stealth + speed window on the wearer (existing <see cref="Content.Shared.Stealth.Components.StealthComponent"/>
///     and <see cref="Content.Shared.Movement.Systems.RefreshMovementSpeedModifiersEvent"/> — no new
///     visibility or movement system, same idiom as the suit's own phase cloak and
///     <c>SolreignMoonTouchedComponent</c>'s cosmetic speed hook).
///
///     Server logic: <see cref="Content.Server._Solreign.Ninjitsu.NinjitsuSystem"/>. Cooldown/expiry
///     math: <see cref="Content.Server._Solreign.Ninjitsu.NinjitsuRules"/>
///     (Content.Tests/_Solreign/NinjitsuRulesTests.cs).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedNinjitsuGearSystem))]
public sealed partial class NinjitsuGearComponent : Component
{
    /// <summary>The action id for Smoke Vanish.</summary>
    [DataField]
    public EntProtoId SmokeVanishAction = "ActionNinjitsuSmokeVanish";

    [DataField, AutoNetworkedField]
    public EntityUid? SmokeVanishActionEntity;

    /// <summary>Suit battery charge spent per use. Cheaper than EMP (180), pricier than throwing stars (14.4).</summary>
    [DataField]
    public float SmokeVanishCharge = 60f;

    /// <summary>Minimum time between Smoke Vanish uses, gated by <see cref="Content.Server._Solreign.Ninjitsu.NinjitsuRules.CooldownReady"/>.</summary>
    [DataField]
    public TimeSpan SmokeVanishCooldown = TimeSpan.FromSeconds(20);

    /// <summary>Game time at which Smoke Vanish may next be used. Server bookkeeping only, not networked.</summary>
    [ViewVariables]
    public TimeSpan NextVanishReadyAt;

    /// <summary>How long the personal stealth + speed window lasts after popping the charge.</summary>
    [DataField]
    public TimeSpan VanishDuration = TimeSpan.FromSeconds(4);

    /// <summary>
    ///     Stealth visibility applied for the duration — matches the suit's own phase cloak preset
    ///     (suits.yml <c>ComponentToggler</c>: <c>minVisibility: 0.1</c>) so Smoke Vanish looks like
    ///     the same tech family, not a stronger or weaker cloak.
    /// </summary>
    [DataField]
    public float VanishVisibility = 0.1f;

    /// <summary>Walk/sprint speed multiplier while vanished — a hasty retreat, not a sprint upgrade.</summary>
    [DataField]
    public float VanishSpeedMultiplier = 1.5f;

    /// <summary>How long the visual smoke cloud itself lingers (separate from the wearer's personal vanish window).</summary>
    [DataField]
    public TimeSpan SmokeDuration = TimeSpan.FromSeconds(6);

    /// <summary>Spread tiles for the smoke cloud — a personal pop, not a room-filler.</summary>
    [DataField]
    public int SmokeSpreadAmount = 2;

    /// <summary>Smoke cloud entity prototype to spawn — same default upstream grenades use.</summary>
    [DataField]
    public EntProtoId SmokePrototype = "Smoke";

    /// <summary>Sound played when the charge pops.</summary>
    [DataField]
    public SoundSpecifier VanishSound = new SoundPathSpecifier("/Audio/Effects/smoke.ogg");
}

/// <summary>Raised on the suit when Smoke Vanish is activated. Handled server-side only (<see cref="Content.Server._Solreign.Ninjitsu.NinjitsuSystem"/>).</summary>
public sealed partial class NinjitsuSmokeVanishEvent : InstantActionEvent;
