using System.Collections.Generic;

namespace Content.Server._Solreign.Bounties;

/// <summary>
///     Pure, unit-testable Liability Board input/output shaping: no ECS, no I/O, kept pure so
///     <c>BountyClaimRulesTests</c> can exercise every branch without spinning up the game (the
///     <c>ContractRules</c> precedent). Two directions of untrusted data cross this boundary:
///     the claim text a player types (bounded, trusted-once-sanitized before it reaches the
///     signed <c>/api/bounty/claim</c> POST) and the bounty listings the daemon's UNSIGNED public
///     <c>/api/public/bounties</c> GET returns (display data only — see
///     <see cref="SolreignBountySystem"/>'s doc comment for why that endpoint skips
///     <c>DirectorChannel.VerifyResponse</c>).
/// </summary>
public static class BountyClaimRules
{
    /// <summary>Hard cap on a submitted claim's length, enforced server-side regardless of the client's own clamp.</summary>
    public const int MaxClaimTextLength = 300;

    /// <summary>Hard cap on the daemon's claim-ack <c>msg</c> before it's shown to the claimant.</summary>
    public const int MaxStatusMessageLength = 200;

    /// <summary>Hard cap on how many rows of the daemon's public bounty list are ever rendered.</summary>
    public const int MaxListingCount = 50;

    /// <summary>Hard cap on a single listing's title, independent of whatever the daemon sent.</summary>
    public const int MaxListingTitleLength = 80;

    /// <summary>Hard cap on a single listing's description, independent of whatever the daemon sent.</summary>
    public const int MaxListingDescriptionLength = 300;

    /// <summary>
    ///     Trims a raw claim submission and rejects it if that leaves nothing. Truncates rather
    ///     than rejects when over-length (the Oracle petition idiom: a slightly-over-limit honest
    ///     claim should still reach the daemon), so the only hard rejection is blank/whitespace.
    /// </summary>
    public static bool TrySanitizeClaimText(string? raw, out string sanitized)
    {
        // grk front-door review finding 2: a modified client can put a null ClaimText on the
        // wire; treat it as blank rather than NRE-ing the game thread.
        sanitized = (raw ?? string.Empty).Trim();
        if (sanitized.Length == 0)
            return false;

        if (sanitized.Length > MaxClaimTextLength)
            sanitized = sanitized[..MaxClaimTextLength];

        return true;
    }

    /// <summary>Clamps the daemon's claim-ack message to <see cref="MaxStatusMessageLength"/> before it's popped up to the claimant.</summary>
    public static string ClampStatusMessage(string raw)
    {
        return raw.Length > MaxStatusMessageLength ? raw[..MaxStatusMessageLength] : raw;
    }

    /// <summary>
    ///     ALIVENESS P0 #1: the in-fiction claim-failure popup loc keys (bounties.ftl), one picked
    ///     per delivered popup so repeated failures don't feel like a canned error dialog — the
    ///     exact <c>SolreignOracleSystem.OracleFailurePopupLocKeys</c> idiom. Player-facing text
    ///     only ever comes from these keys; the raw HTTP status/exception stays server-log-only.
    /// </summary>
    public static readonly string[] ClaimFailurePopupLocKeys =
    {
        "solreign-bounties-claim-failure-popup-hums",
        "solreign-bounties-claim-failure-popup-static",
        "solreign-bounties-claim-failure-popup-quiet",
    };

    /// <summary>
    ///     Maps an arbitrary random roll onto one of <see cref="ClaimFailurePopupLocKeys"/> —
    ///     total for any int (negative rolls included), so a caller can never index out of range.
    ///     Pure so <c>BountyClaimRulesTests</c> can pin the key set and the bounds behavior.
    /// </summary>
    public static string PickClaimFailureLocKey(int roll)
    {
        var index = roll % ClaimFailurePopupLocKeys.Length;
        if (index < 0)
            index += ClaimFailurePopupLocKeys.Length;
        return ClaimFailurePopupLocKeys[index];
    }

    /// <summary>
    ///     ALIVENESS P0 #2: the offline-vs-empty decision seam. A FAILED fetch (null
    ///     <paramref name="fetched"/>) yields an OFFLINE state with zero rows — never the
    ///     true-empty state — while a successful fetch (even an empty one) is never offline.
    ///     Kept pure so the "offline and genuinely-empty are never the same state" invariant is
    ///     directly unit-testable without HTTP.
    /// </summary>
    public static (List<T> Listings, bool Offline) ShapeBoardListings<T>(List<T>? fetched)
    {
        return fetched is null ? (new List<T>(), true) : (fetched, false);
    }

    /// <summary>
    ///     Sanitizes the daemon's unsigned public bounty list: caps the row count, drops any row
    ///     with a blank title (a malformed/failed entry, not worth rendering), and clamps every
    ///     surviving row's title/description independently of whatever length the daemon sent.
    ///     <paramref name="rawTitles"/>/<paramref name="rawDescriptions"/> are parallel to
    ///     <paramref name="ids"/> — kept as primitive lists (rather than a DTO type here) so this
    ///     stays pure and testable without pulling in the JSON deserialization types.
    /// </summary>
    public static List<(int Id, string Title, string Description)> SanitizeListings(
        IReadOnlyList<int> ids, IReadOnlyList<string?> rawTitles, IReadOnlyList<string?> rawDescriptions)
    {
        var result = new List<(int, string, string)>();

        var count = ids.Count;
        for (var i = 0; i < count && result.Count < MaxListingCount; i++)
        {
            var title = rawTitles[i];
            if (string.IsNullOrWhiteSpace(title))
                continue;

            if (title.Length > MaxListingTitleLength)
                title = title[..MaxListingTitleLength];

            var description = rawDescriptions[i] ?? string.Empty;
            if (description.Length > MaxListingDescriptionLength)
                description = description[..MaxListingDescriptionLength];

            result.Add((ids[i], title, description));
        }

        return result;
    }
}
