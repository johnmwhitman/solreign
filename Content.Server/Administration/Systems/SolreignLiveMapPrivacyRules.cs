using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Content.Server.Administration.Systems;

/// <summary>
/// Pure privacy boundary for LiveMap. It emits bounded coarse occupancy cells and no identity.
/// </summary>
public static class SolreignLiveMapPrivacyRules
{
    public const float CellSizeTiles = 8f;
    public const int MaximumOccupants = 256;
    public const int MaximumCells = 128;
    public const int MaximumCellCoordinate = 4096;

    public readonly record struct Observation(float X, float Y);

    public readonly record struct Cell(
        [property: JsonPropertyName("x")] int X,
        [property: JsonPropertyName("y")] int Y,
        [property: JsonPropertyName("count")] int Count)
    {
        [JsonPropertyName("cellId")]
        public string CellId => $"{X}:{Y}";
    }

    public readonly record struct Snapshot(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("roundId")] int RoundId,
        [property: JsonPropertyName("generatedAtUnixMs")] long GeneratedAtUnixMs,
        [property: JsonPropertyName("cells")] IReadOnlyList<Cell> Cells);

    public static IReadOnlyList<Cell> BuildCells(IEnumerable<Observation> observations)
    {
        var counts = new Dictionary<(int X, int Y), int>();
        var accepted = 0;

        foreach (var observation in observations)
        {
            if (accepted >= MaximumOccupants)
                break;

            if (!float.IsFinite(observation.X) || !float.IsFinite(observation.Y))
                continue;

            var x = (int) MathF.Floor(observation.X / CellSizeTiles);
            var y = (int) MathF.Floor(observation.Y / CellSizeTiles);
            if (Math.Abs(x) > MaximumCellCoordinate || Math.Abs(y) > MaximumCellCoordinate)
                continue;

            var key = (x, y);
            if (!counts.ContainsKey(key) && counts.Count >= MaximumCells)
                continue;

            counts.TryGetValue(key, out var count);
            counts[key] = count + 1;
            accepted++;
        }

        return counts
            .OrderBy(entry => entry.Key.X)
            .ThenBy(entry => entry.Key.Y)
            .Select(entry => new Cell(entry.Key.X, entry.Key.Y, entry.Value))
            .ToArray();
    }

    public static Snapshot BuildSnapshot(
        int roundId,
        long generatedAtUnixMs,
        IEnumerable<Observation> observations) =>
        new(1, roundId, generatedAtUnixMs, BuildCells(observations));
}
