using Robust.Shared.Serialization;

namespace Content.Shared._Solreign.Ghost;

/// <summary>
/// Solreign — Afterlife Activities (roadmap D2.2).
/// A client to server request for the list of ghost afterlife activities.
/// Follows the same raw networked-event pattern as <c>GhostWarpsRequestEvent</c>.
/// The response is sent via <see cref="GhostActivitiesResponseEvent"/>.
/// Warping to an activity reuses the upstream <c>GhostWarpToTargetRequestEvent</c>.
/// </summary>
[Serializable, NetSerializable]
public sealed class GhostActivitiesRequestEvent : EntityEventArgs
{
}

/// <summary>
/// One afterlife activity entry, used as part of <see cref="GhostActivitiesResponseEvent"/>.
/// </summary>
[Serializable, NetSerializable]
public struct GhostActivityInfo
{
    public GhostActivityInfo(NetEntity entity, string name, string description)
    {
        Entity = entity;
        Name = name;
        Description = description;
    }

    /// <summary>
    /// The entity carrying <see cref="SolreignGhostActivityComponent"/>.
    /// Passed back to the server in <c>GhostWarpToTargetRequestEvent</c> to warp there.
    /// </summary>
    public NetEntity Entity { get; }

    public string Name { get; }

    public string Description { get; }
}

/// <summary>
/// A server to client response for a <see cref="GhostActivitiesRequestEvent"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class GhostActivitiesResponseEvent : EntityEventArgs
{
    public GhostActivitiesResponseEvent(List<GhostActivityInfo> activities)
    {
        Activities = activities;
    }

    public List<GhostActivityInfo> Activities { get; }
}
