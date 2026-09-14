using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     Thin Noticeboard delegations for <c>SolreignNoticeboardSystem</c> — the exact
///     <see cref="SeasonLedgerSystem"/>.Mark.cs access pattern: reach the ledger through this
///     system rather than standing up a second <see cref="SeasonLedgerStore"/> against the same
///     SQLite file. See <c>SeasonLedgerStore.Noticeboards.cs</c> for the atomic write-time rules.
/// </summary>
public sealed partial class SeasonLedgerSystem
{
    /// <inheritdoc cref="SeasonLedgerStore.TryPostNoteAsync"/>
    public Task<(bool Posted, int Id, NoticeboardPostRejection? Rejection)> TryPostNoticeboardNoteAsync(
        string boardId,
        Guid user,
        bool isProvidence,
        string authorDisplay,
        string body,
        string nowUtc,
        int expiryHours,
        int boardCapacity,
        int providenceCapacity,
        int cooldownHours)
    {
        return _store.TryPostNoteAsync(
            boardId, user, isProvidence, authorDisplay, body, nowUtc,
            expiryHours, boardCapacity, providenceCapacity, cooldownHours);
    }

    /// <inheritdoc cref="SeasonLedgerStore.GetActiveNotesAsync"/>
    public Task<List<NoticeboardNoteRecord>> GetActiveNoticeboardNotesAsync(string boardId, string nowUtc)
    {
        return _store.GetActiveNotesAsync(boardId, nowUtc);
    }

    /// <inheritdoc cref="SeasonLedgerStore.GetActiveProvidenceCountAsync"/>
    public Task<int> GetActiveNoticeboardProvidenceCountAsync(string boardId, string nowUtc)
    {
        return _store.GetActiveProvidenceCountAsync(boardId, nowUtc);
    }

    /// <inheritdoc cref="SeasonLedgerStore.HideNoteAsync"/>
    public Task<bool> HideNoticeboardNoteAsync(int noteId, string reason, string nowUtc)
    {
        return _store.HideNoteAsync(noteId, reason, nowUtc);
    }

    /// <inheritdoc cref="SeasonLedgerStore.UnhideNoteAsync"/>
    public Task<bool> UnhideNoticeboardNoteAsync(int noteId, string nowUtc)
    {
        return _store.UnhideNoteAsync(noteId, nowUtc);
    }

    /// <inheritdoc cref="SeasonLedgerStore.SweepExpiredNotesAsync"/>
    public Task<int> SweepExpiredNoticeboardNotesAsync(string nowUtc)
    {
        return _store.SweepExpiredNotesAsync(nowUtc);
    }

    /// <inheritdoc cref="SeasonLedgerStore.SetNoteExpiresUtcForTests"/>
    internal Task SetNoticeboardNoteExpiresUtcForTests(int noteId, string expiresUtc)
    {
        return _store.SetNoteExpiresUtcForTests(noteId, expiresUtc);
    }

    /// <inheritdoc cref="SeasonLedgerStore.SetAuthorLastPostUtcForTests"/>
    internal Task SetNoticeboardAuthorLastPostUtcForTests(Guid user, string lastPostUtc)
    {
        return _store.SetAuthorLastPostUtcForTests(user, lastPostUtc);
    }
}
