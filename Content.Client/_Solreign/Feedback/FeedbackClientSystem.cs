using Content.Client._Solreign.Feedback.UI;
using Content.Shared._Solreign.Feedback;

namespace Content.Client._Solreign.Feedback;

/// <summary>
///     Client half of roadmap D2.1's structured feedback window. Owns the single
///     <see cref="FeedbackWindow"/> instance, sends <see cref="SubmitFeedbackEvent"/> when the player
///     presses Submit, and relays the server's <see cref="FeedbackSubmittedEvent"/> reply (reference ID
///     or error) back into the window. Opened by <see cref="FeedbackCommand"/> ("feedback" console
///     command) — mirrors the plain request/response idiom in
///     <c>Content.Client.CharacterInfo.CharacterInfoSystem</c> rather than a BoundUserInterface, since
///     there is no owning world entity here.
/// </summary>
public sealed class FeedbackClientSystem : EntitySystem
{
    private FeedbackWindow? _window;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<FeedbackSubmittedEvent>(OnFeedbackSubmitted);
    }

    /// <summary>Opens the feedback window, creating it on first use.</summary>
    public void EnsureWindowOpen()
    {
        if (_window == null)
        {
            _window = new FeedbackWindow();
            _window.OnSubmitPressed += OnWindowSubmit;
        }

        if (_window.IsOpen)
        {
            _window.MoveToFront();
            return;
        }

        _window.OpenCentered();
    }

    private void OnWindowSubmit(FeedbackCategory category, string text)
    {
        RaiseNetworkEvent(new SubmitFeedbackEvent(category, text));
    }

    private void OnFeedbackSubmitted(FeedbackSubmittedEvent msg, EntitySessionEventArgs args)
    {
        _window?.ShowResult(msg.Success, msg.ReferenceId, msg.ErrorMessage);
    }
}
