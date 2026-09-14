using Content.Server._Solreign.Silicons;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
public sealed class SolreignCyborgBatteryTests
{
    private SolreignCyborgBatterySystem _system = default!;

    [SetUp]
    public void SetUp()
    {
        _system = new SolreignCyborgBatterySystem();
    }

    [Test]
    public void ComponentDefaults_InitializedCorrectly()
    {
        var comp = new SolreignCyborgBatteryComponent();

        Assert.That(comp.MaxBatteryCharge, Is.EqualTo(1000f));
        Assert.That(comp.CurrentBatteryCharge, Is.EqualTo(1000f));
        Assert.That(comp.BaseIdleDrainRate, Is.EqualTo(1.5f));
        Assert.That(comp.EmergencyReserveThresholdRatio, Is.EqualTo(0.15f));
        Assert.That(comp.IsEmergencyReserveLocked, Is.False);
        Assert.That(comp.EmergencyReserveTriggeredCount, Is.EqualTo(0));
    }

    [Test]
    public void TotalDrainRate_SumsIdleAndActiveModules()
    {
        var comp = new SolreignCyborgBatteryComponent
        {
            BaseIdleDrainRate = 2.0f
        };
        comp.ActiveModuleDrainRates["medical_hud"] = 1.0f;
        comp.ActiveModuleDrainRates["laser_welder"] = 3.5f;

        var drainRate = _system.CalculateTotalDrainRate(comp);

        Assert.That(drainRate, Is.EqualTo(6.5f));
    }

    [Test]
    public void ModulePower_TogglesActiveState()
    {
        var comp = new SolreignCyborgBatteryComponent();

        var success = _system.SetModulePower(comp, "mining_drill", true, 4.0f);
        Assert.That(success, Is.True);
        Assert.That(comp.ActiveModuleDrainRates.ContainsKey("mining_drill"), Is.True);
        Assert.That(comp.ActiveModuleDrainRates["mining_drill"], Is.EqualTo(4.0f));

        var disableSuccess = _system.SetModulePower(comp, "mining_drill", false, 0f);
        Assert.That(disableSuccess, Is.True);
        Assert.That(comp.ActiveModuleDrainRates.ContainsKey("mining_drill"), Is.False);
    }

    [Test]
    public void DrainBattery_AppliesDrainMath()
    {
        var comp = new SolreignCyborgBatteryComponent
        {
            CurrentBatteryCharge = 500f,
            BaseIdleDrainRate = 10f
        };

        _system.DrainBattery(comp, 5.0f);

        Assert.That(comp.CurrentBatteryCharge, Is.EqualTo(450f));
    }

    [Test]
    public void EmergencyReserveLock_TriggersWhenBelowThreshold()
    {
        var comp = new SolreignCyborgBatteryComponent
        {
            MaxBatteryCharge = 1000f,
            CurrentBatteryCharge = 160f,
            BaseIdleDrainRate = 20f,
            EmergencyReserveThresholdRatio = 0.15f
        };
        comp.ActiveModuleDrainRates["stasis_field"] = 5f;

        _system.DrainBattery(comp, 1.0f); // 160 - 25 = 135 (<= 150 threshold)

        Assert.That(comp.IsEmergencyReserveLocked, Is.True);
        Assert.That(comp.EmergencyReserveTriggeredCount, Is.EqualTo(1));
        Assert.That(comp.ActiveModuleDrainRates.Count, Is.EqualTo(0));
        Assert.That(comp.LockedModules.Contains("stasis_field"), Is.True);
    }

    [Test]
    public void ModuleActivation_BlockedDuringEmergencyReserveLock()
    {
        var comp = new SolreignCyborgBatteryComponent
        {
            IsEmergencyReserveLocked = true
        };

        var success = _system.SetModulePower(comp, "laser_welder", true, 5.0f);

        Assert.That(success, Is.False);
        Assert.That(comp.ActiveModuleDrainRates.ContainsKey("laser_welder"), Is.False);
    }

    [Test]
    public void RechargeBattery_DisengagesEmergencyLock_WhenAboveThreshold()
    {
        var comp = new SolreignCyborgBatteryComponent
        {
            MaxBatteryCharge = 1000f,
            CurrentBatteryCharge = 100f,
            IsEmergencyReserveLocked = true,
            EmergencyReserveThresholdRatio = 0.15f
        };

        _system.RechargeBattery(comp, 100f); // 100 + 100 = 200 > 150

        Assert.That(comp.CurrentBatteryCharge, Is.EqualTo(200f));
        Assert.That(comp.IsEmergencyReserveLocked, Is.False);
    }

    [Test]
    public void Evaluator_PureMath_CalculatesThresholdAndDrain()
    {
        var comp = new SolreignCyborgBatteryComponent
        {
            MaxBatteryCharge = 2000f,
            EmergencyReserveThresholdRatio = 0.20f,
            BaseIdleDrainRate = 5f
        };
        comp.ActiveModuleDrainRates["scanner"] = 10f;

        var threshold = SolreignCyborgBatteryEvaluator.GetEmergencyReserveThreshold(comp);
        var totalDrain = SolreignCyborgBatteryEvaluator.CalculateTotalDrainRate(comp);

        Assert.That(threshold, Is.EqualTo(400f));
        Assert.That(totalDrain, Is.EqualTo(15f));
    }

    [Test]
    public void NullAndEdgeCases_ExecuteZeroExceptions()
    {
        Assert.DoesNotThrow(() =>
        {
            _system.CalculateTotalDrainRate(null!);
            _system.SetModulePower(null!, "", false, -5f);
            _system.DrainBattery(null!, -10f);
            _system.RechargeBattery(null!, -50f);
            SolreignCyborgBatteryEvaluator.GetEmergencyReserveThreshold(null!);
            SolreignCyborgBatteryEvaluator.CalculateTotalDrainRate(null!);
        });
    }
}
