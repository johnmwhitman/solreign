using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Content.Server._Solreign.SeasonLedger;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(SeasonLedgerRecoveryMetrics))]
public sealed class SeasonLedgerRecoveryMetricsTests
{
    private static readonly Regex MetricNameRegex = new(
        "\"(solreign_ledger_[a-z_]+)\"",
        RegexOptions.Compiled);
    private static readonly Regex LabelValueRegex = new(
        "\\.WithLabels\\(\"([a-z_]+)\"\\)",
        RegexOptions.Compiled);
    private static readonly Regex LabelNameRegex = new(
        "new\\[\\]\\s*\\{\\s*\"([a-z_]+)\"\\s*\\}",
        RegexOptions.Compiled);

    [Test]
    public void HealthSnapshot_HasExactImmutableAggregateShape()
    {
        var type = typeof(LedgerRecoveryHealthSnapshot);
        var constructor = type.GetConstructors().Single();

        Assert.Multiple(() =>
        {
            Assert.That(type.IsValueType, Is.True);
            Assert.That(type.GetFields(BindingFlags.Instance | BindingFlags.Public), Is.Empty);
            Assert.That(
                constructor.GetParameters().Select(parameter => (parameter.Name, parameter.ParameterType)),
                Is.EqualTo(new[]
                {
                    ("Unresolved", typeof(int)),
                    ("Pending", typeof(int)),
                    ("Conflict", typeof(int)),
                    ("Quarantined", typeof(int)),
                    ("OldestAgeDays", typeof(int)),
                    ("MaximumAttempts", typeof(int)),
                    ("SweepExamined", typeof(int)),
                    ("SweepAcknowledged", typeof(int)),
                    ("SweepFaulted", typeof(int)),
                }));
            Assert.That(
                type.GetProperties().Select(property => property.Name),
                Is.EqualTo(new[]
                {
                    "Unresolved",
                    "Pending",
                    "Conflict",
                    "Quarantined",
                    "OldestAgeDays",
                    "MaximumAttempts",
                    "SweepExamined",
                    "SweepAcknowledged",
                    "SweepFaulted",
                }));
            Assert.That(type.GetProperties().All(property =>
                property.SetMethod?.ReturnParameter.GetRequiredCustomModifiers()
                    .Contains(typeof(System.Runtime.CompilerServices.IsExternalInit)) == true), Is.True);
        });
    }

    [Test]
    public void Summarize_EmptyInputReturnsZeroes()
    {
        var summary = SeasonLedgerRecoveryMetrics.Summarize(
            Array.Empty<LedgerRecoverySnapshotItem>(),
            new LedgerRecoverySweepResult(0, 0, 0),
            new DateOnly(2026, 7, 14));

        Assert.That(summary, Is.EqualTo(new LedgerRecoveryHealthSnapshot(0, 0, 0, 0, 0, 0, 0, 0, 0)));
    }

    [Test]
    public void Summarize_MixedStatesProjectsOnlyBoundedAggregates()
    {
        IReadOnlyList<LedgerRecoverySnapshotItem> items = new[]
        {
            Item("11111111111111111111111111111111", 101, new DateOnly(2026, 7, 1), 3,
                LedgerRecoveryState.Pending),
            Item("22222222222222222222222222222222", 102, new DateOnly(2026, 6, 20), 9,
                LedgerRecoveryState.Conflict),
            Item("33333333333333333333333333333333", 103, new DateOnly(2026, 7, 20), 5,
                LedgerRecoveryState.Quarantined),
            Item("44444444444444444444444444444444", null, new DateOnly(2026, 7, 10), 7,
                LedgerRecoveryState.Pending),
        };
        var sweep = new LedgerRecoverySweepResult(
            Examined: 8,
            Acknowledged: 4,
            Pending: 4,
            FirstPending: items[0],
            Faulted: 2);

        var summary = SeasonLedgerRecoveryMetrics.Summarize(items, sweep, new DateOnly(2026, 7, 14));

        Assert.That(summary, Is.EqualTo(new LedgerRecoveryHealthSnapshot(
            Unresolved: 4,
            Pending: 2,
            Conflict: 1,
            Quarantined: 1,
            OldestAgeDays: 24,
            MaximumAttempts: 9,
            SweepExamined: 8,
            SweepAcknowledged: 4,
            SweepFaulted: 2)));
    }

