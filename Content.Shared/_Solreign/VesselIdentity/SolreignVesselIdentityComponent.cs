using System;
using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Solreign.VesselIdentity;

/// <summary>
///     Component attached to a vessel or shuttle entity to track player-named identity,
///     moderation state, earned cosmetic registry marks, and mission history (SR-W-043).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SolreignVesselIdentityComponent : Component
{
    /// <summary>
    ///     Unique string registry ID for persistent cross-round tracking (e.g. "VESSEL-4819-ALPHA").
    /// </summary>
    [DataField, AutoNetworkedField]
    public string VesselId = string.Empty;

    /// <summary>
    ///     Current player-assigned or approved custom name of the vessel.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string VesselName = "SV-Expedition-ALPHA-1";

    /// <summary>
    ///     Default corporate registry fallback name (used when name is reset or unassigned).
    /// </summary>
    [DataField, AutoNetworkedField]
    public string DefaultName = "SV-Expedition-ALPHA-1";

    /// <summary>
    ///     Earned cosmetic registry mark tier based on mission count and total salvage.
    /// </summary>
    [DataField, AutoNetworkedField]
    public SolreignVesselRegistryMark RegistryMark = SolreignVesselRegistryMark.Unmarked;

    /// <summary>
    ///     Current moderation and screening state of the vessel's custom name.
    /// </summary>
    [DataField, AutoNetworkedField]
    public SolreignVesselModerationState ModerationState = SolreignVesselModerationState.Approved;

    /// <summary>
    ///     Total completed expedition sorties / missions by this vessel.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int MissionsCompleted = 0;

    /// <summary>
    ///     Cumulative total salvage credits / value recovered by this vessel.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float TotalSalvageValue = 0f;

    /// <summary>
    ///     List of crew NetEntities registered as authorized owners/officers for naming operations.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<NetEntity> OwnerCrewUids = new();

    /// <summary>
    ///     Timestamp (in real seconds or game time) when vessel was last renamed.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan LastRenamedAt = TimeSpan.Zero;

    /// <summary>
    ///     Minimum seconds required between vessel renaming attempts.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float RenameCooldownSeconds = 300f;
}
