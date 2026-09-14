using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     One account's contracts-streak fold input for a round-end batch
///     (<see cref="SeasonLedgerStore.RecordContractStreakOutcomesAsync"/>).
/// </summary>
public readonly record struct ContractStreakFoldRequest(Guid User, bool CompletedThisRound);

/// <summary>
///     Per-account result of a contracts-streak fold. <see cref="WasReplay"/> is true when the write
///     was a same/older-round no-op (caller must suppress milestone notification).
/// </summary>
public readonly record struct ContractStreakFoldResult(
    Guid User,
    int CurrentStreak,
    int BestStreak,
    bool IsNewBest,
    bool WasReplay,
    bool CompletedThisRound);

/// <summary>
///     Fail-closed journal/fold-set conflict. Mapped to the same terminal spool
///     <see cref="LedgerRecoveryState.Conflict"/> disposition as
///     <see cref="RoundEndReplayConflictException"/> so a staged conflicting replay never
///     infinite-retries under recovery.
/// </summary>
public sealed class ContractStreakFoldConflictException : InvalidOperationException
{
    public int RoundId { get; }

    internal ContractStreakFoldConflictException(int roundId, string message)
        : base(message)
    {
        RoundId = roundId;
    }
}

/// <summary>
///     Persistent per-account consecutive-shift streak for Solreign Contracts (v14 quest-board
///     extension, spec §3). The <c>contracts_streak</c> table (schema battery in SeasonLedgerStore.cs)
///     holds one row per account — deliberately NOT season-scoped, same career-scoping law as
///     <c>directives_fax_streak</c> / <c>first_death</c> / <c>social_firsts</c> / <c>mark</c>: a season
///     bump does not un-streak anyone.
///
///     Structurally a verbatim clone of <see cref="RecordDirectivesFaxOutcomeAsync"/> — same table
///     shape, same idempotent-per-(account, round id) replay guard, same
///     current/best-streak-never-decreases math. The one deliberate DIFFERENCE is semantic, not
///     structural, and lives entirely in the CALLER
///     (<c>SeasonLedgerSystem.ContractsStreak.cs</c>): Directives Fax only invokes this shape for
///     accounts present &gt;= a presence-minutes threshold (so a merely-brief visit is never counted as
///     a "miss"), whereas the Contracts streak is invoked for every account in the round-end roster —
///     i.e. every account connected at round end, full stop. Chosen default ("Option B" in the design
///     spec): a shift the account played through with zero Personal-contract completions resets the
///     streak, same as an unmet Directives Fax shift; an account absent the WHOLE shift never appears
///     in the roster and is therefore never touched at all here — absence still never resets a streak.
///
///     <see cref="RecordContractStreakOutcomesAsync"/> folds an entire roster in one SQLite transaction
///     (atomic with respect to partial account folds). Order-safe and idempotent per account: any write
///     whose <c>roundId</c> is not strictly greater than the row's <c>last_round_id</c> (same-round replay
///     OR a delayed older round) returns the existing row with <c>WasReplay=true</c> rather than
///     double-incrementing or overwriting newer state.
///
///     Durability: fold intent is journaled in <c>pending_contracts_streak_folds</c> inside the
///     canonical envelope transaction. This method marks those rows consumed in the same TX as the
///     streak writes. <see cref="RecoverPendingContractStreakFoldsAsync"/> replays unconsumed journals.
/// </summary>
public sealed partial class SeasonLedgerStore
{
    /// <summary>
    ///     Test hook: invoked after each account write inside
    ///     <see cref="RecordContractStreakOutcomesAsync"/>'s open transaction (index is 0-based). Throw
    ///     to force a rollback and prove the batch is atomic. Production leaves this null.
    /// </summary>
    internal Func<int, Task>? ContractStreakAfterAccountHook { get; set; }

