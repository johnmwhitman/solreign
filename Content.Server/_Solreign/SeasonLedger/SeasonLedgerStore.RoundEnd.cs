using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Content.Server._Solreign.SeasonLedger;

public sealed partial class SeasonLedgerStore
{
    public async Task<RoundEndPersistenceResult> PersistRoundEndAsync(
        RoundEndEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        return await PersistRoundEndAsync(envelope, pendingStreakFolds: null, cancellationToken);
    }

    /// <summary>
    ///     Canonical envelope commit; when <paramref name="pendingStreakFolds"/> is non-null/non-empty,
    ///     stages those fold intents on the durable spool with the envelope and journals them inside
    ///     the same IMMEDIATE transaction as the envelope so a subsequent fold failure (or crash
    ///     between stage and commit, or between commits) is recoverable via spool recovery +
    ///     <see cref="RecoverPendingContractStreakFoldsAsync"/>. Exact-envelope
    ///     <see cref="RoundEndPersistenceStatus.AlreadyCommitted"/> replays still journal supplied
    ///     intents first (INSERT OR IGNORE) so a journal gap cannot permanently lose folds.
    /// </summary>
    public async Task<RoundEndPersistenceResult> PersistRoundEndAsync(
        RoundEndEnvelope envelope,
        IReadOnlyList<ContractStreakFoldRequest>? pendingStreakFolds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var captured = envelope.Canonicalize();

        await using var lease = await _spool.AcquireLeaseAsync(cancellationToken);
        var entry = _spool.StageOrReuse(captured, pendingStreakFolds);
        _roundEnvelopeSpoolFaultInjector.Hit(RoundEnvelopeSpoolStage.AfterDurableStage);
        // Always persist using the reconciled entry's folds — never the caller's raw list —
        // so a foldless retry cannot commit-and-acknowledge an entry whose durable folds were
        // the only recovery copy (Finding 4 / ROUND-6).
        return await PersistStagedAsync(entry, captured, entry.PendingStreakFolds, cancellationToken);
    }

    public async Task<LedgerRecoverySweepResult> RecoverPendingRoundEndsAsync(
        int maxEntries = 8,
        CancellationToken cancellationToken = default)
    {
        var observation = await RecoverPendingRoundEndsWithHealthAsync(
            maxEntries,
            DateOnly.FromDateTime(DateTime.UtcNow),
            cancellationToken);
        return observation.Sweep;
    }

    internal async Task<LedgerRecoverySweepObservation> RecoverPendingRoundEndsWithHealthAsync(
        int maxEntries,
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        if (maxEntries < 0)
            throw new ArgumentOutOfRangeException(nameof(maxEntries));

        var examined = 0;
        var acknowledged = 0;
        var faulted = 0;
        await using var lease = await _spool.AcquireLeaseAsync(cancellationToken);
        if (maxEntries > 0)
        {
            foreach (var entry in _spool.ReadPendingEntries(maxEntries))
            {
                cancellationToken.ThrowIfCancellationRequested();
                examined++;

                var captured = SeasonLedgerSpool.ValidateAndCapture(entry);
                try
                {
                    // Finding 4: re-supply fold intents staged on the spool entry so a crash after
                    // durable stage but before envelope persist cannot permanently lose folds.
                    var result = await PersistStagedAsync(entry, captured, entry.PendingStreakFolds, cancellationToken);
                    if (result.Status is RoundEndPersistenceStatus.Committed or RoundEndPersistenceStatus.AlreadyCommitted)
                        acknowledged++;
                }
                catch (RoundEndReplayConflictException)
                {
                    // PersistStagedAsync records the fail-closed state. Recovery continues with other bounded entries.
                }
                catch (ContractStreakFoldConflictException)
                {
                    // Journal-fold conflict: PersistStagedAsync already marked the entry terminal Conflict.
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException
                                              and not AccessViolationException)
                {
                    // A non-retryable failure must not abort the bounded sweep. Persist a best-effort
                    // fault disposition, while keeping the entry retryable and allowing the healthy tail.
                    try
                    {
                        _spool.Update(entry with
                        {
                            Attempts = Math.Min(entry.Attempts + 1, 1_000_000),
                            State = LedgerRecoveryState.Pending,
                            ErrorCategory = LedgerRecoveryErrorCategory.StorageFaulted,
                        });
                    }
                    catch (Exception dispositionFailure) when (dispositionFailure
                        is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
                    {
                        // Best-effort and sanitized-silent; the bounded fault counter is the signal.
                    }
                    faulted++;
                }
            }
        }

        var unresolved = _spool.Snapshot();
        var sweep = new LedgerRecoverySweepResult(
            examined,
            acknowledged,
            unresolved.Count,
            unresolved.FirstOrDefault(),
            faulted);
        var health = SeasonLedgerRecoveryMetrics.Summarize(unresolved, sweep, today);
        return new LedgerRecoverySweepObservation(sweep, health);
    }

    public async Task<RoundEndPersistenceResult> RecoverPendingRoundEndAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await _spool.AcquireLeaseAsync(cancellationToken);
        var entry = _spool.ReadPendingEntry(token) ??
                    throw new KeyNotFoundException("Recovery token was not found.");
        if (entry.State != LedgerRecoveryState.Pending)
            throw new InvalidOperationException("Recovery token is not retryable.");

        var captured = SeasonLedgerSpool.ValidateAndCapture(entry);
        // Finding 4: re-supply fold intents staged on the spool entry.
        return await PersistStagedAsync(entry, captured, entry.PendingStreakFolds, cancellationToken);
    }

