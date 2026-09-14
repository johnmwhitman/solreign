using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._Solreign.VesselIdentity;

/// <summary>
///     Event raised when a vessel's name is successfully changed or updated (SR-W-043).
/// </summary>
[Serializable, NetSerializable]
public sealed class SolreignVesselRenamedEvent : EntityEventArgs
{
    public NetEntity VesselUid { get; }
    public string OldName { get; }
    public string NewName { get; }
    public SolreignVesselModerationState ModerationState { get; }

    public SolreignVesselRenamedEvent(NetEntity vesselUid, string oldName, string newName, SolreignVesselModerationState moderationState)
    {
        VesselUid = vesselUid;
        OldName = oldName;
        NewName = newName;
        ModerationState = moderationState;
    }
}

/// <summary>
///     Event raised when an administrative moderation action resets a vessel name back to default (SR-W-043).
/// </summary>
[Serializable, NetSerializable]
public sealed class SolreignVesselNameResetEvent : EntityEventArgs
{
    public NetEntity VesselUid { get; }
    public string OffendingName { get; }
    public string DefaultName { get; }
    public string Reason { get; }

    public SolreignVesselNameResetEvent(NetEntity vesselUid, string offendingName, string defaultName, string reason)
    {
        VesselUid = vesselUid;
        OffendingName = offendingName;
        DefaultName = defaultName;
        Reason = reason;
    }
}

/// <summary>
///     Event raised when a vessel earns a new cosmetic registry mark tier (SR-W-043).
/// </summary>
[Serializable, NetSerializable]
public sealed class SolreignVesselMarkEarnedEvent : EntityEventArgs
{
    public NetEntity VesselUid { get; }
    public string VesselName { get; }
    public SolreignVesselRegistryMark NewMark { get; }
    public int MissionsCompleted { get; }
    public float TotalSalvageValue { get; }

    public SolreignVesselMarkEarnedEvent(
        NetEntity vesselUid,
        string vesselName,
        SolreignVesselRegistryMark newMark,
        int missionsCompleted,
        float totalSalvageValue)
    {
        VesselUid = vesselUid;
        VesselName = vesselName;
        NewMark = newMark;
        MissionsCompleted = missionsCompleted;
        TotalSalvageValue = totalSalvageValue;
    }
}
