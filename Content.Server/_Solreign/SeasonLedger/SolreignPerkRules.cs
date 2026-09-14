using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
/// Pure, deterministic rules for the cosmetic Director-backed GoldenName perk.
/// </summary>
public static class SolreignPerkRules
{
    private const string ChannelPrefix = "perks:";

    public static string ApplyGoldenName(string? title, bool enabled)
    {
        var normalized = string.IsNullOrWhiteSpace(title) ? "Name" : title.Trim();
        if (!enabled || normalized.StartsWith("Golden ", StringComparison.Ordinal))
            return normalized;

        return $"Golden {normalized}";
    }

    /// <summary>
    /// The private account key is carried only in the signed POST body, never in a URL.
    /// </summary>
    public static string BuildRequestBody(Guid playerId) =>
        JsonSerializer.Serialize(new PerksRequest(playerId));

    /// <summary>
    /// Per-account isolation prevents a join burst from silently dropping every lookup after the
    /// first while retaining DirectorChannel's retry-spam protection for the same account.
    /// </summary>
    public static string RateLimitChannel(Guid playerId) => $"{ChannelPrefix}{playerId:D}";

    /// <summary>
    /// Async responses may only affect the account that originated the signed lookup.
    /// </summary>
    public static bool IsStillOwnedByExpectedUser(Guid expectedUserId, Guid currentUserId) =>
        expectedUserId == currentUserId;

    private readonly record struct PerksRequest(
        [property: JsonPropertyName("player_id")] Guid PlayerId);
}
