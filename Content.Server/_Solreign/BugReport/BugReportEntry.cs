using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Content.Server._Solreign.BugReport;

/// <summary>
///     One player-submitted "bugreport" console command invocation (see <see cref="BugReportCommand"/>).
///     Plain data, no Robust dependencies, so it and its JSON shaping can be unit-tested directly.
/// </summary>
public sealed record BugReportEntry(
    DateTimeOffset Timestamp,
    string PlayerName,
    Guid PlayerGuid,
    int RoundId,
    string Text);

/// <summary>
///     JSON shape of a single data/bug_reports.jsonl line.
/// </summary>
public sealed class BugReportJsonLine
{
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";

    [JsonPropertyName("playerName")]
    public string PlayerName { get; set; } = "";

    [JsonPropertyName("playerGuid")]
    public string PlayerGuid { get; set; } = "";

    [JsonPropertyName("roundId")]
    public int RoundId { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
}

/// <summary>
///     Pure serialization of a <see cref="BugReportEntry"/> to one data/bug_reports.jsonl line.
///     Kept separate from any file I/O so payload shaping is unit-testable without touching disk.
/// </summary>
public static class BugReportJsonl
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>Serializes one entry to a single JSON line (no trailing newline).</summary>
    public static string ToLine(BugReportEntry entry)
    {
        var line = new BugReportJsonLine
        {
            // Round-trippable, sortable, unambiguous timezone -> ISO 8601 ("O").
            Timestamp = entry.Timestamp.ToString("O"),
            PlayerName = entry.PlayerName,
            PlayerGuid = entry.PlayerGuid.ToString(),
            RoundId = entry.RoundId,
            Text = entry.Text,
        };

        return JsonSerializer.Serialize(line, Options);
    }
}