    /// <summary>
    ///     Records this shift's contracts-streak outcome for one account and returns its updated streak.
    ///     Convenience wrapper over <see cref="RecordContractStreakOutcomesAsync"/> (single-row batch).
    ///     Write-before-dispatch: callers must await this and only act on milestone thresholds using
    ///     the returned <c>CurrentStreak</c> when <c>WasReplay</c> is false — never re-notify a replay.
    /// </summary>
    public async Task<(int CurrentStreak, int BestStreak, bool IsNewBest, bool WasReplay)> RecordContractStreakOutcomeAsync(
        Guid user,
        bool completedThisRound,
        int roundId)
    {
        var results = await RecordContractStreakOutcomesAsync(
            new[] { new ContractStreakFoldRequest(user, completedThisRound) },
            roundId);
        var r = results[0];
        return (r.CurrentStreak, r.BestStreak, r.IsNewBest, r.WasReplay);
    }

    /// <summary>
    ///     Atomically folds every account's contracts-streak outcome for this round in one IMMEDIATE
    ///     transaction. Either all accounts are committed together or none are — never a permanent
    ///     partial fold. Returns one result per input (same order), including replay no-ops.
    ///     Also marks any matching <c>pending_contracts_streak_folds</c> rows for <paramref name="roundId"/>
    ///     as consumed in the same transaction so a crash between envelope commit and fold cannot
    ///     permanently lose the journal, and recovery can re-apply unconsumed rows exactly once.
    /// </summary>
    public async Task<IReadOnlyList<ContractStreakFoldResult>> RecordContractStreakOutcomesAsync(
        IReadOnlyList<ContractStreakFoldRequest> folds,
        int roundId)
    {
        ArgumentNullException.ThrowIfNull(folds);

        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var tx = conn.BeginTransaction(deferred: false);

            // Fail closed BEFORE applying anything: if this round has journal rows (consumed or
            // not) whose full set differs from the supplied folds, a mismatched/subset batch could
            // apply itself and consume the journal — destroying the original set's recovery path.
            var journaled = await ReadUnconsumedContractStreakFoldsForRoundAsync(
                conn, tx, roundId, default, includeConsumed: true);
            if (journaled.Count > 0 && !ContractStreakFoldsEqual(journaled, folds))
            {
                throw new ContractStreakFoldConflictException(
                    roundId,
                    $"Supplied contracts-streak folds conflict with the journaled set for round {roundId}; " +
                    "nothing applied, journal preserved for recovery.");
            }

            var results = new List<ContractStreakFoldResult>(folds.Count);
            var nowUtc = DateTime.UtcNow.ToString("o");

            for (var i = 0; i < folds.Count; i++)
            {
                var fold = folds[i];
                var result = await ApplyContractStreakOutcomeAsync(
                    conn,
                    tx,
                    fold.User,
                    fold.CompletedThisRound,
                    roundId,
                    nowUtc);
                results.Add(result);

                if (ContractStreakAfterAccountHook is { } hook)
                    await hook(i);
            }

            // Consume the durable journal in the SAME transaction as the streak writes.
            await MarkPendingContractStreakFoldsConsumedAsync(conn, tx, roundId, nowUtc);

            await tx.CommitAsync();
            return results;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     Journals fold intent for <paramref name="roundId"/> on an already-open IMMEDIATE transaction
    ///     (the canonical envelope TX). INSERT OR IGNORE so a same-round retry never duplicates.
    ///     Full-set reconciliation: if an existing unconsumed journal for this round is non-empty and
    ///     differs from the supplied non-empty intents, fail closed (throw) so the caller cannot apply
    ///     conflicting folds and consume the original journal. Identical or empty-supplied proceed
    ///     idempotently.
    ///     Caller owns lock + commit.
    /// </summary>
    internal static async Task JournalPendingContractStreakFoldsAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int roundId,
        IReadOnlyList<ContractStreakFoldRequest> folds,
        DateTime nowUtc,
        System.Threading.CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folds);
        if (folds.Count == 0)
            return;

        // After spool ack, exact-envelope replays can supply different folds (Connected is excluded
        // from the envelope hash). INSERT OR IGNORE alone would keep the original journal while the
        // caller applied its conflicting set and consumed every journal row — fail closed instead.
        // Reconcile against ALL journal rows for the round (consumed AND unconsumed): a consumed-A
        // history must still veto a conflicting replay-B, or B silently wins after A's consumption.
        var existing = await ReadUnconsumedContractStreakFoldsForRoundAsync(
            conn, tx, roundId, cancellationToken, includeConsumed: true);
        if (existing.Count > 0 && !ContractStreakFoldsEqual(existing, folds))
        {
            throw new ContractStreakFoldConflictException(
                roundId,
                $"Conflicting pending contracts-streak fold journal for round {roundId}.");
        }

