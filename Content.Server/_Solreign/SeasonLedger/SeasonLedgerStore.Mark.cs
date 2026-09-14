using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     One account's Mark claim row, as persisted in the <c>mark</c> table.
///     <c>Kind</c> is the ledger string form of
///     <c>Content.Server._Solreign.PlayerDelight.Mark.MarkKind</c> (SAPLING | LAMP | NAMEPLATE —
///     a closed set, spec §4.1); <c>CharacterName</c> renders ONLY on owner-private surfaces, never
///     on the public object (spec rail 5). <c>PlantedUtc</c>/<c>LastVisitUtc</c> are ISO-8601 ("o")
///     strings, the store's house timestamp format.
/// </summary>
public sealed record MarkRecord(
    Guid User,
    string Kind,
    int PlantedRoundId,
    string PlantedUtc,
    string PlantedMap,
    string CharacterName,
    int ToursAtPlanting,
    int SlotIndex,
    bool NudgeShown,
    string LastVisitUtc,
    int LastVisitStage);

/// <summary>
///     "The Mark" persistence (docs/specs/MARK-SPEC-2026-07-17-DRAFT.md §3.1): one persistent
///     physical keepsake per ACCOUNT, ever, planted at the Continuity Garden. The <c>mark</c> table
///     (schema battery in SeasonLedgerStore.cs) is the single source of truth — round wipes cost
///     nothing because the entity is a per-round projection of this row, never the other way around.
///
///     Exactly-once guarantees, all inherited from the shipped first-death idioms:
///       * <see cref="TryClaimMarkAsync"/> is an atomic INSERT-if-absent (the
///         <see cref="TryClaimFirstDeathAsync"/> copy) that ALSO claims the next dense
///         <c>slot_index</c> in the same statement — the slot subquery and the insert execute inside
///         one BEGIN-IMMEDIATE transaction, so claim+slot stay atomic even across separate store
///         instances on the same file (spec §3.1's fold-the-count-into-the-INSERT resolution).
///       * <see cref="TryMarkNudgeShownAsync"/> / <see cref="TryRecordVisitAsync"/> are conditional
///         UPDATEs with rowcount guards (the <see cref="TryMarkRehireShownAsync"/> copy), written
///         BEFORE their beats dispatch — a racing double-spawn delivers at most one line, and a
///         disconnect mid-beat swallows a cosmetic line, never duplicates it.
///
///     Deliberately NOT season-scoped (the first_death argument verbatim): a mark is a career
///     event, keyed by <c>user_id</c> alone. A season bump does not uproot anyone.
/// </summary>
public sealed partial class SeasonLedgerStore
{
    /// <summary>
    ///     Atomically claims the once-per-account-EVER mark slot. Returns <c>(true, slot)</c> exactly
    ///     once per account, for the caller that won the INSERT — <c>slot</c> is the dense, planting-
    ///     ordered <c>slot_index</c> claimed in the same transaction. Every later (or racing-and-lost)
    ///     call returns <c>(false, -1)</c>. Callers must only spawn/confirm on <c>true</c>.
    /// </summary>
    public async Task<(bool Claimed, int Slot)> TryClaimMarkAsync(
        Guid user,
        string kind,
        int roundId,
        string mapId,
        string characterName,
        int toursAtPlanting)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();

