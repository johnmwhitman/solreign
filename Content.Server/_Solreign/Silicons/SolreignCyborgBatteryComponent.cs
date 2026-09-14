using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._Solreign.Silicons;

[RegisterComponent]
public sealed partial class SolreignCyborgBatteryComponent : Component
{
    [DataField("maxBatteryCharge")]
    public float MaxBatteryCharge = 1000f;

    [DataField("currentBatteryCharge")]
    public float CurrentBatteryCharge = 1000f;

    [DataField("baseIdleDrainRate")]
    public float BaseIdleDrainRate = 1.5f;

    [DataField("emergencyReserveThresholdRatio")]
    public float EmergencyReserveThresholdRatio = 0.15f;

    [DataField("isEmergencyReserveLocked")]
    public bool IsEmergencyReserveLocked;

    [DataField("emergencyReserveTriggeredCount")]
    public int EmergencyReserveTriggeredCount;

    [DataField("activeModuleDrainRates")]
    public Dictionary<string, float> ActiveModuleDrainRates = new();

    [DataField("lockedModules")]
    public HashSet<string> LockedModules = new();
}
