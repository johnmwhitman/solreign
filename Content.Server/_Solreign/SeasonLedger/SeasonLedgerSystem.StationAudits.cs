using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     Thin delegation surface for <c>StationAuditSystem</c> (v14 wave-1 #3) — the same
///     "reach into the ledger through the already-running <see cref="SeasonLedgerSystem"/> rather
///     than a second system standing up its own <see cref="SeasonLedgerStore"/> against the same
///     SQLite file" idiom <see cref="GetCareerStatsAsync"/>'s own doc comment establishes for
///     <c>ProvidenceWelcomeSystem</c>. No round-scoped state lives here — Station Audits owns and
///     snapshots its own per-round trackers before calling this, exactly once, per round end.
/// </summary>
public sealed partial class SeasonLedgerSystem
{
    /// <summary>Appends one shift's compact Station Audit summary row. See <see cref="StationAuditRecord"/>.</summary>
    public Task<bool> AppendStationAuditAsync(StationAuditRecord record) => _store.AppendStationAuditAsync(record);

    /// <summary>Station Lifetime Audit Count (nyanopark-019 fold) — see
    /// <see cref="SeasonLedgerStore.GetStationAuditLifetimeCountAsync"/>.</summary>
    public Task<int> GetStationAuditLifetimeCountAsync() => _store.GetStationAuditLifetimeCountAsync();

    /// <summary>Most recent shift summaries, newest first — see
    /// <see cref="SeasonLedgerStore.GetRecentStationAuditsAsync"/>. Consumed by the Shift Archive board.</summary>
    public Task<List<StationAuditRecord>> GetRecentStationAuditsAsync(int limit = 20)
        => _store.GetRecentStationAuditsAsync(limit);

    /// <summary>Completed-contract counts per round — see
    /// <see cref="SeasonLedgerStore.GetContractCountsForRoundsAsync"/>. Consumed by the Shift Archive board.</summary>
    public Task<Dictionary<int, int>> GetContractCountsForRoundsAsync(IReadOnlyList<int> roundIds)
        => _store.GetContractCountsForRoundsAsync(roundIds);

    /// <summary>
    ///     Public delegation for <see cref="SeasonLedgerStore.AwardHrPointsAsync"/> — the store's own
    ///     method was previously only ever called from inside this system (the title-earned bonus, see
    ///     <c>SeasonLedgerSystem.cs</c>). The inspection layer's checkpoint Commendation consequence
    ///     (<c>StationAuditSystem.Inspection.cs</c>) needs the same already-shipped, always-cumulative
    ///     payout from an external system, so it is exposed here rather than duplicated.
    /// </summary>
    public Task AwardHrPointsAsync(Guid user, int points) => _store.AwardHrPointsAsync(user, points);
}
