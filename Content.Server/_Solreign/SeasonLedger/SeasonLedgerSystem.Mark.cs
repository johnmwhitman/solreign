using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     Thin Mark delegations for <c>SolreignMarkGardenSystem</c> (MG-W2) and the MG-W4 return
///     beats — the exact <see cref="SeasonLedgerSystem"/>.FirstDeath.cs access pattern: other
///     systems reach the ledger through this system rather than standing up a second
///     <see cref="SeasonLedgerStore"/> against the same SQLite file. See
///     <c>SeasonLedgerStore.Mark.cs</c> for the exactly-once semantics.
/// </summary>
public sealed partial class SeasonLedgerSystem
{
    /// <inheritdoc cref="SeasonLedgerStore.TryClaimMarkAsync"/>
    public Task<(bool Claimed, int Slot)> TryClaimMarkAsync(
        Guid user,
        string kind,
        int roundId,
        string mapId,
        string characterName,
        int toursAtPlanting)
    {
        return _store.TryClaimMarkAsync(user, kind, roundId, mapId, characterName, toursAtPlanting);
    }

    /// <inheritdoc cref="SeasonLedgerStore.GetMarkAsync"/>
    public Task<MarkRecord?> GetMarkAsync(Guid user)
    {
        return _store.GetMarkAsync(user);
    }

    /// <inheritdoc cref="SeasonLedgerStore.GetAllMarksAsync"/>
    public Task<List<MarkRecord>> GetAllMarksAsync(int limit)
    {
        return _store.GetAllMarksAsync(limit);
    }

    /// <inheritdoc cref="SeasonLedgerStore.TryMarkNudgeShownAsync"/>
    public Task<bool> TryMarkNudgeShownAsync(Guid user)
    {
        return _store.TryMarkNudgeShownAsync(user);
    }

    /// <inheritdoc cref="SeasonLedgerStore.TryRecordVisitAsync"/>
    public Task<bool> TryRecordVisitAsync(Guid user, int stage, string visitUtc)
    {
        return _store.TryRecordVisitAsync(user, stage, visitUtc);
    }

    /// <inheritdoc cref="SeasonLedgerStore.SetMarkPlantedUtcForTests"/>
    internal Task SetMarkPlantedUtcForTests(Guid user, string plantedUtc)
    {
        return _store.SetMarkPlantedUtcForTests(user, plantedUtc);
    }

    /// <inheritdoc cref="SeasonLedgerStore.ClearAllMarksForTests"/>
    internal Task ClearAllMarksForTests()
    {
        return _store.ClearAllMarksForTests();
    }
}
