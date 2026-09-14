using Content.Client._Solreign.Report.UI;
using Content.Shared._Solreign.Report;

namespace Content.Client._Solreign.Report;

/// <summary>
///     Client half of the "report a player" quick-action. Owns the single <see cref="ReportWindow"/>
///     instance, sends <see cref="SubmitReportEvent"/> when the player presses Submit, and relays the
///     server's <see cref="ReportSubmittedEvent"/> reply (reference ID or error) back into the window.
///     Opened by <see cref="ReportCommand"/> ("report" console command) -- mirrors the plain
///     request/response idiom in <c>Content.Client._Solreign.Feedback.FeedbackClientSystem</c>, since
///     there is no owning world entity here and submission is one-shot, not live synced state.
/// </summary>
public sealed class ReportClientSystem : EntitySystem
{
    private ReportWindow? _window;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<ReportSubmittedEvent>(OnReportSubmitted);
    }

    /// <summary>Opens the report window, creating it on first use. <paramref name="prefillTarget"/> pre-fills the target field, if given.</summary>
    public void EnsureWindowOpen(string? prefillTarget = null)
    {
        if (_window == null)
        {
            _window = new ReportWindow();
            _window.OnSubmitPressed += OnWindowSubmit;
        }

        if (prefillTarget != null)
            _window.SetTarget(prefillTarget);

        if (_window.IsOpen)
        {
            _window.MoveToFront();
            return;
        }

        _window.OpenCentered();
    }

    private void OnWindowSubmit(string target, ReportCategory category, string text)
    {
        RaiseNetworkEvent(new SubmitReportEvent(target, category, text));
    }

    private void OnReportSubmitted(ReportSubmittedEvent msg, EntitySessionEventArgs args)
    {
        _window?.ShowResult(msg.Success, msg.ReferenceId, msg.ErrorMessage);
    }
}
