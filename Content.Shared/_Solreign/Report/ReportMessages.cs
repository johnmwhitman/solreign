using System;
using Robust.Shared.Serialization;

namespace Content.Shared._Solreign.Report;

/// <summary>
///     The conduct category a player picks in the "report" window before naming a target and writing a
///     short reason. Deliberately small and non-hierarchical -- this is a triage tag for whoever reads
///     data/reports/*.jsonl, not a taxonomy. Distinct from <c>Content.Shared._Solreign.Feedback.FeedbackCategory</c>:
///     feedback is about the game (bug/idea/balance/fun/confusion), this is about a player's conduct.
/// </summary>
[Serializable, NetSerializable]
public enum ReportCategory : byte
{
    Griefing = 0,
    Harassment = 1,
    Rules = 2,
}

/// <summary>
///     Client -&gt; server: file one conduct report from the "report" window (target name + category +
///     short reason). Plain networked event rather than a BoundUserInterface message -- same reasoning
///     as <c>Content.Shared._Solreign.Feedback.SubmitFeedbackEvent</c>: the window has no owning world
///     entity (it opens from the "report" console command) and the submission is fire-and-forget.
///
///     This is explicitly NOT ahelp/bwoink -- it does not open a live conversation with staff. It is a
///     fast, low-friction "flag this and move on" path that lands in an admin-visible log (and pings
///     online admins), so a player who just got griefed doesn't have to context-switch into a full ahelp
///     thread to get it on record. Players who need urgent, two-way help are still pointed at ahelp.
/// </summary>
[Serializable, NetSerializable]
public sealed class SubmitReportEvent : EntityEventArgs
{
    public readonly string Target;
    public readonly ReportCategory Category;
    public readonly string Text;

    public SubmitReportEvent(string target, ReportCategory category, string text)
    {
        Target = target;
        Category = category;
        Text = text;
    }
}

/// <summary>
///     Server -&gt; client: the outcome of a <see cref="SubmitReportEvent"/>. On success, carries the
///     reference ID the player can quote later. On failure (empty target/text, self-report, or the
///     per-round rate limit), <see cref="Success"/> is false and <see cref="ErrorMessage"/> is a
///     pre-localized string safe to show directly in the window.
/// </summary>
[Serializable, NetSerializable]
public sealed class ReportSubmittedEvent : EntityEventArgs
{
    public readonly bool Success;
    public readonly string? ReferenceId;
    public readonly string? ErrorMessage;

    public ReportSubmittedEvent(bool success, string? referenceId, string? errorMessage)
    {
        Success = success;
        ReferenceId = referenceId;
        ErrorMessage = errorMessage;
    }
}
