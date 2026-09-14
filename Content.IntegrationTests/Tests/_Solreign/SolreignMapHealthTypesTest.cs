#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
/// Regression coverage for the bounds <see cref="MapHealthResultV1Jsonl.ToLine"/> enforces at the
/// emission boundary (SR-W-012 review P2: "the ToLine serializer bounds are implemented but
/// untested"). Pure unit tests -- no server pair, no map load.
/// </summary>
[TestFixture]
public sealed class SolreignMapHealthTypesTest
{
    private static MapHealthResultV1 MakeResult(
        int schemaVersion = 1,
        string mapId = "SolreignOasis",
        IReadOnlyList<string>? failureCodes = null)
    {
        return new MapHealthResultV1(
            schemaVersion,
            mapId,
            MinPlayers: 0,
            MaxPlayers: 35,
            StationGrid: MapHealthStatus.Pass,
            LatejoinSpawn: MapHealthStatus.Pass,
            JobSpawns: MapHealthStatus.Pass,
            EmergencyShuttle: MapHealthStatus.Pass,
            ApcLoad: MapHealthStatus.Pass,
            SpawnAtmosphere: MapHealthStatus.Pass,
            StoredPowerRunway: MapHealthStatus.NotEvaluated,
            StationGridCount: 1,
            ConfiguredJobCount: 34,
            MissingJobSpawnCount: 0,
            LatejoinSpawnCount: 4,
            ApcCount: 41,
            OverloadedApcCount: 0,
            EvaluatedSpawnTileCount: 60,
            UnsafeSpawnTileCount: 0,
            FailureCodes: failureCodes ?? new List<string>());
    }

    private static JsonElement Emit(MapHealthResultV1 result)
    {
        using var doc = JsonDocument.Parse(MapHealthResultV1Jsonl.ToLine(result));
        return doc.RootElement.Clone();
    }

    [Test]
    public void SchemaVersionIsForcedToOneRegardlessOfInput()
    {
        var root = Emit(MakeResult(schemaVersion: 99));
        Assert.That(root.GetProperty("schemaVersion").GetInt32(),
            Is.EqualTo(MapHealthResultV1Jsonl.EmittedSchemaVersion).And.EqualTo(1));
    }

    [Test]
    public void MapIdIsTruncatedToTheLengthBound()
    {
        var longId = new string('m', MapHealthResultV1Jsonl.MaxMapIdLength + 36);
        var root = Emit(MakeResult(mapId: longId));
        var emitted = root.GetProperty("mapId").GetString();

        Assert.Multiple(() =>
        {
            Assert.That(emitted, Has.Length.EqualTo(MapHealthResultV1Jsonl.MaxMapIdLength));
            Assert.That(emitted, Is.EqualTo(longId[..MapHealthResultV1Jsonl.MaxMapIdLength]));
        });

        // A catalog-length id passes through unchanged.
        var normal = Emit(MakeResult(mapId: "SolreignTerminus"));
        Assert.That(normal.GetProperty("mapId").GetString(), Is.EqualTo("SolreignTerminus"));
    }

    [Test]
    public void UnknownFailureCodesAreDropped()
    {
        var root = Emit(MakeResult(failureCodes: new List<string>
        {
            "totally_made_up_code",
            MapHealthFailureCode.ApcOverloaded,
            "entity 1234 at (13, 62)", // the exact shape of leak the allowlist exists to stop
        }));

        var emitted = root.GetProperty("failureCodes").EnumerateArray()
            .Select(e => e.GetString()).ToList();

        Assert.That(emitted, Is.EqualTo(new[] { MapHealthFailureCode.ApcOverloaded }));
    }

    [Test]
    public void DuplicateFailureCodesAreDeduplicatedPreservingFirstOccurrenceOrder()
    {
        var root = Emit(MakeResult(failureCodes: new List<string>
        {
            MapHealthFailureCode.UnsafeSpawnTileOxygen,
            MapHealthFailureCode.ApcOverloaded,
            MapHealthFailureCode.UnsafeSpawnTileOxygen,
            MapHealthFailureCode.ApcOverloaded,
            MapHealthFailureCode.UnsafeSpawnTileOxygen,
        }));

        var emitted = root.GetProperty("failureCodes").EnumerateArray()
            .Select(e => e.GetString()).ToList();

        Assert.That(emitted, Is.EqualTo(new[]
        {
            MapHealthFailureCode.UnsafeSpawnTileOxygen,
            MapHealthFailureCode.ApcOverloaded,
        }));
    }

    [Test]
    public void FailureCodesAreCappedAndTheCapCanNeverDropALegitimateSet()
    {
        // The allowlist + dedupe already bound the emitted set to the closed code set, so the cap is
        // only reachable if the closed set ever outgrows it -- guard that invariant explicitly.
        Assert.That(MapHealthFailureCode.All.Count, Is.LessThanOrEqualTo(MapHealthResultV1Jsonl.MaxFailureCodes),
            "MaxFailureCodes must stay >= the closed code set, or the cap would silently drop legitimate codes.");

        // Worst legitimate case: every allowed code, duplicated, with junk interleaved -> exactly the
        // closed set once each, in first-occurrence order, within the cap.
        var input = new List<string>();
        foreach (var code in MapHealthFailureCode.All)
        {
            input.Add(code);
            input.Add("junk_" + code);
            input.Add(code);
        }

        var root = Emit(MakeResult(failureCodes: input));
        var emitted = root.GetProperty("failureCodes").EnumerateArray()
            .Select(e => e.GetString()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(emitted, Is.EqualTo(MapHealthFailureCode.All));
            Assert.That(emitted, Has.Count.LessThanOrEqualTo(MapHealthResultV1Jsonl.MaxFailureCodes));
        });
    }
}
