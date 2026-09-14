using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Content.Shared._Solreign.VesselIdentity;

/// <summary>
///     Pure static helper class containing vessel name validation, profanity screening rules,
///     and cosmetic registry mark calculation logic for SR-W-043.
/// </summary>
public static class SolreignVesselIdentityMath
{
    /// <summary>Collapses any run of whitespace to a single space.</summary>
    /// <remarks>
    ///     <para>
    ///     DO NOT convert this back to <c>[GeneratedRegex]</c>. It was source-generated once, to
    ///     satisfy analyzer RA0026, and that shipped a client-side TOTAL OUTAGE: the regex source
    ///     generator emits a <c>RegexRunnerFactory</c> subclass annotated with
    ///     <c>GeneratedCodeAttribute</c>, and BOTH types are outside the RobustToolbox client
    ///     sandbox allowlist. Every connecting client failed <c>Content.Shared</c> type checks and
    ///     aborted before reaching the lobby.
    ///     </para>
    ///     <para>
    ///     Nothing server-side can catch that: the sandbox check runs only in the client, so the
    ///     full test battery, the YAML linter and the deploy boot-test were all green while no
    ///     human could connect. RA0026's concern (re-parsing the pattern per call) is already
    ///     answered by a static readonly Regex with RegexOptions.Compiled — same compile-once
    ///     behaviour, no forbidden types — which is the idiom ValidNameRegex below already uses.
    ///     </para>
    /// </remarks>
    private static readonly Regex WhitespaceRunRegex = new(@"\s+", RegexOptions.Compiled);

    private static readonly Regex ValidNameRegex = new(@"^[A-Za-z0-9][A-Za-z0-9\s'-]{1,30}[A-Za-z0-9]$", RegexOptions.Compiled);

    /// <summary>
    ///     Known blocklist of inappropriate terms, offensive words, or corporate policy violations.
    /// </summary>
    private static readonly HashSet<string> BlockedKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "moderator", "grief", "exploit", "nigger", "faggot", "retard", "cunt",
        "bitch", "whore", "bastard", "shit", "fuck", "nazi", "hitler", "syndicate_master",
        "nanotrasen_sucks", "solreign_sucks"
    };

    /// <summary>
    ///     Sanitizes and normalizes a candidate vessel name string (trimming whitespace and collapsing duplicate spaces).
    /// </summary>
    public static string SanitizeName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return string.Empty;

        var trimmed = rawName.Trim();
        var collapsed = WhitespaceRunRegex.Replace(trimmed, " ");
        return collapsed;
    }

    /// <summary>
    ///     Checks if a sanitized vessel name meets basic structural character and length rules.
    /// </summary>
    public static bool IsValidNameFormat(string candidateName)
    {
        if (string.IsNullOrEmpty(candidateName))
            return false;

        if (candidateName.Length < 3 || candidateName.Length > 32)
            return false;

        return ValidNameRegex.IsMatch(candidateName);
    }

    /// <summary>
    ///     Evaluates whether a candidate vessel name contains blocked, profane, or inappropriate keywords.
    /// </summary>
    public static bool ContainsBlockedKeywords(string candidateName)
    {
        if (string.IsNullOrEmpty(candidateName))
            return false;

        var normalized = candidateName.ToLowerInvariant();

        // 1. Direct word check
        var words = normalized.Split(new[] { ' ', '-', '\'' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            if (BlockedKeywords.Contains(word))
                return true;
        }

        // 2. Substring check for severe offensive tokens
        foreach (var blocked in BlockedKeywords)
        {
            if (blocked.Length >= 4 && normalized.Contains(blocked))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Evaluates full moderation clearance for a proposed vessel name.
    /// </summary>
    public static SolreignVesselModerationState EvaluateNameModeration(string candidateName)
    {
        var sanitized = SanitizeName(candidateName);

        if (!IsValidNameFormat(sanitized))
            return SolreignVesselModerationState.Flagged;

        if (ContainsBlockedKeywords(sanitized))
            return SolreignVesselModerationState.Flagged;

        return SolreignVesselModerationState.Approved;
    }

    /// <summary>
    ///     Calculates the appropriate cosmetic registry mark tier based on completed missions and total salvage value.
    /// </summary>
    public static SolreignVesselRegistryMark CalculateRegistryMark(int missionsCompleted, float totalSalvageValue)
    {
        if (missionsCompleted >= 100 || totalSalvageValue >= 1_000_000f)
            return SolreignVesselRegistryMark.CorporateChevrons;

        if (missionsCompleted >= 50 || totalSalvageValue >= 500_000f)
            return SolreignVesselRegistryMark.VeteranPennant;

        if (missionsCompleted >= 25 || totalSalvageValue >= 150_000f)
            return SolreignVesselRegistryMark.GoldEmblem;

        if (missionsCompleted >= 10 || totalSalvageValue >= 50_000f)
            return SolreignVesselRegistryMark.SilverInsignia;

        if (missionsCompleted >= 3 || totalSalvageValue >= 10_000f)
            return SolreignVesselRegistryMark.BronzeStripe;

        return SolreignVesselRegistryMark.Unmarked;
    }

    /// <summary>
    ///     Generates a human-readable title / examine description for a cosmetic registry mark.
    /// </summary>
    public static string GetRegistryMarkTitle(SolreignVesselRegistryMark mark)
    {
        return mark switch
        {
            SolreignVesselRegistryMark.CorporateChevrons => "Solreign Prime Fleet Chevron",
            SolreignVesselRegistryMark.VeteranPennant => "Veteran Fleet Pennant",
            SolreignVesselRegistryMark.GoldEmblem => "Gold Emblem Flagship",
            SolreignVesselRegistryMark.SilverInsignia => "Silver Star Escort",
            SolreignVesselRegistryMark.BronzeStripe => "Bronze Stripe Commendation",
            _ => "Standard Registry Craft"
        };
    }
}
