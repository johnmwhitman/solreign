using Content.Client._Solreign.Noticeboards.UI;
using Content.Shared._Solreign.Noticeboards;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Solreign.Noticeboards;

/// <summary>
///     Client half of the crew Noticeboard (the Liability Board idiom): create the window on
///     open, translate its post/report actions into the two BUI messages, re-render on every
///     state push. All rules (quota, cooldown, capacity, the classifier gate) live server/daemon
///     side — this window is a dumb, plain bulletin board.
/// </summary>
[UsedImplicitly]
public sealed class SolreignNoticeboardBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SolreignNoticeboardWindow? _menu;

    public SolreignNoticeboardBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<SolreignNoticeboardWindow>();

        _menu.OnNoteSubmitted += text => SendMessage(new SolreignNoticeboardPostMessage(text));
        _menu.OnNoteReported += noteId => SendMessage(new SolreignNoticeboardReportMessage(noteId));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not SolreignNoticeboardUiState boardState)
            return;

        _menu?.UpdateState(boardState.Notes, boardState.Status, boardState.Offline);
    }
}
