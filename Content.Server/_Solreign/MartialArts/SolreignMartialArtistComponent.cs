using Robust.Shared.Audio;

namespace Content.Server._Solreign.MartialArts;

/// <summary>
///     Marks an entity as a practitioner of the Way of the Ornamental Carp (roadmap martial-arts
///     toy: ONE style, three hardcoded combos, strictly nonlethal). Granted one-time by using a
///     <see cref="SolreignCarpScrollComponent"/> scroll; this component IS the "already learned"
///     marker.
///
///     Combos are sequences of EXISTING interactions — landed unarmed melee hits (Strike) and
///     successful disarm shoves (Shove) — recognized inside a time window:
///       Strike, Strike -> Carp Rush      (stamina jolt + battle cry)
///       Strike, Shove  -> Rising Tide    (knockdown)
///       Shove,  Shove  -> Gentle Current (disarm throw, the item flies)
///     Chain/window math lives in <see cref="CarpComboRules"/>.
///
///     Server-only on purpose, like <c>SolreignTrainingBatonComponent</c>: every finisher is
///     applied through existing shared systems (stamina, stun, hands, throwing), so nothing here
///     needs its own networking.
/// </summary>
[RegisterComponent, Access(typeof(SolreignMartialArtsSystem))]
public sealed partial class SolreignMartialArtistComponent : Component
{
    /// <summary>
    ///     How long after a step the follow-up may land and still combo (inclusive edge).
    /// </summary>
    [DataField]
    public TimeSpan ComboWindow = TimeSpan.FromSeconds(1.5);

    /// <summary>
    ///     Seconds between combo FINISHERS, so shove spam can't chain-throw a whole locker room.
    ///     While on cooldown, steps still register as chain openers.
    /// </summary>
    [DataField]
    public TimeSpan ComboCooldown = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     Stamina damage dealt by Carp Rush. Stamina only — never health; the target tires,
    ///     nothing more. Upstream stamina crit sits at ~100.
    /// </summary>
    [DataField]
    public float CarpRushStaminaJolt = 25f;

    /// <summary>How long Rising Tide keeps the target on the floor.</summary>
    [DataField]
    public TimeSpan RisingTideKnockdownDuration = TimeSpan.FromSeconds(3);

    /// <summary>Throw speed for the item Gentle Current sends flying.</summary>
    [DataField]
    public float GentleCurrentThrowSpeed = 12f;

    /// <summary>How far (in tiles) Gentle Current aims the flying item.</summary>
    [DataField]
    public float GentleCurrentThrowDistance = 4f;

    /// <summary>Distinct sound for Carp Rush (rapid punches).</summary>
    [DataField]
    public SoundSpecifier CarpRushSound = new SoundPathSpecifier("/Audio/Weapons/boxingpunch1.ogg");

    /// <summary>Distinct sound for Rising Tide (the floor arrives).</summary>
    [DataField]
    public SoundSpecifier RisingTideSound = new SoundPathSpecifier("/Audio/Effects/slip.ogg");

    /// <summary>Distinct sound for Gentle Current (water takes the item away).</summary>
    [DataField]
    public SoundSpecifier GentleCurrentSound = new SoundPathSpecifier("/Audio/Effects/waterswirl.ogg");

    /// <summary>
    ///     The chain opener recorded by the previous step. Server-side scheduling state only —
    ///     not saved, not networked (same pattern as SolreignTrainingBatonComponent.NextEffectTime).
    /// </summary>
    [ViewVariables]
    public CarpStep LastStep = CarpStep.None;

    /// <summary>Game time of the previous step.</summary>
    [ViewVariables]
    public TimeSpan LastStepTime;

    /// <summary>Who the previous step landed on; combos must stay on one target.</summary>
    [ViewVariables]
    public EntityUid? LastTarget;

    /// <summary>Game time at which the next combo finisher may fire.</summary>
    [ViewVariables]
    public TimeSpan NextComboTime;
}
