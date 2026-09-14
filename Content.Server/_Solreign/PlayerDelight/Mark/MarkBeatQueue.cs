using System;
using System.Collections.Generic;

namespace Content.Server._Solreign.PlayerDelight.Mark;

/// <summary>Which private Mark beat a pending entry delivers when it comes due (spec §3.5).</summary>
public enum MarkBeatKind : byte
{
    /// <summary>C7 — the once-EVER first-return nudge ("something you left is still growing").</summary>
    FirstReturnNudge,

    /// <summary>C2 — the growth return line, at most once per stage advance ("You are welcome.").</summary>
    GrowthReturn,
}

/// <summary>
///     One scheduled Mark beat. Deliberately does NOT store an <c>ICommonSession</c> or trust any
///     entity captured at schedule time — sessions die on disconnect, the account
///     <see cref="AccountId"/> (a stable <c>NetUserId.UserId</c>) does not, and the caller must
///     re-resolve the live session/entity at fire time with the full ghost/dead/deleted/CVar guard
///     chain (the <c>ProvidenceFirstShiftPersonalPending</c> contract). The beat's TEXT is composed
///     at schedule time (snapshotted from the ledger row at the spawn moment and immutable). All
///     Mark beats are PRIVATE — popup + private chat, never PA — so there is no public-text field.
/// </summary>
public readonly record struct MarkPendingBeat(
    MarkBeatKind Kind,
    Guid AccountId,
    string PrivateText,
    TimeSpan FireAt);

/// <summary>
///     Pure (engine-free) scheduling queue for the Mark return beats — the THIRD instance of the
///     <c>ProvidenceFirstShiftPersonalQueue</c> idiom (see <c>FirstDeathBeatQueue</c> for the
///     second): drain-before-handing-back so a due entry can never be drained twice; the caller
///     supplies "now" from its own <c>IGameTiming</c>. Unit-tested in isolation
///     (Content.Tests/_Solreign/MarkBeatQueueTests.cs).
///
///     Cleared on every round start/restart so a beat scheduled near a round's end can never bleed
///     into the next round's lobby. (The persistent mark row is unaffected — the write-first stamps
///     <c>nudge_shown</c>/<c>last_visit_*</c> mean a swallowed beat is a lost cosmetic line, never a
///     duplicated one.)
/// </summary>
public sealed class MarkBeatQueue
{
    private readonly List<MarkPendingBeat> _pending = new();

    /// <summary>Number of not-yet-fired entries — exposed for tests, not used by production logic.</summary>
    public int Count => _pending.Count;

    /// <summary>Number of not-yet-fired entries of <paramref name="kind"/> — test visibility only.</summary>
    public int CountOf(MarkBeatKind kind)
    {
        var count = 0;
        foreach (var pending in _pending)
        {
            if (pending.Kind == kind)
                count++;
        }

        return count;
    }

    public void Schedule(MarkPendingBeat beat)
    {
        _pending.Add(beat);
    }

    /// <summary>
    ///     Removes and returns every entry due at or before <paramref name="now"/>. Entries are
    ///     removed BEFORE being handed back, so the caller's side effects can never cause a due entry
    ///     to be drained twice even if firing one somehow re-entered this method.
    /// </summary>
    public List<MarkPendingBeat> DrainDue(TimeSpan now)
    {
        var due = new List<MarkPendingBeat>();

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
