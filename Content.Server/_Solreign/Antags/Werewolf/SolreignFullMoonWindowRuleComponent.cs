namespace Content.Server._Solreign.Antags.Werewolf;

/// <summary>
///     Event-night entry point for the werewolf antag (spec §3.2: "Trigger source: the Full Moon Window
///     is round-event-driven"). A <c>GameRuleSystem</c> game rule
///     (Resources/Prototypes/_Solreign/GameRules/full_moon_window.yml) that, while active, opens a Full
///     Moon Window: it drafts exactly one Lunar-Reactive employee (anti-grief rule 5, spec §3.5: "one
///     werewolf per event unless an admin says otherwise") and flips
///     <see cref="SolreignWerewolfSystem.MoonWindowActive"/> for its duration.
/// </summary>
[RegisterComponent, Access(typeof(SolreignFullMoonWindowRuleSystem))]
public sealed partial class SolreignFullMoonWindowRuleComponent : Component
{
    /// <summary>How long the moon stays up once the window opens.</summary>
    [DataField]
    public float WindowSeconds = 300f;

    /// <summary>Accumulated time since the window opened. Scheduling state, not config.</summary>
    [ViewVariables]
    public float Elapsed;

    /// <summary>Whether this instance has already drafted its one werewolf for the round.</summary>
    [ViewVariables]
    public bool Drafted;
}
