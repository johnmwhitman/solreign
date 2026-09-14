using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     One noticeboard note, as persisted in the <c>noticeboard_notes</c> table. <c>User</c> is
///     <see cref="Guid.Empty"/> for a PROVIDENCE-authored row (spec rule 5) — PROVIDENCE has no
///     account. <c>AuthorDisplay</c> is the in-character name (or the PROVIDENCE marker) cached at
///     post time; the underlying account identity is <c>User</c> itself, reachable only through
///     the normal restricted admin-log path, never rendered on the board (spec rule 2).
/// </summary>
public sealed record NoticeboardNoteRecord(
    int Id,
    string BoardId,
    Guid User,
    bool IsProvidence,
    string AuthorDisplay,
    string Body,
    string PostedUtc,
    string ExpiresUtc,
    bool Hidden);

/// <summary>Result of a post attempt — <see cref="Rejection"/> is non-null only when <see cref="Posted"/> is false.</summary>
public enum NoticeboardPostRejection
{
    BoardFull,
    ProvidenceQuotaFull,
    AuthorHasActiveNote,
    AuthorOnCooldown,
}

/// <summary>
///     Crew Noticeboards persistence (docs/council/2026-07-17-player-text-safety.md — the C2
///     posture memo, LAW for this feature). The <c>noticeboard_notes</c> table is the single
///     source of truth a board's BUI projects from (the Continuity Garden discipline: the row is
///     truth, what's rendered is a disposable read of it) — mirrored here as a data projection
///     rather than a spawned-entity projection, since a board's notes are list content on ONE
///     board entity, not one entity per note (unlike Mark's one-projection-per-record model).
///
///     Every quota/capacity/cooldown rule from the memo is enforced ATOMICALLY inside
///     <see cref="TryPostNoteAsync"/>'s single BEGIN-IMMEDIATE transaction — the same
///     fold-the-checks-into-the-INSERT discipline as <see cref="TryClaimMarkAsync"/> — so a caller
///     that already passed the daemon's classifier can still be safely refused at the moment of
///     write if the board filled up (or the author's cooldown/quota changed) while that round trip
///     was in flight. Callers MUST treat a classifier approval as advisory only until this method
///     returns <c>Posted: true</c>; a discarded approval writes nothing.
///
///     The 24h author cooldown (spec rule 2) is tracked in a SEPARATE <c>noticeboard_authors</c>
///     table, deliberately not derived from the note row's own <c>posted_utc</c> via
///     <c>MAX(posted_utc)</c> — the periodic expiry sweep (<see cref="SweepExpiredNotesAsync"/>)
///     physically deletes expired rows, which would silently erase the cooldown clock along with
///     them and let a player re-pin the instant their old note aged out. The durable cooldown
///     stamp survives that deletion.
/// </summary>
public sealed partial class SeasonLedgerStore
{
    /// <summary>
    ///     Atomically enforces every write-time rule from the posture memo and inserts the row
    ///     only if all of them pass, in one BEGIN-IMMEDIATE transaction:
    ///     <list type="bullet">
    ///     <item>total active (unhidden, unexpired) rows on <paramref name="boardId"/> &lt;
    ///     <paramref name="boardCapacity"/> (spec rule 2, shared cap incl. PROVIDENCE);</item>
    ///     <item>if <paramref name="isProvidence"/>: active PROVIDENCE rows on this board &lt;
    ///     <paramref name="providenceCapacity"/> (spec rule 5); PROVIDENCE has no author cooldown
    ///     or one-active-note cap — it is quota-gated only;</item>
    ///     <item>otherwise: <paramref name="user"/> holds no other active row on ANY board (spec
    ///     rule 2's "one active note per author," read account-wide since a future multi-board
    ///     world should not let one player occupy a slot on every board simultaneously), AND at
    ///     least <paramref name="cooldownHours"/> have passed since their
    ///     <c>noticeboard_authors.last_post_utc</c> (absent = never posted = no cooldown).</item>
    ///     </list>
    ///     On success, also upserts <paramref name="user"/>'s cooldown stamp to
    ///     <paramref name="nowUtc"/> (skipped for PROVIDENCE, which has no stamp) in the SAME
    ///     transaction, so a racing second post from the same account cannot slip between the
    ///     check and the stamp.
    /// </summary>
    public async Task<(bool Posted, int Id, NoticeboardPostRejection? Rejection)> TryPostNoteAsync(
        string boardId,
        Guid user,
        bool isProvidence,
        string authorDisplay,
        string body,
        string nowUtc,
        int expiryHours,
        int boardCapacity,
        int providenceCapacity,
        int cooldownHours)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var tx = conn.BeginTransaction(deferred: false); // BEGIN IMMEDIATE — see TryClaimMarkAsync

            var totalActive = await CountActiveAsync(conn, tx, boardId, nowUtc, providenceOnly: false);
            if (totalActive >= boardCapacity)
            {
                await tx.CommitAsync();
                return (false, -1, NoticeboardPostRejection.BoardFull);
            }

