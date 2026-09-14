using Robust.Shared.Audio;

namespace Content.Server._Solreign.MartialArts;

/// <summary>
///     Granted to the wearer of a <see cref="SolreignChefApronComponent"/> apron for as long as it
///     stays worn (added/removed by <see cref="SolreignChefApronSystem"/> off
///     ClothingGotEquippedEvent/ClothingGotUnequippedEvent) — this IS the "currently certified"
///     marker, the wear-conditioned counterpart to <see cref="SolreignMartialArtistComponent"/>'s
///     permanent one-time grant.
///
///     Chef CQC: a two-input pairwise style (same shape as
///     <see cref="CarpComboRules"/>/<see cref="SolreignMartialArtistComponent"/>, not the
///     three-step ordered chain <see cref="SolreignJudoBeltWearerComponent"/> uses) — no new input
///     bindings:
///       Swat (a landed unarmed hit) and Toss (a successful disarm) recognized inside a rolling
///       window:
///         Swat, Swat -&gt; Heat Check   (stamina jolt + callout)
///         Swat, Toss -&gt; 86'd         (knockdown)
///         Toss, Toss -&gt; Order Up     (disarm throw, the item flies)
///     Chain/window math lives in <see cref="ChefComboRules"/>.
///
///     All nonlethal, works WITH the stamina system rather than around it, same finishers idiom as
///     the Carp style. A cooldown between combos keeps it from stunlocking one target.
///
///     Server-only on purpose, same reasoning as the other combo-core toys: every finisher goes
///     through existing shared systems (stamina, stun, hands, throwing), so nothing here needs its
///     own networking.
/// </summary>
[RegisterComponent, Access(typeof(SolreignChefApronSystem))]
public sealed partial class SolreignChefApronWearerComponent : Component
{
    /// <summary>How long after a step the follow-up may land and still combo (inclusive edge).</summary>
    [DataField]
    public TimeSpan ComboWindow = TimeSpan.FromSeconds(1.5);

    /// <summary>
    ///     Seconds between combo FINISHERS, so swat/toss spam can't chain-throw a whole kitchen.
    ///     While on cooldown, steps still register as chain openers.
    /// </summary>
    [DataField]
    public TimeSpan ComboCooldown = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     Stamina damage dealt by Heat Check. Stamina only — never health; the target tires,
    ///     nothing more.
    /// </summary>
    [DataField]
    public float HeatCheckStaminaJolt = 20f;

    /// <summary>How long 86'd keeps the target on the floor.</summary>
    [DataField]
    public TimeSpan EightySixedKnockdownDuration = TimeSpan.FromSeconds(3);

    /// <summary>Throw speed for the item Order Up sends flying.</summary>
    [DataField]
    public float OrderUpThrowSpeed = 12f;

    /// <summary>How far (in tiles) Order Up aims the flying item.</summary>
    [DataField]
    public float OrderUpThrowDistance = 4f;

    /// <summary>Distinct sound for Heat Check (rapid open-hand swats).</summary>
    [DataField]
    public SoundSpecifier HeatCheckSound = new SoundPathSpecifier("/Audio/Effects/Footsteps/meatslap.ogg");

    /// <summary>Distinct sound for 86'd (the floor arrives).</summary>
    [DataField]
    public SoundSpecifier EightySixedSound = new SoundPathSpecifier("/Audio/Effects/slip.ogg");

    /// <summary>Distinct sound for Order Up (the item leaves the building).</summary>
    [DataField]
    public SoundSpecifier OrderUpSound = new SoundPathSpecifier("/Audio/Effects/pop.ogg");

    /// <summary>
    ///     The chain opener recorded by the previous step. Server-side scheduling state only —
    ///     not saved, not networked (same pattern as SolreignMartialArtistComponent.LastStep).
    /// </summary>
    [ViewVariables]
    public ChefStep LastStep = ChefStep.None;

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
