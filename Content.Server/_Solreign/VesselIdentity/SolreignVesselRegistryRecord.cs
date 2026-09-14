using System;
using System.Text.Json.Serialization;
using Content.Shared._Solreign.VesselIdentity;

namespace Content.Server._Solreign.VesselIdentity;

/// <summary>
///     Serializable persistence model representing a crew vessel's registered identity,
///     cosmetic marks, and performance record across rounds (SR-W-043).
/// </summary>
public sealed class SolreignVesselRegistryRecord
{
    [JsonPropertyName("vessel_id")]
    public string VesselId { get; set; } = string.Empty;

    [JsonPropertyName("vessel_name")]
    public string VesselName { get; set; } = string.Empty;

    [JsonPropertyName("default_name")]
    public string DefaultName { get; set; } = string.Empty;

    [JsonPropertyName("registry_mark")]
    public SolreignVesselRegistryMark RegistryMark { get; set; } = SolreignVesselRegistryMark.Unmarked;

    [JsonPropertyName("moderation_state")]
    public SolreignVesselModerationState ModerationState { get; set; } = SolreignVesselModerationState.Approved;

    [JsonPropertyName("missions_completed")]
    public int MissionsCompleted { get; set; } = 0;

    [JsonPropertyName("total_salvage_value")]
    public float TotalSalvageValue { get; set; } = 0f;

    [JsonPropertyName("registered_owner_guid")]
    public string RegisteredOwnerGuid { get; set; } = string.Empty;

    [JsonPropertyName("created_at_timestamp")]
    public long CreatedAtTimestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    [JsonPropertyName("last_active_timestamp")]
    public long LastActiveTimestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
