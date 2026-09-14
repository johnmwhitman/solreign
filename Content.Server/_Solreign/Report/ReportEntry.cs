using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Content.Shared._Solreign.Report;

namespace Content.Server._Solreign.Report;

/// <summary>
///     One player-filed conduct report. Plain data, no Robust dependencies, so it and its JSON shaping
///     can be unit-tested directly -- mirrors <c>Content.Server._Solreign.Feedback.FeedbackEntry</c>/
///     <c>FeedbackJsonl</c>.
/// </summary>
public sealed record ReportEntry(
    DateTimeOffset Timestamp,
    string ReferenceId,
    string ReporterName,
    Guid ReporterGuid,
    string TargetAsTyped,
    string? TargetResolvedName,
    Guid? TargetGuid,
    int RoundId,
    ReportCategory Category,
    string Text,
    string Map);

/// <summary>
///     JSON shape of a single data/reports/reports-yyyy-MM-dd.jsonl line.
/// </summary>
public sealed class ReportJsonLine
{
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";

    [JsonPropertyName("referenceId")]
    public string ReferenceId { get; set; } = "";

    [JsonPropertyName("reporterName")]
    public string ReporterName { get; set; } = "";

    [JsonPropertyName("reporterGuid")]
    public string ReporterGuid { get; set; } = "";

    [JsonPropertyName("targetAsTyped")]
    public string TargetAsTyped { get; set; } = "";

    [JsonPropertyName("targetResolvedName")]
    public string? TargetResolvedName { get; set; }

    [JsonPropertyName("targetGuid")]
    public string? TargetGuid { get; set; }

    [JsonPropertyName("roundId")]
    public int RoundId { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("map")]
    public string Map { get; set; } = "";
}

/// <summary>
///     Pure serialization of a <see cref="ReportEntry"/> to one JSONL line. Kept separate from any file
///     I/O so payload shaping is unit-testable without touching disk (same split as
///     <c>Content.Server._Solreign.Feedback.FeedbackJsonl</c>).
/// </summary>
public static class ReportJsonl
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>Serializes one entry to a single JSON line (no trailing newline).</summary>
    public static string ToLine(ReportEntry entry)
    {
        var line = new ReportJsonLine
        {
            // Round-trippable, sortable, unambiguous timezone -> ISO 8601 ("O"), same idiom as
            // FeedbackJsonl/BugReportJsonl.
            Timestamp = entry.Timestamp.ToString("O"),
            ReferenceId = entry.ReferenceId,
            ReporterName = entry.ReporterName,
            ReporterGuid = entry.ReporterGuid.ToString(),
            TargetAsTyped = entry.TargetAsTyped,
            TargetResolvedName = entry.TargetResolvedName,
            TargetGuid = entry.TargetGuid?.ToString(),
            RoundId = entry.RoundId,
            Category = entry.Category.ToString().ToLowerInvariant(),
            Text = entry.Text,
            Map = entry.Map,
        };

        return JsonSerializer.Serialize(line, Options);
    }
}
