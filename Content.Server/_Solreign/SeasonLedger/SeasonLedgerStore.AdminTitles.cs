using System;
using System.Threading.Tasks;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     Admin-granted title persistence (community rewards program). A grant lives in its own
///     <c>admin_title_grants</c> table — deliberately separate from <c>title_grants</c>, which is the
///     ceremony-announcement dedup record for EARNED titles — and is season-scoped exactly like earned
///     titles: rows are keyed by (user, season), so a season bump retires the grant while leaving the old
///     season's row archived on record. Each row carries who granted it and when, for the paper trail.
/// </summary>
public sealed partial class SeasonLedgerStore
{
    /// <summary>
    ///     Records (or replaces) the admin-granted title for an account in the CURRENT season. The full
    ///     <see cref="TitleGrantRules.TryNormalize"/> policy is enforced HERE, at the persistence boundary
    ///     (not only in the console command), so no future caller — e.g. a Director/website integration —
    ///     can persist markup, control characters, or an over-length title that later reaches
    ///     <c>PushMarkup</c>. The normalized form is what gets stored.
    /// </summary>
    public async Task SetAdminTitleAsync(Guid user, string title, string grantedBy)
    {
        if (!TitleGrantRules.TryNormalize(title, out var normalized, out var problem))
            throw new ArgumentException($"Granted title rejected: {problem}", nameof(title));
        title = normalized;
        if (string.IsNullOrWhiteSpace(grantedBy))
            throw new ArgumentException("Granting operator must be recorded.", nameof(grantedBy));

        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            var season = await GetCurrentSeasonInternalAsync(conn);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO admin_title_grants (user_id, season_id, title, granted_by, granted_utc)
                VALUES ($uid, $sid, $title, $by, $utc)
                ON CONFLICT(user_id, season_id) DO UPDATE
                    SET title = $title, granted_by = $by, granted_utc = $utc;
                """;
            cmd.Parameters.AddWithValue("$uid", user.ToString());
            cmd.Parameters.AddWithValue("$sid", season);
            cmd.Parameters.AddWithValue("$title", title);
            cmd.Parameters.AddWithValue("$by", grantedBy);
            cmd.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     The admin-granted title for this account in the CURRENT season, or null when none is active.
    ///     Read at spawn by <c>SeasonLedgerSystem.LoadTitle</c> to mask the earned display title.
    /// </summary>
    public async Task<string?> GetAdminTitleAsync(Guid user)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            var season = await GetCurrentSeasonInternalAsync(conn);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT title FROM admin_title_grants WHERE user_id = $uid AND season_id = $sid;";
            cmd.Parameters.AddWithValue("$uid", user.ToString());
            cmd.Parameters.AddWithValue("$sid", season);

            return await cmd.ExecuteScalarAsync() as string;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     Removes the CURRENT season's admin-granted title for an account. Returns true when a grant
    ///     existed and was removed, false when there was nothing to revoke. Prior seasons' archived rows
    ///     are never touched.
    /// </summary>
    public async Task<bool> RevokeAdminTitleAsync(Guid user)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            var season = await GetCurrentSeasonInternalAsync(conn);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM admin_title_grants WHERE user_id = $uid AND season_id = $sid;";
            cmd.Parameters.AddWithValue("$uid", user.ToString());
            cmd.Parameters.AddWithValue("$sid", season);

            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        finally
        {
            _lock.Release();
        }
    }
}
