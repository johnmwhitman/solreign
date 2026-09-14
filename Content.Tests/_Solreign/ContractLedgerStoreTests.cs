using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Content.Server._Solreign.SeasonLedger;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Season Ledger persistence for Solreign Contracts (spec §4): the two additive player_stats columns
///     and the contract_log audit table. Companion to <see cref="SeasonLedgerStoreTests"/> — same temp-DB
///     idiom. Covers the SQLite half of the M1 exit test: "see contracts_completed=1 in SQLite after
///     round end."
/// </summary>
[TestFixture]
[TestOf(typeof(SeasonLedgerStore))]
public sealed class ContractLedgerStoreTests
{
    private string _dbPath = default!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"solreign_contracts_test_{Guid.NewGuid():N}.db");
    }

    [TearDown]
    public void TearDown()
    {
        // Best-effort cleanup of the temp DB + WAL/SHM sidecars.
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

    // --- player_stats columns (spec §4.1) ---

    [Test]
    public async Task NewPlayer_HasZeroContractStats()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var stats = await store.GetStatsAsync(Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(stats.ContractsCompleted, Is.EqualTo(0));
            Assert.That(stats.ContractScore, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ExitTest_OneCompletedContract_FoldsIntoSqliteAtRoundEnd()
    {
        // The M1 exit test's persistence half: a round with one personal completion (score 2) lands as
        // contracts_completed=1 in player_stats.
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.AddRoundRecordAsync(user, new RoundContribution(
            WasCaptainClean: false, AntagWin: false, EarlyDeath: false,
            RoundId: 1, Gamemode: "Secret",
            Standing: 3, ContractsCompleted: 1, ContractScore: 2));

        var stats = await store.GetStatsAsync(user);

        Assert.Multiple(() =>
        {
            Assert.That(stats.ContractsCompleted, Is.EqualTo(1), "contracts_completed=1 in SQLite after round end");
            Assert.That(stats.ContractScore, Is.EqualTo(2));
            Assert.That(stats.StandingTotal, Is.EqualTo(3), "the +3 Standing rides the existing pipeline");
        });
    }

    [Test]
    public async Task ContractStats_AccumulateAcrossRounds()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.AddRoundRecordAsync(user, new RoundContribution(false, false, false,
            RoundId: 1, ContractsCompleted: 2, ContractScore: 4));
        await store.AddRoundRecordAsync(user, new RoundContribution(false, false, false,
            RoundId: 2, ContractsCompleted: 1, ContractScore: 3));

        var stats = await store.GetStatsAsync(user);

        Assert.Multiple(() =>
        {
            Assert.That(stats.ContractsCompleted, Is.EqualTo(3));
            Assert.That(stats.ContractScore, Is.EqualTo(7));
        });
    }

    [Test]
    public async Task ContractStats_SurviveStoreReopen()
    {
        var user = Guid.NewGuid();

        var store1 = new SeasonLedgerStore(_dbPath);
        await store1.AddRoundRecordAsync(user, new RoundContribution(false, false, false,
            RoundId: 1, ContractsCompleted: 1, ContractScore: 2));

        var store2 = new SeasonLedgerStore(_dbPath);
        var stats = await store2.GetStatsAsync(user);

        Assert.That(stats.ContractsCompleted, Is.EqualTo(1));
        Assert.That(stats.ContractScore, Is.EqualTo(2));
    }

    [Test]
    public async Task CareerContractStats_SumAcrossSeasons()
    {
        // Rank reads career (all-season) totals; contract stats must survive a season bump the same way
        // standing does (never-demote across seasons).
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.AddRoundRecordAsync(user, new RoundContribution(false, false, false,
            RoundId: 1, ContractsCompleted: 2, ContractScore: 4));
        await store.BumpSeasonAsync();
        await store.AddRoundRecordAsync(user, new RoundContribution(false, false, false,
            RoundId: 2, ContractsCompleted: 1, ContractScore: 3));

        var season = await store.GetStatsAsync(user);
        var career = await store.GetCareerStatsAsync(user);

        Assert.Multiple(() =>
        {
            Assert.That(season.ContractsCompleted, Is.EqualTo(1), "current season only");
            Assert.That(career.ContractsCompleted, Is.EqualTo(3), "career never resets");
            Assert.That(career.ContractScore, Is.EqualTo(7));
        });
    }

    [Test]
    public async Task PreContractDatabase_MigratesAdditively()
    {
        // A DB created and written before the contract columns existed (simulated by a plain write with
        // the defaulted fields) must read back cleanly with zeroed contract stats — the EnsureColumnAsync
        // additive-migration contract.
        var user = Guid.NewGuid();

        var store1 = new SeasonLedgerStore(_dbPath);
        await store1.AddRoundRecordAsync(user, new RoundContribution(true, false, false, RoundId: 1));

        var store2 = new SeasonLedgerStore(_dbPath);
        var stats = await store2.GetStatsAsync(user);

        Assert.Multiple(() =>
        {
            Assert.That(stats.Tours, Is.EqualTo(1), "pre-existing stats intact");
            Assert.That(stats.ContractsCompleted, Is.EqualTo(0));
            Assert.That(stats.ContractScore, Is.EqualTo(0));
        });
    }

    // --- contract_log audit table (spec §4.2) ---

    [Test]
    public async Task ContractLog_AppendsOneRowPerCompletion()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();

        await store.AddContractLogAsync(new List<ContractLogRecord>
        {
            new(RoundId: 7, User: alice, ContractId: "SolContractColaAudit", Scope: "personal"),
            new(RoundId: 7, User: alice, ContractId: "SolRaidScrapReclamation", Scope: "salvage-raid"),
            new(RoundId: 7, User: bob, ContractId: "SolRaidScrapReclamation", Scope: "salvage-raid"),
        });

        Assert.That(await store.GetContractLogCountAsync(alice), Is.EqualTo(2));
        Assert.That(await store.GetContractLogCountAsync(bob), Is.EqualTo(1));
    }

    [Test]
    public async Task RoundEndReplay_IsIdempotentAndPreservesRepeatedIdenticalCompletions()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();
        var contribution = new RoundContribution(false, false, false,
            RoundId: 81, ContractsCompleted: 2, ContractScore: 4);
        var contractLog = new List<ContractLogRecord>
        {
            new(RoundId: 81, User: user, ContractId: "SolContractPenRecovery", Scope: "personal"),
            new(RoundId: 81, User: user, ContractId: "SolContractPenRecovery", Scope: "personal"),
        };

        await store.AddRoundRecordAsync(user, contribution);
        await store.AddContractLogAsync(contractLog);
        await store.AddRoundRecordAsync(user, contribution);
        await store.AddContractLogAsync(contractLog);

        var stats = await store.GetStatsAsync(user);
        var contractRows = await store.GetContractLogCountAsync(user);
        Assert.Multiple(() =>
        {
            Assert.That(stats.Tours, Is.EqualTo(1));
            Assert.That(stats.ContractsCompleted, Is.EqualTo(2));
            Assert.That(stats.ContractScore, Is.EqualTo(4));
            Assert.That(contractRows, Is.EqualTo(2),
                "two real identical completions survive, but replaying the same batch adds none");
        });
    }

    [Test]
    public async Task ContractLog_NonPositiveRoundIdsRemainAdditive()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();
        var batch = new List<ContractLogRecord>
        {
            new(RoundId: 0, User: user, ContractId: "SolContractPenRecovery", Scope: "personal"),
        };

        await store.AddContractLogAsync(batch);
        await store.AddContractLogAsync(batch);

        Assert.That(await store.GetContractLogCountAsync(user), Is.EqualTo(2));
    }

    [Test]
    public async Task ContractLog_InvalidIdentity_RollsBackEarlierRowsInBatch()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();
        var valid = new ContractLogRecord(91, user, "SolContractPenRecovery", "personal");

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.AddContractLogAsync(new List<ContractLogRecord>
            {
                valid,
                new(91, user, " ", "personal"),
            }));
        Assert.That(await store.GetContractLogCountAsync(user), Is.Zero,
            "the valid row before an invalid contract ID must roll back");

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.AddContractLogAsync(new List<ContractLogRecord>
            {
                valid,
                new(91, user, "SolContractPenRecovery", "\t"),
            }));
        Assert.That(await store.GetContractLogCountAsync(user), Is.Zero,
            "the valid row before an invalid scope must roll back");
    }

    [Test]
    public async Task ContractLog_EmptyBatch_IsANoOp()
    {
        var store = new SeasonLedgerStore(_dbPath);
        await store.AddContractLogAsync(new List<ContractLogRecord>());

        Assert.That(await store.GetContractLogCountAsync(Guid.NewGuid()), Is.EqualTo(0));
    }

    [Test]
    public async Task ContractLog_SurvivesStoreReopen()
    {
        var user = Guid.NewGuid();

        var store1 = new SeasonLedgerStore(_dbPath);
        await store1.AddContractLogAsync(new List<ContractLogRecord>
        {
            new(RoundId: 1, User: user, ContractId: "SolContractPenRecovery", Scope: "personal"),
        });

        var store2 = new SeasonLedgerStore(_dbPath);
        Assert.That(await store2.GetContractLogCountAsync(user), Is.EqualTo(1));
    }

    [Test]
    public async Task ContractCountsForRounds_GroupsPerRoundAndOmitsRoundsWithoutCompletions()
    {
        // The Shift Archive board reads its rounds from station_audit_log and then asks this query
        // how many completions each logged — a round the log never saw must be absent, not zero.
        var store = new SeasonLedgerStore(_dbPath);
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await store.AddContractLogAsync(new List<ContractLogRecord>
        {
            new(RoundId: 10, User: userA, ContractId: "SolContractPenRecovery", Scope: "personal"),
            new(RoundId: 10, User: userB, ContractId: "SolContractColaAudit", Scope: "personal"),
            new(RoundId: 11, User: userA, ContractId: "SolContractColaAudit", Scope: "personal"),
        });

        var counts = await store.GetContractCountsForRoundsAsync(new[] { 10, 11, 12, 13 });
        Assert.Multiple(() =>
        {
            Assert.That(counts, Is.EquivalentTo(new Dictionary<int, int> { [10] = 2, [11] = 1 }));
            Assert.That(counts.ContainsKey(12), Is.False, "a round with no completions is absent, never fabricated as zero");
        });

        Assert.That(await store.GetContractCountsForRoundsAsync(Array.Empty<int>()), Is.Empty,
            "an empty round set short-circuits to an empty result without touching the database");
    }
}

[TestFixture]
[TestOf(typeof(SeasonLedgerSystem))]
public sealed class ContractLedgerSubmissionTests
{
    [TestCase("", "personal")]
    [TestCase("  ", "personal")]
    [TestCase("SolContractPenRecovery", "")]
    [TestCase("SolContractPenRecovery", "\t")]
    public void SubmitContractCompletion_BlankIdentity_IsRejectedBeforeAggregation(string contractId, string scope)
    {
        var system = new SeasonLedgerSystem();

        Assert.Throws<ArgumentException>(() =>
            system.SubmitContractCompletion(Guid.NewGuid(), contractId, scope, 2));
    }
}
