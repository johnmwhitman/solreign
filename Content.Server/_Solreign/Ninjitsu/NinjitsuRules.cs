using Content.Server._Solreign.MartialArts;

namespace Content.Server._Solreign.Ninjitsu;

/// <summary>
///     Pure cooldown/expiry math for <see cref="NinjitsuSystem"/>, kept free of IoC/engine types so
///     it is directly unit-testable (Content.Tests/_Solreign/NinjitsuRulesTests.cs) — same shape as
///     <c>CarpComboRules</c>, <c>WerewolfMaulRules</c> and <c>ZoneRules</c> elsewhere in _Solreign.
///
///     The nonlethal takedown deliberately does NOT re-derive its own window/finisher-cooldown math:
///     it calls straight into <see cref="CarpComboRules.WithinWindow"/> and
///     <see cref="CarpComboRules.ComboReady"/>/<see cref="CarpComboRules.NextComboTime"/> — those
///     three functions take only <see cref="TimeSpan"/>s, no <c>CarpStep</c>/<c>CarpCombo</c>, so
///     they're genuinely reusable "combo core" math, not a coincidence of shared shape. See
///     <see cref="NinjitsuSystem.OnNinjaMeleeHit"/>.
/// </summary>
public static class NinjitsuRules
{
    /// <summary>
    ///     Whether an ability/toggle is off cooldown at <paramref name="curTime"/>. Same shape as
    ///     <c>CarpComboRules.ComboReady</c>: default(TimeSpan) is immediately ready.
    /// </summary>
    public static bool CooldownReady(TimeSpan curTime, TimeSpan readyAt)
    {
        return curTime >= readyAt;
    }

    /// <summary>
    ///     Computes when an ability/toggle may next be used. A negative cooldown (YAML
    ///     misconfiguration) clamps to zero rather than scheduling into the past.
    /// </summary>
    public static TimeSpan NextReady(TimeSpan curTime, TimeSpan cooldown)
    {
        return curTime + (cooldown > TimeSpan.Zero ? cooldown : TimeSpan.Zero);
    }

    /// <summary>Whether a Smoke Vanish window (personal stealth + speed) is still active.</summary>
    public static bool VanishActive(TimeSpan curTime, TimeSpan expiresAt)
    {
        return curTime < expiresAt;
    }

    /// <summary>
    ///     Whether a takedown strike still chains off the previous one. Deliberately delegates
    ///     straight to <see cref="CarpComboRules.WithinWindow"/> rather than re-deriving the
    ///     window math — see the reuse note on <see cref="NinjitsuSystem.OnNinjaMeleeHit"/>.
    /// </summary>
    public static bool StrikesChain(TimeSpan previousStrikeTime, TimeSpan curTime, TimeSpan window)
    {
        return CarpComboRules.WithinWindow(previousStrikeTime, curTime, window);
    }
}
