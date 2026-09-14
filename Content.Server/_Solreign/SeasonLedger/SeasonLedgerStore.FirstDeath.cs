using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     One account's authored-first-death claim row, as persisted in the <c>first_death</c> table.
///     <c>Cause</c> is the string form of <c>Content.Server._Solreign.Providence.FirstDeathCause</c>
///     (VIOLENCE | VACUUM | BURN | MISADVENTURE | UNKNOWN); <c>EpitaphId</c> is a plate id from the
///     first-death copy pack (spec §8B), never free text. <c>CryptReported</c> (FD-W3.5) records
///     whether the extended crypt memorial POST for this claim has been handed to the sender —
///     rows claimed before the FD-W3 wire shipped (or while the crypt gates were closed) carry
///     <c>false</c> and are picked up by the round-start plaque backfill.
/// </summary>
public sealed record FirstDeathRecord(
    Guid User,
    int RoundId,
    string CharacterName,
    string Cause,
    int ToursAtDeath,
    string TitleAtDeath,
    string EpitaphId,
    string DiedAtUtc,
    bool RehireShown,
    bool CryptReported);

/// <summary>
///     "The Authored First Death" persistence (docs/specs/FIRST-DEATH-SPEC-2026-07-16-DRAFT.md §3.1):
///     the first time a given ACCOUNT ever has a player-controlled crew character die on SOLREIGN,
///     PROVIDENCE performs a one-time authored scene. The <c>first_death</c> table (schema battery in
///     SeasonLedgerStore.cs) is the single linchpin of the exactly-once guarantee:
///       * <see cref="TryClaimFirstDeathAsync"/> is an atomic INSERT-if-absent — two deaths racing in
///         the same tick both reach it, SQLite's <c>ON CONFLICT DO NOTHING</c> + rowcount check hands
///         the claim to exactly one. Same write-before-dispatch idiom as
///         <see cref="SetAnnouncedTitleAsync"/> ("Written BEFORE the bulletin dispatches so a racing
///         double-spawn can never double-announce").
///       * <see cref="TryMarkRehireShownAsync"/> is the rehire beat's own once-ever stamp — a
///         conditional UPDATE with a rowcount guard (the same idiom the daemon's <c>salary_awards</c>
///         table documents in <c>orchestrator/salary.py</c>): the caller only dispatches the beat when
///         this returns <c>true</c>, so a racing double-spawn can never double-deliver.
///
///     Deliberately NOT season-scoped (unlike <c>title_grants</c>): a first death is a career event,
///     keyed by <c>user_id</c> alone. A season bump does not resurrect anyone.
/// </summary>
public sealed partial class SeasonLedgerStore
{
    /// <summary>
    ///     Atomically claims the once-per-account-EVER first-death slot. Returns <c>true</c> exactly
    ///     once per account, for the caller that won the INSERT; every later (or racing-and-lost) call
    ///     returns <c>false</c>. Callers must only dispatch the authored scene on <c>true</c>.
    /// </summary>
    public async Task<bool> TryClaimFirstDeathAsync(
        Guid user,
        int roundId,
        string characterName,
        string cause,
        int toursAtDeath,
        string titleAtDeath,
        string epitaphId)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO first_death
                    (user_id, round_id, character_name, cause, tours_at_death, title_at_death, epitaph_id, died_at_utc, rehire_shown)
                VALUES ($uid, $rid, $name, $cause, $tours, $title, $epitaph, $utc, 0)
                ON CONFLICT(user_id) DO NOTHING;
                """;
            cmd.Parameters.AddWithValue("$uid", user.ToString());
            cmd.Parameters.AddWithValue("$rid", roundId);
            cmd.Parameters.AddWithValue("$name", characterName);
            cmd.Parameters.AddWithValue("$cause", cause);
            cmd.Parameters.AddWithValue("$tours", toursAtDeath);
            cmd.Parameters.AddWithValue("$title", titleAtDeath);
            cmd.Parameters.AddWithValue("$epitaph", epitaphId);
            cmd.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("o"));
            return await cmd.ExecuteNonQueryAsync() == 1;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     Returns the account's first-death claim row, or <c>null</c> if the account has never had a
    ///     first death claimed. No season filter — the claim is a career event.
    /// </summary>
    public async Task<FirstDeathRecord?> GetFirstDeathAsync(Guid user)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT round_id, character_name, cause, tours_at_death, title_at_death, epitaph_id, died_at_utc, rehire_shown, crypt_reported
                FROM first_death
                WHERE user_id = $uid;
                """;
            cmd.Parameters.AddWithValue("$uid", user.ToString());

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            return new FirstDeathRecord(
                user,
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetInt32(7) != 0,
                reader.GetInt32(8) != 0);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     Conditionally stamps the rehire beat as shown. Returns <c>true</c> exactly once per claimed
    ///     account (the conditional UPDATE's rowcount guard); <c>false</c> when there is no claim row
    ///     or the beat was already stamped. Written BEFORE the rehire beat dispatches — worst case a
    ///     beat is swallowed (e.g. disconnect between stamp and delivery), never duplicated.
    /// </summary>
    public async Task<bool> TryMarkRehireShownAsync(Guid user)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE first_death
                SET rehire_shown = 1
                WHERE user_id = $uid AND rehire_shown = 0;
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
    ///     FD-W3.5: conditionally stamps the claim row's crypt memorial as reported — the same
    ///     rowcount-guarded conditional-UPDATE idiom as <see cref="TryMarkRehireShownAsync"/>.
    ///     Returns <c>true</c> exactly once per claimed account; <c>false</c> when there is no claim
    ///     row or the memorial was already stamped. The daemon's <c>handle_first_death</c> is NOT
    ///     idempotent per victim (a plain INSERT into <c>crypt_obituaries</c>), so this stamp is the
    ///     ONLY dedupe between the claim-time wire and the round-start backfill: callers stamp
    ///     immediately after — and only after — the report was successfully HANDED to the crypt
    ///     sender (gates + rate limit passed), never before knowing the gate outcome (a
    ///     stamp-before-gate would burn banked memorials forever the first time the backfill ran
    ///     with the default-OFF crypt gates closed). Tradeoff recorded in
    ///     docs/receipts/PLAQUE-BACKFILL-2026-07-17.md.
    /// </summary>
    public async Task<bool> TryMarkCryptReportedAsync(Guid user)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE first_death
                SET crypt_reported = 1
                WHERE user_id = $uid AND crypt_reported = 0;
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
    ///     FD-W3.5: every claim row whose crypt memorial has never been handed to the sender —
    ///     the round-start plaque backfill's work list (rows banked before the FD-W3 wire shipped,
    ///     or claimed while the crypt gates were closed). Oldest death first, so the earliest lost
    ///     memorial is the first one restored. Unbounded by design: the table holds at most one row
    ///     per account ever, and the backfill queue paces dispatch itself.
    /// </summary>
    public async Task<List<FirstDeathRecord>> GetCryptUnreportedFirstDeathsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT user_id, round_id, character_name, cause, tours_at_death, title_at_death, epitaph_id, died_at_utc, rehire_shown, crypt_reported
                FROM first_death
                WHERE crypt_reported = 0
                ORDER BY died_at_utc ASC;
                """;

            var rows = new List<FirstDeathRecord>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new FirstDeathRecord(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetInt32(8) != 0,
                    reader.GetInt32(9) != 0));
            }

            return rows;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     Echoes of the Departed (v14, EOD-spec §5): every claimed first-death row, most-recent
    ///     death first, capped at <paramref name="limit"/> — the round-start Echo projection read,
    ///     the exact <see cref="SeasonLedgerStore.Mark.cs"/>'s <c>GetAllMarksAsync</c> shape except
    ///     for the ordering direction. <c>first_death</c> rows are immutable after claim
    ///     (<c>died_at_utc</c> never changes), so a row's position in this ordering is stable
    ///     across every projection, forever — no slot-index column is needed, unlike <c>mark</c>.
    ///     Records beyond <paramref name="limit"/> simply don't come back, so they never spawn a
    ///     physical marker; the permanent claim row is untouched either way. Non-positive
    ///     <paramref name="limit"/> returns an empty list (the <c>GetAllMarksAsync</c> contract).
    /// </summary>
    public async Task<List<FirstDeathRecord>> GetAllFirstDeathsAsync(int limit)
    {
        var rows = new List<FirstDeathRecord>();
        if (limit <= 0)
            return rows;

        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT user_id, round_id, character_name, cause, tours_at_death, title_at_death, epitaph_id, died_at_utc, rehire_shown, crypt_reported
                FROM first_death
                ORDER BY died_at_utc DESC, user_id ASC
                LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$limit", limit);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new FirstDeathRecord(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetInt32(8) != 0,
                    reader.GetInt32(9) != 0));
            }

            return rows;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     TEST SEAM ONLY (Echoes of the Departed, v14, EOD-W1 ordering scenarios): rewrites a
    ///     claimed first-death's <c>died_at_utc</c> so a test can materialize deterministic
    ///     <see cref="GetAllFirstDeathsAsync"/> ordering fixtures without depending on wall-clock
    ///     claim timing. The exact <see cref="SetMarkPlantedUtcForTests"/> idiom. Production code
    ///     has no caller and must never grow one — the death stamp is immutable by design (a first
    ///     death's <c>died_at_utc</c> is the aging clock's zero point, EOD-spec §7).
    /// </summary>
    internal async Task SetFirstDeathDiedAtUtcForTests(Guid user, string diedAtUtc)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE first_death
                SET died_at_utc = $utc
                WHERE user_id = $uid;
                """;
            cmd.Parameters.AddWithValue("$uid", user.ToString());
            cmd.Parameters.AddWithValue("$utc", diedAtUtc);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _lock.Release();
        }
    }
}
