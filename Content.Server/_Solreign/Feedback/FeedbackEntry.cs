using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Content.Shared._Solreign.Feedback;

namespace Content.Server._Solreign.Feedback;

/// <summary>
///     One player-submitted structured feedback report (roadmap D2.1). Plain data, no Robust
///     dependencies, so it and its JSON shaping can be unit-tested directly — mirrors
///     <c>BugReportEntry</c>/<c>BugReportJsonl</c> (Content.Server/_Solreign/BugReport).
/// </summary>
public sealed record FeedbackEntry(
    DateTimeOffset Timestamp,
    string ReferenceId,
    string PlayerName,
    Guid PlayerGuid,
    int RoundId,
    FeedbackCategory Category,
    string Text,
    string Map,
    string Job,
    string Location,
    string ServerBuild);

/// <summary>
///     JSON shape of a single data/feedback/feedback-yyyy-MM-dd.jsonl line.
/// </summary>
public sealed class FeedbackJsonLine
{
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";

    [JsonPropertyName("referenceId")]
    public string ReferenceId { get; set; } = "";

    [JsonPropertyName("playerName")]
    public string PlayerName { get; set; } = "";

    [JsonPropertyName("playerGuid")]
    public string PlayerGuid { get; set; } = "";

    [JsonPropertyName("roundId")]
    public int RoundId { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("map")]
    public string Map { get; set; } = "";

    [JsonPropertyName("job")]
    public string Job { get; set; } = "";

    [JsonPropertyName("location")]
    public string Location { get; set; } = "";

    [JsonPropertyName("serverBuild")]
    public string ServerBuild { get; set; } = "";
}

/// <summary>
///     Pure serialization of a <see cref="FeedbackEntry"/> to one JSONL line. Kept separate from any
///     file I/O so payload shaping is unit-testable without touching disk (same split as
///     <c>BugReportJsonl</c>).
/// </summary>
public static class FeedbackJsonl
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>Serializes one entry to a single JSON line (no trailing newline).</summary>
    public static string ToLine(FeedbackEntry entry)
    {
        var line = new FeedbackJsonLine
        {
            // Round-trippable, sortable, unambiguous timezone -> ISO 8601 ("O"), same idiom as
            // BugReportJsonl.
            Timestamp = entry.Timestamp.ToString("O"),
            ReferenceId = entry.ReferenceId,
            PlayerName = entry.PlayerName,
            PlayerGuid = entry.PlayerGuid.ToString(),
            RoundId = entry.RoundId,
            Category = entry.Category.ToString().ToLowerInvariant(),
            Text = entry.Text,
            Map = entry.Map,
            Job = entry.Job,
            Location = entry.Location,
            ServerBuild = entry.ServerBuild,
        };

        return JsonSerializer.Serialize(line, Options);
    }
}
