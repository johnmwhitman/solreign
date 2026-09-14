using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Content.Shared._Solreign.Corporate.Projects;
using Microsoft.Data.Sqlite;

namespace Content.Server._Solreign.SeasonLedger;

public sealed record CorporateProjectRecord(
    string ProjectId,
    int TotalContribution,
    bool Completed,
    List<string> UnlockedMilestones,
    string UpdatedUtc);

public sealed record CorporateProjectContributionRecord(
    string ProjectId,
    Guid User,
    int AccountContribution,
    string LastContributedUtc);

public enum CorporateProjectContributionResultKind
{
    Success,
    ProjectNotFound,
    AlreadyCompleted,
    InvalidAmount,
    AccountCapReached,
}

public sealed record CorporateProjectContributionResult(
    CorporateProjectContributionResultKind Kind,
    int AmountGranted,
    int NewTotalContribution,
    int NewAccountContribution,
    bool ProjectNowCompleted,
    List<string> NewlyUnlockedMilestones);

/// <summary>
///     Persistence for Corporate Projects (SR-W-030).
///     Enforces bounded contributions, anti-monopoly caps per account, round survival, and milestone unlocks inside atomic transactions.
/// </summary>
public sealed partial class SeasonLedgerStore
{
    private static async Task EnsureCorporateProjectsSchemaAsync(SqliteConnection conn, SqliteTransaction tx)
    {
        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS corporate_projects (
                    project_id TEXT PRIMARY KEY,
                    total_contribution INT NOT NULL DEFAULT 0,
                    completed INT NOT NULL DEFAULT 0,
                    unlocked_milestones_json TEXT NOT NULL DEFAULT '[]',
                    updated_utc TEXT NOT NULL
                );
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS corporate_project_contributions (
                    project_id TEXT NOT NULL,
                    user_id TEXT NOT NULL,
                    account_contribution INT NOT NULL DEFAULT 0,
                    last_contributed_utc TEXT NOT NULL,
                    PRIMARY KEY(project_id, user_id)
                );
                """;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task<CorporateProjectRecord?> GetCorporateProjectRecordAsync(string projectId)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var tx = conn.BeginTransaction(deferred: false);
            await EnsureCorporateProjectsSchemaAsync(conn, tx);

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT project_id, total_contribution, completed, unlocked_milestones_json, updated_utc
                FROM corporate_projects
                WHERE project_id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", projectId);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                await tx.CommitAsync();
                return null;
            }

            var record = ReadCorporateProjectRecord(reader);
            await tx.CommitAsync();
            return record;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Dictionary<string, CorporateProjectRecord>> GetAllCorporateProjectRecordsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var tx = conn.BeginTransaction(deferred: false);
            await EnsureCorporateProjectsSchemaAsync(conn, tx);

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT project_id, total_contribution, completed, unlocked_milestones_json, updated_utc
                FROM corporate_projects;
                """;

            var result = new Dictionary<string, CorporateProjectRecord>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var rec = ReadCorporateProjectRecord(reader);
                result[rec.ProjectId] = rec;
            }

            await tx.CommitAsync();
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<int> GetAccountContributionAsync(string projectId, Guid user)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var tx = conn.BeginTransaction(deferred: false);
            await EnsureCorporateProjectsSchemaAsync(conn, tx);

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT account_contribution FROM corporate_project_contributions
                WHERE project_id = $id AND user_id = $uid;
                """;
            cmd.Parameters.AddWithValue("$id", projectId);
            cmd.Parameters.AddWithValue("$uid", user.ToString());

            var raw = await cmd.ExecuteScalarAsync();
            await tx.CommitAsync();

            return raw != null && raw != DBNull.Value ? Convert.ToInt32(raw) : 0;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<CorporateProjectContributionRecord>> GetProjectContributorsAsync(string projectId)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var tx = conn.BeginTransaction(deferred: false);
            await EnsureCorporateProjectsSchemaAsync(conn, tx);

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT project_id, user_id, account_contribution, last_contributed_utc
                FROM corporate_project_contributions
                WHERE project_id = $id AND account_contribution > 0;
                """;
            cmd.Parameters.AddWithValue("$id", projectId);

