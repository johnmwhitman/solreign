using Content.Client._Solreign.Contracts.UI;
using Content.Shared._Solreign.Contracts;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Solreign.Contracts;

/// <summary>
///     Client half of the Solreign Contracts Board (spec Milestone 2, "the board UI"). Copies the upstream
///     <c>CargoBountyConsoleBoundUserInterface</c> idiom: create the window on open, translate its button
///     events into the Milestone 1 BUI messages, re-render on every state push. All rules (claim caps, rank
///     gates, skip cooldowns, roster limits) live server-side — this window is a dumb, cheerful terminal.
/// </summary>
[UsedImplicitly]
public sealed class SolreignContractsBoardBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SolreignContractsBoardMenu? _menu;

    public SolreignContractsBoardBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<SolreignContractsBoardMenu>();

        _menu.OnClaimPressed += id => SendMessage(new SolreignContractClaimMessage(id));
        _menu.OnSkipPressed += id => SendMessage(new SolreignContractSkipMessage(id));
        _menu.OnJoinPressed += id => SendMessage(new SolreignRaidJoinMessage(id));
        _menu.OnLaunchPressed += id => SendMessage(new SolreignRaidLaunchMessage(id));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not SolreignContractsBoardState boardState)
            return;

        _menu?.UpdateEntries(boardState.Contracts, boardState.UntilNextSkip);
    }
}
