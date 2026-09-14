using System;
using System.Threading.Tasks;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     Pet companionship progression ledger integration (SR-W-047).
///     Records pet taming, follow bonds, and grooming milestones in the Season Ledger without blocking core gameplay loops.
/// </summary>
public sealed partial class SeasonLedgerSystem
{
    /// <summary>
    ///     Records a pet companionship milestone stamp for a player. Non-blocking async operation.
    /// </summary>
    public async Task<bool> RecordPetCompanionshipStampAsync(Guid user, string petId, string stampType)
    {
        var flagId = $"pet_stamp_{petId}_{stampType}";
        return await _store.TryClaimSocialFirstAsync(user, flagId, 0);
    }
}
