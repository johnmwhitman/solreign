using System;
using System.Collections.Generic;

namespace Content.Server._Solreign.Providence;

/// <summary>
///     Pure per-round in-flight guard for the authored first-death scene — the
///     <see cref="ProvidenceWelcomeGate"/> shape (HashSet keyed by account <see cref="Guid"/>, reset
///     on every round start/restart), unit-tested in isolation
///     (Content.Tests/_Solreign/FirstDeathRoundGuardTests.cs).
///
///     This guard is NOT the exactly-once mechanism — the atomic <c>first_death</c> claim row
///     (SeasonLedgerStore.FirstDeath.cs) is. It only stops double-ASYNC-DISPATCH inside one round:
///     the death handler marks the account here BEFORE its first await (same reason
///     <c>ProvidenceWelcomeSystem.OnPlayerSpawnComplete</c> marks its gate before the ledger read),
///     so a die → revive → die-again sequence in one round costs one DB round-trip, not two.
/// </summary>
public sealed class FirstDeathRoundGuard
{
    private readonly HashSet<Guid> _dispatchedThisRound = new();

    /// <summary>
    ///     True if no first-death dispatch is in flight (or completed) for <paramref name="player"/>
    ///     this round. Does not itself record anything — pair with <see cref="MarkFired"/> before the
    ///     first await.
    /// </summary>
    public bool CanFire(Guid player)
    {
        return !_dispatchedThisRound.Contains(player);
    }

    /// <summary>Records that a first-death dispatch is now in flight for <paramref name="player"/>.</summary>
    public void MarkFired(Guid player)
    {
        _dispatchedThisRound.Add(player);
    }

    /// <summary>Clears all tracked players — call on round start/restart so state never leaks across rounds.</summary>
    public void Reset()
    {
        _dispatchedThisRound.Clear();
    }
}
