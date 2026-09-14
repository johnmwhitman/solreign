using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     Thin social-firsts delegations for the Social systems
///     (<c>SolreignSocialFirstsSystem</c> / <c>SolreignWingmatePromptSystem</c>) — same access
///     pattern as <see cref="SeasonLedgerSystem.TryClaimFirstDeathAsync"/>: other systems reach the
///     ledger through this system rather than standing up a second <see cref="SeasonLedgerStore"/>
///     against the same SQLite file. See <c>SeasonLedgerStore.SocialFirsts.cs</c> for the
///     exactly-once semantics.
/// </summary>
public sealed partial class SeasonLedgerSystem
{
    /// <inheritdoc cref="SeasonLedgerStore.TryClaimSocialFirstAsync"/>
    public Task<bool> TryClaimSocialFirstAsync(Guid user, string flagId, int roundId)
    {
        return _store.TryClaimSocialFirstAsync(user, flagId, roundId);
    }

    /// <inheritdoc cref="SeasonLedgerStore.GetSocialFirstFlagsAsync"/>
    public Task<IReadOnlyList<string>> GetSocialFirstFlagsAsync(Guid user)
    {
        return _store.GetSocialFirstFlagsAsync(user);
    }
}
