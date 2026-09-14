namespace Content.Server._Solreign.MartialArts;

/// <summary>
///     The three inputs the Security Judo Belt recognizes, in the ONLY order that matters. All three
///     already exist upstream — Push and Shove are the same shape of interaction the Way of the
///     Ornamental Carp already reuses (see <see cref="CarpStep"/>), and Grab is a third existing
///     interaction (starting a pull) that Carp doesn't touch.
/// </summary>
public enum JudoStep : byte
{
    Push,
    Shove,
    Grab,
}

/// <summary>
///     How far into the ordered Push -> Shove -> Grab chain the wearer currently sits. Unlike the
///     Carp style's pairwise lookup (<see cref="CarpComboRules.Pair"/>), the judo takedown is a
///     single three-token SEQUENCE: landing the right step out of order does not partially count.
/// </summary>
public enum JudoChainState : byte
{
    /// <summary>No progress, or progress that reset. Only a Push can advance out of here.</summary>
    Empty,

    /// <summary>Push has landed. A Shove on the same target inside the window continues the chain.</summary>
    PushLanded,

    /// <summary>Push then Shove have landed. A Grab on the same target inside the window throws.</summary>
    PushShoveLanded,
}

/// <summary>
///     Pure chain/window/cooldown math for <see cref="SolreignJudoBeltSystem"/>. Kept free of IoC/
///     engine types so it is directly unit-testable (Content.Tests/_Solreign/JudoComboRulesTests.cs),
///     the same isolation shape as <see cref="CarpComboRules"/> (deliberately NOT shared with it —
///     the belt lane owns its own tiny copy of the window/cooldown helpers so the two martial-arts
///     toys stay independently removable).
/// </summary>
public static class JudoComboRules
{
    /// <summary>
    ///     Whether a step at <paramref name="curTime"/> chains off a previous step at
    ///     <paramref name="previousStepTime"/>. The window edge is inclusive: landing exactly
    ///     <paramref name="window"/> after the opener still chains. A negative window (YAML
    ///     misconfiguration) clamps to zero (same-tick only); a previous step that is somehow in the
    ///     future never chains. Same shape as CarpComboRules.WithinWindow.
    /// </summary>
    public static bool WithinWindow(TimeSpan previousStepTime, TimeSpan curTime, TimeSpan window)
    {
        if (window < TimeSpan.Zero)
            window = TimeSpan.Zero;

        var elapsed = curTime - previousStepTime;
        return elapsed >= TimeSpan.Zero && elapsed <= window;
    }

    /// <summary>
    ///     Advances the ordered Push -&gt; Shove -&gt; Grab chain by one recognized step.
    ///
    ///     Rules:
    ///     * From <see cref="JudoChainState.Empty"/>, only Push advances the chain (to PushLanded);
    ///       Shove/Grab are observation no-ops.
    ///     * From <see cref="JudoChainState.PushLanded"/>, a same-target in-window Shove advances to
    ///       PushShoveLanded. A same-target in-window Push refreshes the opener (re-times it). Anything
    ///       else (wrong step, stale window, different target) resets to Empty — except a fresh Push
    ///       always re-opens regardless of what broke the old chain.
    ///     * From <see cref="JudoChainState.PushShoveLanded"/>, a same-target in-window Grab THROWS —
    ///       returns true and consumes the chain back to Empty. Anything else resets the same way as
    ///       above.
    /// </summary>
    /// <param name="nextState">The chain state the caller should store for the next step.</param>
    /// <returns>True only when Grab lands as the third step and the takedown fires.</returns>
    public static bool Advance(
        JudoChainState state,
        TimeSpan previousStepTime,
        JudoStep incoming,
        TimeSpan curTime,
        TimeSpan window,
        bool sameTarget,
        out JudoChainState nextState)
    {
        var chains = sameTarget && WithinWindow(previousStepTime, curTime, window);

        switch (state)
        {
            case JudoChainState.PushLanded when chains && incoming == JudoStep.Shove:
                nextState = JudoChainState.PushShoveLanded;
                return false;

            case JudoChainState.PushShoveLanded when chains && incoming == JudoStep.Grab:
                nextState = JudoChainState.Empty;
                return true;

            default:
                // Every other case (wrong step, stale window, different target, or Empty seeing a
                // non-Push step): only a fresh Push can (re)open the chain.
                nextState = incoming == JudoStep.Push ? JudoChainState.PushLanded : JudoChainState.Empty;
                return false;
        }
    }

    /// <summary>
    ///     Whether the belt's throw is off cooldown at <paramref name="curTime"/>. Same shape as
    ///     CarpComboRules.ComboReady: default(TimeSpan) is immediately ready.
    /// </summary>
    public static bool ComboReady(TimeSpan curTime, TimeSpan nextComboTime)
    {
        return curTime >= nextComboTime;
    }

    /// <summary>
    ///     Computes when the next throw may fire. A negative cooldown (YAML misconfiguration) clamps
    ///     to zero rather than scheduling into the past.
    /// </summary>
    public static TimeSpan NextComboTime(TimeSpan curTime, TimeSpan cooldown)
    {
        return curTime + (cooldown > TimeSpan.Zero ? cooldown : TimeSpan.Zero);
    }
}
