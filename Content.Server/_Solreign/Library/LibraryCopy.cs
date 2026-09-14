namespace Content.Server._Solreign.Library;

/// <summary>
///     Loc-key lookups for STATION-LIBRARY player-facing copy (library.ftl) — the
///     <c>NoticeboardCopy</c>/<c>MarkCopy</c> idiom of keeping every player-facing string behind a
///     named key, never an inline literal.
/// </summary>
public static class LibraryCopy
{
    public const string SubmitBusyKey = "solreign-library-submit-busy-popup";

    /// <summary>The honest "daemon is not answering" UX — channel unready or the classifier round
    /// trip itself failed/timed out/was unverifiable (the Noticeboard <c>NotAcceptingKey</c> idiom).</summary>
    public const string NotAcceptingKey = "solreign-library-not-accepting-popup";

    /// <summary>The classifier declined the submission. Generic, private, never echoes the
    /// submitted title/body and never states the specific reason.</summary>
    public const string WithheldKey = "solreign-library-submit-withheld-popup";

    public const string SuccessKey = "solreign-library-submit-success-popup";

    public const string QuotaKey = "solreign-library-quota-popup";

    public const string ReportedKey = "solreign-library-report-confirm-popup";

    public const string VerbSubmitKey = "solreign-library-verb-submit";

    public const string VerbReportKey = "solreign-library-verb-report";

    /// <summary>
    ///     One entry per PROVIDENCE-seeded corpus work (this lane's design brief §4): a title key,
    ///     body key, and in-fiction byline key. Seeded at most once EVER per archive
    ///     (<see cref="SolreignLibrarySystem"/> checks <c>GetProvidenceLibraryWorkCountAsync</c>
    ///     before ever firing one of these) — unlike Noticeboard's per-round reseed, a library seed
    ///     is a permanent ledger row exactly like a player submission, just authored in-house. Every
    ///     seed still passes the SAME fail-closed classifier round trip as a player work (defense in
    ///     depth, the Noticeboard <c>ProvidenceSeedKeys</c> precedent) — authoring them in-house does
    ///     not exempt them.
    /// </summary>
    public static readonly (string TitleKey, string BodyKey, string AuthorKey)[] SeedWorks =
    {
        ("solreign-library-seed-handbook-title", "solreign-lore-handbook-page-3", "solreign-library-seed-author-hr"),
        ("solreign-library-seed-safety-title", "solreign-library-seed-safety-body", "solreign-library-seed-author-safety"),
        ("solreign-library-seed-poetry-title", "solreign-library-seed-poetry-body", "solreign-library-seed-author-compliance"),
        ("solreign-library-seed-opening-title", "solreign-library-seed-opening-body", "solreign-library-seed-author-command"),
    };
}
