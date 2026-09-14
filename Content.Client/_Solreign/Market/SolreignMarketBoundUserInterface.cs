using Content.Client._Solreign.Market.UI;
using Content.Shared._Solreign.Market;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Solreign.Market;

/// <summary>
///     Client half of the "Requisitions Anonymous" black-market console. Copies the
///     <c>SolreignContractsBoardBoundUserInterface</c> idiom: create the window on open, translate
///     its one button-per-row event into the one BUI message, re-render on every state push. Every
///     rule (rate limits, Standing affordability, stock) lives server/daemon-side — this window is
///     a dumb, cheerful (if slightly ominous) terminal.
/// </summary>
[UsedImplicitly]
public sealed class SolreignMarketBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SolreignMarketMenu? _menu;

    public SolreignMarketBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<SolreignMarketMenu>();

        _menu.OnBuyPressed += id => SendMessage(new SolreignMarketBuyMessage(id));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not SolreignMarketUiState marketState)
            return;

        _menu?.UpdateEntries(marketState.Listings, marketState.StatusText);
    }
}
