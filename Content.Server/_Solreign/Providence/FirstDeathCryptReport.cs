using System;
using System.Text.Json.Serialization;

namespace Content.Server._Solreign.Providence;

/// <summary>
///     The additive first-death extension of the crypt death-report wire payload (FD-W3,
///     docs/specs/FIRST-DEATH-SPEC-2026-07-16-DRAFT.md §5.1) — the exact JSON the daemon's
///     <c>CryptDeathEvent</c> pydantic model parses on <c>POST /api/crypt/death</c>. Pure data +
///     one pure builder, directly unit-tested to the serialized byte
///     (Content.Tests/_Solreign/FirstDeathCryptReportTests.cs): the wire contract IS the test.
///
///     Version-skew law (spec §5.1): every field beyond the legacy pair
///     (<c>victim_guid</c>/<c>attacker_guid</c>) is additive with daemon-side defaults, so an OLD
///     daemon receiving this payload ignores the extras and runs its legacy path, and an old game
///     against a new daemon sends none of them — both skews degrade to today's behavior.
///
///     Redaction law (spec §3.3): <c>attacker_guid</c> is ALWAYS empty on this payload — the
///     first-death surface never carries attacker data (the T+0 telemetry report from
///     <c>SolreignDeathTelemetrySystem</c> already reported attribution for the daemon's own
///     nemesis bookkeeping; the authored memorial does not).
/// </summary>
public sealed record FirstDeathCryptReport
{
    [JsonPropertyName("victim_guid")]
    public string VictimGuid { get; init; } = string.Empty;

    [JsonPropertyName("attacker_guid")]
    public string AttackerGuid { get; init; } = string.Empty;

    [JsonPropertyName("first_death")]
    public bool FirstDeath { get; init; } = true;

    [JsonPropertyName("character_name")]
    public string CharacterName { get; init; } = string.Empty;

    [JsonPropertyName("epitaph")]
    public string Epitaph { get; init; } = string.Empty;

    [JsonPropertyName("cause_label")]
    public string CauseLabel { get; init; } = string.Empty;

    [JsonPropertyName("tours")]
    public int Tours { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>
    ///     Composes the report from the claimed scene's snapshot — the same values
    ///     <c>ProvidenceFirstDeathSystem.ClaimAndSchedule</c> just persisted into the claim row,
    ///     so the daemon's memorial and the ledger's record can never disagree.
    /// </summary>
    public static FirstDeathCryptReport Build(
        Guid victim,
        string characterName,
        string epitaphText,
        FirstDeathCause cause,
        int tours,
        string title)
    {
        return new FirstDeathCryptReport
        {
            VictimGuid = victim.ToString(),
            AttackerGuid = string.Empty,
            FirstDeath = true,
            CharacterName = characterName,
            Epitaph = epitaphText,
            CauseLabel = FirstDeathCopy.CauseLabelFor(cause),
            Tours = tours,
            Title = title,
        };
    }
}
