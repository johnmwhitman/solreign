using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     One STATION-LIBRARY work, as persisted in the <c>library_works</c> table.
///     <see cref="Author"/> is <see cref="Guid.Empty"/> for a PROVIDENCE-authored seed work (the
///     Noticeboard precedent) — PROVIDENCE has no account. <see cref="AuthorDisplay"/> is the
///     in-character name (or in-fiction byline) cached at submit time; the underlying account
///     identity is <see cref="Author"/> itself, reachable only through the normal restricted
///     admin-log path, never rendered on the shelf. Deliberately carries NO expiry field — a
///     library persists; the only ways a work stops re-materializing are a hide (reversible
///     containment) or a moderator's deliberate deletion, never a timer.
/// </summary>
public sealed record LibraryWorkRecord(
    int Id,
    string ArchiveId,
    Guid Author,
    bool IsProvidence,
    string AuthorDisplay,
    string Title,
    string Body,
    int RoundId,
    string SubmittedUtc,
    bool Hidden);

/// <summary>Result of a submit attempt — <see cref="Rejection"/> is non-null only when <see cref="Posted"/> is false.</summary>
public enum LibraryPostRejection
{
    AuthorAlreadySubmittedThisRound,
    ProvidenceSeedTargetReached,
}

/// <summary>
///     STATION-LIBRARY persistence (wave-2 item; docs/council/2026-07-17-player-text-safety.md's
///     write-path posture is LAW where applicable — see this file's divergence notes). The
///     <c>library_works</c> table is the single source of truth an Annex's shelf projects from
///     every round start (the Continuity Garden discipline: the row is truth, what's spawned is a
///     disposable per-round read of it) — one row per submitted work, list content materialized as
///     N separate book ITEM entities (unlike Noticeboard's one-board-many-notes list projection),
///     because "reading" here reuses the existing per-item Paper BUI rather than a shared board BUI.
///
///     The author quota (<see cref="TryPostWorkAsync"/>'s only atomic rule) is enforced INSIDE one
///     BEGIN-IMMEDIATE transaction — the same fold-the-check-into-the-INSERT discipline as
///     <see cref="TryClaimMarkAsync"/>/<c>TryPostNoteAsync</c> — so a caller that already passed the
///     daemon's classifier can still be safely refused at the moment of write if a same-round race
///     already used the author's one submission. Callers MUST treat a classifier approval as
///     advisory only until this method returns <c>Posted: true</c>; a discarded approval writes
///     nothing.
///
///     Divergence from the Noticeboard precedent (deliberate, per this lane's design brief): NO
///     expiry column, NO expiry sweep, NO cooldown, NO shared-board capacity cap. A library's whole
///     point is that a work outlives the round it was written in; the only closed numbers here are
///     the once-per-round author quota and the two length caps (<c>LibraryRules</c>), and the only
///     way a row stops re-materializing is <see cref="HideWorkAsync"/> (reversible containment,
///     player-report or moderator-triggered) or a moderator's own out-of-band deletion — never a
///     timer.
/// </summary>
public sealed partial class SeasonLedgerStore
{
    /// <summary>
    ///     Atomically enforces the write-time rule and inserts the row only if it passes, in one
    ///     BEGIN-IMMEDIATE transaction:
    ///     <list type="bullet">
    ///     <item>a player submission (<paramref name="isProvidence"/> false) is refused if
    ///     <paramref name="author"/> already holds ANY row (hidden or not — hiding is containment,
    ///     not a refund) for <paramref name="roundId"/> — the once-per-account-per-round quota;</item>
    ///     <item>a PROVIDENCE seed (<paramref name="isProvidence"/> true, <paramref name="author"/>
    ///     == <see cref="Guid.Empty"/>) is instead refused if the archive's existing PROVIDENCE row
    ///     count is already &gt;= <paramref name="providenceSeedTarget"/> — closing the race where
    ///     two Annex entities sharing one <c>archive_id</c> both <c>MapInit</c> around the same
    ///     tick and both observe the same stale <see cref="GetProvidenceWorkCountAsync"/> snapshot
    ///     (that read is a cheap pre-check/optimization only, the Noticeboard idiom — this atomic
    ///     recheck is the actual guarantee).</item>
    ///     </list>
    /// </summary>
    public async Task<(bool Posted, int Id, LibraryPostRejection? Rejection)> TryPostWorkAsync(
        string archiveId,
        Guid author,
        bool isProvidence,
        string authorDisplay,
        string title,
        string body,
        int roundId,
        string nowUtc,
        int providenceSeedTarget = int.MaxValue)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var tx = conn.BeginTransaction(deferred: false); // BEGIN IMMEDIATE — see TryClaimMarkAsync

            if (!isProvidence)
            {
                await using var check = conn.CreateCommand();
                check.Transaction = tx;
                check.CommandText = """
                    SELECT EXISTS(
                        SELECT 1 FROM library_works
                        WHERE author_guid = $author AND round_id = $round AND is_providence = 0
                    );
                    """;
                check.Parameters.AddWithValue("$author", author.ToString());
                check.Parameters.AddWithValue("$round", roundId);
                if (Convert.ToInt32(await check.ExecuteScalarAsync()) == 1)
                {
                    await tx.CommitAsync();
                    return (false, -1, LibraryPostRejection.AuthorAlreadySubmittedThisRound);
                }
            }
            else
            {
                await using var check = conn.CreateCommand();
                check.Transaction = tx;
                check.CommandText = """
                    SELECT COUNT(*) FROM library_works
                    WHERE archive_id = $archive AND is_providence = 1;
                    """;
                check.Parameters.AddWithValue("$archive", archiveId);
                if (Convert.ToInt32(await check.ExecuteScalarAsync()) >= providenceSeedTarget)
                {
                    await tx.CommitAsync();
                    return (false, -1, LibraryPostRejection.ProvidenceSeedTargetReached);
                }
            }

