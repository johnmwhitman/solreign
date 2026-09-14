namespace Content.Server._Solreign.MartialArts;

/// <summary>
///     The two inputs the kitchen brigade style recognizes. Swats are landed unarmed melee hits
///     (an open-hand smack — "get out of my kitchen"); tosses are successful disarm actions (the
///     traditional "OUT" escort). Both already exist upstream — same shape as
///     <see cref="CarpStep"/>, the style never invents new interactions, only sequences of existing
///     ones.
/// </summary>
public enum ChefStep : byte
{
    None,
    Swat,
    Toss,
}

/// <summary>
///     The three hardcoded Chef CQC combos (roadmap martial-arts toy — ONE style, nonlethal,
///     granted while wearing <see cref="SolreignChefApronComponent"/>):
///     Heat Check  = Swat, Swat -&gt; stamina jolt + kitchen callout.
///     86'd        = Swat, Toss -&gt; knockdown.
///     Order Up    = Toss, Toss -&gt; disarm throw, the item flies.
/// </summary>
public enum ChefCombo : byte
{
    None,
    HeatCheck,
    EightySixed,
    OrderUp,
}

/// <summary>
///     Pure combo-window and chain math for <see cref="SolreignChefApronSystem"/>. Kept free of
///     IoC/engine types so it is directly unit-testable
///     (Content.Tests/_Solreign/ChefComboRulesTests.cs) — same isolation shape as
///     <see cref="CarpComboRules"/>/<see cref="JudoComboRules"/>, deliberately NOT shared with
///     either (each martial-arts toy owns its own tiny copy of the window/cooldown helpers so the
///     three stay independently removable).
/// </summary>
public static class ChefComboRules
{
    /// <summary>
    ///     The combo produced by two consecutive steps, ignoring timing. Toss-then-Swat is
    ///     deliberately not a combo — the swat instead opens a fresh chain (see
    ///     <see cref="Advance"/>).
    /// </summary>
    public static ChefCombo Pair(ChefStep previous, ChefStep current)
    {
        return (previous, current) switch
        {
            (ChefStep.Swat, ChefStep.Swat) => ChefCombo.HeatCheck,
            (ChefStep.Swat, ChefStep.Toss) => ChefCombo.EightySixed,
            (ChefStep.Toss, ChefStep.Toss) => ChefCombo.OrderUp,
            _ => ChefCombo.None,
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
    ///     Advances the chef's combo chain by one step and reports the combo that fired, if any.
    ///
    ///     Chain rules:
    ///     * No previous step, a different target, or an expired window -&gt; the step OPENS a new
    ///       chain (no combo).
    ///     * A recognized pair (<see cref="Pair"/>) -&gt; the combo fires and the chain is CONSUMED
    ///       (<paramref name="nextPrevious"/> = None), so three quick swats yield one Heat Check,
    ///       not two.
    ///     * An unrecognized pair (Toss then Swat) -&gt; no combo; the new step becomes the opener.
    ///     * A None step is an observation no-op and leaves the chain untouched.
    /// </summary>
    /// <param name="nextPrevious">The opener the caller should store for the next step.</param>
    public static ChefCombo Advance(
        ChefStep previous,
        TimeSpan previousStepTime,
        ChefStep current,
        TimeSpan curTime,
        TimeSpan window,
        bool sameTarget,
        out ChefStep nextPrevious)
    {
        if (current == ChefStep.None)
        {
            nextPrevious = previous;
            return ChefCombo.None;
        }

        if (previous == ChefStep.None || !sameTarget || !WithinWindow(previousStepTime, curTime, window))
        {
            nextPrevious = current;
            return ChefCombo.None;
        }

        var combo = Pair(previous, current);
        nextPrevious = combo == ChefCombo.None ? current : ChefStep.None;
        return combo;
    }

    /// <summary>
    ///     Whether the chef's combo finisher is off cooldown at <paramref name="curTime"/>.
    ///     Same shape as <c>CarpComboRules.ComboReady</c>: default(TimeSpan) is immediately ready.
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
