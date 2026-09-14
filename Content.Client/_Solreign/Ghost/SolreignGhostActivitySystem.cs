using Content.Shared._Solreign.Ghost;

namespace Content.Client._Solreign.Ghost;

/// <summary>
/// Solreign — Afterlife Activities (roadmap D2.2).
/// Client half of the afterlife activity list: raises the request and republishes
/// the response for <c>GhostUIController</c> (same shape as the upstream
/// ghost-warps flow in <c>Content.Client.Ghost.GhostSystem</c>).
/// </summary>
public sealed class SolreignGhostActivitySystem : EntitySystem
{
    public event Action<GhostActivitiesResponseEvent>? ActivitiesResponse;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<GhostActivitiesResponseEvent>(OnActivitiesResponse);
    }

    private void OnActivitiesResponse(GhostActivitiesResponseEvent msg, EntitySessionEventArgs args)
    {
        ActivitiesResponse?.Invoke(msg);
    }

    public void RequestActivities()
    {
        RaiseNetworkEvent(new GhostActivitiesRequestEvent());
    }
}
