namespace Content.Server._Solreign.Antags.Vampire;

/// <summary>
///     Marks an employee as a Nocturnal Acquisitions Specialist (Night Audit division). All meter
///     arithmetic lives in the pure <see cref="VampireThirstMath"/>; this component is its per-entity
///     memory plus YAML-tunable rates. Design contract:
///     docs/specs/2026-07-11-werewolf-vampire-spec.md §4.
///
///     Server-only: thirst surfaces to the owning client through HUD alerts at build time.
/// </summary>
[RegisterComponent, Access(typeof(SolreignVampireSystem))]
public sealed partial class SolreignVampireComponent : Component
{
    /// <summary>Current thirst in [0, 100]. Higher = thirstier = weaker (never wilder).</summary>
    [ViewVariables]
    public double Thirst = 10d;

    /// <summary>Thirst gained per minute outside an Executive Recharge Pod.</summary>
    [DataField]
    public double ThirstPerMinute = 1.0d;

    /// <summary>Thirst RECOVERED per minute while resting inside a pod (accrual reverses).</summary>
    [DataField]
    public double CoffinRecoveryPerMinute = 4.0d;

    /// <summary>Thirst removed by one standard blood pack / donor sitting.</summary>
    [DataField]
    public double DrinkAmount = 25d;

    /// <summary>
    ///     Sunrise Clause progress in [0, 100]; each completed chapel ritual advances it. At 100 the
    ///     specialist is cured, keeps their memories, and gains "Reformed Night Auditor" flair.
    /// </summary>
    [ViewVariables]
    public double CureProgress;

    /// <summary>Cure progress granted per completed Sunrise Clause ritual.</summary>
    [DataField]
    public double CurePerRitual = 34d;

    /// <summary>
    ///     Situational flags refreshed by detection hooks (containers / proximity / area — TODO(build),
    ///     see <see cref="SolreignVampireSystem"/>). The skeleton stores them so the pure math is fully
    ///     drivable before those hooks exist.
    /// </summary>
    [ViewVariables]
    public bool InCoffin;

    /// <summary>A garlic ward is within range: feeding blocked, accrual ×1.5, sneezing.</summary>
    [ViewVariables]
    public bool GarlicNearby;

    /// <summary>Standing on chapel ground: powers off, feeding blocked, pod regen disabled.</summary>
    [ViewVariables]
    public bool InChapel;

    /// <summary>Game time thirst was last advanced (compared against IGameTiming.CurTime).</summary>
    [ViewVariables]
    public TimeSpan LastThirstTick;

    /// <summary>How often the meter advances. Coarse on purpose: thirst is a slow dial, not physics.</summary>
    [DataField]
    public float ThirstTickSeconds = 5f;

    /// <summary>
    ///     Blood drawn from a willing donor per completed "offer a donation" do-after (spec §4.2: "small
    ///     volume, never crits the donor"). Applied via <c>BloodstreamSystem.TryModifyBloodLevel</c>,
    ///     whose own cap ("Blood will not go over normal volume... " — symmetric floor) already prevents
    ///     this from ever bottoming the donor out on its own; kept small besides.
    /// </summary>
    [DataField]
    public double DonorBloodDraw = 15d;

    /// <summary>How long the donor consent do-after takes (spec §4.2: "do-after with explicit consent verb").</summary>
    [DataField]
    public float FeedDoAfterSeconds = 3f;

    /// <summary>How long a chaplain's Sunrise Clause ritual do-after takes (spec §4.4, shared with werewolf §3.4).</summary>
    [DataField]
    public float RitualDoAfterSeconds = 6f;
}