            if (isProvidence)
            {
                var providenceActive = await CountActiveAsync(conn, tx, boardId, nowUtc, providenceOnly: true);
                if (providenceActive >= providenceCapacity)
                {
                    await tx.CommitAsync();
                    return (false, -1, NoticeboardPostRejection.ProvidenceQuotaFull);
                }
            }
            else
            {
                if (await AuthorHasActiveNoteAsync(conn, tx, user, nowUtc))
                {
                    await tx.CommitAsync();
                    return (false, -1, NoticeboardPostRejection.AuthorHasActiveNote);
                }

                if (await AuthorOnCooldownAsync(conn, tx, user, nowUtc, cooldownHours))
                {
                    await tx.CommitAsync();
                    return (false, -1, NoticeboardPostRejection.AuthorOnCooldown);
                }
            }

            var expiresUtc = DateTime.Parse(nowUtc, null, System.Globalization.DateTimeStyles.RoundtripKind)
                .AddHours(expiryHours)
                .ToString("o");

            int id;
            await using (var insert = conn.CreateCommand())
            {
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT INTO noticeboard_notes
                        (board_id, user_id, is_providence, author_display, body, posted_utc, expires_utc, hidden)
                    VALUES ($board, $uid, $providence, $author, $body, $posted, $expires, 0);
                    """;
                insert.Parameters.AddWithValue("$board", boardId);
                insert.Parameters.AddWithValue("$uid", user.ToString());
                insert.Parameters.AddWithValue("$providence", isProvidence ? 1 : 0);
                insert.Parameters.AddWithValue("$author", authorDisplay);
                insert.Parameters.AddWithValue("$body", body);
                insert.Parameters.AddWithValue("$posted", nowUtc);
                insert.Parameters.AddWithValue("$expires", expiresUtc);
                await insert.ExecuteNonQueryAsync();
            }

            await using (var idCmd = conn.CreateCommand())
            {
                // Separate statement (rather than relying on multi-statement ExecuteScalar batch
                // semantics) — last_insert_rowid() is connection-scoped, so this reliably reads
                // back the row this transaction just inserted.
                idCmd.Transaction = tx;
                idCmd.CommandText = "SELECT last_insert_rowid();";
                id = Convert.ToInt32(await idCmd.ExecuteScalarAsync());
            }

            if (!isProvidence)
            {
                await using var stamp = conn.CreateCommand();
                stamp.Transaction = tx;
                stamp.CommandText = """
                    INSERT INTO noticeboard_authors (user_id, last_post_utc)
                    VALUES ($uid, $now)
                    ON CONFLICT(user_id) DO UPDATE SET last_post_utc = $now;
                    """;
                stamp.Parameters.AddWithValue("$uid", user.ToString());
                stamp.Parameters.AddWithValue("$now", nowUtc);
                await stamp.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            return (true, id, null);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task<int> CountActiveAsync(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        Microsoft.Data.Sqlite.SqliteTransaction tx,
        string boardId,
        string nowUtc,
        bool providenceOnly)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = providenceOnly
            ? """
                SELECT COUNT(*) FROM noticeboard_notes
                WHERE board_id = $board AND hidden = 0 AND expires_utc > $now AND is_providence = 1;
                """
            : """
                SELECT COUNT(*) FROM noticeboard_notes
                WHERE board_id = $board AND hidden = 0 AND expires_utc > $now;
                """;
        cmd.Parameters.AddWithValue("$board", boardId);
        cmd.Parameters.AddWithValue("$now", nowUtc);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static async Task<bool> AuthorHasActiveNoteAsync(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        Microsoft.Data.Sqlite.SqliteTransaction tx,
        Guid user,
        string nowUtc)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT EXISTS(
                SELECT 1 FROM noticeboard_notes
                WHERE user_id = $uid AND is_providence = 0 AND hidden = 0 AND expires_utc > $now
            );
            """;
        cmd.Parameters.AddWithValue("$uid", user.ToString());
        cmd.Parameters.AddWithValue("$now", nowUtc);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
    }

    private static async Task<bool> AuthorOnCooldownAsync(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        Microsoft.Data.Sqlite.SqliteTransaction tx,
        Guid user,
        string nowUtc,
        int cooldownHours)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT last_post_utc FROM noticeboard_authors WHERE user_id = $uid;";
        cmd.Parameters.AddWithValue("$uid", user.ToString());
        var raw = await cmd.ExecuteScalarAsync() as string;
        if (raw is null)
            return false; // never posted before — no cooldown to serve

