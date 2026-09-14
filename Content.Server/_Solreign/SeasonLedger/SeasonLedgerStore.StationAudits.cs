using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     One shift's compact Station Audit summary row, as persisted in the <c>station_audit_log</c>
///     table (v14 wave-1 #3). Round-scoped, not account-scoped — this is the station remembering its
///     own shifts (future website/Chronicle feed material), not a per-crew-member career stat, so it
///     is keyed by <c>round_id</c> alone rather than folding into <c>player_stats</c>. The season the
///     row belongs to is resolved internally at write time (the same in-transaction
///     <c>GetCurrentSeasonInternalAsync</c> read every other write in this store uses) rather than
///     supplied by the caller — <c>StationAuditSystem</c> has no reason to know or guess the current
///     season id.
/// </summary>
public sealed record StationAuditRecord(
    int RoundId,
    string EndedAtUtc,
    int ShiftDurationMinutes,
    int CrewCount,
    int DeathCount,
    bool FirstDeathCommemorated,
    string? DirectiveTitle,
    bool DirectiveOutcomeReported,
    bool DirectiveOutcomeFulfilled,
    int StipendsProcessed,
    int BountyVerdicts,
    int NotableEventCount,
    string? CommendationName,
    int CommendationScore,
    string ItemOfConcernId,
    // --- Inspection layer / mandatory PROVIDENCE consequence (gap-closure pass) — trailing, defaulted
    // fields so pre-existing call sites (StationAuditStoreTests.FullRecord) keep compiling unmodified.
    // CriteriaJson is a compact JSON array of {criterionId, verdict}, chosen over N fixed columns
    // because SolreignStationAuditInspectionCount is CVar-tunable; a future count change never needs a
    // further migration. "none"/null mean the layer never fired this shift (disabled, or a round that
    // ended before any grading pass could run — should not happen given the round-end fallback, but
    // never assumed).
    string CriteriaJson = "[]",
    string CheckpointConsequenceKind = "none",
    string? CheckpointFiredUtc = null);

/// <summary>
///     Persistence for the Station Audit history (schema battery in <c>SeasonLedgerStore.cs</c>). One
///     row per round, written once at shift end by <c>StationAuditSystem</c> through
///     <c>SeasonLedgerSystem.AppendStationAuditAsync</c> — the same "don't stand up a second store
///     against the same SQLite file" law <c>SeasonLedgerSystem.GetCareerStatsAsync</c>'s doc comment
///     already states for <c>ProvidenceWelcomeSystem</c>.
/// </summary>
public sealed partial class SeasonLedgerStore
{
    /// <summary>
    ///     Idempotent append: <c>INSERT OR IGNORE</c> keyed on <c>round_id</c>, so a retried/duplicate
    ///     call (e.g. a caller that fires this more than once for the same round-end) never produces a
    ///     second row or throws — it just no-ops on the repeat. Returns <c>true</c> only for the write
    ///     that actually inserted a row.
    /// </summary>
    public async Task<bool> AppendStationAuditAsync(StationAuditRecord record)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();