            int id;
            await using (var insert = conn.CreateCommand())
            {
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT INTO library_works
                        (archive_id, author_guid, is_providence, author_display, title, body, round_id, submitted_utc, hidden)
                    VALUES ($archive, $author, $providence, $display, $title, $body, $round, $now, 0);
                    """;
                insert.Parameters.AddWithValue("$archive", archiveId);
                insert.Parameters.AddWithValue("$author", author.ToString());
                insert.Parameters.AddWithValue("$providence", isProvidence ? 1 : 0);
                insert.Parameters.AddWithValue("$display", authorDisplay);
                insert.Parameters.AddWithValue("$title", title);
                insert.Parameters.AddWithValue("$body", body);
                insert.Parameters.AddWithValue("$round", roundId);
                insert.Parameters.AddWithValue("$now", nowUtc);
                await insert.ExecuteNonQueryAsync();
            }

            await using (var idCmd = conn.CreateCommand())
            {
                // Separate statement (the TryPostNoteAsync idiom) — last_insert_rowid() is
                // connection-scoped, so this reliably reads back the row this transaction just inserted.
                idCmd.Transaction = tx;
                idCmd.CommandText = "SELECT last_insert_rowid();";
                id = Convert.ToInt32(await idCmd.ExecuteScalarAsync());
            }

            await tx.CommitAsync();
            return (true, id, null);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     The N most-recently-submitted non-hidden works on an archive, newest first — the round-
    ///     start projection read (this lane's design brief §3). A hidden work simply never comes
    ///     back; there is no "hidden placeholder" spawned (the Noticeboard report/hide idiom). A
    ///     work beyond <paramref name="limit"/> keeps its row untouched; it just doesn't get a
    ///     physical copy this round.
    /// </summary>
    public async Task<List<LibraryWorkRecord>> GetRecentWorksAsync(string archiveId, int limit)
    {
        var works = new List<LibraryWorkRecord>();
        if (limit <= 0)
            return works;

        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, archive_id, author_guid, is_providence, author_display, title, body, round_id, submitted_utc, hidden
                FROM library_works
                WHERE archive_id = $archive AND hidden = 0
                ORDER BY submitted_utc DESC
                LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$archive", archiveId);
            cmd.Parameters.AddWithValue("$limit", limit);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                works.Add(ReadLibraryWorkRecord(reader));

            return works;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Cheap pre-check used before firing the classifier round trip for a PROVIDENCE seed
    /// work — seeding is once-EVER per archive (unlike Noticeboard's per-round reseed), so this is
    /// the gate a caller checks before submitting any missing seed at all.</summary>
    public async Task<int> GetProvidenceWorkCountAsync(string archiveId)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT COUNT(*) FROM library_works
                WHERE archive_id = $archive AND is_providence = 1;
                """;
            cmd.Parameters.AddWithValue("$archive", archiveId);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     Containment: immediately and reversibly hides a work (player one-tap report, or a
    ///     moderator's own action — the Noticeboard idiom). Conditional UPDATE (rowcount guard) —
    ///     idempotent, so a second report/hide on an already-hidden work is a harmless no-op, never
    ///     an error. <paramref name="reason"/> is internal-only and never surfaced with a reporter
    ///     or moderator identity.
    /// </summary>
    public async Task<bool> HideWorkAsync(int workId, string reason, string nowUtc)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE library_works
                SET hidden = 1, hidden_utc = $now, hidden_reason = $reason
                WHERE id = $id AND hidden = 0;
                """;
            cmd.Parameters.AddWithValue("$id", workId);
            cmd.Parameters.AddWithValue("$now", nowUtc);
            cmd.Parameters.AddWithValue("$reason", reason);
            return await cmd.ExecuteNonQueryAsync() == 1;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     Reverses a hide — moderator-only tool (<c>LibraryUnhideCommand</c>), for correcting a
    ///     bad-faith report. Unlike Noticeboard's <c>UnhideNoteAsync</c> there is no expiry guard to
    ///     re-check (a library work never expires), so this is a plain conditional UPDATE.
    /// </summary>
    public async Task<bool> UnhideWorkAsync(int workId, string nowUtc)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE library_works
                SET hidden = 0, hidden_utc = NULL, hidden_reason = NULL
                WHERE id = $id AND hidden = 1;
                """;
            cmd.Parameters.AddWithValue("$id", workId);
            // nowUtc is accepted for symmetry with the Noticeboard signature and future audit use;
            // a library work has no expiry to re-validate against.
            _ = nowUtc;
            return await cmd.ExecuteNonQueryAsync() == 1;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static LibraryWorkRecord ReadLibraryWorkRecord(System.Data.Common.DbDataReader reader)
    {
        return new LibraryWorkRecord(
            reader.GetInt32(0),
            reader.GetString(1),
            Guid.Parse(reader.GetString(2)),
            reader.GetInt32(3) != 0,
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetInt32(7),
            reader.GetString(8),
            reader.GetInt32(9) != 0);
    }
}
