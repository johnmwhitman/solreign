using System;
using System.Threading.Tasks;

namespace Content.Server._Solreign.SeasonLedger;

public sealed partial class SeasonLedgerSystem
{
    public Task RecordRivalryEventAsync(Guid attacker, Guid victim)
    {
        return _store.RecordRivalryEventAsync(attacker, victim);
    }
}
