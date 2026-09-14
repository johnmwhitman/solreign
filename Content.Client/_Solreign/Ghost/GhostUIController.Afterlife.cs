using Content.Client._Solreign.Ghost;
using Content.Shared._Solreign.Ghost;
using Content.Shared.Ghost.Systems;
using Robust.Client.UserInterface.Controllers;

namespace Content.Client.UserInterface.Systems.Ghost;

/// <summary>
/// Solreign — Afterlife Activities (roadmap D2.2).
/// Partial extension of the upstream <c>GhostUIController</c>: wires the "Afterlife"
/// button on <c>GhostGui</c> to the activity list and warps via the upstream
/// <c>GhostWarpToTargetRequestEvent</c> path (so warp validation/logging is reused).
/// </summary>
public sealed partial class GhostUIController : IOnSystemChanged<SolreignGhostActivitySystem>
{
    private SolreignGhostActivitySystem? _afterlifeSystem;

    public void OnSystemLoaded(SolreignGhostActivitySystem system)
    {
        _afterlifeSystem = system;
        system.ActivitiesResponse += OnActivitiesResponse;
    }

    public void OnSystemUnloaded(SolreignGhostActivitySystem system)
    {
        _afterlifeSystem = null;
        system.ActivitiesResponse -= OnActivitiesResponse;
    }

    private void OnActivitiesResponse(GhostActivitiesResponseEvent msg)
    {
        if (Gui?.AfterlifeWindow is not { } window)
            return;

        window.UpdateActivities(msg.Activities);
    }

    private void OnActivityClicked(NetEntity target)
    {
        // Reuse the upstream ghost warp path; the server validates the sender is a ghost.
        var msg = new GhostWarpToTargetRequestEvent(target);
        _net.SendSystemNetworkMessage(msg);
    }

    private void RequestAfterlife()
    {
        _afterlifeSystem?.RequestActivities();
        Gui?.AfterlifeWindow.OpenCentered();
    }

    private void LoadAfterlifeGui()
    {
        if (Gui == null)
            return;

        Gui.AfterlifePressed += RequestAfterlife;
        Gui.AfterlifeWindow.ActivityClicked += OnActivityClicked;
    }

    private void UnloadAfterlifeGui()
    {
        if (Gui == null)
            return;

        Gui.AfterlifePressed -= RequestAfterlife;
        Gui.AfterlifeWindow.ActivityClicked -= OnActivityClicked;
    }
}