            var season = await GetCurrentSeasonInternalAsync(conn);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO station_audit_log
                    (round_id, season_id, ended_utc, shift_duration_minutes, crew_count, death_count,
                     first_death_commemorated, directive_title, directive_outcome_reported,
                     directive_outcome_fulfilled, stipends_processed, bounty_verdicts,
                     notable_event_count, commendation_name, commendation_score, item_of_concern_id,
                     criteria_json, checkpoint_consequence_kind, checkpoint_fired_utc)
                VALUES
                    ($rid, $season, $ended, $duration, $crew, $deaths,
                     $commemorated, $directive, $outcomeReported,
                     $outcomeFulfilled, $stipends, $bounties,
                     $notable, $commName, $commScore, $concern,
                     $criteriaJson, $checkpointKind, $checkpointFiredUtc)
                ON CONFLICT(round_id) DO NOTHING;
                """;
            cmd.Parameters.AddWithValue("$rid", record.RoundId);
            cmd.Parameters.AddWithValue("$season", season);
            cmd.Parameters.AddWithValue("$ended", record.EndedAtUtc);
            cmd.Parameters.AddWithValue("$duration", record.ShiftDurationMinutes);
            cmd.Parameters.AddWithValue("$crew", record.CrewCount);
            cmd.Parameters.AddWithValue("$deaths", record.DeathCount);
            cmd.Parameters.AddWithValue("$commemorated", record.FirstDeathCommemorated ? 1 : 0);
            cmd.Parameters.AddWithValue("$directive", (object?) record.DirectiveTitle ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$outcomeReported", record.DirectiveOutcomeReported ? 1 : 0);
            cmd.Parameters.AddWithValue("$outcomeFulfilled", record.DirectiveOutcomeFulfilled ? 1 : 0);
            cmd.Parameters.AddWithValue("$stipends", record.StipendsProcessed);
            cmd.Parameters.AddWithValue("$bounties", record.BountyVerdicts);
            cmd.Parameters.AddWithValue("$notable", record.NotableEventCount);
            cmd.Parameters.AddWithValue("$commName", (object?) record.CommendationName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$commScore", record.CommendationScore);
            cmd.Parameters.AddWithValue("$concern", record.ItemOfConcernId);
            cmd.Parameters.AddWithValue("$criteriaJson", record.CriteriaJson);
            cmd.Parameters.AddWithValue("$checkpointKind", record.CheckpointConsequenceKind);
            cmd.Parameters.AddWithValue("$checkpointFiredUtc", (object?) record.CheckpointFiredUtc ?? DBNull.Value);
            return await cmd.ExecuteNonQueryAsync() == 1;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     Most recent audits, newest first — the read side for tests today and a future
    ///     website/Chronicle feed later (no daemon/website wiring in this lane; the row is simply
    ///     available for one to be built against).
    /// </summary>
    public async Task<List<StationAuditRecord>> GetRecentStationAuditsAsync(int limit = 20)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT round_id, ended_utc, shift_duration_minutes, crew_count, death_count,
                       first_death_commemorated, directive_title, directive_outcome_reported,
                       directive_outcome_fulfilled, stipends_processed, bounty_verdicts,
                       notable_event_count, commendation_name, commendation_score, item_of_concern_id,
                       criteria_json, checkpoint_consequence_kind, checkpoint_fired_utc
                FROM station_audit_log
                ORDER BY round_id DESC
                LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$limit", limit);

            var rows = new List<StationAuditRecord>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new StationAuditRecord(
                    RoundId: reader.GetInt32(0),
                    EndedAtUtc: reader.GetString(1),
                    ShiftDurationMinutes: reader.GetInt32(2),
                    CrewCount: reader.GetInt32(3),
                    DeathCount: reader.GetInt32(4),
                    FirstDeathCommemorated: reader.GetInt32(5) != 0,
                    DirectiveTitle: reader.IsDBNull(6) ? null : reader.GetString(6),
                    DirectiveOutcomeReported: reader.GetInt32(7) != 0,
                    DirectiveOutcomeFulfilled: reader.GetInt32(8) != 0,
                    StipendsProcessed: reader.GetInt32(9),
                    BountyVerdicts: reader.GetInt32(10),
                    NotableEventCount: reader.GetInt32(11),
                    CommendationName: reader.IsDBNull(12) ? null : reader.GetString(12),
                    CommendationScore: reader.GetInt32(13),
                    ItemOfConcernId: reader.GetString(14),
                    // Columns added additively for pre-existing rows (EnsureColumnAsync defaults them
                    // to "[]" / "none" / NULL) — reader.IsDBNull guards are defense-in-depth, not just
                    // for rows written before this migration.
                    CriteriaJson: reader.IsDBNull(15) ? "[]" : reader.GetString(15),
                    CheckpointConsequenceKind: reader.IsDBNull(16) ? "none" : reader.GetString(16),
                    CheckpointFiredUtc: reader.IsDBNull(17) ? null : reader.GetString(17)));
            }

            return rows;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     Station Lifetime Audit Count (nyanopark-019 fold, "borrowed, not built" — no new stats
    ///     system): a trivial <c>COUNT(*)</c> against the already-existing table, zero schema change.
    ///     Used to cite "this is shift #{N} in the station's audit history."
    /// </summary>
    public async Task<int> GetStationAuditLifetimeCountAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM station_audit_log;";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }
        finally
        {
            _lock.Release();
        }
    }
}
