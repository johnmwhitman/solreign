#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._Solreign.SeasonLedger;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Unit coverage for the <c>station_audit_log</c> table (SeasonLedgerStore.StationAudits.cs, v14
///     wave-1 #3): idempotent append keyed on <c>round_id</c>, and every column round-trips including
///     the honest-null cases (no directive on file, no commendation). Same per-test temp-DB harness
///     as <see cref="FirstDeathStoreTests"/> — never a bin path, never a shared file.
/// </summary>
[TestFixture]
[TestOf(typeof(SeasonLedgerStore))]
public sealed class StationAuditStoreTests
{
    private string _dbPath = default!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"solreign_station_audit_test_{Guid.NewGuid():N}.db");
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

    private static StationAuditRecord FullRecord(int roundId) => new(
        RoundId: roundId,
        EndedAtUtc: DateTime.UtcNow.ToString("o"),
        ShiftDurationMinutes: 45,
        CrewCount: 12,
        DeathCount: 2,
        FirstDeathCommemorated: true,
        DirectiveTitle: "Q3 Incident Quota",
        DirectiveOutcomeReported: true,
        DirectiveOutcomeFulfilled: true,
        StipendsProcessed: 12,
        BountyVerdicts: 3,
        NotableEventCount: 4,
        CommendationName: "Juno Pike",
        CommendationScore: 5,
        ItemOfConcernId: "unrepaired-breach",
        CriteriaJson: """[{"criterionId":"zero-fatalities","verdict":"Pass"}]""",
        CheckpointConsequenceKind: "commendation",
        CheckpointFiredUtc: DateTime.UtcNow.ToString("o"));

    [Test]
    public async Task Append_Succeeds_AndRoundTripsEveryColumn()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var roundId = 4200;

        Assert.That(await store.AppendStationAuditAsync(FullRecord(roundId)), Is.True);

        var rows = await store.GetRecentStationAuditsAsync();
        var row = rows.SingleOrDefault(r => r.RoundId == roundId);
        Assert.That(row, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(row!.ShiftDurationMinutes, Is.EqualTo(45));
            Assert.That(row.CrewCount, Is.EqualTo(12));
            Assert.That(row.DeathCount, Is.EqualTo(2));
            Assert.That(row.FirstDeathCommemorated, Is.True);
            Assert.That(row.DirectiveTitle, Is.EqualTo("Q3 Incident Quota"));
            Assert.That(row.DirectiveOutcomeReported, Is.True);
            Assert.That(row.DirectiveOutcomeFulfilled, Is.True);
            Assert.That(row.StipendsProcessed, Is.EqualTo(12));
            Assert.That(row.BountyVerdicts, Is.EqualTo(3));
            Assert.That(row.NotableEventCount, Is.EqualTo(4));
            Assert.That(row.CommendationName, Is.EqualTo("Juno Pike"));
            Assert.That(row.CommendationScore, Is.EqualTo(5));
            Assert.That(row.ItemOfConcernId, Is.EqualTo("unrepaired-breach"));
            Assert.That(row.CriteriaJson, Is.EqualTo("""[{"criterionId":"zero-fatalities","verdict":"Pass"}]"""));
            Assert.That(row.CheckpointConsequenceKind, Is.EqualTo("commendation"));
            Assert.That(row.CheckpointFiredUtc, Is.Not.Null);
        });
    }

    [Test]
    public async Task Append_InspectionLayerNeverFired_RoundTripsHonestDefaults()
    {
        // The base-only shape (inspection layer disabled this shift): criteria_json defaults to "[]",
        // checkpoint_consequence_kind to "none", checkpoint_fired_utc to NULL — never a fabricated value.
        var store = new SeasonLedgerStore(_dbPath);
        var roundId = 4210;
        var record = FullRecord(roundId) with
        {
            CriteriaJson = "[]",
            CheckpointConsequenceKind = "none",
            CheckpointFiredUtc = null,
        };

        Assert.That(await store.AppendStationAuditAsync(record), Is.True);

        var rows = await store.GetRecentStationAuditsAsync();
        var row = rows.Single(r => r.RoundId == roundId);
        Assert.Multiple(() =>
        {
            Assert.That(row.CriteriaJson, Is.EqualTo("[]"));
            Assert.That(row.CheckpointConsequenceKind, Is.EqualTo("none"));
            Assert.That(row.CheckpointFiredUtc, Is.Null);
        });
    }

    [Test]
    public async Task GetStationAuditLifetimeCount_ReflectsTotalRowsAcrossAllSeasons()
    {
        var store = new SeasonLedgerStore(_dbPath);

        Assert.That(await store.GetStationAuditLifetimeCountAsync(), Is.EqualTo(0), "a fresh DB has no audit history yet");

        await store.AppendStationAuditAsync(FullRecord(4220));
        await store.AppendStationAuditAsync(FullRecord(4221));

        Assert.That(await store.GetStationAuditLifetimeCountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task Append_HonestlyNullFields_RoundTripAsNull()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var roundId = 4201;
        var record = FullRecord(roundId) with
        {
            DirectiveTitle = null,
            DirectiveOutcomeReported = false,
            DirectiveOutcomeFulfilled = false,
            CommendationName = null,
            CommendationScore = 0,
        };

        Assert.That(await store.AppendStationAuditAsync(record), Is.True);

        var rows = await store.GetRecentStationAuditsAsync();
        var row = rows.Single(r => r.RoundId == roundId);
        Assert.Multiple(() =>
        {
            Assert.That(row.DirectiveTitle, Is.Null);
            Assert.That(row.CommendationName, Is.Null);
        });
    }

    [Test]
    public async Task SecondAppend_SameRoundId_IsIgnored_NeverThrows()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var roundId = 4202;

        Assert.That(await store.AppendStationAuditAsync(FullRecord(roundId)), Is.True);
        Assert.That(await store.AppendStationAuditAsync(FullRecord(roundId) with { CrewCount = 999 }), Is.False,
            "a duplicate append for the same round must no-op, not overwrite");

        var rows = await store.GetRecentStationAuditsAsync();
        var row = rows.Single(r => r.RoundId == roundId);
        Assert.That(row.CrewCount, Is.EqualTo(12), "the original row must survive the duplicate append untouched");
    }

    [Test]
    public async Task GetRecent_ReturnsNewestFirst()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var first = 4300;
        var second = 4301;

        await store.AppendStationAuditAsync(FullRecord(first));
        await store.AppendStationAuditAsync(FullRecord(second));

        var rows = await store.GetRecentStationAuditsAsync();
        var firstIndex = rows.FindIndex(r => r.RoundId == first);
        var secondIndex = rows.FindIndex(r => r.RoundId == second);

        Assert.That(secondIndex, Is.LessThan(firstIndex), "the higher round id should sort before the lower one");
    }
}