            // BEGIN IMMEDIATE (deferred: false — the AddRoundRecordAsync idiom) takes the write
            // reservation BEFORE the slot subquery evaluates, so two processes racing distinct-user
            // claims serialize and can never both read the same COUNT(*) — the loser waits, then
            // sees the winner's committed row and claims the next slot.
            await using var tx = conn.BeginTransaction(deferred: false);

            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO mark
                        (user_id, kind, planted_round_id, planted_utc, planted_map, character_name,
                         tours_at_planting, slot_index, nudge_shown, last_visit_utc, last_visit_stage)
                    VALUES ($uid, $kind, $rid, $utc, $map, $name, $tours,
                            (SELECT COUNT(*) FROM mark), 0, $utc, 0)
                    ON CONFLICT(user_id) DO NOTHING;
                    """;
                cmd.Parameters.AddWithValue("$uid", user.ToString());
                cmd.Parameters.AddWithValue("$kind", kind);
                cmd.Parameters.AddWithValue("$rid", roundId);
                cmd.Parameters.AddWithValue("$map", mapId);
                cmd.Parameters.AddWithValue("$name", characterName);
                cmd.Parameters.AddWithValue("$tours", toursAtPlanting);
                cmd.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("o"));

                if (await cmd.ExecuteNonQueryAsync() != 1)
                {
                    await tx.CommitAsync();
                    return (false, -1);
                }
            }

            int slot;
            await using (var read = conn.CreateCommand())
            {
                read.Transaction = tx;
                read.CommandText = "SELECT slot_index FROM mark WHERE user_id = $uid;";
                read.Parameters.AddWithValue("$uid", user.ToString());
                slot = Convert.ToInt32(await read.ExecuteScalarAsync());
            }

            await tx.CommitAsync();
            return (true, slot);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     Returns the account's mark row, or <c>null</c> if the account has never planted. No season
    ///     filter — the claim is a career event.
    /// </summary>
    public async Task<MarkRecord?> GetMarkAsync(Guid user)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT user_id, kind, planted_round_id, planted_utc, planted_map, character_name,
                       tours_at_planting, slot_index, nudge_shown, last_visit_utc, last_visit_stage
                FROM mark
                WHERE user_id = $uid;
                """;
            cmd.Parameters.AddWithValue("$uid", user.ToString());

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            return ReadMarkRecord(reader);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     Every recorded mark, ordered by <c>slot_index</c> (planting order, forever), capped at
    ///     <paramref name="limit"/> — the round-start projection read (spec §3.3). Records beyond the
    ///     physical slot capacity simply don't come back, so they never spawn; their owners get the
    ///     overflow line instead. Non-positive <paramref name="limit"/> returns an empty list.
    /// </summary>
    public async Task<List<MarkRecord>> GetAllMarksAsync(int limit)
    {
        var marks = new List<MarkRecord>();
        if (limit <= 0)
            return marks;

        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT user_id, kind, planted_round_id, planted_utc, planted_map, character_name,
                       tours_at_planting, slot_index, nudge_shown, last_visit_utc, last_visit_stage
                FROM mark
                ORDER BY slot_index
                LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$limit", limit);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                marks.Add(ReadMarkRecord(reader));

            return marks;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     Conditionally stamps the first-return nudge as shown. Returns <c>true</c> exactly once per
    ///     claimed account (the conditional UPDATE's rowcount guard); <c>false</c> when there is no
    ///     mark row or the nudge was already stamped. Written BEFORE the nudge dispatches — worst
    ///     case a nudge is swallowed, never duplicated.
    /// </summary>
    public async Task<bool> TryMarkNudgeShownAsync(Guid user)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE mark
                SET nudge_shown = 1
                WHERE user_id = $uid AND nudge_shown = 0;
                """;
            cmd.Parameters.AddWithValue("$uid", user.ToString());
            return await cmd.ExecuteNonQueryAsync() == 1;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     Conditionally records an owner visit at <paramref name="stage"/>. The
    ///     <c>last_visit_stage &lt; $stage</c> guard makes this <c>true</c> at most once per stage
    ///     ADVANCE — a racing double-spawn delivers at most one growth line, and a re-visit at the
    ///     same stage stays silent (the anti-fatigue law, spec §3.5). Written BEFORE the return beat
    ///     dispatches. <paramref name="visitUtc"/> is the caller's ISO-8601 "now" so the growth delta
    ///     the beat renders and the stamp it writes agree exactly.
    /// </summary>
    public async Task<bool> TryRecordVisitAsync(Guid user, int stage, string visitUtc)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE mark
                SET last_visit_stage = $stage, last_visit_utc = $utc
                WHERE user_id = $uid AND last_visit_stage < $stage;
                """;
            cmd.Parameters.AddWithValue("$uid", user.ToString());
            cmd.Parameters.AddWithValue("$stage", stage);
            cmd.Parameters.AddWithValue("$utc", visitUtc);
            return await cmd.ExecuteNonQueryAsync() == 1;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     TEST SEAM ONLY (MG-W2 projection scenarios): rewrites a claimed mark's aging clock so an
    ///     integration test can materialize "planted 30 days ago" fixtures without waiting 30 days.
    ///     Rewinds <c>planted_utc</c> AND <c>last_visit_utc</c> together (a visit can never predate
    ///     the planting). Production code has no caller and must never grow one — the planting stamp
    ///     is immutable by design (spec §3.4: the aging clock's zero point).
    /// </summary>
    internal async Task SetMarkPlantedUtcForTests(Guid user, string plantedUtc)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE mark
                SET planted_utc = $utc, last_visit_utc = $utc
                WHERE user_id = $uid;
                """;
            cmd.Parameters.AddWithValue("$uid", user.ToString());
            cmd.Parameters.AddWithValue("$utc", plantedUtc);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     TEST SEAM ONLY: deletes every row from the <c>mark</c> table. Unlike
    ///     <c>first_death</c>/<c>social_firsts</c> (isolated per test purely by drawing a fresh
    ///     <see cref="Guid"/> each test — no cross-account state to leak), a mark's
    ///     <c>slot_index</c> is claimed from a GLOBAL <c>COUNT(*) FROM mark</c> in the same INSERT
    ///     (see <see cref="TryClaimMarkAsync"/>) — a dense sequence over the WHOLE table, not scoped
    ///     to any one account. A pooled server instance is eligible for reuse across every test
    ///     method in a fixture that requests identical <c>PoolSettings</c> (recycling only requires a
    ///     non-fast cleanup when <c>Dirty</c> — it does not force a brand-new pair/DB per method), so
    ///     without this seam a later test's "first claim in this table = slot 0" precondition silently
    ///     inherits rows planted by earlier tests sharing the same recycled ledger file. Production
    ///     code has no caller and must never grow one — planting is permanent by design.
    /// </summary>
    internal async Task ClearAllMarksForTests()
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM mark;";
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    private static MarkRecord ReadMarkRecord(System.Data.Common.DbDataReader reader)
    {
        return new MarkRecord(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetInt32(8) != 0,
            reader.GetString(9),
            reader.GetInt32(10));
    }
}