        var last = DateTime.Parse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind);
        var now = DateTime.Parse(nowUtc, null, System.Globalization.DateTimeStyles.RoundtripKind);
        return now < last.AddHours(cooldownHours);
    }

    /// <summary>
    ///     Every currently-active (unhidden, unexpired) note on a board, oldest first — the board's
    ///     entire display projection. A hidden note or one past <c>expires_utc</c> simply never
    ///     comes back; there is no "hidden placeholder" row rendered (spec rule 3: "the board's own
    ///     display simply shows the slot as unavailable").
    /// </summary>
    public async Task<List<NoticeboardNoteRecord>> GetActiveNotesAsync(string boardId, string nowUtc)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, board_id, user_id, is_providence, author_display, body, posted_utc, expires_utc, hidden
                FROM noticeboard_notes
                WHERE board_id = $board AND hidden = 0 AND expires_utc > $now
                ORDER BY posted_utc;
                """;
            cmd.Parameters.AddWithValue("$board", boardId);
            cmd.Parameters.AddWithValue("$now", nowUtc);

            var notes = new List<NoticeboardNoteRecord>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                notes.Add(ReadNoticeboardNoteRecord(reader));

            return notes;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Cheap pre-check used before firing the classifier round-trip for a PROVIDENCE seed
    /// post (an optimization only — <see cref="TryPostNoteAsync"/> re-checks atomically regardless).</summary>
    public async Task<int> GetActiveProvidenceCountAsync(string boardId, string nowUtc)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT COUNT(*) FROM noticeboard_notes
                WHERE board_id = $board AND hidden = 0 AND expires_utc > $now AND is_providence = 1;
                """;
            cmd.Parameters.AddWithValue("$board", boardId);
            cmd.Parameters.AddWithValue("$now", nowUtc);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     Containment: immediately and reversibly hides a note (spec rule 3). Conditional UPDATE
    ///     (rowcount guard) — idempotent, so a second report/hide on an already-hidden note is a
    ///     harmless no-op, never an error. <paramref name="reason"/> is internal-only (e.g.
    ///     "reported" / "moderator") and is never surfaced with a reporter or moderator identity —
    ///     it exists solely for the restricted admin-log path.
    /// </summary>
    public async Task<bool> HideNoteAsync(int noteId, string reason, string nowUtc)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE noticeboard_notes
                SET hidden = 1, hidden_utc = $now, hidden_reason = $reason
                WHERE id = $id AND hidden = 0;
                """;
            cmd.Parameters.AddWithValue("$id", noteId);
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
    ///     Reverses a hide — moderator-only tool (<c>NoticeboardUnhideCommand</c>), for correcting a
    ///     bad-faith report. Conditionally guarded on <c>expires_utc &gt; $now</c>: spec rule 3,
    ///     "silence always resolves to the safe outcome — the note is gone, never quietly restored
    ///     to view" — a note that expired WHILE hidden must never come back just because someone
    ///     unhid it after the fact.
    /// </summary>
    public async Task<bool> UnhideNoteAsync(int noteId, string nowUtc)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE noticeboard_notes
                SET hidden = 0, hidden_utc = NULL, hidden_reason = NULL
                WHERE id = $id AND hidden = 1 AND expires_utc > $now;
                """;
            cmd.Parameters.AddWithValue("$id", noteId);
            cmd.Parameters.AddWithValue("$now", nowUtc);
            return await cmd.ExecuteNonQueryAsync() == 1;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     Physically deletes every note past its <paramref name="nowUtc"/> expiry, hidden or not
    ///     (spec rule 4: flat hard expiry, no exceptions). Storage hygiene only — correctness of
    ///     "an expired note is invisible" is already guaranteed by <see cref="GetActiveNotesAsync"/>'s
    ///     <c>expires_utc &gt; $now</c> filter regardless of when this sweep last ran. Returns the
    ///     number of rows removed (test/log visibility). Never touches <c>noticeboard_authors</c> —
    ///     the whole point of that table is to survive this sweep so the cooldown clock persists.
    /// </summary>
    public async Task<int> SweepExpiredNotesAsync(string nowUtc)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM noticeboard_notes WHERE expires_utc <= $now;";
            cmd.Parameters.AddWithValue("$now", nowUtc);
            return await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>TEST SEAM ONLY: rewrites a note's expiry so an integration test can materialize an
    /// "about to expire" / "already expired" fixture without waiting out the real window. Production
    /// code has no caller.</summary>
    internal async Task SetNoteExpiresUtcForTests(int noteId, string expiresUtc)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE noticeboard_notes SET expires_utc = $expires WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", noteId);
            cmd.Parameters.AddWithValue("$expires", expiresUtc);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>TEST SEAM ONLY: rewinds an author's cooldown stamp so an integration test can
    /// materialize "cooldown already elapsed" without waiting 24h. Production code has no caller.</summary>
    internal async Task SetAuthorLastPostUtcForTests(Guid user, string lastPostUtc)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO noticeboard_authors (user_id, last_post_utc)
                VALUES ($uid, $utc)
                ON CONFLICT(user_id) DO UPDATE SET last_post_utc = $utc;
                """;
            cmd.Parameters.AddWithValue("$uid", user.ToString());
            cmd.Parameters.AddWithValue("$utc", lastPostUtc);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    private static NoticeboardNoteRecord ReadNoticeboardNoteRecord(System.Data.Common.DbDataReader reader)
    {
        return new NoticeboardNoteRecord(
            reader.GetInt32(0),
            reader.GetString(1),
            Guid.Parse(reader.GetString(2)),
            reader.GetInt32(3) != 0,
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetInt32(8) != 0);
    }
}
