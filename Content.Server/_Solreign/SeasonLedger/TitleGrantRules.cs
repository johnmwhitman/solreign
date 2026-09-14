using System;
using System.Linq;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     Pure, unit-testable policy for ADMIN-GRANTED Season Ledger titles (community rewards program —
///     the website /rewards page hands out real in-game titles). No ECS, no I/O.
///
///     A granted title renders inside examine markup shown to every player, so the input policy is
///     fail-closed: printable ASCII only, no RobustToolbox rich-text metacharacters (<c>[</c>, <c>]</c>,
///     the <c>\</c> escape), no control characters, and a hard length cap. Anything outside the policy is
///     REJECTED with a reason rather than silently rewritten — the granting admin sees exactly what will
///     be stored. The only normalization applied is whitespace collapse (trim + runs fold to one space).
/// </summary>
public static class TitleGrantRules
{
    /// <summary>Hard cap on a granted title's length AFTER whitespace normalization.</summary>
    public const int MaxLength = 48;

    /// <summary>
    ///     Validates and normalizes raw admin console input into a storable title. Returns false with a
    ///     human-readable <paramref name="problem"/> (safe to echo to the console) when the input violates
    ///     the policy; on success <paramref name="title"/> is the exact string that will be persisted and
    ///     rendered in examine text.
    /// </summary>
    public static bool TryNormalize(string? raw, out string title, out string problem)
    {
        title = string.Empty;
        problem = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            problem = "Title text must not be empty.";
            return false;
        }

        // Trim and fold internal whitespace runs (including tabs/newlines) to single spaces.
        var normalized = string.Join(' ', raw.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries));

        foreach (var character in normalized)
        {
            // Printable ASCII only: rejects control characters outright and keeps the examine surface to
            // glyphs every client font renders. '[' / ']' / '\' are RobustToolbox rich-text metacharacters.
            if (character is < ' ' or > '~')
            {
                problem = "Title text must be printable ASCII (no control characters, no non-ASCII symbols).";
                return false;
            }

            if (character is '[' or ']' or '\\')
            {
                problem = "Title text must not contain markup characters ('[', ']' or '\\').";
                return false;
            }
        }

        if (normalized.Length > MaxLength)
        {
            problem = $"Title text must be at most {MaxLength} characters (got {normalized.Length}).";
            return false;
        }

        title = normalized;
        return true;
    }

    /// <summary>
    ///     Display fold: an active admin-granted title MASKS the earned title on examine; with no grant the
    ///     earned title shows unchanged. Earned progression (ceremonies, HR bonuses, announced-title
    ///     records) is intentionally NOT this function's business — it keeps running underneath the mask.
    /// </summary>
    public static string ResolveDisplayTitle(string earnedTitle, string? grantedTitle) =>
        string.IsNullOrEmpty(grantedTitle) ? earnedTitle : grantedTitle;

    /// <summary>Paper-trail line for a grant — who granted what to whom. Pure so the copy is testable.</summary>
    public static string FormatGrantAudit(string grantedBy, string targetName, Guid target, string title) =>
        $"Season Ledger title grant: {grantedBy} granted \"{title}\" to {targetName} ({target}).";

    /// <summary>Paper-trail line for a revocation. Pure so the copy is testable.</summary>
    public static string FormatRevokeAudit(string revokedBy, string targetName, Guid target) =>
        $"Season Ledger title grant revoked: {revokedBy} revoked the granted title of {targetName} ({target}).";
}
