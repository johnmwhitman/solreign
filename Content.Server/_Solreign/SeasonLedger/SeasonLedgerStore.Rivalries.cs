using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Content.Server._Solreign.SeasonLedger;

public sealed partial class SeasonLedgerStore
{
    public async Task RecordRivalryEventAsync(Guid attacker, Guid victim)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO rivalry_events (attacker_guid, victim_guid, event_utc)
                VALUES ($attacker, $victim, $utc);
                """;
            cmd.Parameters.AddWithValue("$attacker", attacker.ToString());
            cmd.Parameters.AddWithValue("$victim", victim.ToString());
            cmd.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _lock.Release();
        }
    }
}
