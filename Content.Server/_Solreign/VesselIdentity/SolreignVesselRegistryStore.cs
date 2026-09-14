using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Robust.Shared.Utility;

namespace Content.Server._Solreign.VesselIdentity;

/// <summary>
///     In-memory and file-backed store for persisting player-named vessel identities across round restarts (SR-W-043).
/// </summary>
public sealed class SolreignVesselRegistryStore
{
    private readonly Dictionary<string, SolreignVesselRegistryRecord> _records = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public IReadOnlyDictionary<string, SolreignVesselRegistryRecord> Records => _records;

    /// <summary>
    ///     Registers or updates a vessel identity record in the store.
    /// </summary>
    public void SaveRecord(SolreignVesselRegistryRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.VesselId))
            return;

        record.LastActiveTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _records[record.VesselId] = record;
    }

    /// <summary>
    ///     Retrieves a vessel identity record by its registry ID.
    /// </summary>
    public bool TryGetRecord(string vesselId, out SolreignVesselRegistryRecord? record)
    {
        return _records.TryGetValue(vesselId, out record);
    }

    /// <summary>
    ///     Removes or clears a vessel identity record (e.g. upon admin reset/deletion).
    /// </summary>
    public bool RemoveRecord(string vesselId)
    {
        return _records.Remove(vesselId);
    }

    /// <summary>
    ///     Clears all records in memory.
    /// </summary>
    public void Clear()
    {
        _records.Clear();
    }

    /// <summary>
    ///     Serializes the store's vessel records to a JSON payload string.
    /// </summary>
    public string SerializeToJson()
    {
        return JsonSerializer.Serialize(_records.Values, JsonOptions);
    }

    /// <summary>
    ///     Deserializes vessel records from a JSON payload string and populates the store.
    /// </summary>
    public void LoadFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            var list = JsonSerializer.Deserialize<List<SolreignVesselRegistryRecord>>(json, JsonOptions);
            if (list == null)
                return;

            _records.Clear();
            foreach (var rec in list)
            {
                if (!string.IsNullOrWhiteSpace(rec.VesselId))
                    _records[rec.VesselId] = rec;
            }
        }
        catch
        {
            // Invalid JSON or corrupted format gracefully handled
        }
    }

    /// <summary>
    ///     Saves the current registry store to a target file path.
    /// </summary>
    public void SaveToFile(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = SerializeToJson();
        File.WriteAllText(path, json);
    }

    /// <summary>
    ///     Loads registry store contents from a target file path if it exists.
    /// </summary>
    public void LoadFromFile(string path)
    {
        if (!File.Exists(path))
            return;

        var json = File.ReadAllText(path);
        LoadFromJson(json);
    }
}
