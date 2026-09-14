using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Content.Server._Solreign.SeasonLedger;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(SeasonLedgerStore))]
public sealed class SeasonLedgerStoreTests
{
    private string _dbPath = default!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"solreign_ledger_test_{Guid.NewGuid():N}.db");
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

    [Test]
    public async Task NewPlayer_ReturnsZeroStats()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var stats = await store.GetStatsAsync(Guid.NewGuid());
        Assert.That(stats, Is.EqualTo(new PlayerStats(0, 0, 0, 0)));
    }

    [Test]
    public async Task TwoRounds_AccumulateToursAndStats()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.AddRoundRecordAsync(user, new RoundContribution(WasCaptainClean: true, AntagWin: false, EarlyDeath: false, RoundId: 1, Gamemode: "Secret"));
        await store.AddRoundRecordAsync(user, new RoundContribution(WasCaptainClean: true, AntagWin: true, EarlyDeath: false, RoundId: 2, Gamemode: "Nukies"));

        var stats = await store.GetStatsAsync(user);

        Assert.Multiple(() =>
        {
            Assert.That(stats.Tours, Is.EqualTo(2), "both rounds count as tours");
            Assert.That(stats.CaptainClean, Is.EqualTo(2), "both captaincies accumulate");
            Assert.That(stats.AntagWins, Is.EqualTo(1));
            Assert.That(stats.EarlyDeaths, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ConcreteRoundRetry_IsIdempotentForPlayerAndSeason()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();
        var contribution = new RoundContribution(
            WasCaptainClean: true,
            AntagWin: true,
            EarlyDeath: false,
            RoundId: 42,
            Gamemode: "Secret",
            Standing: 7,
            ContractsCompleted: 2,
            ContractScore: 11,
            HrPointsEarned: 15);

        await store.AddRoundRecordAsync(user, contribution);
        await store.AddRoundRecordAsync(user, contribution);

        var stats = await store.GetStatsAsync(user);
        Assert.Multiple(() =>
        {
            Assert.That(stats.Tours, Is.EqualTo(1), "a retry must not fabricate a second tour");
            Assert.That(stats.CaptainClean, Is.EqualTo(1));
            Assert.That(stats.AntagWins, Is.EqualTo(1));
            Assert.That(stats.StandingTotal, Is.EqualTo(7));
            Assert.That(stats.ContractsCompleted, Is.EqualTo(2));
            Assert.That(stats.ContractScore, Is.EqualTo(11));
            Assert.That(stats.HrPoints, Is.EqualTo(15));
        });

        Assert.That(await CountRoundLogRowsAsync(), Is.EqualTo(1),
            "a retry must not append a duplicate audit row");

        var (payload, payloadHash) = await ReadRoundPayloadAsync(42, user);
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("round_id").GetInt32(), Is.EqualTo(42));
            Assert.That(root.GetProperty("gamemode").GetString(), Is.EqualTo("Secret"));
            Assert.That(root.GetProperty("contracts_completed").GetInt32(), Is.EqualTo(2));
            Assert.That(root.GetProperty("contract_score").GetInt32(), Is.EqualTo(11));
            Assert.That(root.GetProperty("hr_points_earned").GetInt32(), Is.EqualTo(15));
            Assert.That(payloadHash, Is.Not.Empty, "concrete round replays need a persisted identity hash");
        });
    }

    [Test]
    public async Task ConcreteRoundReplay_WithDifferentContribution_FailsWithoutMutation()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();
        var accepted = new RoundContribution(false, false, false,
            RoundId: 43, Gamemode: "Secret", ContractsCompleted: 1, ContractScore: 2, HrPointsEarned: 10);

        await store.AddRoundRecordAsync(user, accepted);
        var error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.AddRoundRecordAsync(user, accepted with { ContractScore = 99 }));

        var stats = await store.GetStatsAsync(user);
        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Does.Contain("conflicting replay"));
            Assert.That(stats.Tours, Is.EqualTo(1));
            Assert.That(stats.ContractsCompleted, Is.EqualTo(1));
            Assert.That(stats.ContractScore, Is.EqualTo(2));
            Assert.That(stats.HrPoints, Is.EqualTo(10));
        });
        Assert.That(await CountRoundLogRowsAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task ConcreteRoundId_IsScopedToPlayerAndSeason()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var contribution = new RoundContribution(true, false, false, RoundId: 77, Gamemode: "Secret");

        await store.AddRoundRecordAsync(alice, contribution);
        await store.AddRoundRecordAsync(bob, contribution);
        var bobFirstSeason = await store.GetStatsAsync(bob);

        await store.BumpSeasonAsync();
        await store.AddRoundRecordAsync(alice, contribution);

        var aliceSecondSeason = await store.GetStatsAsync(alice);
        var logRows = await CountRoundLogRowsAsync();
        Assert.Multiple(() =>
        {
            Assert.That(bobFirstSeason.Tours, Is.EqualTo(1), "another player may record the same round ID");
            Assert.That(aliceSecondSeason.Tours, Is.EqualTo(1), "a new season may record the same round ID");
            Assert.That(logRows, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task UnspecifiedRoundIds_PreserveExistingAccumulationSemantics()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.AddRoundRecordAsync(user, new RoundContribution(true, false, false));
        await store.AddRoundRecordAsync(user, new RoundContribution(true, false, false));

        var stats = await store.GetStatsAsync(user);
        var logRows = await CountRoundLogRowsAsync();
        Assert.Multiple(() =>
        {
            Assert.That(stats.Tours, Is.EqualTo(2));
            Assert.That(logRows, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task ExistingUnversionedDatabase_MigratesWithoutLosingStats()
    {
        var user = Guid.NewGuid();
        await CreateLegacyDatabaseAsync(user);

        var store = new SeasonLedgerStore(_dbPath);
        var stats = await store.GetStatsAsync(user);
        var schemaVersion = await ReadSchemaVersionAsync();
        var legacyRound = await ReadLegacyRoundAsync();
        var legacyContract = await ReadLegacyContractAsync();

        Assert.Multiple(() =>
        {
            Assert.That(stats.Tours, Is.EqualTo(3));
            Assert.That(stats.CaptainClean, Is.EqualTo(2));
            // Noticeboards (v14 wave-1 #2) bumped SeasonLedgerStore.CurrentSchemaVersion 4 -> 5;
            // Station-Library (wave-2) bumped 5 -> 6. Assertion stays version-number-agnostic.
            Assert.That(schemaVersion, Is.EqualTo(SeasonLedgerStore.CurrentSchemaVersion));
            Assert.That(legacyRound.RoundId, Is.EqualTo(17));
            Assert.That(legacyRound.Gamemode, Is.EqualTo("Secret"));
            Assert.That(legacyRound.Payload, Is.EqualTo("{\"legacy\":true}"));
            Assert.That(legacyRound.SeasonId, Is.Null);
            Assert.That(legacyRound.UserId, Is.Null);
            Assert.That(legacyContract.RoundId, Is.EqualTo(17));
            Assert.That(legacyContract.SeasonId, Is.EqualTo("S1"));
            Assert.That(legacyContract.UserId, Is.EqualTo(user.ToString()));
            Assert.That(legacyContract.ContractId, Is.EqualTo("LegacyContract"));
            Assert.That(legacyContract.Scope, Is.EqualTo("personal"));
            Assert.That(legacyContract.CompletedUtc, Is.EqualTo("2026-01-01T00:01:00Z"));
            Assert.That(legacyContract.Occurrence, Is.Null);
        });
    }

    [Test]
    public async Task NewerSchemaVersion_FailsClosedWithoutMutatingDatabase()
    {
        await using (var conn = await OpenTestConnectionAsync())
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA user_version = 99;";
            await cmd.ExecuteNonQueryAsync();
        }

        var store = new SeasonLedgerStore(_dbPath);
        var error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.GetStatsAsync(Guid.NewGuid()));
        var schemaVersion = await ReadSchemaVersionAsync();
        var tableCount = await CountTablesAsync();
        var journalMode = await ReadJournalModeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Does.Contain("newer"));
            Assert.That(schemaVersion, Is.EqualTo(99), "an old binary must not downgrade the database");
            Assert.That(tableCount, Is.Zero, "rejection must happen before any schema mutation");
            Assert.That(journalMode, Is.EqualTo("delete"), "rejection must not rewrite database pragmas");
        });
    }

    [Test]
    public async Task RepeatedSchemaFailureAllowsSameStoreToRecoverAfterDatabaseIsCorrected()
    {
        await using (var conn = await OpenTestConnectionAsync())
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA user_version = 99;";
            await cmd.ExecuteNonQueryAsync();
        }

        var store = new SeasonLedgerStore(_dbPath);
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.GetStatsAsync(Guid.NewGuid()));
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.GetStatsAsync(Guid.NewGuid()));

        await using (var conn = await OpenTestConnectionAsync())
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA user_version = 0;";
            await cmd.ExecuteNonQueryAsync();
        }

        var stats = await store.GetStatsAsync(Guid.NewGuid());
        var schemaVersion = await ReadSchemaVersionAsync();
        Assert.Multiple(() =>
        {
            Assert.That(stats.Tours, Is.Zero);
            // Noticeboards (v14 wave-1 #2) bumped SeasonLedgerStore.CurrentSchemaVersion 4 -> 5;
            // Station-Library (wave-2) bumped 5 -> 6. Assertion stays version-number-agnostic.
            Assert.That(schemaVersion, Is.EqualTo(SeasonLedgerStore.CurrentSchemaVersion));
        });
    }

    [Test]
    public async Task SchemaInitialization_WaitsForConcurrentWriterThenRejectsNewerVersion()
    {
        await using var writer = await OpenTestConnectionAsync();
        await using (var wal = writer.CreateCommand())
        {
            wal.CommandText = "PRAGMA journal_mode=WAL;";
            await wal.ExecuteNonQueryAsync();
        }

        await using var writerTx = writer.BeginTransaction(deferred: false);
        await using (var version = writer.CreateCommand())
        {
            version.Transaction = writerTx;
            version.CommandText = "PRAGMA user_version = 99;";
            await version.ExecuteNonQueryAsync();
        }

        var store = new SeasonLedgerStore(_dbPath);
        var openTask = Task.Run(() => store.GetStatsAsync(Guid.NewGuid()));
        await Task.Delay(100);
        Assert.That(openTask.IsCompleted, Is.False,
            "schema initialization should wait for the migration writer lock");

        await writerTx.CommitAsync();

        var error = Assert.ThrowsAsync<InvalidOperationException>(async () => await openTask);
        Assert.That(error!.Message, Does.Contain("newer"));
        Assert.That(await ReadSchemaVersionAsync(), Is.EqualTo(99));
    }

    [Test]
    public async Task FailedMigration_RollsBackAllSchemaChanges()
    {
        await CreateIncompatibleLegacyDatabaseAsync();
        var store = new SeasonLedgerStore(_dbPath);

        Assert.ThrowsAsync<SqliteException>(async () =>
            await store.GetStatsAsync(Guid.NewGuid()));

        var schemaVersion = await ReadSchemaVersionAsync();
        var occurrenceColumnExists = await ColumnExistsAsync("contract_log", "occurrence");
        var metaTableExists = await TableExistsAsync("meta");
        var roundEnvelopesTableExists = await TableExistsAsync("round_envelopes");
        var outboxTableExists = await TableExistsAsync("ledger_outbox");

        Assert.Multiple(() =>
        {
            Assert.That(schemaVersion, Is.EqualTo(1));
            Assert.That(occurrenceColumnExists, Is.False,
                "the column added before index creation must roll back with the failed migration");
            Assert.That(metaTableExists, Is.False);
            Assert.That(roundEnvelopesTableExists, Is.False);
            Assert.That(outboxTableExists, Is.False);
        });
    }

    [Test]
    public async Task StatsAreIsolatedPerPlayer()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();

        await store.AddRoundRecordAsync(alice, new RoundContribution(true, false, false));
        await store.AddRoundRecordAsync(alice, new RoundContribution(true, false, false));
        await store.AddRoundRecordAsync(bob, new RoundContribution(false, false, true));

        var aliceStats = await store.GetStatsAsync(alice);
        var bobStats = await store.GetStatsAsync(bob);

        Assert.Multiple(() =>
        {
            Assert.That(aliceStats.Tours, Is.EqualTo(2));
            Assert.That(aliceStats.CaptainClean, Is.EqualTo(2));
            Assert.That(bobStats.Tours, Is.EqualTo(1));
            Assert.That(bobStats.EarlyDeaths, Is.EqualTo(1));
            Assert.That(bobStats.CaptainClean, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task PersistsAcrossStoreReopen()
    {
        var user = Guid.NewGuid();

        var store1 = new SeasonLedgerStore(_dbPath);
        await store1.AddRoundRecordAsync(user, new RoundContribution(true, false, false));

        // Re-open a fresh store against the same file — data must survive (this is the whole point).
        var store2 = new SeasonLedgerStore(_dbPath);
        var stats = await store2.GetStatsAsync(user);

        Assert.That(stats.Tours, Is.EqualTo(1));
        Assert.That(stats.CaptainClean, Is.EqualTo(1));
    }

    [Test]
    public async Task BumpSeason_ResetsAccumulatedStats()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.AddRoundRecordAsync(user, new RoundContribution(true, false, false));
        Assert.That((await store.GetStatsAsync(user)).Tours, Is.EqualTo(1));

        var next = await store.BumpSeasonAsync();
        Assert.That(next, Is.EqualTo("S2"));

        // New season → totals start fresh.
        var fresh = await store.GetStatsAsync(user);
        Assert.That(fresh, Is.EqualTo(new PlayerStats(0, 0, 0, 0)));

        // Accumulating again is scoped to the new season.
        await store.AddRoundRecordAsync(user, new RoundContribution(false, true, false));
        var afterBump = await store.GetStatsAsync(user);
        Assert.That(afterBump.Tours, Is.EqualTo(1));
        Assert.That(afterBump.AntagWins, Is.EqualTo(1));
    }

    [Test]
    public async Task DefaultSeason_IsS1()
    {
        var store = new SeasonLedgerStore(_dbPath);
        Assert.That(await store.GetCurrentSeasonAsync(), Is.EqualTo("S1"));
    }

    // --- Corporate Standing persistence: the "close the loop" path (P2.2). ---

    [Test]
    public async Task SubmittedRoundStanding_FlowsIntoStats()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        // A single round whose handed-over Corporate Standing was 7.
        await store.AddRoundRecordAsync(user, new RoundContribution(false, false, false, RoundId: 1, Standing: 7));

        var stats = await store.GetStatsAsync(user);
        Assert.That(stats.StandingTotal, Is.EqualTo(7), "the round's standing lands in the season total");
        Assert.That(stats.Tours, Is.EqualTo(1));
    }

    [Test]
    public async Task StandingTotal_AccumulatesAcrossRounds()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.AddRoundRecordAsync(user, new RoundContribution(false, false, false, RoundId: 1, Standing: 3));
        await store.AddRoundRecordAsync(user, new RoundContribution(false, false, false, RoundId: 2, Standing: 4));
        await store.AddRoundRecordAsync(user, new RoundContribution(false, false, false, RoundId: 3, Standing: 0));

        var stats = await store.GetStatsAsync(user);
        Assert.That(stats.StandingTotal, Is.EqualTo(7), "standing sums across rounds; a zero-standing round adds nothing");
        Assert.That(stats.Tours, Is.EqualTo(3));
    }

    [Test]
    public async Task StandingTotal_SurvivesStoreReopen()
    {
        var user = Guid.NewGuid();

        var store1 = new SeasonLedgerStore(_dbPath);
        await store1.AddRoundRecordAsync(user, new RoundContribution(false, false, false, RoundId: 1, Standing: 11));

        // Re-open a fresh store against the same file — standing must persist like every other stat.
        var store2 = new SeasonLedgerStore(_dbPath);
        var stats = await store2.GetStatsAsync(user);
        Assert.That(stats.StandingTotal, Is.EqualTo(11));
    }

    [Test]
    public async Task CareerStanding_SumsAcrossSeasons()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        // Season 1 standing.
        await store.AddRoundRecordAsync(user, new RoundContribution(false, false, false, RoundId: 1, Standing: 6));

        await store.BumpSeasonAsync();

        // Season 2 standing — season-scoped stats reset, but career sums both.
        await store.AddRoundRecordAsync(user, new RoundContribution(false, false, false, RoundId: 2, Standing: 9));

        var season = await store.GetStatsAsync(user);
        Assert.That(season.StandingTotal, Is.EqualTo(9), "season stats are scoped to the current season");

        var career = await store.GetCareerStatsAsync(user);
        Assert.That(career.StandingTotal, Is.EqualTo(15), "career standing sums every season (6 + 9)");
    }

    // --- Title ceremonies: the announced-title grant record (anti-repeat guard). ---

    [Test]
    public async Task AnnouncedTitle_DefaultsToNull()
    {
        var store = new SeasonLedgerStore(_dbPath);
        Assert.That(await store.GetAnnouncedTitleAsync(Guid.NewGuid()), Is.Null,
            "an account with no ceremony on record has no announced title");
    }

    [Test]
    public async Task AnnouncedTitle_RoundTripsAndUpserts()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.SetAnnouncedTitleAsync(user, "Amortized Asset");
        Assert.That(await store.GetAnnouncedTitleAsync(user), Is.EqualTo("Amortized Asset"));

        // A later grant replaces the record (upsert on user/season), it doesn't duplicate it.
        await store.SetAnnouncedTitleAsync(user, "Brand Ambassador");
        Assert.That(await store.GetAnnouncedTitleAsync(user), Is.EqualTo("Brand Ambassador"));
    }

    [Test]
    public async Task AnnouncedTitle_SurvivesStoreReopen()
    {
        var user = Guid.NewGuid();

        var store1 = new SeasonLedgerStore(_dbPath);
        await store1.SetAnnouncedTitleAsync(user, "Restructuring Specialist");

        // Server restart (fresh store, same file) must NOT re-fire old ceremonies.
        var store2 = new SeasonLedgerStore(_dbPath);
        Assert.That(await store2.GetAnnouncedTitleAsync(user), Is.EqualTo("Restructuring Specialist"));
    }

    [Test]
    public async Task AnnouncedTitle_ResetsOnSeasonBump()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.SetAnnouncedTitleAsync(user, "Brand Ambassador");
        await store.BumpSeasonAsync();

        // New season, clean slate: the first earned title of S2 gets its ceremony again.
        Assert.That(await store.GetAnnouncedTitleAsync(user), Is.Null);
    }

    // --- HR Points system (Beta Feedback 01, Lane B — the ALWAYS-CUMULATIVE model). ---

    [Test]
    public async Task RoundContribution_HrPointsEarned_FlowsIntoStats()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.AddRoundRecordAsync(user, new RoundContribution(false, false, false, RoundId: 1, HrPointsEarned: 10));

        var stats = await store.GetStatsAsync(user);
        Assert.That(stats.HrPoints, Is.EqualTo(10), "the round's HR Points payout lands in the season total");
        Assert.That(stats.Tours, Is.EqualTo(1));
    }

    [Test]
    public async Task HrPoints_AccumulateAcrossRounds_NeverReset()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.AddRoundRecordAsync(user, new RoundContribution(false, false, false, RoundId: 1, HrPointsEarned: 10));
        await store.AddRoundRecordAsync(user, new RoundContribution(false, false, false, RoundId: 2, HrPointsEarned: 15));
        await store.AddRoundRecordAsync(user, new RoundContribution(false, false, false, RoundId: 3, HrPointsEarned: 0));

        var stats = await store.GetStatsAsync(user);
        Assert.That(stats.HrPoints, Is.EqualTo(25), "HR Points sum across rounds; a zero-payout round adds nothing");
    }

    [Test]
    public async Task AwardHrPoints_CreatesARow_WhenAccountHasNone()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        // No round ever recorded for this account — the title-earned bonus is the first write it ever gets.
        await store.AwardHrPointsAsync(user, 25);

        var stats = await store.GetStatsAsync(user);
        Assert.Multiple(() =>
        {
            Assert.That(stats.HrPoints, Is.EqualTo(25));
            Assert.That(stats.Tours, Is.EqualTo(0), "awarding points alone must not fabricate a tour");
        });
    }

    [Test]
    public async Task AwardHrPoints_AddsOnTopOfExistingPoints()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.AddRoundRecordAsync(user, new RoundContribution(false, false, false, RoundId: 1, HrPointsEarned: 10));
        await store.AwardHrPointsAsync(user, 25); // title-earned bonus

        var stats = await store.GetStatsAsync(user);
        Assert.That(stats.HrPoints, Is.EqualTo(35));
    }

    [Test]
    public async Task AwardHrPoints_NonPositiveAward_IsANoOp()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.AwardHrPointsAsync(user, 0);
        await store.AwardHrPointsAsync(user, -5);

        var stats = await store.GetStatsAsync(user);
        Assert.That(stats.HrPoints, Is.EqualTo(0));
    }

    [Test]
    public async Task HrPoints_SurviveStoreReopen()
    {
        var user = Guid.NewGuid();

        var store1 = new SeasonLedgerStore(_dbPath);
        await store1.AddRoundRecordAsync(user, new RoundContribution(false, false, false, RoundId: 1, HrPointsEarned: 40));

        var store2 = new SeasonLedgerStore(_dbPath);
        var stats = await store2.GetStatsAsync(user);
        Assert.That(stats.HrPoints, Is.EqualTo(40));
    }

    [Test]
    public async Task CareerHrPoints_SumAcrossSeasons_NeverResetByBump()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        // Season 1 points.
        await store.AddRoundRecordAsync(user, new RoundContribution(false, false, false, RoundId: 1, HrPointsEarned: 20));

        await store.BumpSeasonAsync();

        // Season 2 points — season-scoped stats reset like every other column, but the ALWAYS-CUMULATIVE
        // model reads career totals for display, which sum every season.
        await store.AddRoundRecordAsync(user, new RoundContribution(false, false, false, RoundId: 2, HrPointsEarned: 30));

        var season = await store.GetStatsAsync(user);
        Assert.That(season.HrPoints, Is.EqualTo(30), "season stats are scoped to the current season");

        var career = await store.GetCareerStatsAsync(user);
        Assert.That(career.HrPoints, Is.EqualTo(50), "career HR Points sum every season (20 + 30)");
    }

    // --- Wingmates persistent block ledger (P1 cross-round guide/requester blocks). ---

    [Test]
    public async Task WingmateBlock_DefaultsToEmpty()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var blocks = await store.GetWingmateBlocksAsync();
        Assert.That(blocks, Is.Empty);
    }

    [Test]
    public async Task WingmateBlock_PersistsAcrossStoreReopen()
    {
        var blocker = Guid.NewGuid();
        var blocked = Guid.NewGuid();

        var store1 = new SeasonLedgerStore(_dbPath);
        await store1.AddWingmateBlockAsync(blocker, blocked);

        // Re-open a fresh store against the same file — the block must survive a server restart, which
        // is the entire point of persisting it (round-local blocks alone reset every round).
        var store2 = new SeasonLedgerStore(_dbPath);
        var blocks = await store2.GetWingmateBlocksAsync();

        Assert.That(blocks, Is.EquivalentTo(new[] { (blocker, blocked) }));
    }

    [Test]
    public async Task WingmateBlock_IsIdempotent()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var blocker = Guid.NewGuid();
        var blocked = Guid.NewGuid();

        await store.AddWingmateBlockAsync(blocker, blocked);
        await store.AddWingmateBlockAsync(blocker, blocked);
        await store.AddWingmateBlockAsync(blocker, blocked);

        var blocks = await store.GetWingmateBlocksAsync();
        Assert.That(blocks, Is.EquivalentTo(new[] { (blocker, blocked) }),
            "repeated blocks of the same pair must not duplicate rows");
    }

    [Test]
    public async Task WingmateBlocks_AreNotRemovedBySeasonBump()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var blocker = Guid.NewGuid();
        var blocked = Guid.NewGuid();

        await store.AddWingmateBlockAsync(blocker, blocked);
        await store.BumpSeasonAsync();

        var blocks = await store.GetWingmateBlocksAsync();
        Assert.That(blocks, Is.EquivalentTo(new[] { (blocker, blocked) }),
            "Wingmates blocks are a safety control, not a season-scoped stat — a season bump must not silently unblock anyone");
    }

    private async Task<long> CountRoundLogRowsAsync()
    {
        await using var conn = await OpenTestConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM round_log;";
        return (long) (await cmd.ExecuteScalarAsync())!;
    }

    private async Task<(string Payload, string PayloadHash)> ReadRoundPayloadAsync(int roundId, Guid user)
    {
        await using var conn = await OpenTestConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT payload_json, payload_hash FROM round_log WHERE round_id = $rid AND user_id = $uid;";
        cmd.Parameters.AddWithValue("$rid", roundId);
        cmd.Parameters.AddWithValue("$uid", user.ToString());
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        return (reader.GetString(0), reader.GetString(1));
    }

    private async Task<long> ReadSchemaVersionAsync()
    {
        await using var conn = await OpenTestConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return (long) (await cmd.ExecuteScalarAsync())!;
    }

    private async Task<string> ReadJournalModeAsync()
    {
        await using var conn = await OpenTestConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode;";
        return (string) (await cmd.ExecuteScalarAsync())!;
    }

    private async Task<long> CountTablesAsync()
    {
        await using var conn = await OpenTestConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table';";
        return (long) (await cmd.ExecuteScalarAsync())!;
    }

    private async Task<bool> ColumnExistsAsync(string table, string column)
    {
        await using var conn = await OpenTestConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (reader.GetString(1) == column)
                return true;
        }

        return false;
    }

    private async Task<bool> TableExistsAsync(string table)
    {
        await using var conn = await OpenTestConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name;";
        cmd.Parameters.AddWithValue("$name", table);
        return await cmd.ExecuteScalarAsync() is not null;
    }

    private async Task<(long RoundId, string Gamemode, string Payload, string SeasonId, string UserId)>
        ReadLegacyRoundAsync()
    {
        await using var conn = await OpenTestConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT round_id, gamemode, payload_json, season_id, user_id
            FROM round_log
            WHERE round_id = 17;
            """;
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        return (
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    private async Task<(long RoundId, string SeasonId, string UserId, string ContractId, string Scope,
        string CompletedUtc, long? Occurrence)> ReadLegacyContractAsync()
    {
        await using var conn = await OpenTestConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT round_id, season_id, user_id, contract_id, scope, completed_utc, occurrence
            FROM contract_log
            WHERE contract_id = 'LegacyContract';
            """;
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        return (
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetInt64(6));
    }

    private async Task<SqliteConnection> OpenTestConnectionAsync()
    {
        SQLitePCL.Batteries_V2.Init();
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        return conn;
    }

    private async Task CreateLegacyDatabaseAsync(Guid user)
    {
        await using var conn = await OpenTestConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE player_stats (
                user_id TEXT NOT NULL,
                season_id TEXT NOT NULL,
                tours INTEGER NOT NULL DEFAULT 0,
                captain_clean INTEGER NOT NULL DEFAULT 0,
                antag_wins INTEGER NOT NULL DEFAULT 0,
                early_deaths INTEGER NOT NULL DEFAULT 0,
                standing_total INTEGER NOT NULL DEFAULT 0,
                contracts_completed INTEGER NOT NULL DEFAULT 0,
                contract_score INTEGER NOT NULL DEFAULT 0,
                hr_points INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (user_id, season_id)
            );
            CREATE TABLE round_log (
                round_id INTEGER,
                ended_utc TEXT NOT NULL,
                gamemode TEXT,
                payload_json TEXT
            );
            CREATE TABLE contract_log (
                round_id INTEGER,
                season_id TEXT NOT NULL,
                user_id TEXT NOT NULL,
                contract_id TEXT NOT NULL,
                scope TEXT NOT NULL,
                completed_utc TEXT NOT NULL
            );
            CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE title_grants (
                user_id TEXT NOT NULL,
                season_id TEXT NOT NULL,
                title TEXT NOT NULL,
                PRIMARY KEY (user_id, season_id)
            );
            INSERT INTO meta (key, value) VALUES ('current_season', 'S1');
            INSERT INTO player_stats
                (user_id, season_id, tours, captain_clean, antag_wins, early_deaths,
                 standing_total, contracts_completed, contract_score, hr_points)
            VALUES ($uid, 'S1', 3, 2, 1, 0, 9, 1, 4, 20);
            INSERT INTO round_log (round_id, ended_utc, gamemode, payload_json)
            VALUES (17, '2026-01-01T00:00:00Z', 'Secret', '{"legacy":true}');
            INSERT INTO contract_log
                (round_id, season_id, user_id, contract_id, scope, completed_utc)
            VALUES
                (17, 'S1', $uid, 'LegacyContract', 'personal', '2026-01-01T00:01:00Z');
            """;
        cmd.Parameters.AddWithValue("$uid", user.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task CreateIncompatibleLegacyDatabaseAsync()
    {
        await using var conn = await OpenTestConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE player_stats (
                user_id TEXT NOT NULL,
                season_id TEXT NOT NULL,
                tours INTEGER NOT NULL DEFAULT 0,
                captain_clean INTEGER NOT NULL DEFAULT 0,
                antag_wins INTEGER NOT NULL DEFAULT 0,
                early_deaths INTEGER NOT NULL DEFAULT 0,
                standing_total INTEGER NOT NULL DEFAULT 0,
                contracts_completed INTEGER NOT NULL DEFAULT 0,
                contract_score INTEGER NOT NULL DEFAULT 0,
                hr_points INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (user_id, season_id)
            );
            CREATE TABLE round_log (
                round_id INTEGER,
                season_id TEXT,
                user_id TEXT,
                ended_utc TEXT NOT NULL,
                gamemode TEXT,
                payload_json TEXT
            );
            CREATE TABLE contract_log (
                round_id INTEGER,
                season_id TEXT NOT NULL,
                user_id TEXT NOT NULL,
                contract_id TEXT NOT NULL,
                scope TEXT NOT NULL,
                completed_utc TEXT NOT NULL
            );
            INSERT INTO round_log (round_id, season_id, user_id, ended_utc)
            VALUES
                (5, 'S1', 'duplicate-user', '2026-01-01T00:00:00Z'),
                (5, 'S1', 'duplicate-user', '2026-01-01T00:00:01Z');
            PRAGMA user_version = 1;
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    [Test]
    public async Task WingmateBlock_ReturnTrueOnlyWhenTheRowIsDurablyConfirmedPresent()
    {
        // WingmateSystem treats the boolean return as the sole durability signal: a block only takes
        // effect once this returns true. An already-present row (idempotent re-block) must still
        // confirm true — it IS durably present — while a rejected self-block must not.
        var store = new SeasonLedgerStore(_dbPath);
        var blocker = Guid.NewGuid();
        var blocked = Guid.NewGuid();

        Assert.That(await store.AddWingmateBlockAsync(blocker, blocked), Is.True,
            "a freshly inserted row is confirmed durable");
        Assert.That(await store.AddWingmateBlockAsync(blocker, blocked), Is.True,
            "an already-existing row is still a confirmed, durable block — not a failure");
        Assert.That(await store.AddWingmateBlockAsync(blocker, blocker), Is.False,
            "a self-block is rejected and must not report success");

        var blocks = await store.GetWingmateBlocksAsync();
        Assert.That(blocks, Is.EquivalentTo(new[] { (blocker, blocked) }));
    }

    // --- Station Audit inspection layer migration (v14 gap-closure pass) ---------------------------

    [Test]
    public async Task StationAuditLog_MissingInspectionColumns_MigratesAdditivelyWithoutLosingExistingRows()
    {
        await CreateV6ShapedStationAuditDatabaseAsync();

        var criteriaColumnExistsBefore = await ColumnExistsAsync("station_audit_log", "criteria_json");
        Assert.That(criteriaColumnExistsBefore, Is.False, "pre-condition: the v6-shaped fixture must not already carry the new columns");

        var store = new SeasonLedgerStore(_dbPath);
        var rows = await store.GetRecentStationAuditsAsync();

        var hasCriteriaJson = await ColumnExistsAsync("station_audit_log", "criteria_json");
        var hasCheckpointKind = await ColumnExistsAsync("station_audit_log", "checkpoint_consequence_kind");
        var hasCheckpointFiredUtc = await ColumnExistsAsync("station_audit_log", "checkpoint_fired_utc");

        Assert.Multiple(() =>
        {
            Assert.That(hasCriteriaJson, Is.True);
            Assert.That(hasCheckpointKind, Is.True);
            Assert.That(hasCheckpointFiredUtc, Is.True);
        });

        var row = rows.Single(r => r.RoundId == 900);
        Assert.Multiple(() =>
        {
            // Existing pre-migration row's original columns survive untouched.
            Assert.That(row.CrewCount, Is.EqualTo(7));
            Assert.That(row.ItemOfConcernId, Is.EqualTo("unrepaired-breach"));
            // Additive columns backfill to the documented honest defaults for a row that predates
            // the inspection layer — never a fabricated value.
            Assert.That(row.CriteriaJson, Is.EqualTo("[]"));
            Assert.That(row.CheckpointConsequenceKind, Is.EqualTo("none"));
            Assert.That(row.CheckpointFiredUtc, Is.Null);
        });
    }

    private async Task CreateV6ShapedStationAuditDatabaseAsync()
    {
        await using var conn = await OpenTestConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE station_audit_log (
                round_id                  INTEGER PRIMARY KEY,
                season_id                 TEXT NOT NULL,
                ended_utc                 TEXT NOT NULL,
                shift_duration_minutes    INTEGER NOT NULL,
                crew_count                INTEGER NOT NULL,
                death_count               INTEGER NOT NULL,
                first_death_commemorated  INTEGER NOT NULL DEFAULT 0,
                directive_title           TEXT,
                directive_outcome_reported INTEGER NOT NULL DEFAULT 0,
                directive_outcome_fulfilled INTEGER NOT NULL DEFAULT 0,
                stipends_processed        INTEGER NOT NULL DEFAULT 0,
                bounty_verdicts           INTEGER NOT NULL DEFAULT 0,
                notable_event_count       INTEGER NOT NULL DEFAULT 0,
                commendation_name         TEXT,
                commendation_score        INTEGER NOT NULL DEFAULT 0,
                item_of_concern_id        TEXT NOT NULL
            );
            CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            INSERT INTO meta (key, value) VALUES ('current_season', 'S1');
            INSERT INTO station_audit_log
                (round_id, season_id, ended_utc, shift_duration_minutes, crew_count, death_count,
                 item_of_concern_id)
            VALUES
                (900, 'S1', '2026-01-01T00:00:00Z', 45, 7, 0, 'unrepaired-breach');
            PRAGMA user_version = 6;
            """;
        await cmd.ExecuteNonQueryAsync();
    }
}
