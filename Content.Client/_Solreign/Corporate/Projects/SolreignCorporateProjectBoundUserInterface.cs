using Content.Client._Solreign.Corporate.Projects.UI;
using Content.Shared._Solreign.Corporate.Projects;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.Client._Solreign.Corporate.Projects;

public sealed class SolreignCorporateProjectBoundUserInterface : BoundUserInterface
{
    private SolreignCorporateProjectWindow? _window;

    public SolreignCorporateProjectBoundUserInterface(EntityUid owner, Enum uiKey)
        : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = new SolreignCorporateProjectWindow();
        _window.OnClose += Close;
        _window.OnContributePressed += (projectId, amount) =>
        {
            SendMessage(new SolreignCorporateProjectContributeMessage(projectId, amount));
        };
        _window.OnProjectSelected += (projectId) =>
        {
            SendMessage(new SolreignCorporateProjectSelectMessage(projectId));
        };

        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is SolreignCorporateProjectUiState projectState && _window != null)
        {
            _window.UpdateState(projectState);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _window?.Close();
            _window = null;
        }
    }
}
