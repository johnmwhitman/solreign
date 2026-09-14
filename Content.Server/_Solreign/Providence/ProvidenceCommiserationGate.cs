using System;
using System.Collections.Generic;

namespace Content.Server._Solreign.Providence;

/// <summary>
///     Pure anti-fatigue math for Providence's death commiseration line (roadmap D2.2: "Providence
///     death commiseration wired with anti-fatigue rules"). Free of IoC/engine types so both halves of
///     the rule are directly unit-testable (Content.Tests/_Solreign/ProvidenceCommiserationGateTests.cs)
///     — mirrors <c>FeedbackRateLimiter</c>'s per-player tracking idiom (keyed by <see cref="Guid"/>,
///     the same underlying type <c>NetUserId</c> implicitly converts to/from) and
///     <c>PeriodicEffectTiming.NextFireTime</c>'s "caller supplies the random sample" idiom for the
///     probability half.
///
///     The rule has two independent halves, both required:
///       1. Hard cap: at most one SUCCESSFUL commiseration per player per round
///          (<see cref="CanFire"/>/<see cref="MarkFired"/>, a HashSet reset on round start/restart —
///          same Dictionary-then-Clear-on-round-boundary shape as
///          <c>SeasonLedgerSystem.EarlyDeath.cs</c>'s <c>_earliestDeath</c> map).
///       2. Per-death coin flip (<see cref="ShouldFire"/>) so most individual deaths do not trigger the
///          line even before the cap is reached — it should read as occasional and special, not a
///          guaranteed per-round freebie the moment someone first dies.
///     A miss on the coin flip does NOT consume the per-round slot: only <see cref="MarkFired"/>
///     (called by the system after a successful flip) closes out the round for that player, so a player
///     who dies multiple times keeps rolling until they either get the line once or the round ends.
/// </summary>
public sealed class ProvidenceCommiserationGate
{
    private readonly HashSet<Guid> _firedThisRound = new();

    /// <summary>
    ///     True if <paramref name="player"/> has not yet received a successful commiseration this round.
    ///     Does not itself record anything — pair with <see cref="MarkFired"/> once the caller has also
    ///     passed <see cref="ShouldFire"/>, so a probability miss never burns the player's one-per-round
    ///     slot.
    /// </summary>
    public bool CanFire(Guid player)
    {
        return !_firedThisRound.Contains(player);
    }

    /// <summary>Records that <paramref name="player"/> has now received their one commiseration for the round.</summary>
    public void MarkFired(Guid player)
    {
        _firedThisRound.Add(player);
    }

    /// <summary>Clears all tracked players — call on round start/restart so state never leaks across rounds.</summary>
    public void Reset()
    {
        _firedThisRound.Clear();
    }

    /// <summary>
    ///     Pure probability check: true if <paramref name="roll"/> (a uniform sample in [0, 1], e.g.
    ///     <c>IRobustRandom.NextDouble()</c>) falls under <paramref name="chance"/>. <paramref name="chance"/>
    ///     is clamped into [0, 1] so a misconfigured CVar can't invert the odds or throw — same
    ///     defensive-clamp idiom as <c>PeriodicEffectTiming.NextFireTime</c>.
    /// </summary>
    public static bool ShouldFire(double roll, double chance)
    {
        var clampedChance = Math.Clamp(chance, 0d, 1d);
        return roll < clampedChance;
    }
}
