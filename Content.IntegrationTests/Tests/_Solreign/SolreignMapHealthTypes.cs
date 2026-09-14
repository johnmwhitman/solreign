using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
/// Per-dimension outcome for one row of <see cref="MapHealthResultV1"/>.
/// </summary>
internal enum MapHealthStatus
{
    Pass,
    Fail,
    NotEvaluated,
}

/// <summary>
/// Bounded, test-local failure codes emitted by <see cref="SolreignMapHealthScorecardIntegrationTest"/>.
/// The set is intentionally closed -- never an entity ID, a coordinate, a serialized component, an
/// exception message, or player/account data. See
/// docs/research/SR-W-012-MAP-HEALTH-GAP-AUDIT-2026-07-15.md, "Smallest honest implementation slice".
/// </summary>
internal static class MapHealthFailureCode
{
    internal const string MapPrototypeUnresolved = "map_prototype_unresolved";
    internal const string MapLoadFailed = "map_load_failed";
    internal const string NoStationGrid = "no_station_grid";
    internal const string NoLatejoinSpawn = "no_latejoin_spawn";
    internal const string NoJobConfiguration = "no_job_configuration";
    internal const string MissingJobSpawn = "missing_job_spawn";
    internal const string NoEmergencyShuttleComponent = "no_emergency_shuttle_component";
    internal const string EmergencyShuttleLoadFailed = "emergency_shuttle_load_failed";
    internal const string EmergencyShuttleDockFailed = "emergency_shuttle_dock_failed";
    internal const string ApcOverloaded = "apc_overloaded";
    internal const string UnsafeSpawnTileSpace = "unsafe_spawn_tile_space";
    internal const string UnsafeSpawnTileAtmosphere = "unsafe_spawn_tile_atmosphere";
    internal const string UnsafeSpawnTileOxygen = "unsafe_spawn_tile_oxygen";

    /// <summary>
    /// The complete closed set. <see cref="MapHealthResultV1Jsonl.ToLine"/> allowlists against this,
    /// so a stray/unbounded string can never reach the emitted JSON.
    /// </summary>
    internal static readonly IReadOnlyList<string> All = new[]
    {
        MapPrototypeUnresolved,
        MapLoadFailed,
        NoStationGrid,
        NoLatejoinSpawn,
        NoJobConfiguration,
        MissingJobSpawn,
        NoEmergencyShuttleComponent,
        EmergencyShuttleLoadFailed,
        EmergencyShuttleDockFailed,
        ApcOverloaded,
        UnsafeSpawnTileSpace,
        UnsafeSpawnTileAtmosphere,
        UnsafeSpawnTileOxygen,
    };
}

/// <summary>
/// Bounded, test-local record describing SR-W-012 Phase A results for one map. One NUnit test case
/// produces one row through <see cref="MapHealthResultV1Jsonl.ToLine"/>; the row is schema version, map
/// identity, declared population band, one pass/fail/not_evaluated status per dimension, bounded
/// aggregate counts, and a bounded failure-code list -- never entity IDs, coordinates, serialized
/// components, exception dumps, or player/account data. Stored-power runway is always
/// <see cref="MapHealthStatus.NotEvaluated"/> in Phase A (the existing calculation stays
/// <c>[Explicit]</c> and unextracted -- see docs/research/SR-W-012-MAP-HEALTH-GAP-AUDIT-2026-07-15.md).
/// </summary>
/// <param name="MaxPlayers">
/// The map's declared maximum population, or <c>-1</c> if the prototype leaves it unbounded
/// (<c>GameMapPrototype.MaxPlayers == uint.MaxValue</c>, e.g. SolreignTerminus).
/// </param>
internal sealed record MapHealthResultV1(
    int SchemaVersion,
    string MapId,
    int MinPlayers,
    int MaxPlayers,
    MapHealthStatus StationGrid,
    MapHealthStatus LatejoinSpawn,
    MapHealthStatus JobSpawns,
    MapHealthStatus EmergencyShuttle,
    MapHealthStatus ApcLoad,
    MapHealthStatus SpawnAtmosphere,
    MapHealthStatus StoredPowerRunway,
    int StationGridCount,
    int ConfiguredJobCount,
    int MissingJobSpawnCount,
    int LatejoinSpawnCount,
    int ApcCount,
    int OverloadedApcCount,
    int EvaluatedSpawnTileCount,
    int UnsafeSpawnTileCount,
    IReadOnlyList<string> FailureCodes);

