namespace Content.Server._Solreign.Noticeboards;

/// <summary>
///     Loc-key lookups for Noticeboard player-facing copy (noticeboards.ftl) — the
///     <c>BountyClaimRules</c>/<c>MarkCopy</c> idiom of keeping every player-facing string behind
///     a named key, never an inline literal, so the closed vocabulary is testable and localizable.
/// </summary>
public static class NoticeboardCopy
{
    public const string SubmitBusyKey = "solreign-noticeboards-post-busy-popup";

    /// <summary>Spec rule 2: the honest "daemon is not answering" UX — channel unready or the
    /// classifier round-trip itself failed/timed out/was unverifiable. Never the generic
    /// Oracle-style in-fiction static/hum flavor: this is a plain operational fact, not lore.</summary>
    public const string NotAcceptingKey = "solreign-noticeboards-not-accepting-popup";

    /// <summary>Spec rule 2: the classifier declined the note. Generic, private, never echoes the
    /// submitted text and never states the specific reason.</summary>
    public const string WithheldKey = "solreign-noticeboards-post-withheld-popup";

    public const string SuccessKey = "solreign-noticeboards-post-success-popup";

    public const string QuotaKey = "solreign-noticeboards-quota-popup";

    public const string CooldownKey = "solreign-noticeboards-cooldown-popup";

    public const string BoardFullKey = "solreign-noticeboards-full-popup";

    public const string ReportedKey = "solreign-noticeboards-report-confirm-popup";

    /// <summary>The in-character author name PROVIDENCE-authored rows display (spec rule 5: a
    /// clearly labeled system-authored marker).</summary>
    public const string ProvidenceAuthorKey = "solreign-noticeboards-providence-author";

    /// <summary>
    ///     Static, pre-authored seed lines for a freshly-spawned board (C1 council ask: boards
    ///     ship seeded so they never look empty). Every seed line still passes the SAME
    ///     fail-closed classifier round-trip as any player note (spec rule 5: "PROVIDENCE notices
    ///     pass the same fail-closed gate under their own surface budget") — authoring them
    ///     in-house does not exempt them, defense in depth. None reference a specific player,
    ///     report, moderation decision, or hidden note (spec rule 5's hard prohibition).
    /// </summary>
    public static readonly string[] ProvidenceSeedKeys =
    {
        "solreign-noticeboards-seed-welcome",
        "solreign-noticeboards-seed-reminder",
    };

    /// <summary>
    ///     Maps an arbitrary roll onto one of <see cref="ProvidenceSeedKeys"/> — total for any
    ///     int, so a caller can never index out of range. Pure so
    ///     <c>NoticeboardCopyTests</c> can pin the bounds behavior (the
    ///     <c>BountyClaimRules.PickClaimFailureLocKey</c> idiom).
    /// </summary>
    public static string PickProvidenceSeedKey(int roll)
    {
        var index = roll % ProvidenceSeedKeys.Length;
        if (index < 0)
            index += ProvidenceSeedKeys.Length;
        return ProvidenceSeedKeys[index];
    }
}
