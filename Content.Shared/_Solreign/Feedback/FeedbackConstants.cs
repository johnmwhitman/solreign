namespace Content.Shared._Solreign.Feedback;

/// <summary>
///     Shared limits for roadmap D2.1 structured feedback, referenced by both the server-side
///     <c>FeedbackSystem</c> (Content.Server) -- which enforces them -- and the client-side
///     <c>FeedbackWindow</c> (Content.Client) -- which shows them to the player as it types, so the
///     two never drift out of sync.
/// </summary>
public static class FeedbackConstants
{
    /// <summary>Free-text hard cap (roadmap D2.1 "input limits").</summary>
    public const int MaxTextLength = 2000;

    /// <summary>Per-player, per-round submission cap (roadmap D2.1 "rate limits").</summary>
    public const int MaxSubmissionsPerRound = 5;
}
