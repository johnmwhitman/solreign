using System.Text;

namespace Content.Server._Solreign.Providence;

/// <summary>
///     Pure allowlist sanitizer for any player-authored string that reaches a PROVIDENCE
///     event-reactive line — named risk in PROVIDENCE-VOICE-DESIGN.md ("player names and chat are
///     attacker-controlled input... sanitize/allowlist any player-authored string that enters the
///     prompt, names especially"). Zero I/O, zero engine dependencies, directly unit-tested
///     (Content.Tests/_Solreign/ProvidenceNameSanitizerTests.cs).
///
///     ALLOWLIST, not escaping (the design doc's explicit instruction): every character in the input
///     is either kept (letters, digits, space, hyphen, apostrophe, period) or dropped outright.
///     Nothing is escaped, quoted, or re-encoded — there is no attempt to make a hostile character
///     "safe to display", it is simply never let through. This particularly matters for characters
///     that could be used to forge a fake PA message inside a real one (newlines to start a new-looking
///     announcement line, brackets/braces to imitate a tag like "[ADMIN]" or a template placeholder,
///     colons to imitate a "SYSTEM:" prefix) — all of those are outside the allowlist and are stripped.
///
///     This lane never calls an LLM (scripted-only, per the lane brief) — the sanitizer's job here is
///     narrower than the Oracle-path prompt-injection risk the design doc describes for a future LLM
///     wave. It is still worth being strict now: the allowlist this class enforces is a superset-safe
///     starting point that would also hold if a future wave threads reactive event context into an
///     LLM prompt.
///
///     Fails CLOSED: a name that sanitizes down to nothing (all-hostile input, or empty/whitespace to
///     start with) returns null, never an empty string or a placeholder — callers must treat null as
///     "skip this line entirely", per the lane's "never a broken/blank line" rail.
/// </summary>
public static class ProvidenceNameSanitizer
{
    /// <summary>Hard length cap after filtering — a PA line must never balloon because of an
    /// absurdly long character name.</summary>
    public const int MaxLength = 32;

    /// <summary>
    ///     Filters <paramref name="raw"/> down to an allowlisted, length-capped, whitespace-collapsed
    ///     string, or returns null if nothing usable survives. Allowed characters: Unicode
    ///     letters/digits (so non-English names are not needlessly mangled), space, hyphen, apostrophe,
    ///     period — closed set, everything else (including all control characters, newlines, brackets,
    ///     braces, colons, pipes, backticks, angle brackets) is dropped, not escaped.
    /// </summary>
    public static string? Sanitize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var builder = new StringBuilder(raw.Length > MaxLength ? MaxLength : raw.Length);
        foreach (var ch in raw)
        {
            if (builder.Length >= MaxLength)
                break;

            if (char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '\'' or '.')
                builder.Append(ch);
        }

        var collapsed = CollapseWhitespace(builder.ToString()).Trim();
        return collapsed.Length == 0 ? null : collapsed;
    }

    /// <summary>Collapses any run of whitespace (the allowlist only lets plain spaces through, but a
    /// name like "A     B" after stripping should not read as a formatting attack either) into a
    /// single space.</summary>
    private static string CollapseWhitespace(string s)
    {
        var builder = new StringBuilder(s.Length);
        var lastWasSpace = false;

        foreach (var ch in s)
        {
            if (ch == ' ')
            {
                if (lastWasSpace)
                    continue;

                lastWasSpace = true;
                builder.Append(ch);
            }
            else
            {
                lastWasSpace = false;
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }
}
