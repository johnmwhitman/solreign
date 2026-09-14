namespace Content.Server._Solreign.Antags;

/// <summary>
///     SR-W-036: Antag Rotation Cooldown & Selection Rules Component.
///     Tracks rounds since last antagonist role, minimum cooldowns, and streak weights.
/// </summary>
// NOTE: deliberately NOT [NetworkedComponent]. This component lives only in Content.Server and has
// no Content.Client counterpart; marking it networked would add a server-only entry to the ordinal
// networked-component list in ComponentFactory.GenerateNetIds(), shifting every NetId that sorts
// after "SolreignAntagRotation" on the server but not the client. See root-cause note for SR-W-036.
[RegisterComponent]
public sealed partial class SolreignAntagRotationComponent : Component
{
    /// <summary>Number of consecutive shifts played without receiving an antagonist role.</summary>
    [DataField("roundsSinceLastAntag")]
    public int RoundsSinceLastAntag = 3;

    /// <summary>Minimum cooldown in shifts before becoming eligible for antagonist selection again.</summary>
    [DataField("minimumCooldownRounds")]
    public int MinimumCooldownRounds = 3;

    /// <summary>Dynamic selection weight multiplier based on non-antagonist shift streak.</summary>
    [DataField("antagWeight")]
    public int AntagWeight = 1;

    /// <summary>Whether the player has satisfied the minimum shift cooldown requirement.</summary>
    [DataField("eligibleForAntag")]
    public bool EligibleForAntag = true;
}
