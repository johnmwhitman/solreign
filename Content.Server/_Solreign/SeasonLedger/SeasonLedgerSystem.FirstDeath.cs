using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     Thin first-death delegations for <c>ProvidenceFirstDeathSystem</c> — same access pattern as
///     <see cref="SeasonLedgerSystem.GetCareerStatsAsync"/> (exposed for ProvidenceWelcomeSystem):
///     other systems reach the ledger through this system rather than standing up a second
///     <see cref="SeasonLedgerStore"/> against the same SQLite file. See
///     <c>SeasonLedgerStore.FirstDeath.cs</c> for the exactly-once semantics.
/// </summary>
public sealed partial class SeasonLedgerSystem
{
    /// <inheritdoc cref="SeasonLedgerStore.TryClaimFirstDeathAsync"/>
    public Task<bool> TryClaimFirstDeathAsync(
        Guid user,
        int roundId,
        string characterName,
        string cause,
        int toursAtDeath,
        string titleAtDeath,
        string epitaphId)
    {
        return _store.TryClaimFirstDeathAsync(user, roundId, characterName, cause, toursAtDeath, titleAtDeath, epitaphId);
    }

    /// <inheritdoc cref="SeasonLedgerStore.GetFirstDeathAsync"/>
    public Task<FirstDeathRecord?> GetFirstDeathAsync(Guid user)
    {
        return _store.GetFirstDeathAsync(user);
    }

    /// <inheritdoc cref="SeasonLedgerStore.TryMarkRehireShownAsync"/>
    public Task<bool> TryMarkRehireShownAsync(Guid user)
    {
        return _store.TryMarkRehireShownAsync(user);
    }

    /// <inheritdoc cref="SeasonLedgerStore.TryMarkCryptReportedAsync"/>
    public Task<bool> TryMarkFirstDeathCryptReportedAsync(Guid user)
    {
        return _store.TryMarkCryptReportedAsync(user);
    }

    /// <inheritdoc cref="SeasonLedgerStore.GetCryptUnreportedFirstDeathsAsync"/>
    public Task<List<FirstDeathRecord>> GetCryptUnreportedFirstDeathsAsync()
    {
        return _store.GetCryptUnreportedFirstDeathsAsync();
    }

    /// <inheritdoc cref="SeasonLedgerStore.GetAllFirstDeathsAsync"/>
    public Task<List<FirstDeathRecord>> GetAllFirstDeathsAsync(int limit)
    {
        return _store.GetAllFirstDeathsAsync(limit);
    }

    /// <inheritdoc cref="SeasonLedgerStore.SetFirstDeathDiedAtUtcForTests"/>
    internal Task SetFirstDeathDiedAtUtcForTests(Guid user, string diedAtUtc)
    {
        return _store.SetFirstDeathDiedAtUtcForTests(user, diedAtUtc);
    }
}
