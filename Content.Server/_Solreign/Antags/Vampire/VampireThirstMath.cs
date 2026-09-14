namespace Content.Server._Solreign.Antags.Vampire;

/// <summary>
///     Hydration status of a Nocturnal Acquisitions Specialist. Higher = thirstier = WEAKER: the
///     bottom of the meter disables powers instead of enabling violence (anti-grief rule 2, spec
///     docs/specs/2026-07-11-werewolf-vampire-spec.md §4.5).
/// </summary>
public enum ThirstBand
{
    /// <summary>0–24: full powers. The Night Audit division at its most punctual.</summary>
    Sated = 0,

    /// <summary>25–59: baseline. A gentle HUD nudge suggests a hydration opportunity.</summary>
    Peckish = 1,

    /// <summary>60–84: powers dim, pallor shows.</summary>
    Thirsty = 2,

    /// <summary>85–100: powers offline, loud stomach-growl popups broadcast position, movement slowed.</summary>
    Ravenous = 3,
}

/// <summary>
///     Pure, unit-testable thirst-meter arithmetic. No ECS, no I/O — <see cref="SolreignVampireSystem"/>
///     feeds it elapsed time and situational flags and stores what comes back (RankRules/
///     PeriodicEffectTiming pure-logic doctrine; tested in Content.Tests/_Solreign/VampireThirstMathTests.cs).
///
///     All config inputs arrive from YAML, so they are sanitized (clamped) rather than validated
///     (thrown), matching <c>PeriodicEffectTiming</c>.
/// </summary>
public static class VampireThirstMath
{
    public const double MinThirst = 0d;
    public const double MaxThirst = 100d;

    /// <summary>Thirst below this is <see cref="ThirstBand.Sated"/>.</summary>
    public const double PeckishThreshold = 25d;

    /// <summary>Thirst at or above this is <see cref="ThirstBand.Thirsty"/>.</summary>
    public const double ThirstyThreshold = 60d;

    /// <summary>Thirst at or above this is <see cref="ThirstBand.Ravenous"/>.</summary>
    public const double RavenousThreshold = 85d;

    /// <summary>Garlic in range makes thirst accrue this much faster (comedy, not damage).</summary>
    public const double GarlicAccrualMultiplier = 1.5d;

    /// <summary>Classifies a thirst value (clamped first) into its band.</summary>
    public static ThirstBand Band(double thirst)
    {
        var t = Math.Clamp(thirst, MinThirst, MaxThirst);
        if (t >= RavenousThreshold)
            return ThirstBand.Ravenous;
        if (t >= ThirstyThreshold)
            return ThirstBand.Thirsty;
        if (t >= PeckishThreshold)
            return ThirstBand.Peckish;
        return ThirstBand.Sated;
    }

    /// <summary>
    ///     Advances the meter by <paramref name="elapsed"/>. Outside the pod thirst rises at
    ///     <paramref name="accrualPerMinute"/> (times the garlic multiplier when a ward is in range);
    ///     inside an Executive Recharge Pod it FALLS at <paramref name="coffinRecoveryPerMinute"/>
    ///     (spec §4.2: the pod reverses accrual — the vampire's power has an address).
    ///     Negative rates and negative elapsed time clamp to zero; the result clamps to [0, 100].
    /// </summary>
    public static double Accumulate(
        double thirst,
        TimeSpan elapsed,
        double accrualPerMinute,
        bool inCoffin,
        double coffinRecoveryPerMinute,
        bool garlicNearby)
    {
        var minutes = Math.Max(0d, elapsed.TotalMinutes);
        var accrual = Math.Max(0d, accrualPerMinute);
        var recovery = Math.Max(0d, coffinRecoveryPerMinute);

        var delta = inCoffin
            ? -recovery * minutes
            : accrual * (garlicNearby ? GarlicAccrualMultiplier : 1d) * minutes;

        return Math.Clamp(thirst + delta, MinThirst, MaxThirst);
    }

    /// <summary>
    ///     Applies a drink (blood pack or Voluntary Donor Program donation). Negative amounts clamp to
    ///     zero — drinking never makes anyone thirstier. Result floors at 0.
    /// </summary>
    public static double Drink(double thirst, double amount)
    {
        var sip = Math.Max(0d, amount);
        return Math.Clamp(thirst - sip, MinThirst, MaxThirst);
    }

    /// <summary>
    ///     The feeding gate (spec §4.3): drinking — from packs or donors — is blocked near a garlic
    ///     ward and on chapel ground, regardless of band. Ravenous does NOT override the gate; it just
    ///     makes relocation urgent. There is deliberately no forced-bite path for this to guard.
    /// </summary>
    public static bool CanFeed(ThirstBand band, bool garlicNearby, bool inChapel)
    {
        _ = band; // Kept in the signature: the build pass may band-gate donor (not pack) drinking.
        return !garlicNearby && !inChapel;
    }

    /// <summary>
    ///     Passive-heal multiplier for the build pass: the pod doubles regeneration, the chapel and a
    ///     Ravenous meter shut it off, everything else is baseline.
    /// </summary>
    public static double RegenMultiplier(ThirstBand band, bool inCoffin, bool inChapel)
    {
        if (inChapel || band == ThirstBand.Ravenous)
            return 0d;
        return inCoffin ? 2d : 1d;
    }

    /// <summary>
    ///     Whether accumulated Sunrise Clause progress (spec §4.4, shared ritual with werewolf's §3.4)
    ///     has reached the cure threshold. Values above 100 (a misconfigured CurePerRitual overshoot)
    ///     still count as cured rather than requiring an exact match.
    /// </summary>
    public static bool IsCured(double cureProgress) => cureProgress >= MaxThirst;

    /// <summary>
    ///     Movement-speed multiplier by band (spec §4: Sated = "minor speed boost", Thirsty = "speed
    ///     boost gone" i.e. baseline, Ravenous = "movement slowed"). Hunger only ever weakens — no band
    ///     ever grants more than a minor boost, and nothing here is an attack buff.
    /// </summary>
    public static double SpeedMultiplier(ThirstBand band) => band switch
    {
        ThirstBand.Sated => 1.1d,
        ThirstBand.Ravenous => 0.85d,
        _ => 1.0d,
    };
}