        var utc = nowUtc.ToString("o");
        foreach (var fold in folds)
        {
            await using var write = conn.CreateCommand();
            write.Transaction = tx;
            write.CommandText = """
                INSERT OR IGNORE INTO pending_contracts_streak_folds
                    (round_id, user_id, completed_this_round, created_utc, consumed_utc)
                VALUES ($rid, $uid, $done, $utc, NULL);
                """;
            write.Parameters.AddWithValue("$rid", roundId);
            write.Parameters.AddWithValue("$uid", fold.User.ToString());
            write.Parameters.AddWithValue("$done", fold.CompletedThisRound ? 1 : 0);
            write.Parameters.AddWithValue("$utc", utc);
            await write.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task MarkPendingContractStreakFoldsConsumedAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int roundId,
        string nowUtc)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            UPDATE pending_contracts_streak_folds
            SET consumed_utc = $utc
            WHERE round_id = $rid AND consumed_utc IS NULL;
            """;
        cmd.Parameters.AddWithValue("$utc", nowUtc);
        cmd.Parameters.AddWithValue("$rid", roundId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    ///     Recovery: applies every unconsumed fold journal (envelope committed, fold TX failed or
    ///     process died between commits). Idempotent — already-folded accounts return WasReplay via
    ///     last_round_id; journal rows are marked consumed in the fold TX. Returns how many rounds
    ///     were recovered.
    /// </summary>
    public async Task<int> RecoverPendingContractStreakFoldsAsync()
    {
        List<(int RoundId, List<ContractStreakFoldRequest> Folds)> pending;
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            pending = await ReadUnconsumedContractStreakFoldsAsync(conn, tx: null);
        }
        finally
        {
            _lock.Release();
        }

        var recovered = 0;
        foreach (var (roundId, folds) in pending)
        {
            if (folds.Count == 0)
                continue;
            // Re-enters the lock via RecordContractStreakOutcomesAsync; applies + marks consumed.
            await RecordContractStreakOutcomesAsync(folds, roundId);
            recovered++;
        }

        return recovered;
    }

    /// <summary>
    ///     Test/diagnostics: true when any unconsumed fold journal remains for <paramref name="roundId"/>.
    /// </summary>
    public async Task<bool> HasUnconsumedContractStreakFoldsAsync(int roundId)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT 1 FROM pending_contracts_streak_folds
                WHERE round_id = $rid AND consumed_utc IS NULL
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$rid", roundId);
            var scalar = await cmd.ExecuteScalarAsync();
            return scalar is not null and not DBNull;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task<List<(int RoundId, List<ContractStreakFoldRequest> Folds)>> ReadUnconsumedContractStreakFoldsAsync(
        SqliteConnection conn,
        SqliteTransaction? tx)
    {
        var byRound = new Dictionary<int, List<ContractStreakFoldRequest>>();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT round_id, user_id, completed_this_round
            FROM pending_contracts_streak_folds
            WHERE consumed_utc IS NULL
            ORDER BY round_id, user_id COLLATE BINARY;
            """;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var roundId = reader.GetInt32(0);
            if (!Guid.TryParse(reader.GetString(1), out var user))
                continue;
            var completed = reader.GetInt32(2) != 0;
            if (!byRound.TryGetValue(roundId, out var list))
            {
                list = new List<ContractStreakFoldRequest>();
                byRound[roundId] = list;
            }

