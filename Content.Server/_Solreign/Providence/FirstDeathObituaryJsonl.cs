using System.Text.Json;
using System.Text.Json.Serialization;

namespace Content.Server._Solreign.Providence;

/// <summary>
///     JSON shape of a single data/first_deaths.jsonl line — the manual-paste obituary ledger
///     (FD-W4, spec §6.2 "John gate": v1's recommended operating mode is the manual fallback;
///     every claimed first death appends a ready-to-paste obituary block here and John pastes the
///     good ones into Discord by hand). The BugReport <c>bug_reports.jsonl</c> idiom.
/// </summary>
public sealed class FirstDeathObituaryJsonLine
{
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";

    [JsonPropertyName("roundId")]
    public int RoundId { get; set; }

    [JsonPropertyName("characterName")]
    public string CharacterName { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("tours")]
    public int Tours { get; set; }

    [JsonPropertyName("cause")]
    public string Cause { get; set; } = "";

    [JsonPropertyName("causeLabel")]
    public string CauseLabel { get; set; } = "";

    [JsonPropertyName("playerCountAtDeath")]
    public int PlayerCountAtDeath { get; set; }

    /// <summary>The gate stack's decision for this obituary (recorded BEFORE any send —
    /// write-before-dispatch): webhook-empty | below-player-gate | rate-limited | dispatched.</summary>
    [JsonPropertyName("dispatch")]
    public string Dispatch { get; set; } = "";

    /// <summary>The ready-to-paste block: §8D title, body, and footer joined by newlines —
    /// exactly the text of the embed the webhook would have sent.</summary>
    [JsonPropertyName("paste")]
    public string Paste { get; set; } = "";
}

/// <summary>
///     Pure serialization of one composed <see cref="FirstDeathObituary"/> + its gate decision to
///     a single data/first_deaths.jsonl line — kept separate from any file I/O so the shaping is
///     unit-testable without touching disk (the <see cref="BugReport.BugReportJsonl"/> idiom).
///
///     Deliberately GUID-free: the paste block is copied by hand into a public Discord channel,
///     so nothing in the line may carry data that must not travel there (spec §6.3's closed
///     vocabulary, applied to the manual mode too).
/// </summary>
public static class FirstDeathObituaryJsonl
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>The canonical serialized name for each dispatch decision.</summary>
    public static string DispatchName(FirstDeathObituaryDispatch dispatch)
    {
        return dispatch switch
        {
            FirstDeathObituaryDispatch.WebhookEmpty => "webhook-empty",
            FirstDeathObituaryDispatch.BelowPlayerGate => "below-player-gate",
            FirstDeathObituaryDispatch.RateLimited => "rate-limited",
            _ => "dispatched",
        };
    }

    /// <summary>Serializes one obituary + decision to a single JSON line (no trailing newline).</summary>
    public static string ToLine(FirstDeathObituary obituary, FirstDeathObituaryDispatch dispatch)
    {
        var line = new FirstDeathObituaryJsonLine
        {
            // Round-trippable, sortable, unambiguous timezone -> ISO 8601 ("O").
            Timestamp = obituary.ComposedAtUtc.ToString("O"),
            RoundId = obituary.RoundId,
            CharacterName = obituary.CharacterName,
            Title = obituary.Title,
            Tours = obituary.Tours,
            Cause = obituary.Cause.ToString().ToUpperInvariant(),
            CauseLabel = FirstDeathCopy.CauseLabelFor(obituary.Cause),
            PlayerCountAtDeath = obituary.PlayerCountAtDeath,
            Dispatch = DispatchName(dispatch),
            Paste = FirstDeathDiscordPayload.BuildTitle(obituary)
                    + "\n" + FirstDeathDiscordPayload.BuildBody(obituary)
                    + "\n" + FirstDeathDiscordPayload.FooterText,
        };

        return JsonSerializer.Serialize(line, Options);
    }
}