    public IReadOnlyList<LedgerRecoverySnapshotItem> GetRecoverySnapshot()
    {
        using var lease = _spool.AcquireLease();
        return _spool.Snapshot();
    }

    public async Task<IReadOnlyList<LedgerRecoverySnapshotItem>> GetRecoverySnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        await using var lease = await _spool.AcquireLeaseAsync(cancellationToken);
        return _spool.Snapshot();
    }

    internal void ArchivePendingRoundEnd(string token, string reasonCode)
    {
        using var lease = _spool.AcquireLease();
        _spool.Archive(token, reasonCode, _roundEnvelopeSpoolFaultInjector);
    }

    internal async Task ArchivePendingRoundEndAsync(
        string token,
        string reasonCode,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await _spool.AcquireLeaseAsync(cancellationToken);
        _spool.Archive(token, reasonCode, _roundEnvelopeSpoolFaultInjector);
    }

    private async Task<RoundEndPersistenceResult> PersistStagedAsync(
        SpoolEntry entry,
        CapturedRoundEndEnvelope captured,
        IReadOnlyList<ContractStreakFoldRequest>? pendingStreakFolds,
        CancellationToken cancellationToken)
    {
        var attempts = 0;
        try
        {
            RoundEnvelopePersistResult persisted;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempts++;
                try
                {
                    persisted = await PersistRoundEndOnceAsync(captured, pendingStreakFolds, cancellationToken);
                    break;
                }
                catch (SqliteException exception) when (LedgerRetryPolicy.IsRetryable(exception) && attempts < 4)
                {
                    await _ledgerRetryRuntime.DelayAsync(LedgerRetryPolicy.Delays[attempts - 1], cancellationToken);
                }
                catch (SqliteException exception) when (LedgerRetryPolicy.IsRetryable(exception))
                {
                    var pending = entry with
                    {
                        Attempts = checked(entry.Attempts + attempts),
                        State = LedgerRecoveryState.Pending,
                        ErrorCategory = LedgerRecoveryErrorCategory.DatabaseBusy,
                    };
                    _spool.Update(pending);
                    return new RoundEndPersistenceResult(
                        RoundEndPersistenceStatus.Pending,
                        entry.Token,
                        attempts,
                        LedgerRecoveryErrorCategory.DatabaseBusy);
                }
            }

            var committedEntry = entry with
            {
                Attempts = checked(entry.Attempts + attempts),
                State = LedgerRecoveryState.Pending,
                ErrorCategory = LedgerRecoveryErrorCategory.None,
            };
            _spool.Update(committedEntry);
            _roundEnvelopeSpoolFaultInjector.Hit(RoundEnvelopeSpoolStage.BeforeAcknowledge);
            _spool.Acknowledge(entry.Token);
            return new RoundEndPersistenceResult(
                persisted == RoundEnvelopePersistResult.Committed
                    ? RoundEndPersistenceStatus.Committed
                    : RoundEndPersistenceStatus.AlreadyCommitted,
                entry.Token,
                attempts,
                LedgerRecoveryErrorCategory.None);
        }
        catch (RoundEndReplayConflictException)
        {
            _spool.Update(entry with
            {
                Attempts = checked(entry.Attempts + attempts),
                State = LedgerRecoveryState.Conflict,
                ErrorCategory = LedgerRecoveryErrorCategory.ReplayConflict,
            });
            throw;
        }
        catch (ContractStreakFoldConflictException)
        {
            // ROUND-9: journal-fold conflict after durable stage must go terminal Conflict —
            // never leave Pending for recovery to infinite-retry a deterministic conflict.
            _spool.Update(entry with
            {
                Attempts = checked(entry.Attempts + attempts),
                State = LedgerRecoveryState.Conflict,
                ErrorCategory = LedgerRecoveryErrorCategory.ReplayConflict,
            });
            throw;
        }
    }

    internal async Task<RoundEnvelopePersistResult> PersistRoundEndOnceAsync(
        CapturedRoundEndEnvelope captured,
        IReadOnlyList<ContractStreakFoldRequest>? pendingStreakFolds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(captured);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(requireWriterAvailable: true);
            _roundEnvelopeSpoolFaultInjector.Hit(
                RoundEnvelopeSpoolStage.AfterWriterProbeBeforeManagedReservation);
            await using var transaction = connection.BeginTransaction(deferred: false);

            var marker = await ReadRoundMarkerAsync(connection, transaction, captured.RoundId, cancellationToken);
            if (marker != null)
            {
                var replay = BoundCanonicalRoundEndEnvelope.Bind(captured, marker.Value.SeasonId);
                if (marker.Value.SchemaVersion == CapturedRoundEndEnvelope.SchemaVersion &&
                    string.Equals(marker.Value.EnvelopeHash, replay.EnvelopeHash, StringComparison.Ordinal))
                {
                    // Finding 4: journal supplied fold intents BEFORE returning AlreadyCommitted so a
                    // replayed exact envelope still folds exactly once (INSERT OR IGNORE is idempotent).
                    if (pendingStreakFolds is { Count: > 0 })
                    {
                        await JournalPendingContractStreakFoldsAsync(
                            connection,
                            transaction,
                            captured.RoundId,
                            pendingStreakFolds,
                            DateTime.UtcNow,
                            cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                    }
                    else
                    {
                        await transaction.RollbackAsync(cancellationToken);
                    }

                    return RoundEnvelopePersistResult.AlreadyCommitted;
                }

                throw new RoundEndReplayConflictException(captured.RoundId);
            }

            var legacySeason = await FindLegacySeasonAsync(
                connection,
                transaction,
                captured.RoundId,
                cancellationToken);
            var seasonId = legacySeason ?? await GetCurrentSeasonInTransactionAsync(
                connection,
                transaction,
                cancellationToken);
            var envelope = BoundCanonicalRoundEndEnvelope.Bind(captured, seasonId);

            var existingPlayers = await ValidateLegacyPlayerRowsAsync(
                connection,
                transaction,
                envelope,
                cancellationToken);
            var hasCompleteContractMultiset = await ValidateLegacyContractRowsAsync(
                connection,
                transaction,
                envelope,
                cancellationToken);

            var now = DateTime.UtcNow;
            var mutationIndex = 0;
            foreach (var player in envelope.Players)
            {
                if (existingPlayers.Contains(player.User))
                    continue;

                await InsertRoundLogAsync(connection, transaction, envelope, player, now, cancellationToken);
                await UpsertPlayerStatsAsync(connection, transaction, envelope.SeasonId, player, cancellationToken);
                _roundEnvelopeFaultInjector.Hit(RoundEnvelopeStage.AfterPlayerMutation, mutationIndex++);
            }

            if (!hasCompleteContractMultiset)
            {
                foreach (var contract in envelope.Contracts)
                {
                    await InsertContractLogAsync(
                        connection,
                        transaction,
                        envelope,
                        contract,
                        now,
                        cancellationToken);
                }
            }

            await InsertPrivateOutboxAsync(connection, transaction, envelope, now, cancellationToken);
            await InsertRoundMarkerAsync(connection, transaction, envelope, now, cancellationToken);

            // Durability seam: journal fold intent inside the envelope TX. Fold TX marks consumed;
            // if the fold TX fails, recovery replays unconsumed journals exactly once.
            if (pendingStreakFolds is { Count: > 0 })
            {
                await JournalPendingContractStreakFoldsAsync(
                    connection, transaction, captured.RoundId, pendingStreakFolds, now, cancellationToken);
            }

            _roundEnvelopeFaultInjector.Hit(RoundEnvelopeStage.BeforeCommit, 0);
            await transaction.CommitAsync(cancellationToken);
            return RoundEnvelopePersistResult.Committed;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static void VerifyWriterAvailable(SqliteConnection connection)
    {
        SQLitePCL.raw.sqlite3_busy_timeout(connection.Handle, 0);
        var beginResult = SQLitePCL.raw.sqlite3_exec(
            connection.Handle,
            "BEGIN IMMEDIATE;",
            null,
            null,
            out _);
        if (beginResult != SQLitePCL.raw.SQLITE_OK)
        {
            var primary = SQLitePCL.raw.sqlite3_errcode(connection.Handle);
            var extended = SQLitePCL.raw.sqlite3_extended_errcode(connection.Handle);
            throw new SqliteException("Season Ledger writer is unavailable.", primary, extended);
        }

        var rollbackResult = SQLitePCL.raw.sqlite3_exec(
            connection.Handle,
            "ROLLBACK;",
            null,
            null,
            out _);
        if (rollbackResult != SQLitePCL.raw.SQLITE_OK)
        {
            var primary = SQLitePCL.raw.sqlite3_errcode(connection.Handle);
            var extended = SQLitePCL.raw.sqlite3_extended_errcode(connection.Handle);
            throw new SqliteException("Season Ledger writer probe cleanup failed.", primary, extended);
        }
    }

    private static async Task<(string SeasonId, string EnvelopeHash, int SchemaVersion)?> ReadRoundMarkerAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int roundId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT season_id, envelope_hash, envelope_schema FROM round_envelopes WHERE round_id = $round_id;";
        command.Parameters.AddWithValue("$round_id", roundId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return (reader.GetString(0), reader.GetString(1), reader.GetInt32(2));
    }

    private static async Task<string?> FindLegacySeasonAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int roundId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT season_id FROM round_log WHERE round_id = $round_id
            UNION ALL
            SELECT season_id FROM contract_log WHERE round_id = $round_id;
            """;
        command.Parameters.AddWithValue("$round_id", roundId);

        var seasons = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(0))
                throw new RoundEndReplayConflictException(roundId);
            seasons.Add(reader.GetString(0));
        }

        return seasons.Count switch
        {
            0 => null,
            1 => seasons.Single(),
            _ => throw new RoundEndReplayConflictException(roundId),
        };
    }

    private static async Task<string> GetCurrentSeasonInTransactionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT value FROM meta WHERE key = $key;";
        command.Parameters.AddWithValue("$key", CurrentSeasonKey);
        return await command.ExecuteScalarAsync(cancellationToken) as string ?? DefaultSeason;
    }

    private static async Task<HashSet<Guid>> ValidateLegacyPlayerRowsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BoundCanonicalRoundEndEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var expected = envelope.Players.ToDictionary(player => player.User);
        var existing = new HashSet<Guid>();

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT season_id, user_id, payload_hash
            FROM round_log
            WHERE round_id = $round_id;
            """;
        command.Parameters.AddWithValue("$round_id", envelope.RoundId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2) ||
                !string.Equals(reader.GetString(0), envelope.SeasonId, StringComparison.Ordinal) ||
                !Guid.TryParse(reader.GetString(1), out var user) ||
                !expected.TryGetValue(user, out var player) ||
                !existing.Add(user))
            {
                throw new RoundEndReplayConflictException(envelope.RoundId);
            }

            var expectedPayload = SerializeRoundContribution(player.Contribution, envelope.SeasonId);
            var expectedHash = HashPayload(expectedPayload);
            if (!string.Equals(reader.GetString(2), expectedHash, StringComparison.Ordinal))
                throw new RoundEndReplayConflictException(envelope.RoundId);
        }

        return existing;
    }

    private static async Task<bool> ValidateLegacyContractRowsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BoundCanonicalRoundEndEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var existing = new List<CanonicalContractLogRecord>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT season_id, user_id, contract_id, scope, occurrence
            FROM contract_log
            WHERE round_id = $round_id
            ORDER BY user_id COLLATE BINARY, contract_id COLLATE BINARY, scope COLLATE BINARY, occurrence;
            """;
        command.Parameters.AddWithValue("$round_id", envelope.RoundId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(4) ||
                !string.Equals(reader.GetString(0), envelope.SeasonId, StringComparison.Ordinal) ||
                !Guid.TryParse(reader.GetString(1), out var user))
            {
                throw new RoundEndReplayConflictException(envelope.RoundId);
            }

            existing.Add(new CanonicalContractLogRecord(
                user,
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4)));
        }

        if (existing.Count == 0)
            return false;
        if (existing.Count != envelope.Contracts.Count || !existing.SequenceEqual(envelope.Contracts))
            throw new RoundEndReplayConflictException(envelope.RoundId);
        return true;
    }

    private static async Task InsertRoundLogAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BoundCanonicalRoundEndEnvelope envelope,
        CanonicalRoundEndPlayerRecord player,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var payload = SerializeRoundContribution(player.Contribution, envelope.SeasonId);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO round_log
                (round_id, season_id, user_id, ended_utc, gamemode, payload_json, payload_hash)
            VALUES
                ($round_id, $season_id, $user_id, $ended_utc, $gamemode, $payload_json, $payload_hash);
            """;
        command.Parameters.AddWithValue("$round_id", envelope.RoundId);
        command.Parameters.AddWithValue("$season_id", envelope.SeasonId);
        command.Parameters.AddWithValue("$user_id", player.User.ToString());
        command.Parameters.AddWithValue("$ended_utc", now.ToString("o"));
        command.Parameters.AddWithValue("$gamemode", envelope.Gamemode);
        command.Parameters.AddWithValue("$payload_json", payload);
        command.Parameters.AddWithValue("$payload_hash", HashPayload(payload));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertPlayerStatsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string seasonId,
        CanonicalRoundEndPlayerRecord player,
        CancellationToken cancellationToken)
    {
        var contribution = player.Contribution;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO player_stats
                (user_id, season_id, tours, captain_clean, antag_wins, early_deaths,
                 standing_total, contracts_completed, contract_score, hr_points)
            VALUES
                ($user_id, $season_id, 1, $captain_clean, $antag_win, $early_death,
                 $standing, $contracts_completed, $contract_score, $hr_points)
            ON CONFLICT(user_id, season_id) DO UPDATE SET
                tours = tours + 1,
                captain_clean = captain_clean + $captain_clean,
                antag_wins = antag_wins + $antag_win,
                early_deaths = early_deaths + $early_death,
                standing_total = standing_total + $standing,
                contracts_completed = contracts_completed + $contracts_completed,
                contract_score = contract_score + $contract_score,
                hr_points = hr_points + $hr_points;
            """;
        command.Parameters.AddWithValue("$user_id", player.User.ToString());
        command.Parameters.AddWithValue("$season_id", seasonId);
        command.Parameters.AddWithValue("$captain_clean", contribution.WasCaptainClean ? 1 : 0);
        command.Parameters.AddWithValue("$antag_win", contribution.AntagWin ? 1 : 0);
        command.Parameters.AddWithValue("$early_death", contribution.EarlyDeath ? 1 : 0);
        command.Parameters.AddWithValue("$standing", contribution.Standing);
        command.Parameters.AddWithValue("$contracts_completed", contribution.ContractsCompleted);
        command.Parameters.AddWithValue("$contract_score", contribution.ContractScore);
        command.Parameters.AddWithValue("$hr_points", contribution.HrPointsEarned);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertContractLogAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BoundCanonicalRoundEndEnvelope envelope,
        CanonicalContractLogRecord contract,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO contract_log
                (round_id, season_id, user_id, contract_id, scope, occurrence, completed_utc)
            VALUES
                ($round_id, $season_id, $user_id, $contract_id, $scope, $occurrence, $completed_utc);
            """;
        command.Parameters.AddWithValue("$round_id", envelope.RoundId);
        command.Parameters.AddWithValue("$season_id", envelope.SeasonId);
        command.Parameters.AddWithValue("$user_id", contract.User.ToString());
        command.Parameters.AddWithValue("$contract_id", contract.ContractId);
        command.Parameters.AddWithValue("$scope", contract.Scope);
        command.Parameters.AddWithValue("$occurrence", contract.Occurrence);
        command.Parameters.AddWithValue("$completed_utc", now.ToString("o"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertPrivateOutboxAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BoundCanonicalRoundEndEnvelope envelope,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new PrivateRoundCommittedOutboxDto(
            SchemaVersion: 1,
            EventType: "season-ledger.round-committed",
            PrivacyClass: "private-internal",
            RoundId: envelope.RoundId,
            SeasonId: envelope.SeasonId,
            CommittedDate: now.ToString("yyyy-MM-dd"),
            PlayerCount: envelope.Players.Count,
            ContractCount: envelope.Contracts.Count,
            StandingTotal: envelope.Players.Sum(player => player.Contribution.Standing),
            ContractsCompleted: envelope.Players.Sum(player => player.Contribution.ContractsCompleted),
            ContractScore: envelope.Players.Sum(player => player.Contribution.ContractScore),
            HrPointsEarned: envelope.Players.Sum(player => player.Contribution.HrPointsEarned)));

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ledger_outbox
                (event_id, event_type, schema_version, privacy_class, payload_json, created_utc, exported_utc)
            VALUES
                ($event_id, $event_type, 1, 'private-internal', $payload_json, $created_utc, NULL);
            """;
        command.Parameters.AddWithValue("$event_id", $"round-envelope:{envelope.RoundId}");
        command.Parameters.AddWithValue("$event_type", "season-ledger.round-committed");
        command.Parameters.AddWithValue("$payload_json", payload);
        command.Parameters.AddWithValue("$created_utc", now.ToString("yyyy-MM-dd"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertRoundMarkerAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BoundCanonicalRoundEndEnvelope envelope,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO round_envelopes
                (round_id, season_id, envelope_hash, envelope_schema, committed_utc,
                 gamemode, player_count, contract_count)
            VALUES
                ($round_id, $season_id, $envelope_hash, $envelope_schema, $committed_utc,
                 $gamemode, $player_count, $contract_count);
            """;
        command.Parameters.AddWithValue("$round_id", envelope.RoundId);
        command.Parameters.AddWithValue("$season_id", envelope.SeasonId);
        command.Parameters.AddWithValue("$envelope_hash", envelope.EnvelopeHash);
        command.Parameters.AddWithValue("$envelope_schema", CapturedRoundEndEnvelope.SchemaVersion);
        command.Parameters.AddWithValue("$committed_utc", now.ToString("o"));
        command.Parameters.AddWithValue("$gamemode", envelope.Gamemode);
        command.Parameters.AddWithValue("$player_count", envelope.Players.Count);
        command.Parameters.AddWithValue("$contract_count", envelope.Contracts.Count);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ThrowIfEnvelopeCommittedAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int roundId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM round_envelopes WHERE round_id = $round_id;";
        command.Parameters.AddWithValue("$round_id", roundId);
        if (await command.ExecuteScalarAsync(cancellationToken) != null)
            throw new RoundEndReplayConflictException(roundId);
    }

    private sealed record PrivateRoundCommittedOutboxDto(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("event_type")] string EventType,
        [property: JsonPropertyName("privacy_class")] string PrivacyClass,
        [property: JsonPropertyName("round_id")] int RoundId,
        [property: JsonPropertyName("season_id")] string SeasonId,
        [property: JsonPropertyName("committed_date")] string CommittedDate,
        [property: JsonPropertyName("player_count")] int PlayerCount,
        [property: JsonPropertyName("contract_count")] int ContractCount,
        [property: JsonPropertyName("standing_total")] int StandingTotal,
        [property: JsonPropertyName("contracts_completed")] int ContractsCompleted,
        [property: JsonPropertyName("contract_score")] int ContractScore,
        [property: JsonPropertyName("hr_points_earned")] int HrPointsEarned);
}