            list.Add(new ContractStreakFoldRequest(user, completed));
        }

        return byRound
            .OrderBy(kv => kv.Key)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
    }

    private static async Task<List<ContractStreakFoldRequest>> ReadUnconsumedContractStreakFoldsForRoundAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        int roundId,
        System.Threading.CancellationToken cancellationToken,
        bool includeConsumed = false)
    {
        var list = new List<ContractStreakFoldRequest>();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = includeConsumed
            ? """
              SELECT user_id, completed_this_round
              FROM pending_contracts_streak_folds
              WHERE round_id = $rid
              ORDER BY user_id COLLATE BINARY;
              """
            : """
              SELECT user_id, completed_this_round
              FROM pending_contracts_streak_folds
              WHERE round_id = $rid AND consumed_utc IS NULL
              ORDER BY user_id COLLATE BINARY;
              """;
        cmd.Parameters.AddWithValue("$rid", roundId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!Guid.TryParse(reader.GetString(0), out var user))
                continue;
            list.Add(new ContractStreakFoldRequest(user, reader.GetInt32(1) != 0));
        }

        return list;
    }

    /// <summary>
    ///     Full-set equality for fold intents (order-insensitive). Matches spool-side
    ///     <c>PendingFoldsEqual</c> normalization so journal and spool conflict rules agree.
    /// </summary>
    private static bool ContractStreakFoldsEqual(
        IReadOnlyList<ContractStreakFoldRequest> left,
        IReadOnlyList<ContractStreakFoldRequest> right)
    {
        var a = NormalizeContractStreakFolds(left);
        var b = NormalizeContractStreakFolds(right);
        if (a.Count != b.Count)
            return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
                return false;
        }

        return true;
    }

    private static List<ContractStreakFoldRequest> NormalizeContractStreakFolds(
        IReadOnlyList<ContractStreakFoldRequest> folds)
    {
        return folds
            .OrderBy(f => f.User)
            .ThenBy(f => f.CompletedThisRound)
            .ToList();
    }

    private static async Task<ContractStreakFoldResult> ApplyContractStreakOutcomeAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        Guid user,
        bool completedThisRound,
        int roundId,
        string nowUtc)
    {
        var currentStreak = 0;
        var bestStreak = 0;
        var lastRoundId = 0;
        var hasRow = false;

        await using (var read = conn.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = """
                SELECT current_streak, best_streak, last_round_id FROM contracts_streak
                WHERE user_id = $uid;
                """;
            read.Parameters.AddWithValue("$uid", user.ToString());

            await using var reader = await read.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                hasRow = true;
                currentStreak = reader.GetInt32(0);
                bestStreak = reader.GetInt32(1);
                lastRoundId = reader.GetInt32(2);
            }
        }

        // Order-safe replay guard: reject same-round retries AND any delayed older round that
        // would otherwise overwrite a newer fold and permit later double-increment/reset.
        if (hasRow && lastRoundId >= roundId)
        {
            return new ContractStreakFoldResult(
                user,
                currentStreak,
                bestStreak,
                IsNewBest: false,
                WasReplay: true,
                completedThisRound);
        }

        var newStreak = completedThisRound ? currentStreak + 1 : 0;
        var newBest = Math.Max(bestStreak, newStreak);
        var isNewBest = newStreak > 0 && newStreak > bestStreak;

        await using (var write = conn.CreateCommand())
        {
            write.Transaction = tx;
            write.CommandText = """
                INSERT INTO contracts_streak (user_id, current_streak, best_streak, last_round_id, updated_utc)
                VALUES ($uid, $cur, $best, $rid, $utc)
                ON CONFLICT(user_id) DO UPDATE SET
                    current_streak = excluded.current_streak,
                    best_streak = excluded.best_streak,
                    last_round_id = excluded.last_round_id,
                    updated_utc = excluded.updated_utc;
                """;
            write.Parameters.AddWithValue("$uid", user.ToString());
            write.Parameters.AddWithValue("$cur", newStreak);
            write.Parameters.AddWithValue("$best", newBest);
            write.Parameters.AddWithValue("$rid", roundId);
            write.Parameters.AddWithValue("$utc", nowUtc);
            await write.ExecuteNonQueryAsync();
        }

        return new ContractStreakFoldResult(
            user,
            newStreak,
            newBest,
            isNewBest,
            WasReplay: false,
            completedThisRound);
    }

    /// <summary>Read surface for tests and any future display (e.g. a character-sheet stat). Zeroes for
    /// an account with no row yet.</summary>
    public async Task<(int CurrentStreak, int BestStreak)> GetContractStreakAsync(Guid user)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT current_streak, best_streak FROM contracts_streak
                WHERE user_id = $uid;
                """;
            cmd.Parameters.AddWithValue("$uid", user.ToString());

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return (reader.GetInt32(0), reader.GetInt32(1));

            return (0, 0);
        }
        finally
        {
            _lock.Release();
        }
    }
}
