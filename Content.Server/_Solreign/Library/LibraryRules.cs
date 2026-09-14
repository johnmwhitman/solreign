namespace Content.Server._Solreign.Library;

/// <summary>
///     Pure, unit-testable STATION-LIBRARY input shaping and the closed set of length numbers
///     (this lane's design brief §5). No ECS, no I/O, kept pure so <c>LibraryRulesTests</c> can
///     exercise every branch without spinning up the game — the <c>NoticeboardRules</c> idiom.
/// </summary>
public static class LibraryRules
{
    /// <summary>
    ///     Generous but sane: long enough for a genuine title ("Sonnets for Compliance: A
    ///     Chapbook" is 32 characters), short enough that it never wraps past a book's own
    ///     name/description UI treatment.
    /// </summary>
    public const int MaxTitleLength = 100;

    /// <summary>
    ///     "Books are books" — generous, not the 260-character Noticeboard note cap. Picked well
    ///     under <c>PaperComponent</c>'s own default <c>ContentSize</c> (10000) and the
    ///     <c>BookBase</c> prototype's explicit <c>contentSize: 12000</c> (Resources/Prototypes/
    ///     Entities/Objects/Misc/books.yml) so a submitted work always fits comfortably inside the
    ///     existing Paper reading UI it materializes into, with headroom — roughly 1000 words,
    ///     enough for a genuine short story or chapbook without approaching either ceiling.
    /// </summary>
    public const int MaxBodyLength = 6000;

    /// <summary>
    ///     Trims a raw title and rejects it if that leaves nothing. Truncates rather than rejects
    ///     when over-length (the Noticeboard/Bounty idiom: a slightly-over-limit honest submission
    ///     should still reach the daemon) — the client's own live char-count clamp makes truncation
    ///     a rare, modified-client-only path in practice.
    /// </summary>
    public static bool TrySanitizeTitle(string? raw, out string sanitized)
    {
        // A modified client can put a null Title on the wire; treat it as blank rather than
        // NRE-ing the game thread (the NoticeboardRules precedent).
        sanitized = (raw ?? string.Empty).Trim();
        if (sanitized.Length == 0)
            return false;

        if (sanitized.Length > MaxTitleLength)
            sanitized = sanitized[..MaxTitleLength];

        return true;
    }

    /// <summary>Same shape as <see cref="TrySanitizeTitle"/>, capped at <see cref="MaxBodyLength"/> instead.</summary>
    public static bool TrySanitizeBody(string? raw, out string sanitized)
    {
        sanitized = (raw ?? string.Empty).Trim();
        if (sanitized.Length == 0)
            return false;

        if (sanitized.Length > MaxBodyLength)
            sanitized = sanitized[..MaxBodyLength];

        return true;
    }
}
