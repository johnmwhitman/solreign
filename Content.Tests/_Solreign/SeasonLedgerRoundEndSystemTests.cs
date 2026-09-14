using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Server._Solreign.SeasonLedger;
using Content.Shared.Administration;
using NUnit.Framework;

#nullable enable

namespace Content.Tests._Solreign;

[TestFixture]
public sealed class SeasonLedgerRoundEndSystemTests
{
    // RA0026: pre-parsed static instance instead of a static Regex function with a pattern string.
    private static readonly System.Text.RegularExpressions.Regex StatusLineRegex = new(
        "^round=[0-9]+ token=[a-f0-9]{32} attempts=[0-9]+ state=[A-Za-z]+ category=[A-Za-z]+$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Test]
    public void Capture_FreezesCompleteRosterAndContractMultiset()
    {
        var players = new List<RoundEndPlayerRecord>
        {
            new(Alice, Contribution(Alice, contracts: 2)),
            new(Bob, Contribution(Bob, contracts: 1)),
            new(Alice, Contribution(Alice, contracts: 2) with
            {
                WasCaptainClean = false,
                AntagWin = true,
            }),
        };
        var contracts = new List<ContractLogRecord>
        {
            new(92, Alice, "repair-grid", "department"),
            new(92, Alice, "repair-grid", "department"),
            new(92, Bob, "first-aid", "personal"),
        };

        var envelope = SeasonLedgerRoundEndCapture.Capture(92, "Secret", players, contracts);
        players.Clear();
        contracts.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(envelope.Players.Select(player => player.User), Is.EquivalentTo(new[] { Alice, Bob }));
            var alice = envelope.Players.Single(player => player.User == Alice).Contribution;
            Assert.That(alice.WasCaptainClean, Is.True, "account-level captain evidence survives mind changes");
            Assert.That(alice.AntagWin, Is.True, "account-level antagonist evidence survives mind changes");
            Assert.That(envelope.Contracts, Has.Count.EqualTo(3), "duplicate completions are an audit multiset");
            Assert.That(envelope.Contracts.Count(item => item.ContractId == "repair-grid"), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task RecoveryScheduler_StartsAtInitializationAndNeverOverlaps()
    {
        var scheduler = new SeasonLedgerRecoveryScheduler();
        var completion = new TaskCompletionSource<LedgerRecoverySweepResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = 0;
        var requestedMaximum = 0;

        LedgerRecoverySweepResult? Start(int maximum)
        {
            Interlocked.Increment(ref starts);
            requestedMaximum = maximum;
            return null;
        }

        Task<LedgerRecoverySweepResult> StartAsync(int maximum)
        {
            Start(maximum);
            return completion.Task;
        }

        Assert.That(scheduler.Tick(TimeSpan.Zero, StartAsync), Is.Null);
        Assert.That(scheduler.Tick(TimeSpan.FromMinutes(2), StartAsync), Is.Null);
        Assert.That(SpinWait.SpinUntil(() => Volatile.Read(ref starts) == 1, TimeSpan.FromSeconds(1)), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(starts, Is.EqualTo(1));
            Assert.That(requestedMaximum, Is.EqualTo(8));
        });

        completion.SetResult(new LedgerRecoverySweepResult(1, 1, 0));
        await completion.Task;
        LedgerRecoverySweepResult? completed = null;
        Assert.That(SpinWait.SpinUntil(
            () => (completed = scheduler.Tick(TimeSpan.Zero, StartAsync)) is not null,
            TimeSpan.FromSeconds(1)), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(completed, Is.EqualTo(new LedgerRecoverySweepResult(1, 1, 0)));
            Assert.That(starts, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RecoveryScheduler_OffloadsSynchronousStartAndNeverOverlaps()
    {
        var scheduler = new SeasonLedgerRecoveryScheduler();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var completion = new TaskCompletionSource<LedgerRecoverySweepResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = 0;

        Task<LedgerRecoverySweepResult> BlockingStart(int maximum)
        {
            Interlocked.Increment(ref starts);
            entered.Set();
            release.Wait();
            return completion.Task;
        }

        var tick = Task.Run(() => scheduler.Tick(TimeSpan.Zero, BlockingStart));
        try
        {
            var returned = await Task.WhenAny(tick, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.That(returned, Is.SameAs(tick), "Tick must return before synchronous spool work completes");
            Assert.That(await tick, Is.Null);
            Assert.That(entered.Wait(TimeSpan.FromSeconds(1)), Is.True);

            Assert.That(scheduler.Tick(TimeSpan.FromMinutes(2), BlockingStart), Is.Null);
            Assert.That(Volatile.Read(ref starts), Is.EqualTo(1), "an in-flight sweep must never overlap");
        }
        finally
        {
            release.Set();
            completion.TrySetResult(new LedgerRecoverySweepResult(1, 1, 0));
        }
    }

    [Test]
    public void RecoverySweep_SourceHasOneSnapshotScanAndNoSecondSystemSnapshot()
    {
        var root = FindRepositoryRoot();
        var storeSource = File.ReadAllText(Path.Combine(
            root,
            "Content.Server/_Solreign/SeasonLedger/SeasonLedgerStore.RoundEnd.cs"));
        var storeStart = storeSource.IndexOf(
            "public async Task<LedgerRecoverySweepResult> RecoverPendingRoundEndsAsync",
            StringComparison.Ordinal);
        var storeEnd = storeSource.IndexOf(
            "public async Task<RoundEndPersistenceResult> RecoverPendingRoundEndAsync",
            storeStart,
            StringComparison.Ordinal);
        var sweep = storeSource[storeStart..storeEnd];

        var systemSource = File.ReadAllText(Path.Combine(
            root,
            "Content.Server/_Solreign/SeasonLedger/SeasonLedgerSystem.cs"));
        var systemStart = systemSource.IndexOf(
            "private async Task<LedgerRecoverySweepResult> RecoverAndSummarizeAsync",
            StringComparison.Ordinal);
        var systemEnd = systemSource.IndexOf("/// <summary>", systemStart, StringComparison.Ordinal);
        var schedulerWorker = systemSource[systemStart..systemEnd];

        Assert.Multiple(() =>
        {
            Assert.That(
                sweep.Split("_spool.Snapshot()", StringSplitOptions.None).Length - 1,
                Is.EqualTo(1),
                "one recovery invocation must perform one full spool snapshot scan");
            Assert.That(schedulerWorker, Does.Not.Contain("GetRecoverySnapshot"),
                "the scheduler worker must reuse the sweep snapshot instead of scanning again");
        });
    }

    [Test]
    public void RecoveryScheduler_WaitsThirtySecondsAfterCompletion()
    {
        var scheduler = new SeasonLedgerRecoveryScheduler();
        var starts = 0;
        Task<LedgerRecoverySweepResult> Start(int maximum)
        {
            Interlocked.Increment(ref starts);
            return Task.FromResult(new LedgerRecoverySweepResult(0, 0, 0));
        }

        scheduler.Tick(TimeSpan.Zero, Start);
        Assert.That(SpinWait.SpinUntil(() => Volatile.Read(ref starts) == 1, TimeSpan.FromSeconds(1)), Is.True);
        Assert.That(SpinWait.SpinUntil(
            () => scheduler.Tick(TimeSpan.Zero, Start) is not null,
            TimeSpan.FromSeconds(1)), Is.True); // observe the completed initialization sweep
        scheduler.Tick(TimeSpan.FromSeconds(29), Start);
        Assert.That(Volatile.Read(ref starts), Is.EqualTo(1));

        scheduler.Tick(TimeSpan.FromSeconds(1), Start);
        Assert.That(SpinWait.SpinUntil(() => Volatile.Read(ref starts) == 2, TimeSpan.FromSeconds(1)), Is.True);
    }

    [Test]
    public void RecoveryConsole_StatusIsBoundedAndContainsOnlySanitizedFields()
    {
        var items = Enumerable.Range(0, 20)
            .Select(index => new LedgerRecoverySnapshotItem(
                index.ToString("x32"),
                100 + index,
                new DateOnly(2026, 7, 13),
                index,
                LedgerRecoveryState.Pending,
                LedgerRecoveryErrorCategory.DatabaseBusy))
            .ToArray();

        var lines = SeasonLedgerRecoveryConsole.FormatStatus(items);

        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Count.EqualTo(9), "one summary plus at most eight recovery items");
            Assert.That(lines[0], Is.EqualTo("Season Ledger recovery: unresolved=20 shown=8."));
            Assert.That(string.Join('\n', lines), Does.Not.Contain("2026-07-13"));
            Assert.That(lines.Skip(1).All(line => StatusLineRegex.IsMatch(line)), Is.True);
        });
    }

    [Test]
    public async Task RecoveryConsole_StatusFailureDoesNotExposeExceptionDetails()
    {
        var lines = await SeasonLedgerRecoveryConsole.FormatStatusSafelyAsync(() =>
            Task.FromException<IReadOnlyList<LedgerRecoverySnapshotItem>>(
                new IOException("/private/ledger.db contained SecretContract")));

        Assert.That(lines, Is.EqualTo(new[] { "Season Ledger recovery status unavailable." }));
    }

    [Test]
    public void RecoveryCommands_AreHostOnly()
    {
        var commands = new[]
        {
            typeof(SeasonLedgerRecoveryStatusCommand),
            typeof(SeasonLedgerRecoveryRetryCommand),
            typeof(SeasonLedgerRecoveryArchiveCommand),
        };

        Assert.That(commands.All(type =>
            type.GetCustomAttribute<AdminCommandAttribute>()?.Flags == AdminFlags.Host), Is.True);
    }

    [Test]
    public void OnRoundEnd_SourceGuardUsesExactlyOneEnvelopeWriteAndNoLegacyPath()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "Content.Server/_Solreign/SeasonLedger/SeasonLedgerSystem.cs"));
        var start = source.IndexOf("private async void OnRoundEnd", StringComparison.Ordinal);
        var end = source.IndexOf("private void LogRoundEndResult", start, StringComparison.Ordinal);
        var handler = source[start..end];

        Assert.Multiple(() =>
        {
            Assert.That(handler, Does.Not.Contain("AddRoundRecordAsync"));
            Assert.That(handler, Does.Not.Contain("AddContractLogAsync"));
            Assert.That(handler.Split("PersistRoundEndAsync", StringSplitOptions.None), Has.Length.EqualTo(2));
            Assert.That(handler.IndexOf("SeasonLedgerRoundEndCapture.Capture", StringComparison.Ordinal),
                Is.LessThan(handler.IndexOf("await _store.", StringComparison.Ordinal)));
            // ROUND-7: Pending persist must not fold. Gate on Committed/AlreadyCommitted only.
            Assert.That(handler, Does.Contain("RoundEndPersistenceStatus.Committed"));
            Assert.That(handler, Does.Contain("RoundEndPersistenceStatus.AlreadyCommitted"));
            Assert.That(handler, Does.Contain("UpdateContractStreaksAndNotify"));
            var statusGate = handler.IndexOf(
                "result.Status is RoundEndPersistenceStatus.Committed",
                StringComparison.Ordinal);
            var foldCall = handler.IndexOf("UpdateContractStreaksAndNotify", StringComparison.Ordinal);
            Assert.That(statusGate, Is.GreaterThanOrEqualTo(0),
                "OnRoundEnd must gate post-persist folding on Committed/AlreadyCommitted");
            Assert.That(foldCall, Is.GreaterThan(statusGate),
                "UpdateContractStreaksAndNotify must run only after the Committed/AlreadyCommitted gate");
        });
    }

