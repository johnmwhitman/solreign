using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._Solreign.SeasonLedger;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(RoundEndEnvelope))]
public sealed class RoundEndEnvelopeStoreTests
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private string _dbPath = default!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"solreign_round_envelope_{Guid.NewGuid():N}.db");
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try
            {
                File.Delete(_dbPath + suffix);
            }
            catch
            {
                // Best-effort cleanup in the temporary directory.
            }
        }
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Constructor_RequiresPositiveRoundId(int roundId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateEnvelope(roundId: roundId));
    }

    [TestCase(8, 7, "Secret", "Secret")]
    [TestCase(8, 8, "Secret", "Nukies")]
    public void Constructor_RequiresNestedRoundAndGamemodeAgreement(
        int roundId,
        int contributionRoundId,
        string gamemode,
        string contributionGamemode)
    {
        var players = new[]
        {
            new RoundEndPlayerRecord(Alice, Contribution(contributionRoundId, contributionGamemode)),
        };

        Assert.Throws<ArgumentException>(() => new RoundEndEnvelope(roundId, gamemode, players, Array.Empty<ContractLogRecord>()));
    }

    [Test]
    public void Constructor_RequiresUniqueNonemptyRosterGuids()
    {
        var duplicate = new[]
        {
            new RoundEndPlayerRecord(Alice, Contribution()),
            new RoundEndPlayerRecord(Alice, Contribution()),
        };
        var empty = new[]
        {
            new RoundEndPlayerRecord(Guid.Empty, Contribution()),
        };

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => new RoundEndEnvelope(7, "Secret", duplicate, Array.Empty<ContractLogRecord>()));
            Assert.Throws<ArgumentException>(() => new RoundEndEnvelope(7, "Secret", empty, Array.Empty<ContractLogRecord>()));
        });
    }

    [TestCase("", "personal")]
    [TestCase(" ", "personal")]
    [TestCase("SolContractPenRecovery", "")]
    [TestCase("SolContractPenRecovery", "\t")]
    public void Constructor_RejectsBlankContractIdentity(string contractId, string scope)
    {
        var contracts = new[] { new ContractLogRecord(7, Alice, contractId, scope) };

        Assert.Throws<ArgumentException>(() =>
            new RoundEndEnvelope(7, "Secret", Players(), contracts));
    }

    [Test]
    public void Constructor_RequiresContractsToBelongToRoundAndRoster()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => new RoundEndEnvelope(
                7,
                "Secret",
                Players(),
                new[] { new ContractLogRecord(8, Alice, "Contract", "personal") }));
            Assert.Throws<ArgumentException>(() => new RoundEndEnvelope(
                7,
                "Secret",
                Players(),
                new[] { new ContractLogRecord(7, Bob, "Contract", "personal") }));
        });
    }

    [Test]
    public void CaptureCopiesCallerCollections()
    {
        var players = Players().ToList();
        var contracts = new List<ContractLogRecord>
        {
            new(7, Alice, "ContractA", "personal"),
        };
        var envelope = new RoundEndEnvelope(7, "Secret", players, contracts);

        players[0] = new RoundEndPlayerRecord(Bob, Contribution());
        contracts[0] = new ContractLogRecord(7, Alice, "Changed", "department");
        players.Clear();
        contracts.Clear();
        var capture = envelope.Canonicalize();

        Assert.Multiple(() =>
        {
            Assert.That(capture.Players, Has.Count.EqualTo(1));
            Assert.That(capture.Players[0].User, Is.EqualTo(Alice));
            Assert.That(capture.Contracts, Has.Count.EqualTo(1));
            Assert.That(capture.Contracts[0].ContractId, Is.EqualTo("ContractA"));
            Assert.That(capture.CaptureHash, Has.Length.EqualTo(64));
            Assert.That(capture.CaptureHash, Is.EqualTo(capture.CaptureHash.ToUpperInvariant()));
        });
    }

    [Test]
    public void CaptureIsIndependentOfPlayerAndContractOrder()
    {
        var alice = new RoundEndPlayerRecord(Alice, Contribution(standing: 1));
        var bob = new RoundEndPlayerRecord(Bob, Contribution(standing: 2));
        var contractA = new ContractLogRecord(7, Alice, "ContractA", "personal");
        var contractB = new ContractLogRecord(7, Bob, "ContractB", "department");

        var first = new RoundEndEnvelope(7, "Secret", new[] { bob, alice }, new[] { contractB, contractA }).Canonicalize();
        var second = new RoundEndEnvelope(7, "Secret", new[] { alice, bob }, new[] { contractA, contractB }).Canonicalize();

        Assert.Multiple(() =>
        {
            Assert.That(first.CaptureHash, Is.EqualTo(second.CaptureHash));
            Assert.That(first.CanonicalJson, Is.EqualTo(second.CanonicalJson));
            Assert.That(first.Players.Select(player => player.User), Is.EqualTo(new[] { Alice, Bob }));
        });
    }

    [Test]
    public void CapturePreservesDuplicateContractOccurrences()
    {
        var duplicate = new ContractLogRecord(7, Alice, "ContractA", "personal");
        var capture = new RoundEndEnvelope(7, "Secret", Players(), new[] { duplicate, duplicate }).Canonicalize();

        Assert.Multiple(() =>
        {
            Assert.That(capture.Contracts, Has.Count.EqualTo(2));
            Assert.That(capture.Contracts.Select(contract => contract.Occurrence), Is.EqualTo(new[] { 0, 1 }));
        });
    }

    [Test]
    public void CaptureHash_ChangesForEveryIdentityDimension()
    {
        var baseline = CreateEnvelope().Canonicalize().CaptureHash;

        Assert.Multiple(() =>
        {
            Assert.That(CreateEnvelope(players: new[]
            {
                new RoundEndPlayerRecord(Bob, Contribution()),
            }).Canonicalize().CaptureHash, Is.Not.EqualTo(baseline), "roster");
            Assert.That(CreateEnvelope(players: new[]
            {
                new RoundEndPlayerRecord(Alice, Contribution(standing: 9)),
            }).Canonicalize().CaptureHash, Is.Not.EqualTo(baseline), "contribution");
            Assert.That(CreateEnvelope(gamemode: "Nukies", players: new[]
            {
                new RoundEndPlayerRecord(Alice, Contribution(gamemode: "Nukies")),
            }).Canonicalize().CaptureHash, Is.Not.EqualTo(baseline), "gamemode");
            Assert.That(CreateEnvelope(contracts: new[]
            {
                new ContractLogRecord(7, Alice, "ContractA", "personal"),
            }).Canonicalize().CaptureHash, Is.Not.EqualTo(baseline), "contract");
            foreach (var changed in new[]
                     {
                         Contribution() with { WasCaptainClean = true },
                         Contribution() with { AntagWin = true },
                         Contribution() with { EarlyDeath = true },
                         Contribution() with { ContractsCompleted = 1 },
                         Contribution() with { ContractScore = 1 },
                         Contribution() with { HrPointsEarned = 1 },
                     })
            {
                Assert.That(CreateEnvelope(players: new[] { new RoundEndPlayerRecord(Alice, changed) })
                    .Canonicalize().CaptureHash, Is.Not.EqualTo(baseline), changed.ToString());
            }
        });
    }

    [Test]
    public async Task PersistRoundEndOnce_CommitsWholeEnvelopeAndPrivateAggregateOutbox()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var captured = CreateEnvelope(
            players: new[]
            {
                new RoundEndPlayerRecord(Alice, Contribution(standing: 3) with
                {
                    ContractsCompleted = 2,
                    ContractScore = 4,
                    HrPointsEarned = 7,
                }),
                new RoundEndPlayerRecord(Bob, Contribution(standing: 5)),
            },
            contracts: new[]
            {
                new ContractLogRecord(7, Alice, "ContractA", "personal"),
                new ContractLogRecord(7, Alice, "ContractA", "personal"),
            }).Canonicalize();

        var result = await store.PersistRoundEndOnceAsync(captured, null, CancellationToken.None);

        var alice = await store.GetStatsAsync(Alice);
        var bob = await store.GetStatsAsync(Bob);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(RoundEnvelopePersistResult.Committed));
            Assert.That(alice, Is.EqualTo(new PlayerStats(1, 0, 0, 0, 3, 2, 4, 7)));
            Assert.That(bob, Is.EqualTo(new PlayerStats(1, 0, 0, 0, 5)));
            Assert.That(ReadCount("round_log"), Is.EqualTo(2));
            Assert.That(ReadCount("contract_log"), Is.EqualTo(2));
            Assert.That(ReadCount("round_envelopes"), Is.EqualTo(1));
            Assert.That(ReadCount("ledger_outbox"), Is.EqualTo(1));
            Assert.That(ReadScalar("SELECT privacy_class FROM ledger_outbox;"), Is.EqualTo("private-internal"));
            Assert.That(ReadScalar("SELECT exported_utc FROM ledger_outbox;"), Is.Null);
        });
    }

    [Test]
    public void PersistRoundEndOnce_FaultAfterFirstPlayerMutationRollsBackEveryTable()
    {
        var injector = new ThrowAfterFirstPlayerFaultInjector();
        var store = new SeasonLedgerStore(_dbPath, injector);
        var captured = CreateEnvelope(players: new[]
        {
            new RoundEndPlayerRecord(Alice, Contribution()),
            new RoundEndPlayerRecord(Bob, Contribution()),
        }).Canonicalize();

        Assert.ThrowsAsync<InjectedRoundEndFaultException>(async () =>
            await store.PersistRoundEndOnceAsync(captured, null, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(ReadCount("player_stats"), Is.Zero);
            Assert.That(ReadCount("round_log"), Is.Zero);
            Assert.That(ReadCount("contract_log"), Is.Zero);
            Assert.That(ReadCount("round_envelopes"), Is.Zero);
            Assert.That(ReadCount("ledger_outbox"), Is.Zero);
        });
    }

    [Test]
    public void PersistRoundEndOnce_FaultBeforeCommitRollsBackEveryTable()
    {
        var store = new SeasonLedgerStore(_dbPath, new ThrowBeforeCommitFaultInjector());

        Assert.ThrowsAsync<InjectedRoundEndFaultException>(async () =>
            await store.PersistRoundEndOnceAsync(CreateEnvelope().Canonicalize(), null, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(ReadCount("player_stats"), Is.Zero);
            Assert.That(ReadCount("round_log"), Is.Zero);
            Assert.That(ReadCount("contract_log"), Is.Zero);
            Assert.That(ReadCount("round_envelopes"), Is.Zero);
            Assert.That(ReadCount("ledger_outbox"), Is.Zero);
        });
    }

    [Test]
    public void PersistRoundEndOnce_CancellationBeforeCommitRollsBackEveryTable()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new SeasonLedgerStore(_dbPath, new CancelBeforeCommitFaultInjector(cancellation));

        Assert.That(async () =>
                await store.PersistRoundEndOnceAsync(CreateEnvelope().Canonicalize(), null, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());

        Assert.Multiple(() =>
        {
            Assert.That(ReadCount("player_stats"), Is.Zero);
            Assert.That(ReadCount("round_log"), Is.Zero);
            Assert.That(ReadCount("contract_log"), Is.Zero);
            Assert.That(ReadCount("round_envelopes"), Is.Zero);
            Assert.That(ReadCount("ledger_outbox"), Is.Zero);
        });
    }

    [Test]
    public async Task PersistRoundEndOnce_ReorderedExactReplayIsAlreadyCommitted()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var contractA = new ContractLogRecord(7, Alice, "ContractA", "personal");
        var contractB = new ContractLogRecord(7, Bob, "ContractB", "department");
        var first = new RoundEndEnvelope(7, "Secret", new[]
        {
            new RoundEndPlayerRecord(Alice, Contribution(standing: 1)),
            new RoundEndPlayerRecord(Bob, Contribution(standing: 2)),
        }, new[] { contractA, contractB }).Canonicalize();
        var replay = new RoundEndEnvelope(7, "Secret", new[]
        {
            new RoundEndPlayerRecord(Bob, Contribution(standing: 2)),
            new RoundEndPlayerRecord(Alice, Contribution(standing: 1)),
        }, new[] { contractB, contractA }).Canonicalize();

        Assert.That(await store.PersistRoundEndOnceAsync(first, null, CancellationToken.None),
            Is.EqualTo(RoundEnvelopePersistResult.Committed));
        Assert.That(await store.PersistRoundEndOnceAsync(replay, null, CancellationToken.None),
            Is.EqualTo(RoundEnvelopePersistResult.AlreadyCommitted));
        Assert.Multiple(() =>
        {
            Assert.That(ReadCount("round_log"), Is.EqualTo(2));
            Assert.That(ReadCount("contract_log"), Is.EqualTo(2));
            Assert.That(ReadCount("ledger_outbox"), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task PersistRoundEndOnce_ConflictDimensionsFailWithoutMutation()
    {
        var store = new SeasonLedgerStore(_dbPath);
        await store.PersistRoundEndOnceAsync(CreateEnvelope().Canonicalize(), null, CancellationToken.None);
        var conflicting = new[]
        {
            CreateEnvelope(players: new[] { new RoundEndPlayerRecord(Bob, Contribution()) }).Canonicalize(),
            CreateEnvelope(players: new[] { new RoundEndPlayerRecord(Alice, Contribution(standing: 99)) }).Canonicalize(),
            CreateEnvelope(gamemode: "Nukies", players: new[]
            {
                new RoundEndPlayerRecord(Alice, Contribution(gamemode: "Nukies")),
            }).Canonicalize(),
            CreateEnvelope(contracts: new[]
            {
                new ContractLogRecord(7, Alice, "ContractA", "personal"),
            }).Canonicalize(),
        };

        foreach (var conflict in conflicting)
        {
            Assert.ThrowsAsync<RoundEndReplayConflictException>(async () =>
                await store.PersistRoundEndOnceAsync(conflict, null, CancellationToken.None));
        }

        Assert.Multiple(() =>
        {
            Assert.That(ReadCount("player_stats"), Is.EqualTo(1));
            Assert.That(ReadCount("round_log"), Is.EqualTo(1));
            Assert.That(ReadCount("contract_log"), Is.Zero);
            Assert.That(ReadCount("round_envelopes"), Is.EqualTo(1));
            Assert.That(ReadCount("ledger_outbox"), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task PersistRoundEndOnce_ReplayAfterSeasonBumpUsesOriginalSeasonBinding()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var captured = CreateEnvelope().Canonicalize();
        await store.PersistRoundEndOnceAsync(captured, null, CancellationToken.None);
        await store.BumpSeasonAsync();

        var result = await store.PersistRoundEndOnceAsync(captured, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(RoundEnvelopePersistResult.AlreadyCommitted));
            Assert.That(ReadScalar("SELECT season_id FROM round_envelopes WHERE round_id = 7;"), Is.EqualTo("S1"));
            Assert.That(ReadCount("ledger_outbox"), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task PersistRoundEndOnce_TwoStoresSerializeToOneCommit()
    {
        var first = new SeasonLedgerStore(_dbPath);
        var second = new SeasonLedgerStore(_dbPath);
        var captured = CreateEnvelope().Canonicalize();

        var results = await Task.WhenAll(
            first.PersistRoundEndOnceAsync(captured, null, CancellationToken.None),
            second.PersistRoundEndOnceAsync(captured, null, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(results, Does.Contain(RoundEnvelopePersistResult.Committed));
            Assert.That(results, Does.Contain(RoundEnvelopePersistResult.AlreadyCommitted));
            Assert.That(ReadCount("round_envelopes"), Is.EqualTo(1));
            Assert.That(ReadCount("round_log"), Is.EqualTo(1));
            Assert.That(ReadCount("ledger_outbox"), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task OutboxPayload_IsAggregateOnlyAndHasNoFineTimestamp()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var captured = CreateEnvelope(
            players: new[]
            {
                new RoundEndPlayerRecord(Alice, Contribution() with
                {
                    WasCaptainClean = true,
                    AntagWin = true,
                    EarlyDeath = true,
                    Standing = 3,
                    ContractsCompleted = 1,
                    ContractScore = 2,
                    HrPointsEarned = 4,
                }),
            },
            contracts: new[] { new ContractLogRecord(7, Alice, "SecretContract", "personal") })
            .Canonicalize();

        await store.PersistRoundEndOnceAsync(captured, null, CancellationToken.None);

        var payload = (string) ReadScalar("SELECT payload_json FROM ledger_outbox;")!;
        var boundHash = (string) ReadScalar("SELECT envelope_hash FROM round_envelopes;")!;
        using var document = JsonDocument.Parse(payload);
        Assert.Multiple(() =>
        {
            Assert.That(payload, Does.Not.Contain(Alice.ToString("D")));
            Assert.That(payload, Does.Not.Contain("SecretContract"));
            Assert.That(payload, Does.Not.Contain("personal"));
            Assert.That(payload, Does.Not.Contain("Secret"));
            Assert.That(payload, Does.Not.Contain("captain"));
            Assert.That(payload, Does.Not.Contain("antag"));
            Assert.That(payload, Does.Not.Contain("early_death"));
            Assert.That(payload, Does.Not.Contain(captured.CaptureHash));
            Assert.That(payload, Does.Not.Contain(boundHash));
            Assert.That(boundHash, Is.Not.EqualTo(captured.CaptureHash),
                "season binding must create a distinct database identity");
            Assert.That(document.RootElement.GetProperty("privacy_class").GetString(), Is.EqualTo("private-internal"));
            Assert.That(document.RootElement.GetProperty("committed_date").GetString(), Does.Match("^[0-9]{4}-[0-9]{2}-[0-9]{2}$"));
            Assert.That(payload, Does.Not.Match("[0-9]{2}:[0-9]{2}:[0-9]{2}"));
        });
    }

    [Test]
    public async Task MatchingSchemaV3PlayerSubset_IsReconciledWithoutDoubleStats()
    {
        var store = new SeasonLedgerStore(_dbPath);
        await store.AddRoundRecordAsync(Alice, Contribution(standing: 3));
        var captured = CreateEnvelope(players: new[]
        {
            new RoundEndPlayerRecord(Alice, Contribution(standing: 3)),
            new RoundEndPlayerRecord(Bob, Contribution(standing: 5)),
        }).Canonicalize();

        Assert.That(await store.PersistRoundEndOnceAsync(captured, null, CancellationToken.None),
            Is.EqualTo(RoundEnvelopePersistResult.Committed));
        var alice = await store.GetStatsAsync(Alice);
        var bob = await store.GetStatsAsync(Bob);

        Assert.Multiple(() =>
        {
            Assert.That(alice.Tours, Is.EqualTo(1));
            Assert.That(bob.Tours, Is.EqualTo(1));
            Assert.That(ReadCount("round_log"), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task MismatchedSchemaV3PlayerEvidence_FailsClosed()
    {
        var mismatchStore = new SeasonLedgerStore(_dbPath);
        await mismatchStore.AddRoundRecordAsync(Alice, Contribution(standing: 3));
        var mismatched = CreateEnvelope(players: new[]
        {
            new RoundEndPlayerRecord(Alice, Contribution(standing: 4)),
        }).Canonicalize();

        Assert.ThrowsAsync<RoundEndReplayConflictException>(async () =>
            await mismatchStore.PersistRoundEndOnceAsync(mismatched, null, CancellationToken.None));
        Assert.Multiple(() =>
        {
            Assert.That(ReadCount("round_log"), Is.EqualTo(1));
            Assert.That(ReadCount("round_envelopes"), Is.Zero);
            Assert.That(ReadCount("ledger_outbox"), Is.Zero);
        });
    }

    [Test]
    public async Task ExtraneousSchemaV3PlayerEvidence_FailsClosed()
    {
        var store = new SeasonLedgerStore(_dbPath);
        await store.AddRoundRecordAsync(Bob, Contribution());
        var extraneous = CreateEnvelope().Canonicalize();

        Assert.ThrowsAsync<RoundEndReplayConflictException>(async () =>
            await store.PersistRoundEndOnceAsync(extraneous, null, CancellationToken.None));
        Assert.Multiple(() =>
        {
            Assert.That(ReadCount("round_log"), Is.EqualTo(1));
            Assert.That(ReadCount("round_envelopes"), Is.Zero);
            Assert.That(ReadCount("ledger_outbox"), Is.Zero);
        });
    }

    [Test]
    public async Task PartialLegacyContractMultiset_FailsClosed()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var contract = new ContractLogRecord(7, Alice, "ContractA", "personal");
        await store.AddContractLogAsync(new[] { contract });
        var captured = CreateEnvelope(contracts: new[] { contract, contract }).Canonicalize();

        Assert.ThrowsAsync<RoundEndReplayConflictException>(async () =>
            await store.PersistRoundEndOnceAsync(captured, null, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(ReadCount("contract_log"), Is.EqualTo(1));
            Assert.That(ReadCount("round_log"), Is.Zero);
            Assert.That(ReadCount("player_stats"), Is.Zero);
            Assert.That(ReadCount("round_envelopes"), Is.Zero);
            Assert.That(ReadCount("ledger_outbox"), Is.Zero);
        });
    }

    [Test]
    public async Task FullLegacyContractMultiset_IsAcceptedWithoutDuplicates()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var contract = new ContractLogRecord(7, Alice, "ContractA", "personal");
        await store.AddContractLogAsync(new[] { contract, contract });
        var captured = CreateEnvelope(contracts: new[] { contract, contract }).Canonicalize();

        Assert.That(await store.PersistRoundEndOnceAsync(captured, null, CancellationToken.None),
            Is.EqualTo(RoundEnvelopePersistResult.Committed));
        Assert.Multiple(() =>
        {
            Assert.That(ReadCount("contract_log"), Is.EqualTo(2));
            Assert.That(ReadCount("round_log"), Is.EqualTo(1));
            Assert.That(ReadCount("round_envelopes"), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task AmbiguousLegacySeasons_FailClosed()
    {
        var store = new SeasonLedgerStore(_dbPath);
        await store.AddRoundRecordAsync(Alice, Contribution());
        await store.BumpSeasonAsync();
        await store.AddRoundRecordAsync(Bob, Contribution());
        var captured = CreateEnvelope(players: new[]
        {
            new RoundEndPlayerRecord(Alice, Contribution()),
            new RoundEndPlayerRecord(Bob, Contribution()),
        }).Canonicalize();

        Assert.ThrowsAsync<RoundEndReplayConflictException>(async () =>
            await store.PersistRoundEndOnceAsync(captured, null, CancellationToken.None));
        Assert.Multiple(() =>
        {
            Assert.That(ReadCount("round_envelopes"), Is.Zero);
            Assert.That(ReadCount("ledger_outbox"), Is.Zero);
            Assert.That(ReadCount("round_log"), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task LegacyWriters_RejectCommittedEnvelopeRound()
    {
        var store = new SeasonLedgerStore(_dbPath);
        await store.PersistRoundEndOnceAsync(CreateEnvelope().Canonicalize(), null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<RoundEndReplayConflictException>(async () =>
                await store.AddRoundRecordAsync(Alice, Contribution()));
            Assert.ThrowsAsync<RoundEndReplayConflictException>(async () =>
                await store.AddContractLogAsync(new[]
                {
                    new ContractLogRecord(7, Alice, "ContractA", "personal"),
                }));
        });
        Assert.Multiple(() =>
        {
            Assert.That(ReadCount("round_log"), Is.EqualTo(1));
            Assert.That(ReadCount("contract_log"), Is.Zero);
        });
    }

    private static RoundEndEnvelope CreateEnvelope(
        int roundId = 7,
        string gamemode = "Secret",
        IReadOnlyList<RoundEndPlayerRecord> players = null,
        IReadOnlyList<ContractLogRecord> contracts = null)
    {
        return new RoundEndEnvelope(roundId, gamemode, players ?? Players(roundId, gamemode), contracts ?? Array.Empty<ContractLogRecord>());
    }

    private static IReadOnlyList<RoundEndPlayerRecord> Players(int roundId = 7, string gamemode = "Secret")
    {
        return new[] { new RoundEndPlayerRecord(Alice, Contribution(roundId, gamemode)) };
    }

    private static RoundContribution Contribution(
        int roundId = 7,
        string gamemode = "Secret",
        int standing = 0)
    {
        return new RoundContribution(
            WasCaptainClean: false,
            AntagWin: false,
            EarlyDeath: false,
            RoundId: roundId,
            Gamemode: gamemode,
            Standing: standing,
            ContractsCompleted: 0,
            ContractScore: 0,
            HrPointsEarned: 0);
    }

    private int ReadCount(string table)
    {
        return Convert.ToInt32(ReadScalar($"SELECT COUNT(*) FROM {table};"));
    }

    private object ReadScalar(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = command.ExecuteScalar();
        return value == DBNull.Value ? null : value;
    }

    private sealed class ThrowAfterFirstPlayerFaultInjector : IRoundEnvelopeFaultInjector
    {
        public void Hit(RoundEnvelopeStage stage, int index)
        {
            if (stage == RoundEnvelopeStage.AfterPlayerMutation && index == 0)
                throw new InjectedRoundEndFaultException();
        }
    }

    private sealed class ThrowBeforeCommitFaultInjector : IRoundEnvelopeFaultInjector
    {
        public void Hit(RoundEnvelopeStage stage, int index)
        {
            if (stage == RoundEnvelopeStage.BeforeCommit)
                throw new InjectedRoundEndFaultException();
        }
    }

    private sealed class CancelBeforeCommitFaultInjector(CancellationTokenSource cancellation) : IRoundEnvelopeFaultInjector
    {
        public void Hit(RoundEnvelopeStage stage, int index)
        {
            if (stage == RoundEnvelopeStage.BeforeCommit)
                cancellation.Cancel();
        }
    }

    private sealed class InjectedRoundEndFaultException : Exception
    {
    }
}
