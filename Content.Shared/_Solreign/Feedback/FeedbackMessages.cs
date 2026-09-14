using System;
using Robust.Shared.Serialization;

namespace Content.Shared._Solreign.Feedback;

/// <summary>
///     Roadmap D2.1 (docs/ROADMAP-PLAYER-DELIGHT.md): the structured feedback category a player picks
///     in the "feedback" window before typing free text. Kept intentionally small and non-hierarchical —
///     this is a triage tag for whoever reads data/feedback/*.jsonl, not a taxonomy.
/// </summary>
[Serializable, NetSerializable]
public enum FeedbackCategory : byte
{
    Bug = 0,
    Idea = 1,
    Balance = 2,
    Fun = 3,
    Confusion = 4,
}

/// <summary>
///     Client -&gt; server: submit one structured feedback report from the "feedback" window
///     (<see cref="FeedbackCategory"/> + free text). Deliberately a plain networked event rather than a
///     BoundUserInterface message — the window has no owning world entity (it opens straight from the
///     "feedback" console command, mirroring <c>CreditsCommand</c>/<c>OpenAHelpCommand</c>) and the
///     submission is fire-and-forget, not live synced state, so the
///     <see cref="Content.Server.CharacterInfo.CharacterInfoSystem"/> request/response idiom
///     (SubscribeNetworkEvent + RaiseNetworkEvent back to the sender) is the simpler fit.
/// </summary>
[Serializable, NetSerializable]
public sealed class SubmitFeedbackEvent : EntityEventArgs
{
    public readonly FeedbackCategory Category;
    public readonly string Text;

    public SubmitFeedbackEvent(FeedbackCategory category, string text)
    {
        Category = category;
        Text = text;
    }
}

/// <summary>
///     Server -&gt; client: the outcome of a <see cref="SubmitFeedbackEvent"/>. On success, carries the
///     reference ID the player can quote later (roadmap D2.1: "Return a reference ID"). On failure
///     (empty text, or the per-round rate limit — roadmap: rate-limit submissions per player per round),
///     <see cref="Success"/> is false and <see cref="ErrorMessage"/> is a pre-localized string safe to
///     show directly in the window.
/// </summary>
[Serializable, NetSerializable]
public sealed class FeedbackSubmittedEvent : EntityEventArgs
{
    public readonly bool Success;
    public readonly string? ReferenceId;
    public readonly string? ErrorMessage;

    public FeedbackSubmittedEvent(bool success, string? referenceId, string? errorMessage)
    {
        Success = success;
        ReferenceId = referenceId;
        ErrorMessage = errorMessage;
    }
}
