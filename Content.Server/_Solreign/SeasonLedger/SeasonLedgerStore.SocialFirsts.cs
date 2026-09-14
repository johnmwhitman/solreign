using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     "Social firsts" persistence (council memo docs/council/2026-07-16-design-magnetism.md item 5):
///     once-per-account-EVER milestone flags for the small social celebrations — first chirp
///     answered, first time healed by another player, first item handed to you, the one-time
///     third-visit wingmate volunteer prompt. The <c>social_firsts</c> table (schema battery in
///     SeasonLedgerStore.cs) generalizes the <c>first_death</c> pattern from "one row per account"
///     to "one row per (account, flag)":
///       * <see cref="TryClaimSocialFirstAsync"/> is the same atomic INSERT-if-absent linchpin as
///         <see cref="TryClaimFirstDeathAsync"/> — two racing detectors both reach it, SQLite's
///         <c>ON CONFLICT DO NOTHING</c> + rowcount check hands the claim to exactly one, and
///         callers only dispatch the toast on <c>true</c> (write-before-dispatch: worst case a
///         toast is swallowed by a disconnect, never duplicated).
///       * Deliberately NOT season-scoped, exactly like <c>first_death</c>: a social first is a
///         career event. A season bump does not un-meet your first friend.
///
///     Flag ids are a closed vocabulary owned by
///     <c>Content.Server._Solreign.Social.SolreignSocialFirstFlags</c> — never free text and never
///     player-authored.
/// </summary>
public sealed partial class SeasonLedgerStore
{
    /// <summary>
    ///     Atomically claims the once-per-account-EVER slot for one social-first flag. Returns
    ///     <c>true</c> exactly once per (account, flag), for the caller that won the INSERT; every
    ///     later (or racing-and-lost) call returns <c>false</c>. Callers must only dispatch the
    ///     celebration on <c>true</c>.
    /// </summary>
    public async Task<bool> TryClaimSocialFirstAsync(Guid user, string flagId, int roundId)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO social_firsts (user_id, flag_id, round_id, claimed_utc)
                VALUES ($uid, $flag, $rid, $utc)
                ON CONFLICT(user_id, flag_id) DO NOTHING;
                """;
            cmd.Parameters.AddWithValue("$uid", user.ToString());
            cmd.Parameters.AddWithValue("$flag", flagId);
            cmd.Parameters.AddWithValue("$rid", roundId);
            cmd.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("o"));
            return await cmd.ExecuteNonQueryAsync() == 1;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     Returns every social-first flag id the account has ever claimed (career-scoped, no season
    ///     filter). Read surface for tests and for cheap "already done" pre-checks.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetSocialFirstFlagsAsync(Guid user)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT flag_id FROM social_firsts
                WHERE user_id = $uid
                ORDER BY flag_id;
                """;
            cmd.Parameters.AddWithValue("$uid", user.ToString());

            var flags = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                flags.Add(reader.GetString(0));
            }

            return flags;
        }
        finally
        {
            _lock.Release();
        }
    }
}
