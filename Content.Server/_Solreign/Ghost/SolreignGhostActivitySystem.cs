using Content.Shared._Solreign.Ghost;
using Content.Shared.Ghost.Components;

namespace Content.Server._Solreign.Ghost;

/// <summary>
/// Solreign — Afterlife Activities (roadmap D2.2).
/// Answers ghost requests for the list of afterlife activities
/// (<see cref="SolreignGhostActivityComponent"/> entities placed by mappers).
/// Warping to an activity is handled by the upstream <c>GhostSystem</c> via
/// <c>GhostWarpToTargetRequestEvent</c>, so this system only serves the list.
/// </summary>
public sealed class SolreignGhostActivitySystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<GhostActivitiesRequestEvent>(OnActivitiesRequest);
    }

    private void OnActivitiesRequest(GhostActivitiesRequestEvent msg, EntitySessionEventArgs args)
    {
        // Same gate as upstream GhostSystem.CanGhostWarp: only ghosts get the list.
        if (args.SenderSession.AttachedEntity is not { } player || !HasComp<GhostComponent>(player))
        {
            Log.Warning($"User {args.SenderSession.Name} sent a {nameof(GhostActivitiesRequestEvent)} without being a ghost.");
            return;
        }

        var response = new GhostActivitiesResponseEvent(GetActivities());
        RaiseNetworkEvent(response, args.SenderSession.Channel);
    }

    /// <summary>
    /// Builds the current, enabled, display-sorted activity list.
    /// </summary>
    public List<GhostActivityInfo> GetActivities()
    {
        var activities = new List<GhostActivityInfo>();

        var query = AllEntityQuery<SolreignGhostActivityComponent>();
        while (query.MoveNext(out var uid, out var activity))
        {
            if (!activity.Enabled)
                continue;

            var name = GhostActivityRules.Sanitize(activity.Name, GhostActivityRules.MaxNameLength);
            if (name.Length == 0)
                name = Name(uid);

            var description = GhostActivityRules.Sanitize(activity.Description, GhostActivityRules.MaxDescriptionLength);

            activities.Add(new GhostActivityInfo(GetNetEntity(uid), name, description));
        }

        GhostActivityRules.SortForDisplay(activities);
        return activities;
    }
}
