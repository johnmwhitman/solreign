using System;
using System.Collections.Generic;

namespace Content.Server._Solreign.Providence;

/// <summary>
///     One banked first-death memorial awaiting its crypt backfill POST (FD-W3.5). The report is
///     composed at enqueue time from the persisted claim row (the row IS the snapshot — composed
///     text can never disagree with what the ledger recorded); the account id rides along so the
///     dispatcher can stamp <c>crypt_reported</c> after a successful hand-off.
/// </summary>
public readonly record struct FirstDeathCryptBackfillItem(Guid AccountId, FirstDeathCryptReport Report);

/// <summary>
///     Pure (engine-free) paced dispatch queue for the first-death plaque backfill — the
///     <see cref="FirstDeathBeatQueue"/> idiom (caller supplies "now" from its own
///     <c>IGameTiming</c>; entries are removed BEFORE being handed back), plus a pacing window:
///     at most ONE item is released per <c>spacing</c> interval, so a server sitting on many
///     banked memorials (every v13.3-era first death) trickles them through the
///     <c>crypt_first_death</c> rate-limit channel (1 per 750ms,
///     <c>DirectorChannel.MinRequestInterval</c>) instead of bursting into it and losing all but
///     the first. FIFO: the queue is loaded oldest-death-first and dispatches in that order.
///
///     Cleared on round start/restart before a fresh scan reloads it, so an item can never go
///     stale across a round boundary (the persistent <c>crypt_reported</c> flag, not this queue,
///     is the exactly-once surface — a dropped queue entry is re-scanned next round).
///     Unit-tested in isolation (Content.Tests/_Solreign/FirstDeathCryptBackfillTests.cs).
/// </summary>
public sealed class FirstDeathCryptBackfillQueue
{
    private readonly Queue<FirstDeathCryptBackfillItem> _pending = new();
    private TimeSpan _nextDispatchAt = TimeSpan.Zero;

    /// <summary>Number of not-yet-dispatched entries — exposed for tests and the pump's early-out.</summary>
    public int Count => _pending.Count;

    public void Enqueue(FirstDeathCryptBackfillItem item)
    {
        _pending.Enqueue(item);
    }

    /// <summary>
    ///     Releases at most one item, and only when the pacing window has elapsed. On success the
    ///     next window opens at <paramref name="now"/> + <paramref name="spacing"/> — the item is
    ///     dequeued BEFORE being handed back, so the caller's side effects can never release the
    ///     same entry twice.
    /// </summary>
    public bool TryDequeueDue(TimeSpan now, TimeSpan spacing, out FirstDeathCryptBackfillItem item)
    {
        item = default;

        if (_pending.Count == 0)
            return false;

        if (now < _nextDispatchAt)
            return false;

        item = _pending.Dequeue();
        _nextDispatchAt = now + spacing;
        return true;
    }

    /// <summary>Clears all pending entries and re-opens the pacing window — call on round
    /// start/restart so state never leaks across rounds.</summary>
    public void Clear()
    {
        _pending.Clear();
        _nextDispatchAt = TimeSpan.Zero;
    }
}
