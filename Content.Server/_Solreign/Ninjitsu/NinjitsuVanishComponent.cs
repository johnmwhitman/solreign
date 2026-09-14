namespace Content.Server._Solreign.Ninjitsu;

/// <summary>
///     Transient marker while a Smoke Vanish (<see cref="Content.Shared._Solreign.Ninjitsu.NinjitsuGearComponent"/>)
///     is active on the wearer. Server-only scheduling state, not saved or networked — same pattern
///     as <c>SolreignMoonTouchedComponent</c>: the wearer already gets a networked
///     <c>StealthComponent</c> and movement-speed refresh, this component just remembers when to
///     take those back off and whether Smoke Vanish itself added the stealth (so it doesn't rip off
///     a phase cloak the player toggled on independently).
/// </summary>
[RegisterComponent, Access(typeof(NinjitsuSystem))]
public sealed partial class NinjitsuVanishComponent : Component
{
    /// <summary>Game time at which the vanish window ends.</summary>
    [ViewVariables]
    public TimeSpan ExpiresAt;

    /// <summary>Walk/sprint speed multiplier for the duration, copied from <c>NinjitsuGearComponent</c> at activation.</summary>
    [ViewVariables]
    public float SpeedMultiplier = 1f;

    /// <summary>
    ///     True if Smoke Vanish itself added <see cref="Content.Shared.Stealth.Components.StealthComponent"/>
    ///     (i.e. the wearer wasn't already cloaked by the suit's own phase cloak). Only ever remove
    ///     the stealth component on expiry if we're the ones who added it.
    /// </summary>
    [ViewVariables]
    public bool OwnsStealth;
}
