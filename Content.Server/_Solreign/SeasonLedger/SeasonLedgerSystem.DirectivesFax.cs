using System;
using System.Threading.Tasks;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     Thin Directives Fax delegation for <c>DirectivesFaxRuleSystem</c> — same access pattern as
///     <see cref="SeasonLedgerSystem.TryClaimSocialFirstAsync"/>: other systems reach the ledger through
///     this system rather than standing up a second <see cref="SeasonLedgerStore"/> against the same
///     SQLite file. See <c>SeasonLedgerStore.DirectivesFax.cs</c> for the streak semantics.
/// </summary>
public sealed partial class SeasonLedgerSystem
{
    /// <inheritdoc cref="SeasonLedgerStore.RecordDirectivesFaxOutcomeAsync"/>
    public Task<(int CurrentStreak, int BestStreak, bool IsNewBest)> RecordDirectivesFaxOutcomeAsync(Guid user, bool met, int roundId)
    {
        return _store.RecordDirectivesFaxOutcomeAsync(user, met, roundId);
    }

    /// <inheritdoc cref="SeasonLedgerStore.GetDirectivesFaxStreakAsync"/>
    public Task<(int CurrentStreak, int BestStreak)> GetDirectivesFaxStreakAsync(Guid user)
    {
        return _store.GetDirectivesFaxStreakAsync(user);
    }
}
