#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._Solreign.SeasonLedger;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Unit coverage for the <c>contracts_streak</c> table (SeasonLedgerStore.ContractsStreak.cs) — the
///     persistent per-account Solreign Contracts consecutive-shift streak (v14 quest-board extension,
///     spec §3), "Option B" reset semantics chosen at build time:
///       * completed >=1 Personal contract this round -> streak + 1;
///       * present at round end but completed zero -> streak resets to 0 ("a missed shift");
///       * ABSENT (this method simply never called for that account/round, because it never appears in
///         the round-end roster) -> no change at all — same presence-aware discipline Directives Fax
///         documents, never punish an absent player;
///       * a retried write for an already-recorded round is idempotent (no double-increment);
///       * a season bump does NOT reset a streak (career-scoped, the first_death/social_firsts/
///         directives_fax_streak law).
///     Same per-test temp-DB harness as <see cref="DirectivesFaxStreakStoreTests"/> — never a bin path,
///     never a shared file.
/// </summary>
[TestFixture]
[TestOf(typeof(SeasonLedgerStore))]
public sealed class ContractsStreakStoreTests
{
    private string _dbPath = default!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"solreign_contracts_streak_test_{Guid.NewGuid():N}.db");
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = _dbPath + suffix;
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // ignored — temp dir, harmless if left behind
            }
        }
    }

    [Test]
    public async Task FirstOutcome_UnknownAccount_CompletedTrue_StartsStreakAtOne()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        var (streak, best, isNewBest, wasReplay) = await store.RecordContractStreakOutcomeAsync(user, completedThisRound: true, roundId: 1);

        Assert.That(streak, Is.EqualTo(1));
        Assert.That(best, Is.EqualTo(1));
        Assert.That(isNewBest, Is.True);
        Assert.That(wasReplay, Is.False);
    }

    [Test]
    public async Task CompletedEachRound_ConsecutiveRounds_IncrementsTheStreak()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.RecordContractStreakOutcomeAsync(user, completedThisRound: true, roundId: 1);
        await store.RecordContractStreakOutcomeAsync(user, completedThisRound: true, roundId: 2);
        var (streak, best, _, _) = await store.RecordContractStreakOutcomeAsync(user, completedThisRound: true, roundId: 3);

        Assert.That(streak, Is.EqualTo(3));
        Assert.That(best, Is.EqualTo(3));
    }

    [Test]
    public async Task PresentButZeroCompletions_ResetsTheStreakToZero()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.RecordContractStreakOutcomeAsync(user, completedThisRound: true, roundId: 1);
        await store.RecordContractStreakOutcomeAsync(user, completedThisRound: true, roundId: 2);
        // "A missed shift" (Option B): present in the round-end roster, but zero Personal completions.
        var (streak, best, isNewBest, _) = await store.RecordContractStreakOutcomeAsync(user, completedThisRound: false, roundId: 3);

        Assert.That(streak, Is.EqualTo(0), "a shift with zero completions must reset the streak to zero");
        Assert.That(best, Is.EqualTo(2), "the best streak achieved must survive a reset");
        Assert.That(isNewBest, Is.False);
    }

    [Test]
    public async Task MissedShift_ThenCompletedAgain_StreakRebuildsFromZero()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.RecordContractStreakOutcomeAsync(user, completedThisRound: true, roundId: 1);
        await store.RecordContractStreakOutcomeAsync(user, completedThisRound: false, roundId: 2);
        var (streak, _, _, _) = await store.RecordContractStreakOutcomeAsync(user, completedThisRound: true, roundId: 3);

        Assert.That(streak, Is.EqualTo(1));
    }

    [Test]
    public async Task Absent_AccountNeverTouched_LeavesStreakUnchanged()
    {
        // "Absent" in production means the account never appears in ev.AllPlayersEndInfo, so
        // SeasonLedgerSystem.ContractsStreak.cs never calls this method for it that round -- so the
        // store-level contract to verify is: a round this account's row is never written for does not
        // appear in its history, i.e. its streak reflects only the rounds actually recorded, in order,
        // with no phantom resets from skipped rounds.
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.RecordContractStreakOutcomeAsync(user, completedThisRound: true, roundId: 1);
        // Round 2: account was absent -- production code never calls RecordContractStreakOutcomeAsync
        // for it. Nothing to do here; that omission IS the test.
        var (streak, _, _, _) = await store.RecordContractStreakOutcomeAsync(user, completedThisRound: true, roundId: 3);

        Assert.That(streak, Is.EqualTo(2), "skipping an absent round must not reset the streak -- it " +
            "must simply continue counting the rounds the account was actually present+completed for");
    }

    [Test]
    public async Task RetriedWrite_SameRoundId_IsIdempotent_DoesNotDoubleIncrement()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.RecordContractStreakOutcomeAsync(user, completedThisRound: true, roundId: 1);
        var first = await store.RecordContractStreakOutcomeAsync(user, completedThisRound: true, roundId: 2);
        // A retried write for the SAME round id (e.g. a crash-and-replay) must not increment again.
        var retried = await store.RecordContractStreakOutcomeAsync(user, completedThisRound: true, roundId: 2);

        Assert.That(retried.CurrentStreak, Is.EqualTo(first.CurrentStreak));
        Assert.That(retried.CurrentStreak, Is.EqualTo(2));
    }

    [Test]
    public async Task OutOfOrderWrite_OlderRoundId_IsRejected_DoesNotOverwriteNewerState()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.RecordContractStreakOutcomeAsync(user, completedThisRound: true, roundId: 1);
        await store.RecordContractStreakOutcomeAsync(user, completedThisRound: true, roundId: 3);
        // Delayed fold for round 2 must not overwrite last_round_id=3 or reset/increment the streak.
        var delayed = await store.RecordContractStreakOutcomeAsync(user, completedThisRound: false, roundId: 2);

        Assert.That(delayed.CurrentStreak, Is.EqualTo(2),
            "an older roundId must be rejected when last_round_id is already newer");

        // A later same-round replay of round 3 must also remain idempotent under the >= guard.
        var replay = await store.RecordContractStreakOutcomeAsync(user, completedThisRound: true, roundId: 3);
        Assert.That(replay.CurrentStreak, Is.EqualTo(2));

        var (streak, best) = await store.GetContractStreakAsync(user);
        Assert.That(streak, Is.EqualTo(2));
        Assert.That(best, Is.EqualTo(2));
    }

    [Test]
    public async Task DistinctAccounts_TrackStreaksIndependently()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await store.RecordContractStreakOutcomeAsync(userA, completedThisRound: true, roundId: 1);
        await store.RecordContractStreakOutcomeAsync(userA, completedThisRound: true, roundId: 2);
        await store.RecordContractStreakOutcomeAsync(userB, completedThisRound: false, roundId: 1);

        var (streakA, _) = await store.GetContractStreakAsync(userA);
        var (streakB, _) = await store.GetContractStreakAsync(userB);

        Assert.That(streakA, Is.EqualTo(2));
        Assert.That(streakB, Is.EqualTo(0));
    }

    [Test]
    public async Task SeasonBump_DoesNotResetTheStreak()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.RecordContractStreakOutcomeAsync(user, completedThisRound: true, roundId: 1);
        await store.RecordContractStreakOutcomeAsync(user, completedThisRound: true, roundId: 2);

        await store.BumpSeasonAsync();

        var (streak, _) = await store.GetContractStreakAsync(user);
        Assert.That(streak, Is.EqualTo(2), "a contracts streak is a career event -- a season bump must never reset it");
    }

    [Test]
    public async Task GetStreak_UnknownAccount_ReturnsZeroes()
    {
        var store = new SeasonLedgerStore(_dbPath);

        var (streak, best) = await store.GetContractStreakAsync(Guid.NewGuid());

        Assert.That(streak, Is.EqualTo(0));
        Assert.That(best, Is.EqualTo(0));
    }

    [Test]
    public async Task DisconnectedRoster_StoreNeverCalled_LeavesStreakUnchanged()
    {
        // Production honors info.Connected by never invoking RecordContractStreakOutcomeAsync for
        // disconnected roster members (ContractsStreakRules.ShouldFoldAccount). Store-level contract:
        // omission leaves the streak untouched — same as true absence.
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.RecordContractStreakOutcomeAsync(user, completedThisRound: true, roundId: 1);
        // Round 2: disconnected at round end — fold skips the account entirely.
        var (streak, _) = await store.GetContractStreakAsync(user);

        Assert.That(streak, Is.EqualTo(1), "disconnected players must neither advance nor reset");
    }

    [Test]
    public async Task BatchFold_MidBatchFault_RollsBackAllAccounts_NoPartialFold()
    {
        // Atomic-fold failure: a throw after the first account write (still inside the open
        // transaction) must roll back every account — never a permanent partial fold.
        var store = new SeasonLedgerStore(_dbPath);
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        store.ContractStreakAfterAccountHook = index =>
        {
            if (index == 0)
                throw new InvalidOperationException("injected mid-batch fault");
            return Task.CompletedTask;
        };

        var folds = new[]
        {
            new ContractStreakFoldRequest(userA, CompletedThisRound: true),
            new ContractStreakFoldRequest(userB, CompletedThisRound: true),
        };

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.RecordContractStreakOutcomesAsync(folds, roundId: 1));

        store.ContractStreakAfterAccountHook = null;

        var (streakA, _) = await store.GetContractStreakAsync(userA);
        var (streakB, _) = await store.GetContractStreakAsync(userB);
        Assert.That(streakA, Is.EqualTo(0), "rolled-back batch must not leave account A folded");
        Assert.That(streakB, Is.EqualTo(0), "rolled-back batch must not leave account B folded");
    }

    [Test]
    public async Task BatchFold_MultipleAccounts_CommitsAtomically()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        var results = await store.RecordContractStreakOutcomesAsync(
            new[]
            {
                new ContractStreakFoldRequest(userA, CompletedThisRound: true),
                new ContractStreakFoldRequest(userB, CompletedThisRound: false),
            },
            roundId: 1);

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].CurrentStreak, Is.EqualTo(1));
        Assert.That(results[0].WasReplay, Is.False);
        Assert.That(results[1].CurrentStreak, Is.EqualTo(0));
        Assert.That(results[1].WasReplay, Is.False);
    }

    [Test]
    public async Task EnvelopeCommitted_FoldFailed_RecoveryAppliesFoldsExactlyOnce()
    {
        // Durability seam: envelope TX journals fold intent; fold TX fails (both attempts in
        // production). Restart / recovery must apply unconsumed journals exactly once.
        var store = new SeasonLedgerStore(_dbPath);
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var folds = new[]
        {
            new ContractStreakFoldRequest(userA, CompletedThisRound: true),
            new ContractStreakFoldRequest(userB, CompletedThisRound: true),
        };

        var envelope = new RoundEndEnvelope(
            roundId: 42,
            gamemode: "Secret",
            players: new[]
            {
                new RoundEndPlayerRecord(userA, new RoundContribution(
                    WasCaptainClean: false, AntagWin: false, EarlyDeath: false,
                    RoundId: 42, Gamemode: "Secret", Standing: 0,
                    ContractsCompleted: 1, ContractScore: 2, HrPointsEarned: 0)),
                new RoundEndPlayerRecord(userB, new RoundContribution(
                    WasCaptainClean: false, AntagWin: false, EarlyDeath: false,
                    RoundId: 42, Gamemode: "Secret", Standing: 0,
                    ContractsCompleted: 1, ContractScore: 2, HrPointsEarned: 0)),
            },
            contracts: new[]
            {
                new ContractLogRecord(42, userA, "SolContractColaAudit", "personal"),
                new ContractLogRecord(42, userB, "SolContractPenRecovery", "personal"),
            });

        // Envelope + journal commit together.
        var persist = await store.PersistRoundEndAsync(envelope, folds);
        Assert.That(persist.Status, Is.EqualTo(RoundEndPersistenceStatus.Committed)
            .Or.EqualTo(RoundEndPersistenceStatus.AlreadyCommitted));
        Assert.That(await store.HasUnconsumedContractStreakFoldsAsync(42), Is.True,
            "fold intent must be durable after envelope commit, before fold TX");

        // Fold TX fails (simulates production double-attempt exhaustion / crash mid-fold).
        store.ContractStreakAfterAccountHook = _ =>
            throw new InvalidOperationException("injected fold failure after envelope commit");
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.RecordContractStreakOutcomesAsync(folds, roundId: 42));
        store.ContractStreakAfterAccountHook = null;

        // Streaks untouched; journal still unconsumed.
        var (streakA0, _) = await store.GetContractStreakAsync(userA);
        var (streakB0, _) = await store.GetContractStreakAsync(userB);
        Assert.That(streakA0, Is.EqualTo(0));
        Assert.That(streakB0, Is.EqualTo(0));
        Assert.That(await store.HasUnconsumedContractStreakFoldsAsync(42), Is.True);

        // Recovery (new store handle = restart) applies unconsumed journals exactly once.
        var restarted = new SeasonLedgerStore(_dbPath);
        var recovered = await restarted.RecoverPendingContractStreakFoldsAsync();
        Assert.That(recovered, Is.EqualTo(1));

        var (streakA, _) = await restarted.GetContractStreakAsync(userA);
        var (streakB, _) = await restarted.GetContractStreakAsync(userB);
        Assert.That(streakA, Is.EqualTo(1));
        Assert.That(streakB, Is.EqualTo(1));
        Assert.That(await restarted.HasUnconsumedContractStreakFoldsAsync(42), Is.False);

        // Second recovery is a no-op (exactly once).
        var recoveredAgain = await restarted.RecoverPendingContractStreakFoldsAsync();
        Assert.That(recoveredAgain, Is.EqualTo(0));
        var (streakA2, _) = await restarted.GetContractStreakAsync(userA);
        Assert.That(streakA2, Is.EqualTo(1), "recovery must not double-increment");
    }

    [Test]
    public async Task CrashAfterStagingBeforePersist_RecoveryAppliesFoldsExactlyOnce()
    {
        // Finding 4: crash after durable spool stage but before envelope DB persist must not lose
        // fold intents — they ride on SpoolEntry and recovery re-supplies them.
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var folds = new[]
        {
            new ContractStreakFoldRequest(userA, CompletedThisRound: true),
            new ContractStreakFoldRequest(userB, CompletedThisRound: true),
        };

        var envelope = new RoundEndEnvelope(
            roundId: 77,
            gamemode: "Secret",
            players: new[]
            {
                new RoundEndPlayerRecord(userA, new RoundContribution(
                    WasCaptainClean: false, AntagWin: false, EarlyDeath: false,
                    RoundId: 77, Gamemode: "Secret", Standing: 0,
                    ContractsCompleted: 1, ContractScore: 2, HrPointsEarned: 0)),
                new RoundEndPlayerRecord(userB, new RoundContribution(
                    WasCaptainClean: false, AntagWin: false, EarlyDeath: false,
                    RoundId: 77, Gamemode: "Secret", Standing: 0,
                    ContractsCompleted: 1, ContractScore: 2, HrPointsEarned: 0)),
            },
            contracts: new[]
            {
                new ContractLogRecord(77, userA, "SolContractColaAudit", "personal"),
                new ContractLogRecord(77, userB, "SolContractPenRecovery", "personal"),
            });

        var stageCrash = new ThrowAfterDurableStageOnce();
        var store = new SeasonLedgerStore(
            _dbPath,
            NoopRoundEnvelopeFaultInjector.Instance,
            ImmediateRetryRuntime.Instance,
            stageCrash);

        Assert.ThrowsAsync<InjectedStageCrashException>(async () =>
            await store.PersistRoundEndAsync(envelope, folds));

        // No envelope, no journal yet — folds only live on the pending spool entry.
        Assert.That(await store.HasUnconsumedContractStreakFoldsAsync(77), Is.False);
        var (preA, _) = await store.GetContractStreakAsync(userA);
        Assert.That(preA, Is.EqualTo(0));
        Assert.That(store.GetRecoverySnapshot(), Has.Count.EqualTo(1));

        // Recovery re-supplies staged folds into the envelope TX journal; fold recovery applies them.
        var restarted = new SeasonLedgerStore(_dbPath);
        var sweep = await restarted.RecoverPendingRoundEndsAsync();
        Assert.That(sweep.Acknowledged, Is.EqualTo(1));
        Assert.That(await restarted.HasUnconsumedContractStreakFoldsAsync(77), Is.True,
            "recovery must journal folds that were staged on the spool");

        var recovered = await restarted.RecoverPendingContractStreakFoldsAsync();
        Assert.That(recovered, Is.EqualTo(1));
        var (streakA, _) = await restarted.GetContractStreakAsync(userA);
        var (streakB, _) = await restarted.GetContractStreakAsync(userB);
        Assert.That(streakA, Is.EqualTo(1));
        Assert.That(streakB, Is.EqualTo(1));
        Assert.That(await restarted.HasUnconsumedContractStreakFoldsAsync(77), Is.False);

        Assert.That(await restarted.RecoverPendingContractStreakFoldsAsync(), Is.EqualTo(0));
        var (streakA2, _) = await restarted.GetContractStreakAsync(userA);
        Assert.That(streakA2, Is.EqualTo(1), "recovery must not double-increment");
    }

    [Test]
    public async Task ReplayOfCommittedEnvelopeWithFolds_JournalsAndAppliesOnce()
    {
        // Finding 4: exact-envelope AlreadyCommitted path must still journal supplied folds
        // (INSERT OR IGNORE) so a replay that lost the journal side still folds exactly once.
        var store = new SeasonLedgerStore(_dbPath);
        var userA = Guid.NewGuid();
        var folds = new[]
        {
            new ContractStreakFoldRequest(userA, CompletedThisRound: true),
        };

        var envelope = new RoundEndEnvelope(
            roundId: 88,
            gamemode: "Secret",
            players: new[]
            {
                new RoundEndPlayerRecord(userA, new RoundContribution(
                    WasCaptainClean: false, AntagWin: false, EarlyDeath: false,
                    RoundId: 88, Gamemode: "Secret", Standing: 0,
                    ContractsCompleted: 1, ContractScore: 2, HrPointsEarned: 0)),
            },
            contracts: new[]
            {
                new ContractLogRecord(88, userA, "SolContractColaAudit", "personal"),
            });

        // First commit: envelope only, NO folds journaled (simulates pre-fix commit / journal gap).
        var first = await store.PersistRoundEndAsync(envelope, pendingStreakFolds: null);
        Assert.That(first.Status, Is.EqualTo(RoundEndPersistenceStatus.Committed));
        Assert.That(await store.HasUnconsumedContractStreakFoldsAsync(88), Is.False);

        // Replay exact envelope WITH folds — AlreadyCommitted must journal them first.
        var replay = await store.PersistRoundEndAsync(envelope, folds);
        Assert.That(replay.Status, Is.EqualTo(RoundEndPersistenceStatus.AlreadyCommitted));
        Assert.That(await store.HasUnconsumedContractStreakFoldsAsync(88), Is.True,
            "AlreadyCommitted must journal supplied fold intents");

        // Apply once via normal fold path.
        var results = await store.RecordContractStreakOutcomesAsync(folds, roundId: 88);
        Assert.That(results[0].CurrentStreak, Is.EqualTo(1));
        Assert.That(results[0].WasReplay, Is.False);
        Assert.That(await store.HasUnconsumedContractStreakFoldsAsync(88), Is.False);

        // Second AlreadyCommitted replay must not re-open consumed journal (INSERT OR IGNORE).
        var replayAgain = await store.PersistRoundEndAsync(envelope, folds);
        Assert.That(replayAgain.Status, Is.EqualTo(RoundEndPersistenceStatus.AlreadyCommitted));
        Assert.That(await store.HasUnconsumedContractStreakFoldsAsync(88), Is.False,
            "consumed journal rows must not be re-opened by AlreadyCommitted replay");

        // Fold again is WasReplay; streak stays 1.
        var results2 = await store.RecordContractStreakOutcomesAsync(folds, roundId: 88);
        Assert.That(results2[0].WasReplay, Is.True);
        var (streak, _) = await store.GetContractStreakAsync(userA);
        Assert.That(streak, Is.EqualTo(1));
    }

    private sealed class ThrowAfterDurableStageOnce : IRoundEnvelopeSpoolFaultInjector
    {
        public void Hit(RoundEnvelopeSpoolStage stage)
        {
            if (stage == RoundEnvelopeSpoolStage.AfterDurableStage)
                throw new InjectedStageCrashException();
        }
    }

    private sealed class InjectedStageCrashException : Exception;
    [Test]
    public async Task ReuseEnrichFoldlessThenCrash_RecoveryAppliesFoldsExactlyOnce()
    {
        // ROUND-6: foldless stage, then retry with folds must durably enrich before crash.
        var userA = Guid.NewGuid();
        var folds = new[]
        {
            new ContractStreakFoldRequest(userA, CompletedThisRound: true),
        };

        var envelope = new RoundEndEnvelope(
            roundId: 91,
            gamemode: "Secret",
            players: new[]
            {
                new RoundEndPlayerRecord(userA, new RoundContribution(
                    WasCaptainClean: false, AntagWin: false, EarlyDeath: false,
                    RoundId: 91, Gamemode: "Secret", Standing: 0,
                    ContractsCompleted: 1, ContractScore: 2, HrPointsEarned: 0)),
            },
            contracts: new[]
            {
                new ContractLogRecord(91, userA, "SolContractColaAudit", "personal"),
            });

        // First attempt: stage foldless, crash after durable stage.
        var foldlessCrash = new ThrowAfterDurableStageOnce();
        var foldlessStore = new SeasonLedgerStore(
            _dbPath,
            NoopRoundEnvelopeFaultInjector.Instance,
            ImmediateRetryRuntime.Instance,
            foldlessCrash);
        Assert.ThrowsAsync<InjectedStageCrashException>(async () =>
            await foldlessStore.PersistRoundEndAsync(envelope, pendingStreakFolds: null));
        Assert.That(foldlessStore.GetRecoverySnapshot(), Has.Count.EqualTo(1));
        Assert.That(await foldlessStore.HasUnconsumedContractStreakFoldsAsync(91), Is.False);

        // Second attempt: reuse matching entry, enrich with folds, crash again after stage.
        var enrichCrash = new ThrowAfterDurableStageOnce();
        var enrichStore = new SeasonLedgerStore(
            _dbPath,
            NoopRoundEnvelopeFaultInjector.Instance,
            ImmediateRetryRuntime.Instance,
            enrichCrash);
        Assert.ThrowsAsync<InjectedStageCrashException>(async () =>
            await enrichStore.PersistRoundEndAsync(envelope, folds));
        Assert.That(enrichStore.GetRecoverySnapshot(), Has.Count.EqualTo(1));
        Assert.That(await enrichStore.HasUnconsumedContractStreakFoldsAsync(91), Is.False);

        // Recovery must apply the enriched folds from the spool entry.
        var restarted = new SeasonLedgerStore(_dbPath);
        var sweep = await restarted.RecoverPendingRoundEndsAsync();
        Assert.That(sweep.Acknowledged, Is.EqualTo(1));
        Assert.That(await restarted.HasUnconsumedContractStreakFoldsAsync(91), Is.True,
            "reuse-enrich before crash must leave folds on the durable spool entry");

        var recovered = await restarted.RecoverPendingContractStreakFoldsAsync();
        Assert.That(recovered, Is.EqualTo(1));
        var (streakA, _) = await restarted.GetContractStreakAsync(userA);
        Assert.That(streakA, Is.EqualTo(1));
        Assert.That(await restarted.HasUnconsumedContractStreakFoldsAsync(91), Is.False);
        Assert.That(await restarted.RecoverPendingContractStreakFoldsAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task FoldedEntry_FoldlessRetry_PersistsReconciledFolds()
    {
        // ROUND-6: entry staged WITH folds; foldless retry must use entry.PendingStreakFolds
        // and not commit-and-delete the only durable fold copy without journaling.
        var userA = Guid.NewGuid();
        var folds = new[]
        {
            new ContractStreakFoldRequest(userA, CompletedThisRound: true),
        };

        var envelope = new RoundEndEnvelope(
            roundId: 92,
            gamemode: "Secret",
            players: new[]
            {
                new RoundEndPlayerRecord(userA, new RoundContribution(
                    WasCaptainClean: false, AntagWin: false, EarlyDeath: false,
                    RoundId: 92, Gamemode: "Secret", Standing: 0,
                    ContractsCompleted: 1, ContractScore: 2, HrPointsEarned: 0)),
            },
            contracts: new[]
            {
                new ContractLogRecord(92, userA, "SolContractColaAudit", "personal"),
            });

        var stageCrash = new ThrowAfterDurableStageOnce();
        var stager = new SeasonLedgerStore(
            _dbPath,
            NoopRoundEnvelopeFaultInjector.Instance,
            ImmediateRetryRuntime.Instance,
            stageCrash);
        Assert.ThrowsAsync<InjectedStageCrashException>(async () =>
            await stager.PersistRoundEndAsync(envelope, folds));
        Assert.That(stager.GetRecoverySnapshot(), Has.Count.EqualTo(1));

        // Foldless retry reuses the folded entry and must journal those folds on commit.
        var retry = new SeasonLedgerStore(_dbPath);
        var result = await retry.PersistRoundEndAsync(envelope, pendingStreakFolds: null);
        Assert.That(result.Status, Is.EqualTo(RoundEndPersistenceStatus.Committed)
            .Or.EqualTo(RoundEndPersistenceStatus.AlreadyCommitted));
        Assert.That(await retry.HasUnconsumedContractStreakFoldsAsync(92), Is.True,
            "foldless retry must persist the reconciled entry folds, not drop them");

        var applied = await retry.RecordContractStreakOutcomesAsync(folds, roundId: 92);
        Assert.That(applied[0].CurrentStreak, Is.EqualTo(1));
        Assert.That(applied[0].WasReplay, Is.False);
        Assert.That(await retry.HasUnconsumedContractStreakFoldsAsync(92), Is.False);
    }

    [Test]
    public async Task ConflictingFoldsOnReuse_FailClosed()
    {
        // ROUND-6: both existing and caller have nonempty differing folds => throw, never silently pick one.
        var userA = Guid.NewGuid();
        var foldsA = new[]
        {
            new ContractStreakFoldRequest(userA, CompletedThisRound: true),
        };
        var foldsB = new[]
        {
            new ContractStreakFoldRequest(userA, CompletedThisRound: false),
        };

        var envelope = new RoundEndEnvelope(
            roundId: 93,
            gamemode: "Secret",
            players: new[]
            {
                new RoundEndPlayerRecord(userA, new RoundContribution(
                    WasCaptainClean: false, AntagWin: false, EarlyDeath: false,
                    RoundId: 93, Gamemode: "Secret", Standing: 0,
                    ContractsCompleted: 1, ContractScore: 2, HrPointsEarned: 0)),
            },
            contracts: new[]
            {
                new ContractLogRecord(93, userA, "SolContractColaAudit", "personal"),
            });

        var stageCrash = new ThrowAfterDurableStageOnce();
        var stager = new SeasonLedgerStore(
            _dbPath,
            NoopRoundEnvelopeFaultInjector.Instance,
            ImmediateRetryRuntime.Instance,
            stageCrash);
        Assert.ThrowsAsync<InjectedStageCrashException>(async () =>
            await stager.PersistRoundEndAsync(envelope, foldsA));

        var conflicting = new SeasonLedgerStore(_dbPath);
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await conflicting.PersistRoundEndAsync(envelope, foldsB));

        // Original folds must still be on the pending entry for recovery.
        Assert.That(conflicting.GetRecoverySnapshot(), Has.Count.EqualTo(1));
        var recovered = new SeasonLedgerStore(_dbPath);
        var sweep = await recovered.RecoverPendingRoundEndsAsync();
        Assert.That(sweep.Acknowledged, Is.EqualTo(1));
        Assert.That(await recovered.HasUnconsumedContractStreakFoldsAsync(93), Is.True,
            "fail-closed conflict must leave original durable folds intact");
        Assert.That(await recovered.RecoverPendingContractStreakFoldsAsync(), Is.EqualTo(1));
        var (streak, _) = await recovered.GetContractStreakAsync(userA);
        Assert.That(streak, Is.EqualTo(1), "original CompletedThisRound:true folds must win after recovery");
    }

    [Test]
    public async Task ConflictingJournalOnAlreadyCommittedReplay_FailClosed_OriginalRecoverable()
    {
        // ROUND-7: after spool ack, exact-envelope replay with DIFFERENT folds must fail closed
        // at journal time — not INSERT OR IGNORE original A then apply/consume B.
        var store = new SeasonLedgerStore(_dbPath);
        var userA = Guid.NewGuid();
        var foldsA = new[]
        {
            new ContractStreakFoldRequest(userA, CompletedThisRound: true),
        };
        var foldsB = new[]
        {
            new ContractStreakFoldRequest(userA, CompletedThisRound: false),
        };

        var envelope = new RoundEndEnvelope(
            roundId: 94,
            gamemode: "Secret",
            players: new[]
            {
                new RoundEndPlayerRecord(userA, new RoundContribution(
                    WasCaptainClean: false, AntagWin: false, EarlyDeath: false,
                    RoundId: 94, Gamemode: "Secret", Standing: 0,
                    ContractsCompleted: 1, ContractScore: 2, HrPointsEarned: 0)),
            },
            contracts: new[]
            {
                new ContractLogRecord(94, userA, "SolContractColaAudit", "personal"),
            });

        // Commit + journal A; crash before fold (journal remains unconsumed).
        var first = await store.PersistRoundEndAsync(envelope, foldsA);
        Assert.That(first.Status, Is.EqualTo(RoundEndPersistenceStatus.Committed)
            .Or.EqualTo(RoundEndPersistenceStatus.AlreadyCommitted));
        Assert.That(await store.HasUnconsumedContractStreakFoldsAsync(94), Is.True);
        var (preStreak, _) = await store.GetContractStreakAsync(userA);
        Assert.That(preStreak, Is.EqualTo(0), "fold must not have run yet");

        // Exact-envelope replay with conflicting folds B — fail closed; do not apply/consume.
        Assert.ThrowsAsync<ContractStreakFoldConflictException>(async () =>
            await store.PersistRoundEndAsync(envelope, foldsB));

        Assert.That(await store.HasUnconsumedContractStreakFoldsAsync(94), Is.True,
            "original journal A must remain unconsumed after fail-closed conflict");
        var (midStreak, _) = await store.GetContractStreakAsync(userA);
        Assert.That(midStreak, Is.EqualTo(0), "conflicting replay must not apply folds B");

        // Recovery applies original A.
        var recovered = await store.RecoverPendingContractStreakFoldsAsync();
        Assert.That(recovered, Is.EqualTo(1));
        var (streak, _) = await store.GetContractStreakAsync(userA);
        Assert.That(streak, Is.EqualTo(1), "original CompletedThisRound:true journal must apply on recovery");
        Assert.That(await store.HasUnconsumedContractStreakFoldsAsync(94), Is.False);
    }

    [Test]
    public async Task IdenticalJournalReplay_IsIdempotent()
    {
        // ROUND-7 complement: same-envelope same-folds AlreadyCommitted must still journal
        // idempotently (INSERT OR IGNORE) without conflict throw.
        var store = new SeasonLedgerStore(_dbPath);
        var userA = Guid.NewGuid();
        var folds = new[]
        {
            new ContractStreakFoldRequest(userA, CompletedThisRound: true),
        };

        var envelope = new RoundEndEnvelope(
            roundId: 95,
            gamemode: "Secret",
            players: new[]
            {
                new RoundEndPlayerRecord(userA, new RoundContribution(
                    WasCaptainClean: false, AntagWin: false, EarlyDeath: false,
                    RoundId: 95, Gamemode: "Secret", Standing: 0,
                    ContractsCompleted: 1, ContractScore: 2, HrPointsEarned: 0)),
            },
            contracts: new[]
            {
                new ContractLogRecord(95, userA, "SolContractColaAudit", "personal"),
            });

        var first = await store.PersistRoundEndAsync(envelope, folds);
        Assert.That(first.Status, Is.EqualTo(RoundEndPersistenceStatus.Committed));
        Assert.That(await store.HasUnconsumedContractStreakFoldsAsync(95), Is.True);

        var replay = await store.PersistRoundEndAsync(envelope, folds);
        Assert.That(replay.Status, Is.EqualTo(RoundEndPersistenceStatus.AlreadyCommitted));
        Assert.That(await store.HasUnconsumedContractStreakFoldsAsync(95), Is.True);

        var results = await store.RecordContractStreakOutcomesAsync(folds, roundId: 95);
        Assert.That(results[0].CurrentStreak, Is.EqualTo(1));
        Assert.That(results[0].WasReplay, Is.False);
    }


    [Test]
    public async Task ConsumedJournalA_ReplayWithDifferentUserSetB_FailClosed()
    {
        // ROUND-8 P1: after A is folded+consumed, exact-envelope replay with a DIFFERENT user set B
        // must fail closed at journal reconciliation (ALL rows, not only unconsumed). Otherwise B
        // inserts and folds while Connected is excluded from the envelope hash.
        var store = new SeasonLedgerStore(_dbPath);
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var foldsA = new[]
        {
            new ContractStreakFoldRequest(userA, CompletedThisRound: true),
        };
        var foldsB = new[]
        {
            new ContractStreakFoldRequest(userB, CompletedThisRound: true),
        };

        var envelope = new RoundEndEnvelope(
            roundId: 96,
            gamemode: "Secret",
            players: new[]
            {
                new RoundEndPlayerRecord(userA, new RoundContribution(
                    WasCaptainClean: false, AntagWin: false, EarlyDeath: false,
                    RoundId: 96, Gamemode: "Secret", Standing: 0,
                    ContractsCompleted: 1, ContractScore: 2, HrPointsEarned: 0)),
            },
            contracts: new[]
            {
                new ContractLogRecord(96, userA, "SolContractColaAudit", "personal"),
            });

        var first = await store.PersistRoundEndAsync(envelope, foldsA);
        Assert.That(first.Status, Is.EqualTo(RoundEndPersistenceStatus.Committed)
            .Or.EqualTo(RoundEndPersistenceStatus.AlreadyCommitted));
        Assert.That(await store.HasUnconsumedContractStreakFoldsAsync(96), Is.True);

        var applied = await store.RecordContractStreakOutcomesAsync(foldsA, roundId: 96);
        Assert.That(applied[0].CurrentStreak, Is.EqualTo(1));
        Assert.That(applied[0].WasReplay, Is.False);
        Assert.That(await store.HasUnconsumedContractStreakFoldsAsync(96), Is.False,
            "A must be consumed after successful fold");

        Assert.ThrowsAsync<ContractStreakFoldConflictException>(async () =>
            await store.PersistRoundEndAsync(envelope, foldsB));

        Assert.That(await store.HasUnconsumedContractStreakFoldsAsync(96), Is.False,
            "fail-closed B must not insert unconsumed B-only journal rows");
        var (streakA, _) = await store.GetContractStreakAsync(userA);
        var (streakB, _) = await store.GetContractStreakAsync(userB);
        Assert.That(streakA, Is.EqualTo(1), "original A fold must remain");
        Assert.That(streakB, Is.EqualTo(0), "conflicting B must not fold");
    }

    [Test]
    public async Task JournalA_DirectFoldSubsetB_FailClosed_OriginalRecoverable()
    {
        // ROUND-8 P1: journal holds full set A; a mismatched/subset direct fold B must fail closed
        // (nothing applied, nothing consumed) so recovery can still apply A exactly once.
        var store = new SeasonLedgerStore(_dbPath);
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var foldsA = new[]
        {
            new ContractStreakFoldRequest(userA, CompletedThisRound: true),
            new ContractStreakFoldRequest(userB, CompletedThisRound: true),
        };
        var foldsSubset = new[]
        {
            new ContractStreakFoldRequest(userA, CompletedThisRound: true),
        };

        var envelope = new RoundEndEnvelope(
            roundId: 97,
            gamemode: "Secret",
            players: new[]
            {
                new RoundEndPlayerRecord(userA, new RoundContribution(
                    WasCaptainClean: false, AntagWin: false, EarlyDeath: false,
                    RoundId: 97, Gamemode: "Secret", Standing: 0,
                    ContractsCompleted: 1, ContractScore: 2, HrPointsEarned: 0)),
                new RoundEndPlayerRecord(userB, new RoundContribution(
                    WasCaptainClean: false, AntagWin: false, EarlyDeath: false,
                    RoundId: 97, Gamemode: "Secret", Standing: 0,
                    ContractsCompleted: 1, ContractScore: 2, HrPointsEarned: 0)),
            },
            contracts: new[]
            {
                new ContractLogRecord(97, userA, "SolContractColaAudit", "personal"),
                new ContractLogRecord(97, userB, "SolContractColaAudit", "personal"),
            });

        var first = await store.PersistRoundEndAsync(envelope, foldsA);
        Assert.That(first.Status, Is.EqualTo(RoundEndPersistenceStatus.Committed)
            .Or.EqualTo(RoundEndPersistenceStatus.AlreadyCommitted));
        Assert.That(await store.HasUnconsumedContractStreakFoldsAsync(97), Is.True);
        var (preA, _) = await store.GetContractStreakAsync(userA);
        var (preB, _) = await store.GetContractStreakAsync(userB);
        Assert.That(preA, Is.EqualTo(0));
        Assert.That(preB, Is.EqualTo(0));

        Assert.ThrowsAsync<ContractStreakFoldConflictException>(async () =>
            await store.RecordContractStreakOutcomesAsync(foldsSubset, roundId: 97));

        Assert.That(await store.HasUnconsumedContractStreakFoldsAsync(97), Is.True,
            "subset fold must not consume journal A");
        var (midA, _) = await store.GetContractStreakAsync(userA);
        var (midB, _) = await store.GetContractStreakAsync(userB);
        Assert.That(midA, Is.EqualTo(0), "subset fold must not apply A");
        Assert.That(midB, Is.EqualTo(0), "subset fold must not leave B unrecoverable");

        var recovered = await store.RecoverPendingContractStreakFoldsAsync();
        Assert.That(recovered, Is.EqualTo(1));
        var (streakA, _) = await store.GetContractStreakAsync(userA);
        var (streakB, _) = await store.GetContractStreakAsync(userB);
        Assert.That(streakA, Is.EqualTo(1), "recovery must apply full journal A for userA");
        Assert.That(streakB, Is.EqualTo(1), "recovery must apply full journal A for userB");
        Assert.That(await store.HasUnconsumedContractStreakFoldsAsync(97), Is.False);
    }

    [Test]
    public async Task ConflictingJournalReplayB_StagedEntry_GoesTerminalConflict_NoRetryLoop()
    {
        // ROUND-9 P1: after A is journaled (and spool-acked), exact-envelope replay with folds B
        // stages B then fails at journal reconciliation. B must go terminal Conflict — not Pending —
        // so recovery never infinite-retries the deterministic conflict.
        var store = new SeasonLedgerStore(_dbPath);
        var userA = Guid.NewGuid();
        var foldsA = new[]
        {
            new ContractStreakFoldRequest(userA, CompletedThisRound: true),
        };
        var foldsB = new[]
        {
            new ContractStreakFoldRequest(userA, CompletedThisRound: false),
        };

        var envelope = new RoundEndEnvelope(
            roundId: 98,
            gamemode: "Secret",
            players: new[]
            {
                new RoundEndPlayerRecord(userA, new RoundContribution(
                    WasCaptainClean: false, AntagWin: false, EarlyDeath: false,
                    RoundId: 98, Gamemode: "Secret", Standing: 0,
                    ContractsCompleted: 1, ContractScore: 2, HrPointsEarned: 0)),
            },
            contracts: new[]
            {
                new ContractLogRecord(98, userA, "SolContractColaAudit", "personal"),
            });

        var first = await store.PersistRoundEndAsync(envelope, foldsA);
        Assert.That(first.Status, Is.EqualTo(RoundEndPersistenceStatus.Committed)
            .Or.EqualTo(RoundEndPersistenceStatus.AlreadyCommitted));
        Assert.That(await store.HasUnconsumedContractStreakFoldsAsync(98), Is.True);

        Assert.ThrowsAsync<ContractStreakFoldConflictException>(async () =>
            await store.PersistRoundEndAsync(envelope, foldsB));

        var snap = store.GetRecoverySnapshot();
        Assert.That(snap, Has.Count.EqualTo(1), "conflicting B must leave a durable spool entry");
        Assert.That(snap.Single().State, Is.EqualTo(LedgerRecoveryState.Conflict),
            "B must be terminal Conflict, not Pending");
        Assert.That(snap.Single().ErrorCategory, Is.EqualTo(LedgerRecoveryErrorCategory.ReplayConflict));

        // Recovery sweep excludes Conflict entries — no retry loop.
        var sweep = await store.RecoverPendingRoundEndsAsync();
        Assert.That(sweep.Examined, Is.EqualTo(0), "Conflict entries must not be re-examined");
        Assert.That(store.GetRecoverySnapshot().Single().State, Is.EqualTo(LedgerRecoveryState.Conflict));

        // Original journal A still recoverable for the fold path.
        Assert.That(await store.HasUnconsumedContractStreakFoldsAsync(98), Is.True);
        var recovered = await store.RecoverPendingContractStreakFoldsAsync();
        Assert.That(recovered, Is.EqualTo(1));
        var (streak, _) = await store.GetContractStreakAsync(userA);
        Assert.That(streak, Is.EqualTo(1), "original A journal must still fold after B went terminal");
        Assert.That(await store.HasUnconsumedContractStreakFoldsAsync(98), Is.False);
    }

    [Test]
    public async Task JournalsRemainUnconsumed_WhenFoldRecoverySkipped_ThenApplyOnRecovery()
    {
        // ROUND-9 P1 (CVar-off end-to-end store contract): when the system skips fold recovery
        // (CVar off), journals stay durable + unconsumed and no fold occurs; when recovery is
        // later invoked (CVar re-enabled), the same journals apply exactly once.
        var store = new SeasonLedgerStore(_dbPath);
        var userA = Guid.NewGuid();
        var folds = new[]
        {
            new ContractStreakFoldRequest(userA, CompletedThisRound: true),
        };

        var envelope = new RoundEndEnvelope(
            roundId: 99,
            gamemode: "Secret",
            players: new[]
            {
                new RoundEndPlayerRecord(userA, new RoundContribution(
                    WasCaptainClean: false, AntagWin: false, EarlyDeath: false,
                    RoundId: 99, Gamemode: "Secret", Standing: 0,
                    ContractsCompleted: 1, ContractScore: 2, HrPointsEarned: 0)),
            },
            contracts: new[]
            {
                new ContractLogRecord(99, userA, "SolContractColaAudit", "personal"),
            });

        var first = await store.PersistRoundEndAsync(envelope, folds);
        Assert.That(first.Status, Is.EqualTo(RoundEndPersistenceStatus.Committed)
            .Or.EqualTo(RoundEndPersistenceStatus.AlreadyCommitted));
        Assert.That(await store.HasUnconsumedContractStreakFoldsAsync(99), Is.True);

        // Simulate CVar-off: system skips RecoverPendingContractStreakFoldsAsync and post-persist fold.
        var (preStreak, _) = await store.GetContractStreakAsync(userA);
        Assert.That(preStreak, Is.EqualTo(0), "no fold while recovery/fold skipped");
        Assert.That(await store.HasUnconsumedContractStreakFoldsAsync(99), Is.True,
            "journals must remain unconsumed when fold recovery is skipped");

        // Simulate CVar re-enable: recovery applies the durable journals.
        var recovered = await store.RecoverPendingContractStreakFoldsAsync();
        Assert.That(recovered, Is.EqualTo(1));
        var (streak, _) = await store.GetContractStreakAsync(userA);
        Assert.That(streak, Is.EqualTo(1));
        Assert.That(await store.HasUnconsumedContractStreakFoldsAsync(99), Is.False);
        Assert.That(await store.RecoverPendingContractStreakFoldsAsync(), Is.EqualTo(0),
            "second recovery must be a no-op");
    }

    [Test]
    public void OnRoundEnd_SourceGuardGatesPostPersistFoldOnCVar()
    {
        // ROUND-9: the post-persist fold must re-check the quest-board CVar (do not consume when off).
        var root = FindRepositoryRoot();
        var systemSource = File.ReadAllText(Path.Combine(
            root,
            "Content.Server/_Solreign/SeasonLedger/SeasonLedgerSystem.cs"));
        var start = systemSource.IndexOf(
            "or RoundEndPersistenceStatus.AlreadyCommitted",
            StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), "the Committed/AlreadyCommitted fold gate must exist");
        var handler = systemSource[start..Math.Min(systemSource.Length, start + 1200)];

        Assert.That(handler, Does.Contain("_contractsQuestBoardEnabled"),
            "OnRoundEnd must re-check the contracts_lowpop CVar before the post-persist fold");
    }

    [Test]
    public void RecoverAndSummarize_SourceGuardGatesStreakFoldRecoveryOnCVar()
    {
        // ROUND-9: recovery must not apply streak journals while contracts_lowpop is off —
        // journals stay durable for re-enable.
        var root = FindRepositoryRoot();
        var systemSource = File.ReadAllText(Path.Combine(
            root,
            "Content.Server/_Solreign/SeasonLedger/SeasonLedgerSystem.cs"));
        var start = systemSource.IndexOf(
            "private async Task<LedgerRecoverySweepResult> RecoverAndSummarizeAsync",
            StringComparison.Ordinal);
        var end = systemSource.IndexOf("/// <summary>", start, StringComparison.Ordinal);
        var worker = systemSource[start..end];

        Assert.Multiple(() =>
        {
            Assert.That(worker, Does.Contain("RecoverPendingContractStreakFoldsAsync"));
            Assert.That(worker, Does.Contain("_contractsQuestBoardEnabled"),
                "RecoverAndSummarizeAsync must gate streak-fold recovery on the quest-board CVar");
            var gate = worker.IndexOf("if (_contractsQuestBoardEnabled)", StringComparison.Ordinal);
            var recover = worker.IndexOf("RecoverPendingContractStreakFoldsAsync", StringComparison.Ordinal);
            Assert.That(gate, Is.GreaterThanOrEqualTo(0));
            Assert.That(recover, Is.GreaterThan(gate),
                "RecoverPendingContractStreakFoldsAsync must run inside the CVar gate");
        });
    }

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
}
