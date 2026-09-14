using System;
using System.Collections.Generic;

namespace Content.Server._Solreign.PlayerDelight.Vista;

/// <summary>
///     Pure once-per-player-per-round tracking for the first-shift vista beat — free of IoC/engine
///     types so it is directly unit-testable (Content.Tests/_Solreign/VistaBeatGateTests.cs). Same
///     HashSet-keyed-by-<see cref="Guid"/>-reset-on-round-boundary idiom as
///     <c>ProvidenceWelcomeGate</c> and <c>ProvidenceCommiserationGate</c>.
///
///     The rule has a single half: a player's FIRST entry into any vista marker's proximity that
///     round delivers the one PROVIDENCE line; every later entry (re-walking the corridor, dying and
///     retracing, a second marker if one ever existed) is silent until the next round resets the set.
/// </summary>
public sealed class VistaBeatGate
{
    private readonly HashSet<Guid> _deliveredThisRound = new();

    /// <summary>
    ///     True if <paramref name="player"/> has not yet received their vista line this round. Does not
    ///     itself record anything — pair with <see cref="MarkDelivered"/> once the caller has committed
    ///     to sending the line.
    /// </summary>
    public bool CanFire(Guid player)
    {
        return !_deliveredThisRound.Contains(player);
    }

    /// <summary>Records that <paramref name="player"/> has now received their one vista line for the round.</summary>
    public void MarkDelivered(Guid player)
    {
        _deliveredThisRound.Add(player);
    }

    /// <summary>Clears all tracked players — call on round start/restart so state never leaks across rounds.</summary>
    public void Reset()
    {
        _deliveredThisRound.Clear();
    }
}