    [Test]
    public void Summarize_ClampsFutureAgeOldestAgeAndAttempts()
    {
        var items = new[]
        {
            Item("11111111111111111111111111111111", 101, new DateOnly(2030, 1, 1), -5,
                LedgerRecoveryState.Pending),
            Item("22222222222222222222222222222222", 102, new DateOnly(2000, 1, 1), int.MaxValue,
                LedgerRecoveryState.Conflict),
        };

        var summary = SeasonLedgerRecoveryMetrics.Summarize(
            items,
            new LedgerRecoverySweepResult(2, 0, 2),
            new DateOnly(2026, 7, 14));

        Assert.Multiple(() =>
        {
            Assert.That(summary.OldestAgeDays, Is.EqualTo(3_650));
            Assert.That(summary.MaximumAttempts, Is.EqualTo(1_000_000));
        });

        var futureOnly = SeasonLedgerRecoveryMetrics.Summarize(
            new[] { items[0] },
            new LedgerRecoverySweepResult(1, 0, 1),
            new DateOnly(2026, 7, 14));
        Assert.Multiple(() =>
        {
            Assert.That(futureOnly.OldestAgeDays, Is.Zero);
            Assert.That(futureOnly.MaximumAttempts, Is.Zero);
        });
    }

    [Test]
    public void MetricsSurface_UsesOnlyRequiredSeriesAndFixedLabels()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "Content.Server/_Solreign/SeasonLedger/SeasonLedgerRecoveryMetrics.cs"));
        var metricNames = MetricNameRegex.Matches(source)
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToArray();
        var labelValues = LabelValueRegex.Matches(source)
            .Select(match => match.Groups[1].Value)
            .ToArray();
        var labelNames = LabelNameRegex.Matches(source)
            .Select(match => match.Groups[1].Value)
            .ToArray();
        var forbiddenDimensions = new[]
        {
            "token", "round", "path", "season", "user", "guid", "gamemode", "contract",
        };

        Assert.Multiple(() =>
        {
            Assert.That(metricNames, Is.EquivalentTo(new[]
            {
                "solreign_ledger_recovery_unresolved",
                "solreign_ledger_recovery_state",
                "solreign_ledger_recovery_oldest_age_days",
                "solreign_ledger_recovery_max_attempts",
                "solreign_ledger_recovery_sweep_examined",
                "solreign_ledger_recovery_sweep_acknowledged",
                "solreign_ledger_recovery_sweep_faulted",
                "solreign_ledger_round_end_total",
            }));
            Assert.That(labelNames, Is.EquivalentTo(new[] { "state", "status" }));
            Assert.That(labelValues, Is.EquivalentTo(new[]
            {
                "pending",
                "conflict",
                "quarantined",
                "committed",
                "already_committed",
                "pending",
                "failed",
            }));
            Assert.That(labelNames.Any(label => forbiddenDimensions.Any(dimension =>
                label.Contains(dimension, StringComparison.OrdinalIgnoreCase))), Is.False);
        });
    }

    [Test]
    public void MetricsSurface_ExposesPublicationAndBoundedRoundEndMethods()
    {
        var methods = typeof(SeasonLedgerRecoveryMetrics).GetMethods(
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.Multiple(() =>
        {
            Assert.That(methods.Single(method => method.Name == "PublishSnapshot").ReturnType,
                Is.EqualTo(typeof(void)));
            Assert.That(methods.Single(method => method.Name == "RecordRoundEnd").ReturnType,
                Is.EqualTo(typeof(void)));
        });
    }

    private static LedgerRecoverySnapshotItem Item(
        string token,
        int? roundId,
        DateOnly capturedDate,
        int attempts,
        LedgerRecoveryState state) =>
        new(token, roundId, capturedDate, attempts, state, LedgerRecoveryErrorCategory.None);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "Content.Server/_Solreign/SeasonLedger/SeasonLedgerRecoveryMetrics.cs")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
