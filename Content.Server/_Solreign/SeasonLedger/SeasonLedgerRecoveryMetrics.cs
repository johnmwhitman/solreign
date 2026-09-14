using System;
using System.Collections.Generic;
using Prometheus;

namespace Content.Server._Solreign.SeasonLedger;

internal readonly record struct LedgerRecoveryHealthSnapshot(
    int Unresolved,
    int Pending,
    int Conflict,
    int Quarantined,
    int OldestAgeDays,
    int MaximumAttempts,
    int SweepExamined,
    int SweepAcknowledged,
    int SweepFaulted);

internal readonly record struct LedgerRecoverySweepObservation(
    LedgerRecoverySweepResult Sweep,
    LedgerRecoveryHealthSnapshot Health);

internal static class SeasonLedgerRecoveryMetrics
{
    private const int MaximumAgeDays = 3_650;
    private const int MaximumAttempts = 1_000_000;

    private static readonly Gauge RecoveryUnresolved = Metrics.CreateGauge(
        "solreign_ledger_recovery_unresolved",
        "Number of unresolved Season Ledger recovery entries.");

    private static readonly Gauge RecoveryState = Metrics.CreateGauge(
        "solreign_ledger_recovery_state",
        "Number of recovery entries by bounded state.",
        new[] { "state" });

    private static readonly Gauge RecoveryOldestAgeDays = Metrics.CreateGauge(
        "solreign_ledger_recovery_oldest_age_days",
        "Age in days of the oldest unresolved recovery entry, capped at ten years.");

    private static readonly Gauge RecoveryMaximumAttempts = Metrics.CreateGauge(
        "solreign_ledger_recovery_max_attempts",
        "Largest bounded attempt count among unresolved recovery entries.");

    private static readonly Gauge RecoverySweepExamined = Metrics.CreateGauge(
        "solreign_ledger_recovery_sweep_examined",
        "Number of entries examined by the most recently completed recovery sweep.");

    private static readonly Gauge RecoverySweepAcknowledged = Metrics.CreateGauge(
        "solreign_ledger_recovery_sweep_acknowledged",
        "Number of entries acknowledged by the most recently completed recovery sweep.");

    private static readonly Gauge RecoverySweepFaulted = Metrics.CreateGauge(
        "solreign_ledger_recovery_sweep_faulted",
        "Number of entries faulted by the most recently completed recovery sweep.");

    private static readonly Counter RoundEndTotal = Metrics.CreateCounter(
        "solreign_ledger_round_end_total",
        "Number of observed round-end persistence outcomes by bounded status.",
        new[] { "status" });

    internal static LedgerRecoveryHealthSnapshot Summarize(
        IReadOnlyList<LedgerRecoverySnapshotItem> items,
        LedgerRecoverySweepResult sweep,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(sweep);

        var pending = 0;
        var conflict = 0;
        var quarantined = 0;
        var oldestAgeDays = 0;
        var maximumAttempts = 0;

        foreach (var item in items)
        {
            switch (item.State)
            {
                case LedgerRecoveryState.Pending:
                    pending++;
                    break;
                case LedgerRecoveryState.Conflict:
                    conflict++;
                    break;
                case LedgerRecoveryState.Quarantined:
                    quarantined++;
                    break;
            }

            var ageDays = Math.Clamp(today.DayNumber - item.CapturedDate.DayNumber, 0, MaximumAgeDays);
            oldestAgeDays = Math.Max(oldestAgeDays, ageDays);
            maximumAttempts = Math.Max(maximumAttempts, Math.Clamp(item.Attempts, 0, MaximumAttempts));
        }

        return new LedgerRecoveryHealthSnapshot(
            items.Count,
            pending,
            conflict,
            quarantined,
            oldestAgeDays,
            maximumAttempts,
            sweep.Examined,
            sweep.Acknowledged,
            sweep.Faulted);
    }

    internal static void PublishSnapshot(LedgerRecoveryHealthSnapshot snapshot)
    {
        RecoveryUnresolved.Set(snapshot.Unresolved);
        RecoveryState.WithLabels("pending").Set(snapshot.Pending);
        RecoveryState.WithLabels("conflict").Set(snapshot.Conflict);
        RecoveryState.WithLabels("quarantined").Set(snapshot.Quarantined);
        RecoveryOldestAgeDays.Set(snapshot.OldestAgeDays);
        RecoveryMaximumAttempts.Set(snapshot.MaximumAttempts);
        RecoverySweepExamined.Set(snapshot.SweepExamined);
        RecoverySweepAcknowledged.Set(snapshot.SweepAcknowledged);
        RecoverySweepFaulted.Set(snapshot.SweepFaulted);
    }

    internal static void RecordRoundEnd(RoundEndPersistenceStatus? status)
    {
        var counter = status switch
        {
            RoundEndPersistenceStatus.Committed => RoundEndTotal.WithLabels("committed"),
            RoundEndPersistenceStatus.AlreadyCommitted => RoundEndTotal.WithLabels("already_committed"),
            RoundEndPersistenceStatus.Pending => RoundEndTotal.WithLabels("pending"),
            null => RoundEndTotal.WithLabels("failed"),
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
        counter.Inc();
    }
}
