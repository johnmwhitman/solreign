using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;
using Robust.Shared.ViewVariables;
using Robust.Shared.Serialization;

namespace Content.Shared._Solreign.Zoo;

/// <summary>
///     Core data component for Zoo Containment Cells and emitters (SR-W-049).
///     Tracks forcefield integrity, power status, specimen stress levels, authored breach paths,
///     and population scaling states.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SolreignZooContainmentComponent : Component
{
    /// <summary>
    ///     Unique identifier for this containment cell bay.
    /// </summary>
    [DataField("cellId")]
    public string CellId = "ZooCell-01";

    /// <summary>
    ///     Forcefield and containment barrier integrity percentage (0 to 100).
    /// </summary>
    [DataField("integrity"), ViewVariables(VVAccess.ReadWrite)]
    public float Integrity = 100f;

    /// <summary>
    ///     Whether the containment grid power is currently active.
    /// </summary>
    [DataField("isPowered"), ViewVariables(VVAccess.ReadWrite)]
    public bool IsPowered = true;

    /// <summary>
    ///     Agitation and stress level of specimens within this cell (0 to 100).
    /// </summary>
    [DataField("stressLevel"), ViewVariables(VVAccess.ReadWrite)]
    public float StressLevel = 0f;

    /// <summary>
    ///     Currently active authored breach path affecting this cell.
    /// </summary>
    [DataField("activeBreachPath"), ViewVariables(VVAccess.ReadWrite)]
    public ZooBreachPath ActiveBreachPath = ZooBreachPath.None;

    /// <summary>
    ///     Current population tier calculated based on active player count.
    /// </summary>
    [DataField("popTier"), ViewVariables(VVAccess.ReadWrite)]
    public ZooPopTier PopTier = ZooPopTier.MidPop;

    /// <summary>
    ///     Maximum specimens allowed in this cell based on population scaling.
    /// </summary>
    [DataField("maxSpecimenCapacity")]
    public int MaxSpecimenCapacity = 4;

    /// <summary>
    ///     List of tracked specimen entities associated with this cell.
    /// </summary>
    [ViewVariables]
    public List<EntityUid> SpecimenList = new();

    /// <summary>
    ///     Whether the containment has been safely restored post-breach.
    /// </summary>
    [DataField("isRecovered"), ViewVariables(VVAccess.ReadWrite)]
    public bool IsRecovered = true;
}

/// <summary>
///     Authored breach paths supported by SR-W-049.
/// </summary>
[Serializable, NetSerializable]
public enum ZooBreachPath : byte
{
    None = 0,
    PowerCascade = 1,
    BioFeederSurge = 2,
    VentLeak = 3
}

/// <summary>
///     Population scaling tiers for dynamic zoo breach intensity.
/// </summary>
[Serializable, NetSerializable]
public enum ZooPopTier : byte
{
    LowPop = 0,
    MidPop = 1,
    HighPop = 2
}
