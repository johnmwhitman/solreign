using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._Solreign.SeasonLedger;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(SeasonLedgerStore))]
public sealed class RoundEndRecoveryTests
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private string _root = default!;
    private string _dbPath = default!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"solreign_recovery_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "ledger.db");
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best-effort cleanup in the temporary directory.
        }
    }

    [Test]
    public async Task PersistRoundEnd_StagesWithOpaqueRandomTokenThenAcknowledgesCommit()
    {
        var fault = new ThrowBeforeAcknowledge();
        var store = new SeasonLedgerStore(
            _dbPath,
            NoopRoundEnvelopeFaultInjector.Instance,
            ImmediateRetryRuntime.Instance,
            fault);

        Assert.ThrowsAsync<InjectedSpoolCrashException>(async () =>
            await store.PersistRoundEndAsync(Envelope(), CancellationToken.None));

        var snapshot = store.GetRecoverySnapshot();
        Assert.That(snapshot, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(snapshot[0].Token, Does.Match("^[a-f0-9]{32}$"));
            Assert.That(snapshot[0].Token, Is.Not.EqualTo(Envelope().Canonicalize().CaptureHash.ToLowerInvariant()));
            Assert.That(snapshot[0].RoundId, Is.EqualTo(101));
            Assert.That(snapshot[0].Attempts, Is.EqualTo(1));
            Assert.That(snapshot[0].State, Is.EqualTo(LedgerRecoveryState.Pending));
        });

        fault.Enabled = false;
        var sweep = await store.RecoverPendingRoundEndsAsync(cancellationToken: CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(sweep.Acknowledged, Is.EqualTo(1));
            Assert.That(store.GetRecoverySnapshot(), Is.Empty);
        });
    }

    [Test]
    public async Task PendingCapture_ReusesTokenForSameValidatedContent()
    {
        var runtime = new RecordingRetryRuntime();
        var store = new SeasonLedgerStore(
            _dbPath,
            NoopRoundEnvelopeFaultInjector.Instance,
            runtime,
            NoopRoundEnvelopeSpoolFaultInjector.Instance);
        await store.GetCurrentSeasonAsync();
        await using var lockConnection = await InitializeAndLockDatabaseAsync();

        var first = await store.PersistRoundEndAsync(Envelope(), CancellationToken.None);
        var second = await store.PersistRoundEndAsync(Envelope(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first.Status, Is.EqualTo(RoundEndPersistenceStatus.Pending));
            Assert.That(second.Status, Is.EqualTo(RoundEndPersistenceStatus.Pending));
            Assert.That(second.Token, Is.EqualTo(first.Token));
            Assert.That(store.GetRecoverySnapshot(), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void RetryPolicy_RetriesOnlySqliteBusyAndLockedWithExactSchedule()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LedgerRetryPolicy.IsRetryable(new SqliteException("busy", 5)), Is.True);
            Assert.That(LedgerRetryPolicy.IsRetryable(new SqliteException("locked", 6)), Is.True);
            Assert.That(LedgerRetryPolicy.IsRetryable(new SqliteException("constraint", 19)), Is.False);
            Assert.That(LedgerRetryPolicy.Delays, Is.EqualTo(new[]
            {
                TimeSpan.FromMilliseconds(25),
                TimeSpan.FromMilliseconds(50),
                TimeSpan.FromMilliseconds(100),
            }));
        });
    }

    [Test]
    public async Task BusyDatabase_PerformsExactlyFourAttemptsAndLeavesSanitizedPendingEntry()
    {
        var runtime = new RecordingRetryRuntime();
        var store = new SeasonLedgerStore(
            _dbPath,
            NoopRoundEnvelopeFaultInjector.Instance,
            runtime,
            NoopRoundEnvelopeSpoolFaultInjector.Instance);
        await store.GetCurrentSeasonAsync();
        await using var lockConnection = await InitializeAndLockDatabaseAsync();

        var result = await store.PersistRoundEndAsync(Envelope(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(RoundEndPersistenceStatus.Pending));
            Assert.That(result.Attempts, Is.EqualTo(4));
            Assert.That(result.ErrorCategory, Is.EqualTo(LedgerRecoveryErrorCategory.DatabaseBusy));
            Assert.That(runtime.Delays, Is.EqualTo(LedgerRetryPolicy.Delays));
            Assert.That(store.GetRecoverySnapshot().Single().ErrorCategory,
                Is.EqualTo(LedgerRecoveryErrorCategory.DatabaseBusy));
        });
    }

    [Test]
    public async Task BusyDatabase_ReleasedAfterFirstDelay_CommitsOnAttemptTwo()
    {
        var store = new SeasonLedgerStore(_dbPath);
        await store.GetCurrentSeasonAsync();
        await using var lockConnection = await InitializeAndLockDatabaseAsync();
        var runtime = new ReleaseLockRetryRuntime(lockConnection);
        store = new SeasonLedgerStore(
            _dbPath,
            NoopRoundEnvelopeFaultInjector.Instance,
            runtime,
            NoopRoundEnvelopeSpoolFaultInjector.Instance);

        var result = await store.PersistRoundEndAsync(Envelope(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(RoundEndPersistenceStatus.Committed));
            Assert.That(result.Attempts, Is.EqualTo(2));
            Assert.That(runtime.Delays, Is.EqualTo(new[] { TimeSpan.FromMilliseconds(25) }));
            Assert.That(store.GetRecoverySnapshot(), Is.Empty);
        });
    }

    [Test]
    public async Task ReplayConflict_DoesNotDelayAndRemainsVisible()
    {
        var original = new SeasonLedgerStore(_dbPath);
        await original.PersistRoundEndAsync(Envelope(), CancellationToken.None);
        var runtime = new RecordingRetryRuntime();
        var conflicting = new SeasonLedgerStore(
            _dbPath,
            NoopRoundEnvelopeFaultInjector.Instance,
            runtime,
            NoopRoundEnvelopeSpoolFaultInjector.Instance);
        var changed = new RoundEndEnvelope(
            101,
            "Secret",
            new[]
            {
                new RoundEndPlayerRecord(Alice, new RoundContribution(false, false, false, 101, "Secret", 99)),
            },
            Array.Empty<ContractLogRecord>());

        Assert.ThrowsAsync<RoundEndReplayConflictException>(async () =>
            await conflicting.PersistRoundEndAsync(changed, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(runtime.Delays, Is.Empty);
            Assert.That(conflicting.GetRecoverySnapshot().Single().State, Is.EqualTo(LedgerRecoveryState.Conflict));
        });
    }

    [Test]
    public async Task Recovery_IsBoundedAndExactlyOnceAcrossRestart()
    {
        var fault = new ThrowBeforeAcknowledge();
        var first = new SeasonLedgerStore(
            _dbPath,
            NoopRoundEnvelopeFaultInjector.Instance,
            ImmediateRetryRuntime.Instance,
            fault);
        Assert.ThrowsAsync<InjectedSpoolCrashException>(async () =>
            await first.PersistRoundEndAsync(Envelope(), CancellationToken.None));

        var restarted = new SeasonLedgerStore(_dbPath);
        var recovered = await restarted.RecoverPendingRoundEndsAsync(1, CancellationToken.None);
        var second = await restarted.RecoverPendingRoundEndsAsync(1, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(recovered.Examined, Is.EqualTo(1));
            Assert.That(recovered.Acknowledged, Is.EqualTo(1));
            Assert.That(second.Examined, Is.Zero);
            Assert.That(ReadScalar("SELECT tours FROM player_stats WHERE user_id = $user;", Alice), Is.EqualTo(1L));
            Assert.That(ReadScalar("SELECT COUNT(*) FROM ledger_outbox;"), Is.EqualTo(1L));
        });
    }

    [Test]
    public async Task Recovery_PersistsFaultDispositionOnPoisonEntryAndStillCommitsHealthyEntryBehindIt()
    {
        StagePendingEntries(201, 202);
        Assert.That(new SeasonLedgerStore(_dbPath).GetRecoverySnapshot(), Has.Count.EqualTo(2)); // Assert, not Assume — a staging regression must FAIL this test, not skip it (cdx round-2 finding 3)

        // Throws exactly once, on whichever entry the (Attempts, Token) selection sweeps first.
        var poisonFault = new ThrowOnceBeforeCommitFaultInjector();
        var sweeper = new SeasonLedgerStore(
            _dbPath,
            poisonFault,
            ImmediateRetryRuntime.Instance,
            NoopRoundEnvelopeSpoolFaultInjector.Instance);

        var sweep = await sweeper.RecoverPendingRoundEndsAsync(cancellationToken: CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(sweep.Examined, Is.EqualTo(2));
            Assert.That(sweep.Acknowledged, Is.EqualTo(1));
            Assert.That(sweep.Faulted, Is.EqualTo(1));
        });

        var snapshot = sweeper.GetRecoverySnapshot();
        Assert.That(snapshot, Has.Count.EqualTo(1));
        var poison = snapshot.Single();
        var healthyRoundId = poison.RoundId == 201 ? 202 : 201;
        Assert.Multiple(() =>
        {
            Assert.That(poison.RoundId, Is.AnyOf(201, 202));
            Assert.That(poison.Attempts, Is.EqualTo(1));
            Assert.That(poison.State, Is.EqualTo(LedgerRecoveryState.Pending));
            Assert.That(poison.ErrorCategory, Is.EqualTo(LedgerRecoveryErrorCategory.StorageFaulted));
            Assert.That(ReadScalar($"SELECT COUNT(*) FROM round_envelopes WHERE round_id = {healthyRoundId};"),
                Is.EqualTo(1L));
            Assert.That(ReadScalar($"SELECT COUNT(*) FROM round_envelopes WHERE round_id = {poison.RoundId!.Value};"),
                Is.EqualTo(0L));
        });
    }

    [Test]
    public async Task Recovery_BoundedSweepStillReachesHealthyEntryAfterPoisonOccupiesTheFirstSlot()
    {
        StagePendingEntries(301, 302);

        // Throws exactly once ever: whichever entry sweep 1 selects (maxEntries=1) poisons; sweep 2 must
        // then prefer the still-fresh (Attempts=0) entry over the poisoned (Attempts=1) one, proving the
        // selection ordering — NOT filename/token order alone — decides which entry the bound admits.
        var poisonFault = new ThrowOnceBeforeCommitFaultInjector();
        var sweeper = new SeasonLedgerStore(
            _dbPath,
            poisonFault,
            ImmediateRetryRuntime.Instance,
            NoopRoundEnvelopeSpoolFaultInjector.Instance);

        var first = await sweeper.RecoverPendingRoundEndsAsync(1, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(first.Examined, Is.EqualTo(1));
            Assert.That(first.Acknowledged, Is.Zero);
            Assert.That(first.Faulted, Is.EqualTo(1));
            Assert.That(sweeper.GetRecoverySnapshot(), Has.Count.EqualTo(2));
        });

        var second = await sweeper.RecoverPendingRoundEndsAsync(1, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(second.Examined, Is.EqualTo(1));
            Assert.That(second.Acknowledged, Is.EqualTo(1), "the bound must reach the healthy entry, not re-select the poison entry");
            Assert.That(second.Faulted, Is.Zero);
        });

        var snapshot = sweeper.GetRecoverySnapshot();
        Assert.That(snapshot, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(snapshot[0].Attempts, Is.EqualTo(1));
            Assert.That(snapshot[0].ErrorCategory, Is.EqualTo(LedgerRecoveryErrorCategory.StorageFaulted));
        });
    }

    [Test]
    public async Task Recovery_CancellationDuringRetryDelayPropagatesAndIsNotSwallowedByTheFaultCatch()
    {
        StagePendingEntries(401);

        using var cts = new CancellationTokenSource();
        var runtime = new CancelDuringDelayRetryRuntime(cts);
        var sweeper = new SeasonLedgerStore(
            _dbPath,
            NoopRoundEnvelopeFaultInjector.Instance,
            runtime,
            NoopRoundEnvelopeSpoolFaultInjector.Instance);
        // Pre-initialize the schema on this instance before the DB is locked below, exactly like the
        // BusyDatabase_* tests — otherwise schema init itself would block on the provider's one-second
        // wait instead of the fast immediate-busy probe this test wants to drive quickly.
        await sweeper.GetCurrentSeasonAsync();
        await using var lockConnection = await InitializeAndLockDatabaseAsync();

        // CatchAsync (not ThrowsAsync, which requires an exact type match) because Task.Delay surfaces
        // cancellation as TaskCanceledException, a derived OperationCanceledException.
        Assert.CatchAsync<OperationCanceledException>(async () =>
            await sweeper.RecoverPendingRoundEndsAsync(cancellationToken: cts.Token));

        // The escaping cancellation must not have been caught by the broad fault handler and turned
        // into a persisted fault disposition.
        var snapshot = new SeasonLedgerStore(_dbPath).GetRecoverySnapshot();
        Assert.That(snapshot.Single().ErrorCategory, Is.Not.EqualTo(LedgerRecoveryErrorCategory.StorageFaulted));
    }

    [Test]
    public async Task MalformedTmpAndTamperedPending_AreQuarantinedAndBlockSeasonBump()
    {
        var store = new SeasonLedgerStore(_dbPath);
        await store.GetCurrentSeasonAsync();
        var spool = _dbPath + ".pending";
        Directory.CreateDirectory(spool);
        await File.WriteAllTextAsync(Path.Combine(spool, "abandoned.tmp"), "private");
        await File.WriteAllTextAsync(Path.Combine(spool, "0123456789abcdef0123456789abcdef.pending"), "{}");

        var snapshot = store.GetRecoverySnapshot();

        Assert.That(snapshot, Has.Count.EqualTo(2));
        Assert.That(snapshot, Has.All.Property(nameof(LedgerRecoverySnapshotItem.State)).EqualTo(LedgerRecoveryState.Quarantined));
        Assert.That(snapshot.Select(item => item.Token), Has.All.Matches<string>(token =>
            token.Length == 32 && token.All(ch => ch is >= 'a' and <= 'f' || ch is >= '0' and <= '9')));
        Assert.ThrowsAsync<LedgerRecoveryBlockedException>(async () => await store.BumpSeasonAsync());
    }

    [TestCase("null-canonical")]
    [TestCase("missing-canonical")]
    [TestCase("null-token")]
    [TestCase("missing-captured")]
    [TestCase("wrong-state")]
    [TestCase("null-nested")]
    [TestCase("missing-nested-players")]
    [TestCase("wrong-nested-round")]
    public async Task StructurallyInvalidPending_IsQuarantinedWithoutCrashingAndBlocksSeasonBump(string mutation)
    {
        var fault = new ThrowBeforeAcknowledge();
        var store = new SeasonLedgerStore(
            _dbPath,
            NoopRoundEnvelopeFaultInjector.Instance,
            ImmediateRetryRuntime.Instance,
            fault);
        Assert.ThrowsAsync<InjectedSpoolCrashException>(async () =>
            await store.PersistRoundEndAsync(Envelope(), CancellationToken.None));
        var pending = Directory.GetFiles(_dbPath + ".pending", "*.pending").Single();
        var root = JsonNode.Parse(await File.ReadAllTextAsync(pending))!.AsObject();
        switch (mutation)
        {
            case "null-canonical":
                root["canonical_json"] = null;
                break;
            case "missing-canonical":
                root.Remove("canonical_json");
                break;
            case "null-token":
                root["token"] = null;
                break;
            case "missing-captured":
                root.Remove("captured_utc");
                break;
            case "wrong-state":
                root["state"] = 999;
                break;
            case "null-nested":
                root["canonical_json"] = "null";
                break;
            case "missing-nested-players":
            {
                var canonical = JsonNode.Parse(root["canonical_json"]!.GetValue<string>())!.AsObject();
                canonical.Remove("players");
                root["canonical_json"] = canonical.ToJsonString();
                break;
            }
            case "wrong-nested-round":
            {
                var canonical = JsonNode.Parse(root["canonical_json"]!.GetValue<string>())!.AsObject();
                canonical["round_id"] = "not-an-integer";
                root["canonical_json"] = canonical.ToJsonString();
                break;
            }
            default:
                Assert.Fail("Unknown test mutation.");
                break;
        }

        await File.WriteAllTextAsync(pending, root.ToJsonString());

        IReadOnlyList<LedgerRecoverySnapshotItem> snapshot = Array.Empty<LedgerRecoverySnapshotItem>();
        Assert.DoesNotThrow(() => snapshot = store.GetRecoverySnapshot());
        Assert.That(snapshot, Has.Count.EqualTo(1));
        Assert.That(snapshot, Has.All.Property(nameof(LedgerRecoverySnapshotItem.State))
            .EqualTo(LedgerRecoveryState.Quarantined));
        Assert.ThrowsAsync<LedgerRecoveryBlockedException>(async () => await store.BumpSeasonAsync());
        Assert.DoesNotThrowAsync(async () => await store.RecoverPendingRoundEndsAsync());
    }

    [Test]
    public void Snapshot_DoesNotExposePrivateEnvelopeData()
    {
        var fault = new ThrowBeforeAcknowledge();
        var store = new SeasonLedgerStore(
            _dbPath,
            NoopRoundEnvelopeFaultInjector.Instance,
            ImmediateRetryRuntime.Instance,
            fault);
        Assert.ThrowsAsync<InjectedSpoolCrashException>(async () =>
            await store.PersistRoundEndAsync(Envelope(), CancellationToken.None));

        var json = JsonSerializer.Serialize(store.GetRecoverySnapshot());
        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain(Alice.ToString()));
            Assert.That(json, Does.Not.Contain("ContractSecret"));
            Assert.That(json, Does.Not.Contain("Secret"));
            Assert.That(json, Does.Not.Match("[A-F0-9]{64}"));
            Assert.That(json, Does.Not.Contain("InjectedSpoolCrashException"));
        });
    }

    [Test]
    public async Task FilesystemLease_PreventsSeasonBumpBetweenStageAndBinding()
    {
        var gate = new BlockAfterStage();
        var persister = new SeasonLedgerStore(
            _dbPath,
            NoopRoundEnvelopeFaultInjector.Instance,
            ImmediateRetryRuntime.Instance,
            gate);
        var bumper = new SeasonLedgerStore(_dbPath);
        var persistTask = Task.Run(() => persister.PersistRoundEndAsync(Envelope(), CancellationToken.None));
        Assert.That(gate.Staged.Wait(TimeSpan.FromSeconds(5)), Is.True);

        var bumpTask = Task.Run(() => bumper.BumpSeasonAsync());
        await Task.Delay(50);
        Assert.That(bumpTask.IsCompleted, Is.False, "bump must wait for the cross-instance lease");
        gate.Release.Set();
        var persisted = await persistTask;
        var bumped = await bumpTask;

        Assert.Multiple(() =>
        {
            Assert.That(persisted.Status, Is.EqualTo(RoundEndPersistenceStatus.Committed));
            Assert.That(bumped, Is.EqualTo("S2"));
            Assert.That(ReadString("SELECT season_id FROM round_envelopes WHERE round_id = 101;"), Is.EqualTo("S1"));
        });
    }

    [Test]
    public async Task Archive_PreservesEnvelopeAndAuditReceiptAndRejectsUnsafeInput()
    {
        var fault = new ThrowBeforeAcknowledge();
        var store = new SeasonLedgerStore(
            _dbPath,
            NoopRoundEnvelopeFaultInjector.Instance,
            ImmediateRetryRuntime.Instance,
            fault);
        Assert.ThrowsAsync<InjectedSpoolCrashException>(async () =>
            await store.PersistRoundEndAsync(Envelope(), CancellationToken.None));
        var token = store.GetRecoverySnapshot().Single().Token;

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => store.ArchivePendingRoundEnd("../escape", "operator-review"));
            Assert.Throws<ArgumentException>(() => store.ArchivePendingRoundEnd(token, "free form reason"));
        });
        store.ArchivePendingRoundEnd(token, "operator-review");

        var archive = Path.Combine(_dbPath + ".pending", "archive");
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(archive, token + ".pending")), Is.True);
            Assert.That(File.Exists(Path.Combine(archive, token + ".receipt.json")), Is.True);
            Assert.That(store.GetRecoverySnapshot(), Is.Empty);
        });

        Assert.DoesNotThrow(() => store.ArchivePendingRoundEnd(token, "operator-review"),
            "completed archive operations must be idempotent");
    }

    [Test]
    public async Task Archive_HandlesConflictAndQuarantineByTokenAndPreservesOriginalBytes()
    {
        var original = new SeasonLedgerStore(_dbPath);
        await original.PersistRoundEndAsync(Envelope(), CancellationToken.None);
        var conflicting = new SeasonLedgerStore(_dbPath);
        var changed = new RoundEndEnvelope(
            101,
            "Secret",
            new[] { new RoundEndPlayerRecord(Alice, new RoundContribution(false, false, false, 101, "Secret", 99)) },
            Array.Empty<ContractLogRecord>());
        Assert.ThrowsAsync<RoundEndReplayConflictException>(async () =>
            await conflicting.PersistRoundEndAsync(changed, CancellationToken.None));
        var conflictToken = conflicting.GetRecoverySnapshot().Single().Token;
        var conflictPath = Path.Combine(_dbPath + ".pending", conflictToken + ".pending");
        var conflictBytes = await File.ReadAllBytesAsync(conflictPath);

        conflicting.ArchivePendingRoundEnd(conflictToken, "operator-review");
        var archive = Path.Combine(_dbPath + ".pending", "archive");
        Assert.That(await File.ReadAllBytesAsync(Path.Combine(archive, conflictToken + ".pending")),
            Is.EqualTo(conflictBytes));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(archive, conflictToken + ".receipt.json")),
            Does.Contain("conflict"));

        var malformedToken = "0123456789abcdef0123456789abcdef";
        var malformed = Path.Combine(_dbPath + ".pending", malformedToken + ".pending");
        await File.WriteAllTextAsync(malformed, "{\"private\":\"unaltered evidence\"}");
        var quarantined = conflicting.GetRecoverySnapshot().Single();
        Assert.That(quarantined.State, Is.EqualTo(LedgerRecoveryState.Quarantined));
        var quarantinePath = Path.Combine(_dbPath + ".pending", "quarantine", quarantined.Token + ".quarantine");
        var quarantineBytes = await File.ReadAllBytesAsync(quarantinePath);

        conflicting.ArchivePendingRoundEnd(quarantined.Token, "operator-review");
        Assert.That(await File.ReadAllBytesAsync(Path.Combine(archive, quarantined.Token + ".quarantine")),
            Is.EqualTo(quarantineBytes));
        Assert.That(File.Exists(Path.Combine(archive, quarantined.Token + ".receipt.json")), Is.True);
        Assert.That(conflicting.GetRecoverySnapshot(), Is.Empty);
    }

    [TestCase("receipt")]
    [TestCase("evidence")]
    public async Task Archive_InterruptionIsRecoverableAndNeverDeletesEvidence(string failurePoint)
    {
        var failureStage = failurePoint == "receipt"
            ? RoundEnvelopeSpoolStage.AfterArchiveReceiptStaged
            : RoundEnvelopeSpoolStage.AfterArchiveEvidenceMoved;
        var fault = new SelectiveOneShotFault(failureStage);
        var store = new SeasonLedgerStore(
            _dbPath,
            NoopRoundEnvelopeFaultInjector.Instance,
            ImmediateRetryRuntime.Instance,
            fault);
        fault.Enabled = false;
        var stageFault = new ThrowBeforeAcknowledge();
        store = new SeasonLedgerStore(
            _dbPath,
            NoopRoundEnvelopeFaultInjector.Instance,
            ImmediateRetryRuntime.Instance,
            stageFault);
        Assert.ThrowsAsync<InjectedSpoolCrashException>(async () =>
            await store.PersistRoundEndAsync(Envelope(), CancellationToken.None));
        var token = store.GetRecoverySnapshot().Single().Token;

        fault.Enabled = true;
        store = new SeasonLedgerStore(
            _dbPath,
            NoopRoundEnvelopeFaultInjector.Instance,
            ImmediateRetryRuntime.Instance,
            fault);
        Assert.Throws<InjectedSpoolCrashException>(() =>
            store.ArchivePendingRoundEnd(token, "operator-review"));

        var archive = Path.Combine(_dbPath + ".pending", "archive");
        Assert.That(
            File.Exists(Path.Combine(_dbPath + ".pending", token + ".pending")) ||
            File.Exists(Path.Combine(archive, token + ".pending")),
            Is.True,
            "an interrupted archive must retain the original evidence in one durable location");
        Assert.That(File.Exists(Path.Combine(archive, token + ".receipt.staged")), Is.True);
        Assert.That(store.GetRecoverySnapshot().Select(item => item.Token), Does.Contain(token));
        Assert.ThrowsAsync<LedgerRecoveryBlockedException>(async () => await store.BumpSeasonAsync());

        Assert.DoesNotThrow(() => store.ArchivePendingRoundEnd(token, "operator-review"));
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(archive, token + ".pending")), Is.True);
            Assert.That(File.Exists(Path.Combine(archive, token + ".receipt.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(archive, token + ".receipt.staged")), Is.False);
        });
    }

    [Test]
    public void DirectoryDurability_UsesUnixFsyncAndHasExplicitWindowsFallback()
    {
        var result = SeasonLedgerSpool.TryFlushDirectoryDurably(_root);
        Assert.That(result, Is.EqualTo(OperatingSystem.IsWindows()
            ? DirectoryDurabilityResult.Unsupported
            : DirectoryDurabilityResult.Synced));
    }

    [Test]
    public async Task BusyDatabase_ProviderTimeoutDoesNotExtendControlledFourAttemptBudget()
    {
        var runtime = new RecordingRetryRuntime();
        var store = new SeasonLedgerStore(
            _dbPath,
            NoopRoundEnvelopeFaultInjector.Instance,
            runtime,
            NoopRoundEnvelopeSpoolFaultInjector.Instance);
        await store.GetCurrentSeasonAsync();
        await using var lockConnection = await InitializeAndLockDatabaseAsync();
        var stopwatch = Stopwatch.StartNew();

        var result = await store.PersistRoundEndAsync(Envelope(), CancellationToken.None);

        stopwatch.Stop();
        Assert.Multiple(() =>
        {
            Assert.That(result.Attempts, Is.EqualTo(4));
            Assert.That(runtime.Delays, Is.EqualTo(LedgerRetryPolicy.Delays));
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromMilliseconds(750)),
                "SQLite provider waiting must not add an implicit timeout to the controlled retry budget");
        });
    }

    [Test]
    public async Task LegacyLedgerWrite_RetainsBoundedProviderWaitOutsideRoundRetryPolicy()
    {
        var store = new SeasonLedgerStore(_dbPath);
        await store.GetCurrentSeasonAsync();
        await using var lockConnection = await InitializeAndLockDatabaseAsync();
        var stopwatch = Stopwatch.StartNew();

        Assert.ThrowsAsync<SqliteException>(async () => await store.AwardHrPointsAsync(Alice, 1));

        stopwatch.Stop();
        Assert.Multiple(() =>
        {
            Assert.That(stopwatch.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(750)),
                "ordinary Ledger writes retain the bounded one-second provider lock wait");
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2.5)));
        });
    }

    [Test]
    public async Task EveryPreLeaseRootCheckAndNestedDirectoryCreation_DurablySyncsOwningParent()
    {
        var durability = new RecordingDirectoryDurabilityRuntime();
        var spool = new SeasonLedgerSpool(_dbPath, durability);
        var spoolDirectory = _dbPath + ".pending";

        using var firstLease = spool.AcquireLease();
        Assert.That(durability.Paths.ToArray(), Is.EqualTo(new[] { _root }),
            "the new spool-root entry must be synced in the database parent before use");

        var competingLeaseTask = Task.Run(spool.AcquireLease);
        Assert.That(SpinWait.SpinUntil(() => durability.Paths.Count >= 2, TimeSpan.FromSeconds(2)), Is.True);
        Assert.That(competingLeaseTask.IsCompleted, Is.False);
        Assert.That(durability.Paths.ToArray(), Is.EqualTo(new[] { _root, _root }),
            "every contender must sync the root entry before attempting the cross-process lease");
        firstLease.Dispose();
        using var competingLease = await competingLeaseTask;

        var malformed = Path.Combine(spoolDirectory, "0123456789abcdef0123456789abcdef.pending");
        File.WriteAllText(malformed, "{}");
        spool.Snapshot();
        Assert.That(durability.Paths.Count(path => path == spoolDirectory), Is.GreaterThanOrEqualTo(1),
            "the new quarantine directory entry must be synced in the spool root");

        var entry = spool.StageOrReuse(Envelope().Canonicalize());
        var beforeArchive = durability.Paths.Count(path => path == spoolDirectory);
        spool.Archive(entry.Token, "operator-review", NoopRoundEnvelopeSpoolFaultInjector.Instance);
        Assert.That(durability.Paths.Count(path => path == spoolDirectory), Is.GreaterThan(beforeArchive),
            "the new archive directory entry must be synced in the spool root");
    }

    [Test]
    public async Task CompetitorAfterRawProbe_IsStillBoundedAndReturnsToFourAttemptPolicy()
    {
        var runtime = new RecordingRetryRuntime();
        var competitor = new LockAfterWriterProbe(_dbPath);
        var store = new SeasonLedgerStore(
            _dbPath,
            NoopRoundEnvelopeFaultInjector.Instance,
            runtime,
            competitor);
        await store.GetCurrentSeasonAsync();
        var stopwatch = Stopwatch.StartNew();

        var result = await store.PersistRoundEndAsync(Envelope(), CancellationToken.None);

        stopwatch.Stop();
        competitor.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(RoundEndPersistenceStatus.Pending));
            Assert.That(result.Attempts, Is.EqualTo(4));
            Assert.That(runtime.Delays, Is.EqualTo(LedgerRetryPolicy.Delays));
            Assert.That(competitor.Acquired, Is.True);
            Assert.That(stopwatch.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(750)));
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2.5)),
                "the managed one-second reservation bound must return control to the outer policy");
        });
    }

    private async Task<SqliteConnection> InitializeAndLockDatabaseAsync()
    {
        var initializer = new SeasonLedgerStore(_dbPath);
        await initializer.GetCurrentSeasonAsync();
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWrite,
            DefaultTimeout = 1,
        }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "BEGIN IMMEDIATE;";
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    private long ReadScalar(string sql, Guid? user = null)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (user != null)
            command.Parameters.AddWithValue("$user", user.Value.ToString());
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private string ReadString(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar())!;
    }

    private static RoundEndEnvelope Envelope() => new(
        101,
        "Secret",
        new[]
        {
            new RoundEndPlayerRecord(Alice, new RoundContribution(false, false, false, 101, "Secret", 3)),
        },
        new[] { new ContractLogRecord(101, Alice, "ContractSecret", "personal") });

    private static RoundEndEnvelope EnvelopeFor(int roundId) => new(
        roundId,
        "Secret",
        new[]
        {
            new RoundEndPlayerRecord(Alice, new RoundContribution(false, false, false, roundId, "Secret", 3)),
        },
        new[] { new ContractLogRecord(roundId, Alice, "ContractSecret", "personal") });

    /// <summary>
    ///     Stages each round as a fresh, never-attempted Pending spool entry (crashing the spool right
    ///     after the durable stage write, before any persistence attempt) so tests can control exactly
    ///     which entry a subsequent sweep's fault injector poisons.
    /// </summary>
    private void StagePendingEntries(params int[] roundIds)
    {
        var stageOnlyFault = new SelectiveOneShotFault(RoundEnvelopeSpoolStage.AfterDurableStage);
        var stager = new SeasonLedgerStore(
            _dbPath,
            NoopRoundEnvelopeFaultInjector.Instance,
            ImmediateRetryRuntime.Instance,
            stageOnlyFault);
        foreach (var roundId in roundIds)
        {
            stageOnlyFault.Enabled = true;
            Assert.ThrowsAsync<InjectedSpoolCrashException>(async () =>
                await stager.PersistRoundEndAsync(EnvelopeFor(roundId), CancellationToken.None));
        }
    }

    private sealed class RecordingRetryRuntime : ILedgerRetryRuntime
    {
        public List<TimeSpan> Delays { get; } = new();

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDirectoryDurabilityRuntime : IDirectoryDurabilityRuntime
    {
        public ConcurrentQueue<string> Paths { get; } = new();

        public DirectoryDurabilityResult Flush(string path)
        {
            Paths.Enqueue(path);
            return DirectoryDurabilityResult.Synced;
        }
    }

    private sealed class LockAfterWriterProbe(string dbPath) : IRoundEnvelopeSpoolFaultInjector, IDisposable
    {
        private SqliteConnection _connection = null!;
        public bool Acquired { get; private set; }

        public void Hit(RoundEnvelopeSpoolStage stage)
        {
            if (stage != RoundEnvelopeSpoolStage.AfterWriterProbeBeforeManagedReservation || Acquired)
                return;
            _connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWrite,
                DefaultTimeout = 1,
            }.ToString());
            _connection.Open();
            using var command = _connection.CreateCommand();
            command.CommandText = "BEGIN IMMEDIATE;";
            command.ExecuteNonQuery();
            Acquired = true;
        }

        public void Dispose()
        {
            if (!Acquired)
                return;
            using var command = _connection.CreateCommand();
            command.CommandText = "ROLLBACK;";
            command.ExecuteNonQuery();
            _connection.Dispose();
        }
    }

    private sealed class ThrowBeforeAcknowledge : IRoundEnvelopeSpoolFaultInjector
    {
        public bool Enabled { get; set; } = true;

        public void Hit(RoundEnvelopeSpoolStage stage)
        {
            if (Enabled && stage == RoundEnvelopeSpoolStage.BeforeAcknowledge)
                throw new InjectedSpoolCrashException();
        }
    }

    private sealed class ReleaseLockRetryRuntime(SqliteConnection connection) : ILedgerRetryRuntime
    {
        public List<TimeSpan> Delays { get; } = new();

        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            await using var command = connection.CreateCommand();
            command.CommandText = "ROLLBACK;";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private sealed class BlockAfterStage : IRoundEnvelopeSpoolFaultInjector
    {
        public ManualResetEventSlim Staged { get; } = new(false);
        public ManualResetEventSlim Release { get; } = new(false);

        public void Hit(RoundEnvelopeSpoolStage stage)
        {
            if (stage != RoundEnvelopeSpoolStage.AfterDurableStage)
                return;
            Staged.Set();
            if (!Release.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Test did not release the staged persistence gate.");
        }
    }

    private sealed class SelectiveOneShotFault(RoundEnvelopeSpoolStage target) : IRoundEnvelopeSpoolFaultInjector
    {
        public bool Enabled { get; set; }

        public void Hit(RoundEnvelopeSpoolStage stage)
        {
            if (!Enabled || stage != target)
                return;
            Enabled = false;
            throw new InjectedSpoolCrashException();
        }
    }

    private sealed class InjectedSpoolCrashException : Exception;

    /// <summary>
    ///     Throws a plain (non-Sqlite, non-conflict) exception exactly once, on the first entry whose
    ///     persistence attempt reaches <see cref="RoundEnvelopeStage.BeforeCommit"/>, regardless of
    ///     which round it belongs to. Every later attempt — whichever round it is for — succeeds.
    /// </summary>
    private sealed class ThrowOnceBeforeCommitFaultInjector : IRoundEnvelopeFaultInjector
    {
        private bool _armed = true;

        public void Hit(RoundEnvelopeStage stage, int index)
        {
            if (!_armed || stage != RoundEnvelopeStage.BeforeCommit)
                return;
            _armed = false;
            throw new InjectedPoisonException();
        }
    }

    private sealed class InjectedPoisonException : Exception;

    /// <summary>
    ///     Cancels the supplied source as soon as the retry policy asks for a delay, then lets the
    ///     already-cancelled token propagate a real <see cref="OperationCanceledException"/> out of
    ///     <c>Task.Delay</c> — the same interleaving <see cref="RecordingRetryRuntime"/> exercises for
    ///     the fixed retry schedule, but used here to prove cancellation escapes recovery's fault catch.
    /// </summary>
    private sealed class CancelDuringDelayRetryRuntime(CancellationTokenSource cancellation) : ILedgerRetryRuntime
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return Task.Delay(delay, cancellationToken);
        }
    }
}
