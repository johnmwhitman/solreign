using System;
using System.Collections.Generic;

namespace Content.Server._Solreign.Providence;

/// <summary>Which authored first-death beat a pending entry delivers when it comes due.</summary>
public enum FirstDeathBeatKind : byte
{
    /// <summary>The T+4s public scene: station-wide eulogy + voice sting + private ghost line.</summary>
    DeathScene,

    /// <summary>The next-spawn private rehire beat: popup + private chat line. No PA — intimacy.</summary>
    Rehire,
}

/// <summary>
///     One scheduled first-death beat. Deliberately does NOT store an <c>ICommonSession</c> or trust
///     any entity captured at schedule time — sessions die on disconnect, the account
///     <see cref="AccountId"/> (a stable <c>NetUserId.UserId</c>) does not, and the caller must
///     re-resolve the live session/entity at fire time (the
///     <see cref="ProvidenceFirstShiftPersonalPending"/> contract). The beat's TEXT is composed at
///     schedule time (it was snapshotted from game state at the death moment and is immutable).
/// </summary>
public readonly record struct FirstDeathPendingBeat(
    FirstDeathBeatKind Kind,
    Guid AccountId,
    string PublicText,
    string PrivateText,
    TimeSpan FireAt);

/// <summary>
///     Pure (engine-free) scheduling queue for the authored first-death beats — the exact
///     <see cref="ProvidenceFirstShiftPersonalQueue"/> idiom (drain-before-handing-back so a due
///     entry can never be drained twice; caller supplies "now" from its own <c>IGameTiming</c>),
///     unit-tested in isolation (Content.Tests/_Solreign/FirstDeathBeatQueueTests.cs).
///
///     Cleared on every round start/restart so a beat scheduled near a round's end can never bleed
///     into the next round's lobby or a freshly restarted round. (The persistent claim row is
///     unaffected — a death-scene beat swallowed by a round boundary is a lost cosmetic beat, never a
///     duplicated one; the rehire beat re-arms from the DB on the player's next spawn.)
/// </summary>
public sealed class FirstDeathBeatQueue
{
    private readonly List<FirstDeathPendingBeat> _pending = new();

    /// <summary>Number of not-yet-fired entries — exposed for tests, not used by production logic.</summary>
    public int Count => _pending.Count;

    /// <summary>Number of not-yet-fired entries of <paramref name="kind"/> — test visibility only.</summary>
    public int CountOf(FirstDeathBeatKind kind)
    {
        var count = 0;
        foreach (var pending in _pending)
        {
            if (pending.Kind == kind)
                count++;
        }

        return count;
    }

    public void Schedule(FirstDeathPendingBeat beat)
    {
        _pending.Add(beat);
    }

    /// <summary>
    ///     Removes and returns every entry due at or before <paramref name="now"/>. Entries are
    ///     removed BEFORE being handed back, so the caller's side effects can never cause a due entry
    ///     to be drained twice even if firing one somehow re-entered this method.
    /// </summary>
    public List<FirstDeathPendingBeat> DrainDue(TimeSpan now)
    {
        var due = new List<FirstDeathPendingBeat>();

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
