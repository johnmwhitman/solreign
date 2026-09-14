using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     Thin STATION-LIBRARY delegations for <c>SolreignLibrarySystem</c> — the exact
///     <see cref="SeasonLedgerSystem"/>.Mark.cs / .Noticeboards.cs access pattern: reach the ledger
///     through this system rather than standing up a second <see cref="SeasonLedgerStore"/> against
///     the same SQLite file. See <c>SeasonLedgerStore.LibraryWorks.cs</c> for the atomic write-time
///     rule and every query.
/// </summary>
public sealed partial class SeasonLedgerSystem
{
    /// <inheritdoc cref="SeasonLedgerStore.TryPostWorkAsync"/>
    public Task<(bool Posted, int Id, LibraryPostRejection? Rejection)> TryPostLibraryWorkAsync(
        string archiveId,
        Guid author,
        bool isProvidence,
        string authorDisplay,
        string title,
        string body,
        int roundId,
        string nowUtc,
        int providenceSeedTarget = int.MaxValue)
    {
        return _store.TryPostWorkAsync(archiveId, author, isProvidence, authorDisplay, title, body, roundId, nowUtc, providenceSeedTarget);
    }

    /// <inheritdoc cref="SeasonLedgerStore.GetRecentWorksAsync"/>
    public Task<List<LibraryWorkRecord>> GetRecentLibraryWorksAsync(string archiveId, int limit)
    {
        return _store.GetRecentWorksAsync(archiveId, limit);
    }

    /// <inheritdoc cref="SeasonLedgerStore.GetProvidenceWorkCountAsync"/>
    public Task<int> GetProvidenceLibraryWorkCountAsync(string archiveId)
    {
        return _store.GetProvidenceWorkCountAsync(archiveId);
    }

    /// <inheritdoc cref="SeasonLedgerStore.HideWorkAsync"/>
    public Task<bool> HideLibraryWorkAsync(int workId, string reason, string nowUtc)
    {
        return _store.HideWorkAsync(workId, reason, nowUtc);
    }

    /// <inheritdoc cref="SeasonLedgerStore.UnhideWorkAsync"/>
    public Task<bool> UnhideLibraryWorkAsync(int workId, string nowUtc)
    {
        return _store.UnhideWorkAsync(workId, nowUtc);
    }
}
