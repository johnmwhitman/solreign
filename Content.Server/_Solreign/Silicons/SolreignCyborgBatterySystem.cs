using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Server._Solreign.Silicons;

public sealed partial class SolreignCyborgBatterySystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;

    public bool IsEnabled => _config == null || _config.GetCVar(CCVars.SolreignSiliconBatteryEnabled);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SolreignCyborgBatteryComponent, ComponentInit>(OnInit);
    }

    private void OnInit(EntityUid uid, SolreignCyborgBatteryComponent component, ComponentInit args)
    {
        component.CurrentBatteryCharge = Math.Clamp(component.CurrentBatteryCharge, 0f, component.MaxBatteryCharge);
    }

    public float CalculateTotalDrainRate(SolreignCyborgBatteryComponent battery)
    {
        return SolreignCyborgBatteryEvaluator.CalculateTotalDrainRate(battery);
    }

    public bool SetModulePower(SolreignCyborgBatteryComponent battery, string moduleId, bool active, float drainRate)
    {
        if (battery == null || string.IsNullOrWhiteSpace(moduleId))
            return false;

        if (active)
        {
            if (battery.IsEmergencyReserveLocked)
                return false;

            battery.ActiveModuleDrainRates[moduleId] = Math.Max(0f, drainRate);
            battery.LockedModules.Remove(moduleId);
            return true;
        }
        else
        {
            battery.ActiveModuleDrainRates.Remove(moduleId);
            return true;
        }
    }

    public void DrainBattery(SolreignCyborgBatteryComponent battery, float frameTime)
    {
        if (!IsEnabled || battery == null || frameTime <= 0f)
            return;

        var totalDrainRate = CalculateTotalDrainRate(battery);
        battery.CurrentBatteryCharge = Math.Max(0f, battery.CurrentBatteryCharge - totalDrainRate * frameTime);

        var threshold = SolreignCyborgBatteryEvaluator.GetEmergencyReserveThreshold(battery);
        if (battery.CurrentBatteryCharge <= threshold && !battery.IsEmergencyReserveLocked)
        {
            battery.IsEmergencyReserveLocked = true;
            battery.EmergencyReserveTriggeredCount++;

            foreach (var mod in battery.ActiveModuleDrainRates.Keys)
            {
                battery.LockedModules.Add(mod);
            }
            battery.ActiveModuleDrainRates.Clear();
        }
    }

    public void RechargeBattery(SolreignCyborgBatteryComponent battery, float amount)
    {
        if (battery == null || amount <= 0f)
            return;

        battery.CurrentBatteryCharge = Math.Min(battery.MaxBatteryCharge, battery.CurrentBatteryCharge + amount);

        var threshold = SolreignCyborgBatteryEvaluator.GetEmergencyReserveThreshold(battery);
        if (battery.IsEmergencyReserveLocked && battery.CurrentBatteryCharge > threshold)
        {
            battery.IsEmergencyReserveLocked = false;
        }
    }
}

public static class SolreignCyborgBatteryEvaluator
{
    public static float GetEmergencyReserveThreshold(SolreignCyborgBatteryComponent battery)
    {
        if (battery == null) return 0f;
        return battery.MaxBatteryCharge * Math.Clamp(battery.EmergencyReserveThresholdRatio, 0f, 1f);
    }

    public static float CalculateTotalDrainRate(SolreignCyborgBatteryComponent battery)
    {
        if (battery == null) return 0f;
        var sum = Math.Max(0f, battery.BaseIdleDrainRate);
        foreach (var rate in battery.ActiveModuleDrainRates.Values)
        {
            sum += Math.Max(0f, rate);
        }
        return sum;
    }
}
