namespace Content.Server._Solreign.MartialArts;

/// <summary>
///     The two inputs the Way of the Ornamental Carp recognizes. Strikes are landed unarmed melee
///     hits; shoves are successful disarm actions. Both already exist upstream — the style never
///     invents new interactions, only sequences of existing ones.
/// </summary>
public enum CarpStep : byte
{
    None,
    Strike,
    Shove,
}

/// <summary>
///     The three hardcoded Ornamental Carp combos (roadmap martial-arts toy — ONE style, nonlethal):
///     Carp Rush   = Strike, Strike -> stamina jolt + battle-cry popup.
///     Rising Tide = Strike, Shove  -> knockdown.
///     Gentle Current = Shove, Shove -> disarm throw, the item flies.
/// </summary>
public enum CarpCombo : byte
{
    None,
    CarpRush,
    RisingTide,
    GentleCurrent,
}

/// <summary>
///     Pure combo-window and chain math for <see cref="SolreignMartialArtsSystem"/>. Kept free of
///     IoC/engine types so it is directly unit-testable (Content.Tests/_Solreign/CarpComboRulesTests.cs),
///     mirroring how <c>ZoneRules</c> and <c>HotPotatoFuseMath</c> isolate pure logic elsewhere
///     in _Solreign.
/// </summary>
public static class CarpComboRules
{
    /// <summary>
    ///     The combo produced by two consecutive steps, ignoring timing. Shove-then-Strike is
    ///     deliberately not a combo — the strike instead opens a fresh chain (see
    ///     <see cref="Advance"/>).
    /// </summary>
    public static CarpCombo Pair(CarpStep previous, CarpStep current)
    {
        return (previous, current) switch
        {
            (CarpStep.Strike, CarpStep.Strike) => CarpCombo.CarpRush,
            (CarpStep.Strike, CarpStep.Shove) => CarpCombo.RisingTide,
            (CarpStep.Shove, CarpStep.Shove) => CarpCombo.GentleCurrent,
            _ => CarpCombo.None,
        };
    }

    /// <summary>
    ///     Whether a step at <paramref name="curTime"/> chains off a previous step at
    ///     <paramref name="previousStepTime"/>. The window edge is inclusive: landing exactly
    ///     <paramref name="window"/> after the opener still chains. A negative window (YAML
    ///     misconfiguration) clamps to zero (same-tick only); a previous step that is somehow in
    ///     the future never chains.
    /// </summary>
    public static bool WithinWindow(TimeSpan previousStepTime, TimeSpan curTime, TimeSpan window)
    {
        if (window < TimeSpan.Zero)
            window = TimeSpan.Zero;

        var elapsed = curTime - previousStepTime;
        return elapsed >= TimeSpan.Zero && elapsed <= window;
    }

    /// <summary>
    ///     Advances the artist's combo chain by one step and reports the combo that fired, if any.
    ///
    ///     Chain rules:
    ///     * No previous step, a different target, or an expired window -> the step OPENS a new
    ///       chain (no combo).
    ///     * A recognized pair (<see cref="Pair"/>) -> the combo fires and the chain is CONSUMED
    ///       (<paramref name="nextPrevious"/> = None), so three quick strikes yield one Carp Rush,
    ///       not two.
    ///     * An unrecognized pair (Shove then Strike) -> no combo; the new step becomes the opener.
    ///     * A None step is an observation no-op and leaves the chain untouched.
    /// </summary>
    /// <param name="nextPrevious">The opener the caller should store for the next step.</param>
    public static CarpCombo Advance(
        CarpStep previous,
        TimeSpan previousStepTime,
        CarpStep current,
        TimeSpan curTime,
        TimeSpan window,
        bool sameTarget,
        out CarpStep nextPrevious)
    {
        if (current == CarpStep.None)
        {
            nextPrevious = previous;
            return CarpCombo.None;
        }

        if (previous == CarpStep.None || !sameTarget || !WithinWindow(previousStepTime, curTime, window))
        {
            nextPrevious = current;
            return CarpCombo.None;
        }

        var combo = Pair(previous, current);
        nextPrevious = combo == CarpCombo.None ? current : CarpStep.None;
        return combo;
    }

    /// <summary>
    ///     Whether the artist's combo finisher is off cooldown at <paramref name="curTime"/>.
    ///     Same shape as <c>ZoneRules.CooldownReady</c>: default(TimeSpan) is immediately ready.
    /// </summary>
    public static bool ComboReady(TimeSpan curTime, TimeSpan nextComboTime)
    {
        return curTime >= nextComboTime;
    }

    /// <summary>
    ///     Computes when the next combo may fire. A negative cooldown (YAML misconfiguration)
    ///     clamps to zero rather than scheduling into the past.
    /// </summary>
    public static TimeSpan NextComboTime(TimeSpan curTime, TimeSpan cooldown)
    {
        return curTime + (cooldown > TimeSpan.Zero ? cooldown : TimeSpan.Zero);
    }
}
