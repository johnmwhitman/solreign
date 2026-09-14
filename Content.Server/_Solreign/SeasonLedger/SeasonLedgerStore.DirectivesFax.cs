using System;
using System.Threading.Tasks;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     Persistent per-account compliance streak for Directives Fax (v14 wave-1 item #1). The
///     <c>directives_fax_streak</c> table (schema battery in SeasonLedgerStore.cs) holds one row per
///     account — deliberately NOT season-scoped, same career-scoping law as <c>first_death</c> /
///     <c>social_firsts</c> / <c>mark</c>: a season bump does not un-comply anyone.
///
///     Streak semantics (the presence-aware discipline: never punish an absent player):
///       * present ≥ N minutes this shift AND the directive's clauses were met -&gt; streak + 1
///       * present ≥ N minutes this shift AND the clauses were NOT met -&gt; streak resets to 0
///       * NOT present ≥ N minutes (absent, or left early) -&gt; the caller never invokes this method for
///         that account at all (see <c>DirectivesFaxRuleSystem.SnapshotPresentEligibleAccounts</c>), so
///         the row — and therefore the streak — is simply untouched. Absence never resets a streak.
///
///     <see cref="RecordDirectivesFaxOutcomeAsync"/> is idempotent per (account, round id): a retried
///     write for a round already recorded returns the existing row unchanged rather than
///     double-incrementing — mirrors the round-envelope replay-guard's spirit at table scope, without
///     needing the full envelope machinery for a single additive counter.
/// </summary>
public sealed partial class SeasonLedgerStore
{
    /// <summary>
    ///     Records this shift's directive outcome for one account and returns its updated streak.
    ///     Write-before-dispatch: callers must await this and only act on milestone thresholds using
    ///     the returned <c>CurrentStreak</c> — never fire a milestone toast before this completes.
    /// </summary>
    public async Task<(int CurrentStreak, int BestStreak, bool IsNewBest)> RecordDirectivesFaxOutcomeAsync(Guid user, bool met, int roundId)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();

            var currentStreak = 0;
            var bestStreak = 0;
            var lastRoundId = 0;
            var hasRow = false;

            await using (var read = conn.CreateCommand())
            {
                read.CommandText = """
                    SELECT current_streak, best_streak, last_round_id FROM directives_fax_streak
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

            // Idempotent replay guard: this exact round was already folded for this account.
            if (hasRow && lastRoundId == roundId)
                return (currentStreak, bestStreak, false);

            var newStreak = met ? currentStreak + 1 : 0;
            var newBest = Math.Max(bestStreak, newStreak);
            var isNewBest = newStreak > 0 && newStreak > bestStreak;

            await using (var write = conn.CreateCommand())
            {
                write.CommandText = """
                    INSERT INTO directives_fax_streak (user_id, current_streak, best_streak, last_round_id, updated_utc)
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
                write.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("o"));
                await write.ExecuteNonQueryAsync();
            }

            return (newStreak, newBest, isNewBest);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Read surface for tests and any future display (e.g. a character-sheet stat). Zeroes for
    /// an account with no row yet.</summary>
    public async Task<(int CurrentStreak, int BestStreak)> GetDirectivesFaxStreakAsync(Guid user)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT current_streak, best_streak FROM directives_fax_streak
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
