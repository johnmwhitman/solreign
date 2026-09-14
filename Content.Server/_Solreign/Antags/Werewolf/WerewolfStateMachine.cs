namespace Content.Server._Solreign.Antags.Werewolf;

/// <summary>
///     Phases of an Unscheduled Fur Event. See docs/specs/2026-07-11-werewolf-vampire-spec.md §3.2
///     for the full transition diagram.
/// </summary>
public enum WerewolfState
{
    /// <summary>Normal crew form. Waiting for a Full Moon Window.</summary>
    Dormant = 0,

    /// <summary>Warning phase: fur specks, distant paperwork. Colleagues get a fair chance to react.</summary>
    Stirring = 1,

    /// <summary>Wolf form (polymorphed at build time). Time-boxed by duration AND the moon window.</summary>
    Transformed = 2,

    /// <summary>Reverting: slow, visible, vulnerable.</summary>
    Waning = 3,

    /// <summary>Terminal. The Follicle Stabilizer Draught (or the Sunrise Clause) worked.</summary>
    Cured = 4,
}

/// <summary>
///     Sanitized phase durations for the fur-event cycle. Values come from YAML via
///     <see cref="SolreignWerewolfComponent"/>, so negatives clamp to zero rather than throwing
///     (same doctrine as <c>PeriodicEffectTiming</c>).
/// </summary>
public readonly struct WerewolfTimings
{
    public readonly TimeSpan Stirring;
    public readonly TimeSpan Transformed;
    public readonly TimeSpan Waning;

    public WerewolfTimings(TimeSpan stirring, TimeSpan transformed, TimeSpan waning)
    {
        Stirring = stirring < TimeSpan.Zero ? TimeSpan.Zero : stirring;
        Transformed = transformed < TimeSpan.Zero ? TimeSpan.Zero : transformed;
        Waning = waning < TimeSpan.Zero ? TimeSpan.Zero : waning;
    }
}

/// <summary>
///     Pure, unit-testable transition function for the werewolf transformation cycle. No ECS, no I/O —
///     <see cref="SolreignWerewolfSystem"/> feeds it the clock, the moon window and the cure flag each
///     tick and applies whatever state comes back (mirrors the <c>RankRules</c>/<c>HotPotatoFuseMath</c>
///     pure-logic split; tested in Content.Tests/_Solreign/WerewolfStateMachineTests.cs).
///
///     Invariants (the anti-grief contract, spec §3.5):
///     - <see cref="WerewolfState.Cured"/> is terminal: no input ever leaves it.
///     - A cure applied while Dormant resolves instantly; applied mid-episode it resolves at the end of
///       Waning — the machine never yanks a wolf out of play without the visible revert window.
///     - Transformed is bounded by BOTH the transform duration and the moon window, whichever ends first.
/// </summary>
public static class WerewolfStateMachine
{
    /// <summary>
    ///     Computes the next state. Callers pass <paramref name="enteredAt"/> = game time the current
    ///     state began, <paramref name="moonActive"/> = is a Full Moon Window currently declared, and
    ///     <paramref name="cureApplied"/> = has a cure been administered at any point this episode.
    ///     Returns the state to be in now; callers reset <paramref name="enteredAt"/> whenever the
    ///     returned state differs from <paramref name="state"/>.
    /// </summary>
    public static WerewolfState Next(
        WerewolfState state,
        TimeSpan now,
        TimeSpan enteredAt,
        bool moonActive,
        bool cureApplied,
        in WerewolfTimings timings)
    {
        var elapsed = now - enteredAt;

        switch (state)
        {
            case WerewolfState.Cured:
                return WerewolfState.Cured;

            case WerewolfState.Dormant:
                if (cureApplied)
                    return WerewolfState.Cured;
                return moonActive ? WerewolfState.Stirring : WerewolfState.Dormant;

            case WerewolfState.Stirring:
                // Near-miss: the moon window closed before the fur finished arriving.
                if (!moonActive)
                    return cureApplied ? WerewolfState.Cured : WerewolfState.Dormant;
                return elapsed >= timings.Stirring ? WerewolfState.Transformed : WerewolfState.Stirring;

            case WerewolfState.Transformed:
                // Bounded by the moon window AND the per-window transform cap, whichever first.
                if (!moonActive || elapsed >= timings.Transformed)
                    return WerewolfState.Waning;
                return WerewolfState.Transformed;

            case WerewolfState.Waning:
                if (elapsed < timings.Waning)
                    return WerewolfState.Waning;
                return cureApplied ? WerewolfState.Cured : WerewolfState.Dormant;

            default:
                // Unknown states (corrupt save, future enum growth) fail safe to crew form.
                return WerewolfState.Dormant;
        }
    }

    /// <summary>True while the employee is in wolf form and the maul verb should exist.</summary>
    public static bool IsWolfForm(WerewolfState state)
        => state == WerewolfState.Transformed;

    /// <summary>
    ///     True while the polymorphed body should be shown (Transformed plus the visible Waning revert).
    /// </summary>
    public static bool ShowsFur(WerewolfState state)
        => state == WerewolfState.Transformed || state == WerewolfState.Waning;
}
