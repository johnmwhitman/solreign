using System;
using System.Collections.Generic;
using Robust.Shared.GameObjects;

namespace Content.Server._Solreign.Providence;

/// <summary>
///     One scheduled delayed-personal-beat entry (see <see cref="ProvidenceFirstShiftPersonalQueue"/>).
///     Deliberately does NOT store an <c>ICommonSession</c> — sessions die on disconnect, the account
///     <see cref="AccountId"/> (a stable <c>NetUserId.UserId</c>) does not, and the caller must always
///     re-resolve the live session/entity at fire time rather than trust anything captured here.
/// </summary>
public readonly record struct ProvidenceFirstShiftPersonalPending(Guid AccountId, EntityUid SpawnMob, TimeSpan FireAt);

/// <summary>
///     Pure (engine-free) scheduling queue for Providence's delayed, personally-addressed first-shift
///     beat — same "thin ECS glue over a pure, directly-unit-testable class" split as
///     <see cref="ProvidenceWelcomeGate"/>/<see cref="ProvidenceCommiserationGate"/>. Free of
///     IoC/timing-manager dependencies: the caller (<c>ProvidenceWelcomeSystem</c>) supplies "now" from
///     its own <c>IGameTiming</c> each tick, this class only does the pure due/not-due bookkeeping.
///
///     Cleared on every round start/restart (same call sites as <see cref="ProvidenceWelcomeGate.Reset"/>)
///     so a pending beat scheduled near a round's end can never bleed into the next round's lobby or a
///     freshly restarted round.
/// </summary>
public sealed class ProvidenceFirstShiftPersonalQueue
{
    private readonly List<ProvidenceFirstShiftPersonalPending> _pending = new();

    /// <summary>Number of not-yet-fired entries — exposed for tests, not used by production logic.</summary>
    public int Count => _pending.Count;

    public void Schedule(Guid accountId, EntityUid spawnMob, TimeSpan fireAt)
    {
        _pending.Add(new ProvidenceFirstShiftPersonalPending(accountId, spawnMob, fireAt));
    }

    /// <summary>
    ///     Removes and returns every entry whose <see cref="ProvidenceFirstShiftPersonalPending.FireAt"/>
    ///     is at or before <paramref name="now"/>. Entries are removed from the queue BEFORE being
    ///     handed back to the caller — the caller's own side effects (popup/audio/screen-fx) can never
    ///     cause a due entry to be drained twice, even if firing one entry somehow re-entered this method.
    /// </summary>
    public List<ProvidenceFirstShiftPersonalPending> DrainDue(TimeSpan now)
    {
        var due = new List<ProvidenceFirstShiftPersonalPending>();

        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            if (_pending[i].FireAt > now)
                continue;

            due.Add(_pending[i]);
            _pending.RemoveAt(i);
        }

        return due;
    }

    /// <summary>Clears all pending entries — call on round start/restart so state never leaks across rounds.</summary>
    public void Clear()
    {
        _pending.Clear();
    }
}
