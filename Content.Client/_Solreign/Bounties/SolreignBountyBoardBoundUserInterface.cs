using Content.Client._Solreign.Bounties.UI;
using Content.Shared._Solreign.Bounties;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Solreign.Bounties;

/// <summary>
///     Client half of the Liability Board (Contracts Board / Directive Terminal idiom): create
///     the window on open, translate its one button press into the one BUI message, re-render on
///     every state push. All rules (rate limits, GUID resolution, verdict judging) live
///     server/daemon-side — this window is a dumb, cheerful terminal.
/// </summary>
[UsedImplicitly]
public sealed class SolreignBountyBoardBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SolreignBountyBoardWindow? _menu;

    public SolreignBountyBoardBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<SolreignBountyBoardWindow>();

        _menu.OnClaimSubmitted += text => SendMessage(new SolreignBountyClaimMessage(text));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not SolreignBountyUiState boardState)
            return;

        _menu?.UpdateState(boardState.Bounties, boardState.Status, boardState.Offline);
    }
}