    [Test]
    public void RecoveryCommand_SourceGuardHasNoBlockingTaskWaits()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "Content.Server/_Solreign/SeasonLedger/Commands/SeasonLedgerRecoveryCommands.cs"));
        var commands = source[source.IndexOf("[AdminCommand", StringComparison.Ordinal)..];

        Assert.Multiple(() =>
        {
            Assert.That(commands, Does.Not.Contain("GetAwaiter().GetResult()"));
            // "override" since the P0 fix: commands extend LocalizedEntityCommands (entity-system
            // dependencies are only legal on IEntityConsoleCommand implementers — direct
            // IConsoleCommand + [Dependency] EntitySystem kills server boot at LoadConsoleCommands).
            Assert.That(commands.Split("public override async void Execute", StringSplitOptions.None), Has.Length.EqualTo(4));
            Assert.That(commands.Split(": LocalizedEntityCommands", StringSplitOptions.None), Has.Length.EqualTo(4));
        });
    }

    [TestCase("")]
    [TestCase("../ledger.db")]
    [TestCase("11111111-1111-1111-1111-111111111111")]
    [TestCase("ABCDEF0123456789ABCDEF0123456789")]
    public void RecoveryConsole_RejectsNonOpaqueToken(string token)
    {
        Assert.That(SeasonLedgerRecoveryConsole.IsOpaqueToken(token), Is.False);
    }

    [TestCase("operator-review", true)]
    [TestCase("legacy-reconciled", true)]
    [TestCase("superseded-evidence", true)]
    [TestCase("because I said so", false)]
    public void RecoveryConsole_AllowlistArchiveReasons(string reason, bool expected)
    {
        Assert.That(SeasonLedgerRecoveryConsole.IsArchiveReason(reason), Is.EqualTo(expected));
    }

    [Test]
    public async Task TargetedRecovery_RequiresExactTokenAndAcknowledgesOnlyThatEntry()
    {
        var root = Path.Combine(Path.GetTempPath(), $"solreign_system_recovery_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var fault = new ThrowBeforeAcknowledge();
            var store = new SeasonLedgerStore(
                Path.Combine(root, "ledger.db"),
                NoopRoundEnvelopeFaultInjector.Instance,
                ImmediateRetryRuntime.Instance,
                fault);
            Assert.ThrowsAsync<InjectedSpoolCrashException>(async () =>
                await store.PersistRoundEndAsync(Envelope(92, Alice), CancellationToken.None));
            Assert.ThrowsAsync<InjectedSpoolCrashException>(async () =>
                await store.PersistRoundEndAsync(Envelope(93, Bob), CancellationToken.None));
            var token = store.GetRecoverySnapshot().Single(item => item.RoundId == 92).Token;

            Assert.ThrowsAsync<ArgumentException>(async () =>
                await store.RecoverPendingRoundEndAsync("../ledger.db", CancellationToken.None));
            fault.Enabled = false;
            var result = await store.RecoverPendingRoundEndAsync(token, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(RoundEndPersistenceStatus.AlreadyCommitted));
                Assert.That(result.Token, Is.EqualTo(token));
                Assert.That(store.GetRecoverySnapshot().Select(item => item.RoundId), Is.EqualTo(new[] { 93 }));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static RoundContribution Contribution(Guid user, int contracts) => new(
        WasCaptainClean: user == Alice,
        AntagWin: false,
        EarlyDeath: false,
        RoundId: 92,
        Gamemode: "Secret",
        ContractsCompleted: contracts,
        ContractScore: contracts * 10,
        HrPointsEarned: 5 + contracts);

    private static RoundEndEnvelope Envelope(int roundId, Guid user) => new(
        roundId,
        "Secret",
        new[]
        {
            new RoundEndPlayerRecord(user, new RoundContribution(false, false, false, roundId, "Secret")),
        },
        Array.Empty<ContractLogRecord>());

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "Content.Server/_Solreign/SeasonLedger/SeasonLedgerSystem.cs")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed class InjectedSpoolCrashException : Exception;

    private sealed class ThrowBeforeAcknowledge : IRoundEnvelopeSpoolFaultInjector
    {
        public bool Enabled { get; set; } = true;

        public void Hit(RoundEnvelopeSpoolStage stage)
        {
            if (Enabled && stage == RoundEnvelopeSpoolStage.BeforeAcknowledge)
                throw new InjectedSpoolCrashException();
        }
    }
}
