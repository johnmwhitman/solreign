using System;
using System.Collections.Generic;
using Content.Server._Solreign.Diagnostics;
using Content.Server.Power.Pow3r;
using Content.Shared.CCVar;
using NUnit.Framework;
using Robust.Shared.Configuration;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(SolreignPowerDiagnosticSystem))]
public sealed class SolreignPowerDiagnosticTests
{
    [Test]
    public void Evaluator_CalculateHeadroomPercent_ValidatesBoundaries()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SolreignPowerDiagnosticEvaluator.CalculateHeadroomPercent(0.0, 100.0), Is.EqualTo(0.0));
            Assert.That(SolreignPowerDiagnosticEvaluator.CalculateHeadroomPercent(-10.0, 100.0), Is.EqualTo(0.0));
            Assert.That(SolreignPowerDiagnosticEvaluator.CalculateHeadroomPercent(50.0, 0.0), Is.EqualTo(0.0));
            Assert.That(SolreignPowerDiagnosticEvaluator.CalculateHeadroomPercent(50.0, -100.0), Is.EqualTo(0.0));
            Assert.That(SolreignPowerDiagnosticEvaluator.CalculateHeadroomPercent(50.0, 100.0), Is.EqualTo(50.0).Within(0.0001));
            Assert.That(SolreignPowerDiagnosticEvaluator.CalculateHeadroomPercent(100.0, 100.0), Is.EqualTo(100.0).Within(0.0001));
            Assert.That(SolreignPowerDiagnosticEvaluator.CalculateHeadroomPercent(150.0, 100.0), Is.EqualTo(100.0).Within(0.0001));
        });
    }

    [Test]
    public void Evaluator_CalculatePowerDeficit_HandlesSupplyAndDemand()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SolreignPowerDiagnosticEvaluator.CalculatePowerDeficit(100.0, 150.0), Is.EqualTo(0.0));
            Assert.That(SolreignPowerDiagnosticEvaluator.CalculatePowerDeficit(100.0, 100.0), Is.EqualTo(0.0));
            Assert.That(SolreignPowerDiagnosticEvaluator.CalculatePowerDeficit(150.0, 100.0), Is.EqualTo(50.0).Within(0.0001));
            Assert.That(SolreignPowerDiagnosticEvaluator.CalculatePowerDeficit(0.0, 100.0), Is.EqualTo(0.0));
            Assert.That(SolreignPowerDiagnosticEvaluator.CalculatePowerDeficit(-50.0, 100.0), Is.EqualTo(0.0));
            Assert.That(SolreignPowerDiagnosticEvaluator.CalculatePowerDeficit(100.0, -20.0), Is.EqualTo(100.0).Within(0.0001));
        });
    }

    [Test]
    public void Evaluator_CalculateEstimatedReserveTimeSeconds_HandlesEnduranceMath()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SolreignPowerDiagnosticEvaluator.CalculateEstimatedReserveTimeSeconds(0.0, 50.0), Is.EqualTo(0.0));
            Assert.That(SolreignPowerDiagnosticEvaluator.CalculateEstimatedReserveTimeSeconds(100.0, 0.0), Is.EqualTo(double.PositiveInfinity));
            Assert.That(SolreignPowerDiagnosticEvaluator.CalculateEstimatedReserveTimeSeconds(100.0, -10.0), Is.EqualTo(double.PositiveInfinity));

            // 100 kWh reserve, 50 kW deficit -> (100 * 3600) / 50 = 7200 seconds (2 hours)
            Assert.That(SolreignPowerDiagnosticEvaluator.CalculateEstimatedReserveTimeSeconds(100.0, 50.0), Is.EqualTo(7200.0).Within(0.0001));
        });
    }

    [Test]
    public void Evaluator_EvaluatePowerCondition_DetectsNormalBrownoutAndBlackout()
    {
        Assert.Multiple(() =>
        {
            // Normal operation: draw <= supply, headroom >= 20%
            var normal = SolreignPowerDiagnosticEvaluator.EvaluatePowerCondition(
                drawkW: 100.0,
                supplykW: 120.0,
                reservekWh: 80.0,
                capacitykWh: 100.0,
                brownoutThresholdPercent: 20.0f);
            Assert.That(normal, Is.EqualTo(SolreignPowerCondition.Normal));

            // No installed reserve is not itself a power emergency when supply meets demand.
            var noInstalledReserve = SolreignPowerDiagnosticEvaluator.EvaluatePowerCondition(
                drawkW: 0.0,
                supplykW: 0.0,
                reservekWh: 0.0,
                capacitykWh: 0.0,
                brownoutThresholdPercent: 20.0f);
            Assert.That(noInstalledReserve, Is.EqualTo(SolreignPowerCondition.Normal));

            // A discharged backup does not make a supplied grid a blackout.
            var depletedBackup = SolreignPowerDiagnosticEvaluator.EvaluatePowerCondition(
                drawkW: 100.0,
                supplykW: 120.0,
                reservekWh: 0.0,
                capacitykWh: 100.0,
                brownoutThresholdPercent: 20.0f);
            Assert.That(depletedBackup, Is.EqualTo(SolreignPowerCondition.Brownout));

            // Brownout due to demand exceeding supply
            var brownoutDemand = SolreignPowerDiagnosticEvaluator.EvaluatePowerCondition(
                drawkW: 150.0,
                supplykW: 120.0,
                reservekWh: 80.0,
                capacitykWh: 100.0,
                brownoutThresholdPercent: 20.0f);
            Assert.That(brownoutDemand, Is.EqualTo(SolreignPowerCondition.Brownout));

            // Brownout due to low battery headroom (< 20%)
            var brownoutHeadroom = SolreignPowerDiagnosticEvaluator.EvaluatePowerCondition(
                drawkW: 100.0,
                supplykW: 120.0,
                reservekWh: 15.0,
                capacitykWh: 100.0,
                brownoutThresholdPercent: 20.0f);
            Assert.That(brownoutHeadroom, Is.EqualTo(SolreignPowerCondition.Brownout));

            // Blackout due to depleted battery reserve (0 reserve)
            var blackoutDepleted = SolreignPowerDiagnosticEvaluator.EvaluatePowerCondition(
                drawkW: 100.0,
                supplykW: 0.0,
                reservekWh: 0.0,
                capacitykWh: 100.0,
                brownoutThresholdPercent: 20.0f);
            Assert.That(blackoutDepleted, Is.EqualTo(SolreignPowerCondition.Blackout));
        });
    }

    [Test]
    public void Evaluator_EvaluateAlertLevel_CorrectlyEscalatesAlerts()
    {
        Assert.Multiple(() =>
        {
            // Normal condition -> None
            var none = SolreignPowerDiagnosticEvaluator.EvaluateAlertLevel(
                SolreignPowerCondition.Normal,
                headroomPercent: 50.0,
                powerDeficitkW: 0.0,
                brownoutThreshold: 20.0f,
                criticalThreshold: 5.0f,
                loadSheddingThresholdKw: 50.0f);
            Assert.That(none, Is.EqualTo(SolreignLoadSheddingAlertLevel.None));

            var noInstalledReserve = SolreignPowerDiagnosticEvaluator.EvaluateAlertLevel(
                SolreignPowerCondition.Normal,
                headroomPercent: 0.0,
                powerDeficitkW: 0.0,
                brownoutThreshold: 20.0f,
                criticalThreshold: 5.0f,
                loadSheddingThresholdKw: 50.0f);
            Assert.That(noInstalledReserve, Is.EqualTo(SolreignLoadSheddingAlertLevel.None));

            var depletedBackup = SolreignPowerDiagnosticEvaluator.EvaluateAlertLevel(
                SolreignPowerCondition.Brownout,
                headroomPercent: 0.0,
                powerDeficitkW: 0.0,
                brownoutThreshold: 20.0f,
                criticalThreshold: 5.0f,
                loadSheddingThresholdKw: 50.0f);
            Assert.That(depletedBackup, Is.EqualTo(SolreignLoadSheddingAlertLevel.Critical));

            // Brownout with mild deficit -> Warning
            var warning = SolreignPowerDiagnosticEvaluator.EvaluateAlertLevel(
                SolreignPowerCondition.Brownout,
                headroomPercent: 15.0,
                powerDeficitkW: 20.0,
                brownoutThreshold: 20.0f,
                criticalThreshold: 5.0f,
                loadSheddingThresholdKw: 50.0f);
            Assert.That(warning, Is.EqualTo(SolreignLoadSheddingAlertLevel.Warning));

            // Brownout with heavy power deficit (>= 50kW) -> Critical
            var criticalDeficit = SolreignPowerDiagnosticEvaluator.EvaluateAlertLevel(
                SolreignPowerCondition.Brownout,
                headroomPercent: 15.0,
                powerDeficitkW: 60.0,
                brownoutThreshold: 20.0f,
                criticalThreshold: 5.0f,
                loadSheddingThresholdKw: 50.0f);
            Assert.That(criticalDeficit, Is.EqualTo(SolreignLoadSheddingAlertLevel.Critical));

            // Low headroom <= 5% -> Critical
            var criticalHeadroom = SolreignPowerDiagnosticEvaluator.EvaluateAlertLevel(
                SolreignPowerCondition.Brownout,
                headroomPercent: 4.0,
                powerDeficitkW: 10.0,
                brownoutThreshold: 20.0f,
                criticalThreshold: 5.0f,
                loadSheddingThresholdKw: 50.0f);
            Assert.That(criticalHeadroom, Is.EqualTo(SolreignLoadSheddingAlertLevel.Critical));

            // Blackout condition -> Emergency
            var emergency = SolreignPowerDiagnosticEvaluator.EvaluateAlertLevel(
                SolreignPowerCondition.Blackout,
                headroomPercent: 0.0,
                powerDeficitkW: 100.0,
                brownoutThreshold: 20.0f,
                criticalThreshold: 5.0f,
                loadSheddingThresholdKw: 50.0f);
            Assert.That(emergency, Is.EqualTo(SolreignLoadSheddingAlertLevel.Emergency));
        });
    }

    [Test]
    public void Evaluator_CalculateRecommendedLoadShedding_GivesAccurateRecommendations()
    {
        Assert.Multiple(() =>
        {
            // No deficit and headroom healthy -> 0
            var noShedding = SolreignPowerDiagnosticEvaluator.CalculateRecommendedLoadSheddingkW(
                drawkW: 100.0,
                supplykW: 150.0,
                headroomPercent: 50.0,
                brownoutThresholdPercent: 20.0f);
            Assert.That(noShedding, Is.EqualTo(0.0));

            // Direct power deficit -> recommend shedding deficit amount (200 - 150 = 50 kW)
            var deficitShedding = SolreignPowerDiagnosticEvaluator.CalculateRecommendedLoadSheddingkW(
                drawkW: 200.0,
                supplykW: 150.0,
                headroomPercent: 50.0,
                brownoutThresholdPercent: 20.0f);
            Assert.That(deficitShedding, Is.EqualTo(50.0).Within(0.0001));

            // Headroom low (10% vs 20% threshold) with 0 direct deficit -> recommend partial shedding to recover headroom
            var lowHeadroomShedding = SolreignPowerDiagnosticEvaluator.CalculateRecommendedLoadSheddingkW(
                drawkW: 100.0,
                supplykW: 100.0,
                headroomPercent: 10.0,
                brownoutThresholdPercent: 20.0f);
            Assert.That(lowHeadroomShedding, Is.GreaterThan(0.0));
        });
    }

    [Test]
    public void System_InitialState_ZeroedMetrics()
    {
        var sys = new SolreignPowerDiagnosticSystem();

        Assert.Multiple(() =>
        {
            Assert.That(sys.IsEnabled, Is.False);
            Assert.That(sys.BrownoutHeadroomThresholdPercent, Is.EqualTo(20.0f));
            Assert.That(sys.CriticalHeadroomThresholdPercent, Is.EqualTo(5.0f));
            Assert.That(sys.LoadSheddingThresholdKw, Is.EqualTo(50.0f));
            Assert.That(sys.GridPowerDrawkW, Is.EqualTo(0.0));
            Assert.That(sys.GridPowerSupplykW, Is.EqualTo(0.0));
            Assert.That(sys.BatteryCapacitykWh, Is.EqualTo(0.0));
            Assert.That(sys.BatteryReservekWh, Is.EqualTo(0.0));
            Assert.That(sys.BatteryHeadroomPercent, Is.EqualTo(0.0));
            Assert.That(sys.CurrentCondition, Is.EqualTo(SolreignPowerCondition.Normal));
            Assert.That(sys.CurrentAlertLevel, Is.EqualTo(SolreignLoadSheddingAlertLevel.None));
        });
    }

    [Test]
    public void PowerDiagnosticsCVar_IsReplicatedAndDefaultOn()
    {
        // Default ON + replicated per the 2026-07-24 owner activation decision; the
        // original dark-by-default assertion predated that ruling.
        Assert.Multiple(() =>
        {
            Assert.That(CCVars.SolreignPowerDiagnosticsEnabled.DefaultValue, Is.True);
            Assert.That(CCVars.SolreignPowerDiagnosticsEnabled.Flags, Is.EqualTo(CVar.SERVER));
        });
    }

    [Test]
    public void Collector_DeduplicatesNetworksAndConvertsLivePowerUnits()
    {
        var network = new PowerState.Network
        {
            LastCombinedLoad = 120_000f,
            LastCombinedSupply = 100_000f,
        };
        var collector = new SolreignPowerDiagnosticCollector();

        collector.AddNetworkBattery(network, new PowerState.Battery
        {
            Capacity = 7_200_000f,
            CurrentStorage = 3_600_000f,
        }, available: true);
        collector.AddNetworkBattery(network, new PowerState.Battery
        {
            Capacity = 3_600_000f,
            CurrentStorage = 1_800_000f,
        }, available: true);

        var totals = collector.ToDiagnosticUnits();

        Assert.Multiple(() =>
        {
            Assert.That(totals.DrawkW, Is.EqualTo(120d));
            Assert.That(totals.SupplykW, Is.EqualTo(100d));
            Assert.That(totals.BatteryCapacitykWh, Is.EqualTo(3d));
            Assert.That(totals.BatteryReservekWh, Is.EqualTo(1.5d));
        });
    }

    [Test]
    public void Collector_ExcludesUnavailableReserveAndIdleBackupNetwork()
    {
        var failedPrimary = new PowerState.Network
        {
            LastCombinedLoad = 120_000f,
            LastCombinedSupply = 0f,
        };
        var idleBackup = new PowerState.Network
        {
            LastCombinedLoad = 0f,
            LastCombinedSupply = 100_000f,
        };
        var collector = new SolreignPowerDiagnosticCollector();

        collector.AddNetworkBattery(failedPrimary, new PowerState.Battery
        {
            Capacity = 7_200_000f,
            CurrentStorage = 7_200_000f,
        }, available: false);
        collector.AddNetworkBattery(idleBackup, new PowerState.Battery
        {
            Capacity = 36_000_000f,
            CurrentStorage = 36_000_000f,
        }, available: true);

        var totals = collector.ToDiagnosticUnits();

        Assert.Multiple(() =>
        {
            Assert.That(totals.DrawkW, Is.EqualTo(120d));
            Assert.That(totals.SupplykW, Is.Zero);
            Assert.That(totals.BatteryCapacitykWh, Is.Zero);
            Assert.That(totals.BatteryReservekWh, Is.Zero);
        });
    }

    [Test]
    public void Collector_EmptyCollectionHasNoLiveSample()
    {
        var collector = new SolreignPowerDiagnosticCollector();

        Assert.That(collector.HasLiveSample, Is.False);
    }

    [Test]
    public void Collector_PrioritizesFailedNetworkOverHigherDrawHealthyNetworkRegardlessOfInsertionOrder()
    {
        var healthyHighDraw = new PowerState.Network
        {
            LastCombinedLoad = 500_000f,
            LastCombinedSupply = 500_000f,
        };
        var failedLowerDraw = new PowerState.Network
        {
            LastCombinedLoad = 100_000f,
            LastCombinedSupply = 0f,
        };
        var charged = new PowerState.Battery
        {
            Capacity = 7_200_000f,
            CurrentStorage = 7_200_000f,
        };
        var depleted = new PowerState.Battery
        {
            Capacity = 7_200_000f,
            CurrentStorage = 0f,
        };

        static SolreignPowerDiagnosticTotals Collect(
            (PowerState.Network Network, PowerState.Battery Battery) first,
            (PowerState.Network Network, PowerState.Battery Battery) second)
        {
            var collector = new SolreignPowerDiagnosticCollector();
            collector.AddNetworkBattery(first.Network, first.Battery, available: true);
            collector.AddNetworkBattery(second.Network, second.Battery, available: true);
            return collector.ToDiagnosticUnits();
        }

        var forward = Collect((healthyHighDraw, charged), (failedLowerDraw, depleted));
        var reverse = Collect((failedLowerDraw, depleted), (healthyHighDraw, charged));

        Assert.Multiple(() =>
        {
            Assert.That(forward.DrawkW, Is.EqualTo(100d));
            Assert.That(reverse.DrawkW, Is.EqualTo(100d));
            Assert.That(forward.SupplykW, Is.Zero);
            Assert.That(reverse.SupplykW, Is.Zero);
            Assert.That(forward.BatteryReservekWh, Is.Zero);
            Assert.That(reverse.BatteryReservekWh, Is.Zero);
        });
    }

    [Test]
    public void Collector_EqualConditionNetworksChooseLowerAbsoluteReserveRegardlessOfInsertionOrder()
    {
        var largerReserve = new PowerState.Network
        {
            LastCombinedLoad = 100_000f,
            LastCombinedSupply = 50_000f,
        };
        var smallerReserve = new PowerState.Network
        {
            LastCombinedLoad = 100_000f,
            LastCombinedSupply = 50_000f,
        };

        static SolreignPowerDiagnosticTotals Collect(
            (PowerState.Network Network, PowerState.Battery Battery) first,
            (PowerState.Network Network, PowerState.Battery Battery) second)
        {
            var collector = new SolreignPowerDiagnosticCollector();
            collector.AddNetworkBattery(first.Network, first.Battery, available: true);
            collector.AddNetworkBattery(second.Network, second.Battery, available: true);
            return collector.ToDiagnosticUnits();
        }

        var large = new PowerState.Battery
        {
            Capacity = 36_000_000f,
            CurrentStorage = 18_000_000f,
        };
        var small = new PowerState.Battery
        {
            Capacity = 7_200_000f,
            CurrentStorage = 3_600_000f,
        };

        var forward = Collect((largerReserve, large), (smallerReserve, small));
        var reverse = Collect((smallerReserve, small), (largerReserve, large));

        Assert.Multiple(() =>
        {
            Assert.That(forward.BatteryReservekWh, Is.EqualTo(1d));
            Assert.That(reverse.BatteryReservekWh, Is.EqualTo(1d));
        });
    }

    [Test]
    public void Collector_EqualZeroReserveNetworksChooseInstalledDepletedCapacityRegardlessOfInsertionOrder()
    {
        var installedDepleted = new PowerState.Network
        {
            LastCombinedLoad = 100_000f,
            LastCombinedSupply = 0f,
        };
        var noInstalledCapacity = new PowerState.Network
        {
            LastCombinedLoad = 100_000f,
            LastCombinedSupply = 0f,
        };
        var depleted = new PowerState.Battery
        {
            Capacity = 7_200_000f,
            CurrentStorage = 0f,
        };
        var absent = new PowerState.Battery
        {
            Capacity = 0f,
            CurrentStorage = 0f,
        };

        static SolreignPowerDiagnosticTotals Collect(
            (PowerState.Network Network, PowerState.Battery Battery) first,
            (PowerState.Network Network, PowerState.Battery Battery) second)
        {
            var collector = new SolreignPowerDiagnosticCollector();
            collector.AddNetworkBattery(first.Network, first.Battery, available: true);
            collector.AddNetworkBattery(second.Network, second.Battery, available: true);
            return collector.ToDiagnosticUnits();
        }

        var forward = Collect((installedDepleted, depleted), (noInstalledCapacity, absent));
        var reverse = Collect((noInstalledCapacity, absent), (installedDepleted, depleted));

        Assert.Multiple(() =>
        {
            Assert.That(forward.BatteryCapacitykWh, Is.EqualTo(2d));
            Assert.That(reverse.BatteryCapacitykWh, Is.EqualTo(2d));
            Assert.That(forward.BatteryReservekWh, Is.Zero);
            Assert.That(reverse.BatteryReservekWh, Is.Zero);
        });
    }

    [Test]
    public void Collector_EqualDrawSurplusNetworksChooseLowerSupplyRegardlessOfInsertionOrder()
    {
        var lowerSupply = new PowerState.Network
        {
            LastCombinedLoad = 100_000f,
            LastCombinedSupply = 120_000f,
        };
        var higherSupply = new PowerState.Network
        {
            LastCombinedLoad = 100_000f,
            LastCombinedSupply = 150_000f,
        };
        var battery = new PowerState.Battery
        {
            Capacity = 7_200_000f,
            CurrentStorage = 3_600_000f,
        };

        static SolreignPowerDiagnosticTotals Collect(
            (PowerState.Network Network, PowerState.Battery Battery) first,
            (PowerState.Network Network, PowerState.Battery Battery) second)
        {
            var collector = new SolreignPowerDiagnosticCollector();
            collector.AddNetworkBattery(first.Network, first.Battery, available: true);
            collector.AddNetworkBattery(second.Network, second.Battery, available: true);
            return collector.ToDiagnosticUnits();
        }

        var forward = Collect((lowerSupply, battery), (higherSupply, battery));
        var reverse = Collect((higherSupply, battery), (lowerSupply, battery));

        Assert.Multiple(() =>
        {
            Assert.That(forward.SupplykW, Is.EqualTo(120d));
            Assert.That(reverse.SupplykW, Is.EqualTo(120d));
        });
    }

    [Test]
    public void System_UpdateGridPower_UpdatesMetricsAndRaisesEvents()
    {
        var sys = new SolreignPowerDiagnosticSystem { IsEnabled = true };
        var raisedEvents = new List<object>();
        sys.OnEventRaised = raisedEvents.Add;

        // Normal state update
        var snap1 = sys.UpdateGridPower(
            drawkW: 80.0,
            supplykW: 100.0,
            batteryCapacitykWh: 200.0,
            batteryReservekWh: 160.0);

        Assert.Multiple(() =>
        {
            Assert.That(sys.GridPowerDrawkW, Is.EqualTo(80.0));
            Assert.That(sys.GridPowerSupplykW, Is.EqualTo(100.0));
            Assert.That(sys.BatteryHeadroomPercent, Is.EqualTo(80.0).Within(0.0001));
            Assert.That(sys.CurrentCondition, Is.EqualTo(SolreignPowerCondition.Normal));
            Assert.That(sys.CurrentAlertLevel, Is.EqualTo(SolreignLoadSheddingAlertLevel.None));
            Assert.That(snap1.IsDisrupted, Is.False);
            Assert.That(snap1.IsLoadSheddingAlertActive, Is.False);
            Assert.That(raisedEvents.Count, Is.EqualTo(1)); // Metric event only
            Assert.That(raisedEvents[0], Is.InstanceOf<SolreignPowerDiagnosticMetricEvent>());
        });

        raisedEvents.Clear();

        // Brownout state update with deficit of 60 kW (draw 160, supply 100) -> triggers disruption event + critical alert
        var snap2 = sys.UpdateGridPower(
            drawkW: 160.0,
            supplykW: 100.0,
            batteryCapacitykWh: 200.0,
            batteryReservekWh: 100.0);

        Assert.Multiple(() =>
        {
            Assert.That(sys.CurrentCondition, Is.EqualTo(SolreignPowerCondition.Brownout));
            Assert.That(sys.CurrentAlertLevel, Is.EqualTo(SolreignLoadSheddingAlertLevel.Critical));
            Assert.That(snap2.IsDisrupted, Is.True);
            Assert.That(snap2.IsLoadSheddingAlertActive, Is.True);
            Assert.That(snap2.RecommendedLoadSheddingkW, Is.EqualTo(60.0).Within(0.0001));
            Assert.That(raisedEvents.Count, Is.EqualTo(2)); // Metric event + Disruption event
            Assert.That(raisedEvents[0], Is.InstanceOf<SolreignPowerDiagnosticMetricEvent>());
            Assert.That(raisedEvents[1], Is.InstanceOf<SolreignPowerDisruptionEvent>());
        });
    }

    [Test]
    public void System_DisabledState_SuppressesTracking()
    {
        var sys = new SolreignPowerDiagnosticSystem
        {
            IsEnabled = false
        };

        var raisedEvents = new List<object>();
        sys.OnEventRaised = raisedEvents.Add;

        var snap = sys.UpdateGridPower(
            drawkW: 500.0,
            supplykW: 0.0,
            batteryCapacitykWh: 100.0,
            batteryReservekWh: 0.0);

        Assert.Multiple(() =>
        {
            Assert.That(sys.GridPowerDrawkW, Is.EqualTo(0.0));
            Assert.That(snap.Enabled, Is.False);
            Assert.That(raisedEvents, Is.Empty);
        });
    }

    [Test]
    public void System_ResetMetrics_ClearsGridState()
    {
        var sys = new SolreignPowerDiagnosticSystem { IsEnabled = true };

        sys.UpdateGridPower(
            drawkW: 200.0,
            supplykW: 50.0,
            batteryCapacitykWh: 100.0,
            batteryReservekWh: 10.0);

        Assert.That(sys.CurrentCondition, Is.EqualTo(SolreignPowerCondition.Brownout));

        sys.ResetMetrics();

        Assert.Multiple(() =>
        {
            Assert.That(sys.GridPowerDrawkW, Is.EqualTo(0.0));
            Assert.That(sys.GridPowerSupplykW, Is.EqualTo(0.0));
            Assert.That(sys.BatteryCapacitykWh, Is.EqualTo(0.0));
            Assert.That(sys.BatteryReservekWh, Is.EqualTo(0.0));
            Assert.That(sys.CurrentCondition, Is.EqualTo(SolreignPowerCondition.Normal));
            Assert.That(sys.CurrentAlertLevel, Is.EqualTo(SolreignLoadSheddingAlertLevel.None));
        });
    }

    [Test]
    public void Event_Properties_MatchConstructorArguments()
    {
        var timeStamp = TimeSpan.FromSeconds(300);
        var metricEv = new SolreignPowerDiagnosticMetricEvent(
            timeStamp,
            120.0,
            100.0,
            500.0,
            250.0,
            50.0,
            20.0,
            45000.0,
            SolreignPowerCondition.Brownout,
            SolreignLoadSheddingAlertLevel.Warning,
            20.0);

        Assert.Multiple(() =>
        {
            Assert.That(metricEv.TimeStamp, Is.EqualTo(timeStamp));
            Assert.That(metricEv.GridPowerDrawkW, Is.EqualTo(120.0));
            Assert.That(metricEv.GridPowerSupplykW, Is.EqualTo(100.0));
            Assert.That(metricEv.BatteryCapacitykWh, Is.EqualTo(500.0));
            Assert.That(metricEv.BatteryReservekWh, Is.EqualTo(250.0));
            Assert.That(metricEv.BatteryHeadroomPercent, Is.EqualTo(50.0));
            Assert.That(metricEv.PowerDeficitkW, Is.EqualTo(20.0));
            Assert.That(metricEv.EstimatedReserveTimeSeconds, Is.EqualTo(45000.0));
            Assert.That(metricEv.Condition, Is.EqualTo(SolreignPowerCondition.Brownout));
            Assert.That(metricEv.AlertLevel, Is.EqualTo(SolreignLoadSheddingAlertLevel.Warning));
            Assert.That(metricEv.RecommendedLoadSheddingkW, Is.EqualTo(20.0));
        });

        var disruptionEv = new SolreignPowerDisruptionEvent(
            timeStamp,
            SolreignPowerCondition.Normal,
            SolreignPowerCondition.Brownout,
            SolreignLoadSheddingAlertLevel.Warning,
            120.0,
            100.0,
            50.0,
            20.0,
            20.0,
            "Grid brownout detected.");

        Assert.Multiple(() =>
        {
            Assert.That(disruptionEv.TimeStamp, Is.EqualTo(timeStamp));
            Assert.That(disruptionEv.PreviousCondition, Is.EqualTo(SolreignPowerCondition.Normal));
            Assert.That(disruptionEv.NewCondition, Is.EqualTo(SolreignPowerCondition.Brownout));
            Assert.That(disruptionEv.AlertLevel, Is.EqualTo(SolreignLoadSheddingAlertLevel.Warning));
            Assert.That(disruptionEv.Details, Is.EqualTo("Grid brownout detected."));
        });
    }
}
