namespace Content.Server._Solreign.Antags.Vampire;

/// <summary>
///     Event-night entry point for the vampire antag (spec §4: unlike the werewolf, there is no
///     "window" to open — a Nocturnal Acquisitions Specialist just IS one for the rest of the round
///     once drafted, thirst meter running continuously). This game rule
///     (Resources/Prototypes/_Solreign/GameRules/vampire_night.yml) drafts exactly one eligible crew
///     member at Started and otherwise does nothing further — no ActiveTick/Ended toggle needed.
/// </summary>
[RegisterComponent, Access(typeof(SolreignVampireNightRuleSystem))]
public sealed partial class SolreignVampireNightRuleComponent : Component
{
    /// <summary>Whether this instance has already drafted its one vampire for the round.</summary>
    [ViewVariables]
    public bool Drafted;
}