/// <summary>
/// JSON shape of one <see cref="MapHealthResultV1"/> NUnit output line. Kept as a separate DTO (same
/// split as <c>Content.Server._Solreign.Report.ReportJsonLine</c>/<c>ReportJsonl</c>) so the record's
/// C# member names and the wire's camelCase field names can evolve independently.
/// </summary>
internal sealed class MapHealthResultV1JsonLine
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
    [JsonPropertyName("mapId")] public string MapId { get; set; } = "";
    [JsonPropertyName("minPlayers")] public int MinPlayers { get; set; }
    [JsonPropertyName("maxPlayers")] public int MaxPlayers { get; set; }
    [JsonPropertyName("stationGrid")] public string StationGrid { get; set; } = "";
    [JsonPropertyName("latejoinSpawn")] public string LatejoinSpawn { get; set; } = "";
    [JsonPropertyName("jobSpawns")] public string JobSpawns { get; set; } = "";
    [JsonPropertyName("emergencyShuttle")] public string EmergencyShuttle { get; set; } = "";
    [JsonPropertyName("apcLoad")] public string ApcLoad { get; set; } = "";
    [JsonPropertyName("spawnAtmosphere")] public string SpawnAtmosphere { get; set; } = "";
    [JsonPropertyName("storedPowerRunway")] public string StoredPowerRunway { get; set; } = "";
    [JsonPropertyName("stationGridCount")] public int StationGridCount { get; set; }
    [JsonPropertyName("configuredJobCount")] public int ConfiguredJobCount { get; set; }
    [JsonPropertyName("missingJobSpawnCount")] public int MissingJobSpawnCount { get; set; }
    [JsonPropertyName("latejoinSpawnCount")] public int LatejoinSpawnCount { get; set; }
    [JsonPropertyName("apcCount")] public int ApcCount { get; set; }
    [JsonPropertyName("overloadedApcCount")] public int OverloadedApcCount { get; set; }
    [JsonPropertyName("evaluatedSpawnTileCount")] public int EvaluatedSpawnTileCount { get; set; }
    [JsonPropertyName("unsafeSpawnTileCount")] public int UnsafeSpawnTileCount { get; set; }
    [JsonPropertyName("failureCodes")] public List<string> FailureCodes { get; set; } = new();
}

/// <summary>
/// Pure serialization of a <see cref="MapHealthResultV1"/> to one deterministic JSON line, written to
/// NUnit output via <c>TestContext.Out.WriteLine</c> (same idiom as
/// <c>SolreignPerfBaselineTest.Baseline</c>). Never writes a file or performs runtime I/O.
/// </summary>
internal static class MapHealthResultV1Jsonl
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>The only schema version this serializer emits. Enforced regardless of input.</summary>
    internal const int EmittedSchemaVersion = 1;

    /// <summary>Length bound on the emitted map id; Solreign map prototype IDs are all far shorter.</summary>
    internal const int MaxMapIdLength = 64;

    /// <summary>
    /// Hard cap on emitted failure codes. The allowlist itself is the true bound (codes are deduplicated
    /// against a closed set), so this is belt-and-braces.
    /// </summary>
    internal const int MaxFailureCodes = 16;

    private static readonly HashSet<string> AllowedFailureCodes = new(MapHealthFailureCode.All);

    internal static string ToLine(MapHealthResultV1 result)
    {
        // Bounds are enforced HERE, at the emission boundary, not merely by caller discipline:
        // fixed schema version, length-constrained map id, and an allowlisted + deduplicated + capped
        // failure-code list. Anything outside the closed MapHealthFailureCode set is dropped.
        var boundedMapId = result.MapId.Length <= MaxMapIdLength
            ? result.MapId
            : result.MapId[..MaxMapIdLength];

        var boundedCodes = new List<string>();
        foreach (var code in result.FailureCodes)
        {
            if (boundedCodes.Count >= MaxFailureCodes)
                break;
            if (AllowedFailureCodes.Contains(code) && !boundedCodes.Contains(code))
                boundedCodes.Add(code);
        }

        var line = new MapHealthResultV1JsonLine
        {
            SchemaVersion = EmittedSchemaVersion,
            MapId = boundedMapId,
            MinPlayers = result.MinPlayers,
            MaxPlayers = result.MaxPlayers,
            StationGrid = ToJsonValue(result.StationGrid),
            LatejoinSpawn = ToJsonValue(result.LatejoinSpawn),
            JobSpawns = ToJsonValue(result.JobSpawns),
            EmergencyShuttle = ToJsonValue(result.EmergencyShuttle),
            ApcLoad = ToJsonValue(result.ApcLoad),
            SpawnAtmosphere = ToJsonValue(result.SpawnAtmosphere),
            StoredPowerRunway = ToJsonValue(result.StoredPowerRunway),
            StationGridCount = result.StationGridCount,
            ConfiguredJobCount = result.ConfiguredJobCount,
            MissingJobSpawnCount = result.MissingJobSpawnCount,
            LatejoinSpawnCount = result.LatejoinSpawnCount,
            ApcCount = result.ApcCount,
            OverloadedApcCount = result.OverloadedApcCount,
            EvaluatedSpawnTileCount = result.EvaluatedSpawnTileCount,
            UnsafeSpawnTileCount = result.UnsafeSpawnTileCount,
            FailureCodes = boundedCodes,
        };

        return JsonSerializer.Serialize(line, Options);
    }

    private static string ToJsonValue(MapHealthStatus status) => status switch
    {
        MapHealthStatus.Pass => "pass",
        MapHealthStatus.Fail => "fail",
        MapHealthStatus.NotEvaluated => "not_evaluated",
        _ => "fail",
    };
}
