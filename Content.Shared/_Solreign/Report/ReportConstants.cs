namespace Content.Shared._Solreign.Report;

/// <summary>
///     Shared limits for the "report a player" quick-action, referenced by both the server-side
///     <c>ReportSystem</c> (Content.Server) -- which enforces them -- and the client-side
///     <c>ReportWindow</c> (Content.Client) -- which shows them to the player as it types, so the two
///     never drift out of sync. Mirrors <c>Content.Shared._Solreign.Feedback.FeedbackConstants</c>, but
///     both caps are tighter: a conduct report is meant to be a quick flag for admins, not an essay, and
///     the per-round budget is small enough to discourage using it to spam/harass a target while still
///     covering a legitimate handful of incidents in one round.
/// </summary>
public static class ReportConstants
{
    /// <summary>Free-text reason hard cap. Short by design -- this is a triage tag, not a case file.</summary>
    public const int MaxTextLength = 300;

    /// <summary>Target-name field hard cap (a display name, never free text).</summary>
    public const int MaxTargetLength = 64;

    /// <summary>Per-player, per-round submission cap. Deliberately small -- anti-abuse, not a workflow limit.</summary>
    public const int MaxSubmissionsPerRound = 3;
}
