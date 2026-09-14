using System;
using System.Collections.Generic;

namespace Content.Server._Solreign.Providence;

/// <summary>
///     Pure anti-fatigue tracking for Providence's first-shift/welcome-back beat (player-delight lane:
///     "this station remembers you"). Free of IoC/engine types so it is directly unit-testable
///     (Content.Tests/_Solreign/ProvidenceWelcomeGateTests.cs) — same
///     HashSet-keyed-by-<see cref="Guid"/>-reset-on-round-boundary idiom as
///     <c>ProvidenceCommiserationGate</c> and <c>SeasonLedgerSystem.EarlyDeath.cs</c>'s
///     <c>_earliestDeath</c> map.
///
///     Unlike the commiseration gate, there is no per-attempt coin flip here: every eligible spawn is
///     meant to greet the player, exactly once. The rule has a single half:
///       * Hard cap: at most one welcome per player per round (<see cref="CanFire"/>/
///         <see cref="MarkFired"/>), reset on every round start/restart. A player may respawn (death,
///         cryo return, ghost-role swap, etc.) multiple times in a round — only the first eligible
///         spawn gets the greeting.
/// </summary>
public sealed class ProvidenceWelcomeGate
{
    private readonly HashSet<Guid> _greetedThisRound = new();

    /// <summary>
    ///     True if <paramref name="player"/> has not yet been greeted this round. Does not itself record
    ///     anything — pair with <see cref="MarkFired"/> once the caller has committed to sending the
    ///     greeting.
    /// </summary>
    public bool CanFire(Guid player)
    {
        return !_greetedThisRound.Contains(player);
    }

    /// <summary>Records that <paramref name="player"/> has now received their one welcome for the round.</summary>
    public void MarkFired(Guid player)
    {
        _greetedThisRound.Add(player);
    }

    /// <summary>Clears all tracked players — call on round start/restart so state never leaks across rounds.</summary>
    public void Reset()
    {
        _greetedThisRound.Clear();
    }
}