            var list = new List<CorporateProjectContributionRecord>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new CorporateProjectContributionRecord(
                    reader.GetString(0),
                    Guid.Parse(reader.GetString(1)),
                    reader.GetInt32(2),
                    reader.GetString(3)));
            }

            await tx.CommitAsync();
            return list;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<CorporateProjectContributionResult> TryContributeToProjectAsync(
        string projectId,
        Guid user,
        int requestedAmount,
        string nowUtc,
        SolreignCorporateProjectPrototype prototype)
    {
        if (requestedAmount <= 0)
        {
            return new CorporateProjectContributionResult(
                CorporateProjectContributionResultKind.InvalidAmount, 0, 0, 0, false, new List<string>());
        }

        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var tx = conn.BeginTransaction(deferred: false);
            await EnsureCorporateProjectsSchemaAsync(conn, tx);

            // 1. Read existing project record
            int currentTotal = 0;
            bool isCompleted = false;
            var unlockedMilestones = new List<string>();

            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    SELECT total_contribution, completed, unlocked_milestones_json
                    FROM corporate_projects WHERE project_id = $id;
                    """;
                cmd.Parameters.AddWithValue("$id", projectId);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    currentTotal = reader.GetInt32(0);
                    isCompleted = reader.GetInt32(1) != 0;
                    var json = reader.GetString(2);
                    unlockedMilestones = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                }
            }

            if (isCompleted || currentTotal >= prototype.TargetContribution)
            {
                await tx.CommitAsync();
                return new CorporateProjectContributionResult(
                    CorporateProjectContributionResultKind.AlreadyCompleted, 0, currentTotal, 0, true, new List<string>());
            }

            // 2. Read existing account contribution
            int existingAccountTotal = 0;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    SELECT account_contribution FROM corporate_project_contributions
                    WHERE project_id = $id AND user_id = $uid;
                    """;
                cmd.Parameters.AddWithValue("$id", projectId);
                cmd.Parameters.AddWithValue("$uid", user.ToString());
                var raw = await cmd.ExecuteScalarAsync();
                if (raw != null && raw != DBNull.Value)
                {
                    existingAccountTotal = Convert.ToInt32(raw);
                }
            }

            // 3. Enforce Bounded Contribution (per action)
            int boundedAmount = Math.Min(requestedAmount, prototype.MaxContributionPerAction);

            // 4. Enforce Anti-Monopoly Account Cap
            int maxAccountCap = prototype.GetMaxAccountContribution();
            int accountRemaining = maxAccountCap - existingAccountTotal;
            if (accountRemaining <= 0)
            {
                await tx.CommitAsync();
                return new CorporateProjectContributionResult(
                    CorporateProjectContributionResultKind.AccountCapReached, 0, currentTotal, existingAccountTotal, false, new List<string>());
            }

            int granted = Math.Min(boundedAmount, accountRemaining);

            // 5. Enforce Remaining Project Target Limit
            int projectRemaining = prototype.TargetContribution - currentTotal;
            granted = Math.Min(granted, projectRemaining);

            if (granted <= 0)
            {
                await tx.CommitAsync();
                return new CorporateProjectContributionResult(
                    CorporateProjectContributionResultKind.AccountCapReached, 0, currentTotal, existingAccountTotal, false, new List<string>());
            }

            // 6. Update Totals
            int newTotal = currentTotal + granted;
            int newAccountTotal = existingAccountTotal + granted;
            bool nowCompleted = newTotal >= prototype.TargetContribution;

            // 7. Check Milestone Thresholds
            var newlyUnlocked = new List<string>();
            foreach (var milestone in prototype.Milestones)
            {
                int thresholdPoints = (int) Math.Floor(prototype.TargetContribution * milestone.ThresholdFraction);
                if (newTotal >= thresholdPoints && !unlockedMilestones.Contains(milestone.MilestoneId))
                {
                    unlockedMilestones.Add(milestone.MilestoneId);
                    newlyUnlocked.Add(milestone.MilestoneId);
                }
            }

            var updatedJson = JsonSerializer.Serialize(unlockedMilestones);

            // 8. Persist updates
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO corporate_projects (project_id, total_contribution, completed, unlocked_milestones_json, updated_utc)
                    VALUES ($id, $total, $completed, $json, $now)
                    ON CONFLICT(project_id) DO UPDATE SET
                        total_contribution = $total,
                        completed = $completed,
                        unlocked_milestones_json = $json,
                        updated_utc = $now;
                    """;
                cmd.Parameters.AddWithValue("$id", projectId);
                cmd.Parameters.AddWithValue("$total", newTotal);
                cmd.Parameters.AddWithValue("$completed", nowCompleted ? 1 : 0);
                cmd.Parameters.AddWithValue("$json", updatedJson);
                cmd.Parameters.AddWithValue("$now", nowUtc);
                await cmd.ExecuteNonQueryAsync();
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO corporate_project_contributions (project_id, user_id, account_contribution, last_contributed_utc)
                    VALUES ($id, $uid, $acct, $now)
                    ON CONFLICT(project_id, user_id) DO UPDATE SET
                        account_contribution = $acct,
                        last_contributed_utc = $now;
                    """;
                cmd.Parameters.AddWithValue("$id", projectId);
                cmd.Parameters.AddWithValue("$uid", user.ToString());
                cmd.Parameters.AddWithValue("$acct", newAccountTotal);
                cmd.Parameters.AddWithValue("$now", nowUtc);
                await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();

            return new CorporateProjectContributionResult(
                CorporateProjectContributionResultKind.Success,
                granted,
                newTotal,
                newAccountTotal,
                nowCompleted,
                newlyUnlocked);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static CorporateProjectRecord ReadCorporateProjectRecord(SqliteDataReader reader)
    {
        var json = reader.GetString(3);
        var milestones = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        return new CorporateProjectRecord(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetInt32(2) != 0,
            milestones,
            reader.GetString(4));
    }
}
