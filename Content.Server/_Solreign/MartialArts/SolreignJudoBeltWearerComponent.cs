using Robust.Shared.Audio;

namespace Content.Server._Solreign.MartialArts;

/// <summary>
///     Granted to the wearer of a <see cref="SolreignJudoBeltComponent"/> belt for as long as it
///     stays worn (added/removed by <see cref="SolreignJudoBeltSystem"/> off
///     ClothingGotEquippedEvent/ClothingGotUnequippedEvent) — this IS the "currently certified"
///     marker, the wear-conditioned counterpart to <see cref="SolreignMartialArtistComponent"/>'s
///     permanent one-time grant.
///
///     The combo core's judo moveset: an ORDERED three-step chain of EXISTING interactions, all
///     landing on the same target inside a rolling window — no new input bindings, same philosophy
///     as the Carp style:
///       Push (a landed unarmed hit) -&gt; Shove (a successful disarm) -&gt; Grab (starting a pull) -&gt; STUN
///     Landing a step out of order, on a stale target, or outside the window resets the chain; only
///     a fresh Push can (re)open it. Chain/window math lives in <see cref="JudoComboRules"/>.
///
///     All nonlethal, and it WORKS WITH the stamina system rather than around it: the throw still
///     feeds a stamina jolt (same shape as Carp Rush — respects resistance, visualizes, logs) before
///     applying the guaranteed stun through the very same paralyze status effect a stamina crit
///     would apply. A cooldown between throws (mirroring
///     SolreignMartialArtistComponent.ComboCooldown) keeps it from stunlocking one target.
///
///     Server-only on purpose, same reasoning as SolreignMartialArtistComponent: every finisher goes
///     through existing shared systems (stamina, stun, pulling), so nothing here needs its own
///     networking.
/// </summary>
[RegisterComponent, Access(typeof(SolreignJudoBeltSystem))]
public sealed partial class SolreignJudoBeltWearerComponent : Component
{
    /// <summary>How long after a step the follow-up may land and still continue the chain (inclusive edge).</summary>
    [DataField]
    public TimeSpan ComboWindow = TimeSpan.FromSeconds(1.5);

    /// <summary>
    ///     Seconds between completed throws, so Push-Shove-Grab spam can't stunlock one target. While
    ///     on cooldown, steps still register (a fresh Push still opens a chain) but cannot complete
    ///     one — same shape as SolreignMartialArtistComponent.ComboCooldown.
    /// </summary>
    [DataField]
    public TimeSpan ComboCooldown = TimeSpan.FromSeconds(4);

    /// <summary>How long the finished throw stuns the target for (the real Stunned status effect, not just a knockdown).</summary>
    [DataField]
    public TimeSpan StunDuration = TimeSpan.FromSeconds(4);

    /// <summary>
    ///     Stamina damage the throw ALSO deals — this is what "works with the stamina system" means
    ///     in practice: the jolt respects stamina resistance and is applied through
    ///     SharedStaminaSystem.TakeStaminaDamage same as any other stamina hit, so a target who's
    ///     already near their own crit threshold just crits normally on top of the guaranteed stun
    ///     below. Stamina only — never health.
    /// </summary>
    [DataField]
    public float GrabStaminaJolt = 20f;

    /// <summary>Distinct sound for the finished throw.</summary>
    [DataField]
    public SoundSpecifier ThrowSound = new SoundPathSpecifier("/Audio/Effects/thudswoosh.ogg");

    /// <summary>
    ///     How far into Push -&gt; Shove -&gt; Grab the wearer currently sits. Server-side scheduling
    ///     state only — not saved, not networked (same pattern as
    ///     SolreignMartialArtistComponent.LastStep).
    /// </summary>
    [ViewVariables]
    public JudoChainState ChainState = JudoChainState.Empty;

    /// <summary>Game time of the previous step.</summary>
    [ViewVariables]
    public TimeSpan LastStepTime;

    /// <summary>Who the previous step landed on; the chain must stay on one target.</summary>
    [ViewVariables]
    public EntityUid? LastTarget;

    /// <summary>Game time at which the next throw may fire.</summary>
    [ViewVariables]
    public TimeSpan NextComboTime;
}
