using System;
using System.Collections.Generic;
using Content.Server._Solreign.Diagnostics;
using Content.Shared.Atmos;
using Content.Shared.CCVar;
using NUnit.Framework;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(SolreignAtmosDiagnosticSystem))]
public sealed class SolreignAtmosDiagnosticTests
{
    [Test]
    public void Evaluator_CalculateOxygenRatioPercent_ValidatesBoundaries()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SolreignAtmosDiagnosticEvaluator.CalculateOxygenRatioPercent(0.0, 100.0), Is.EqualTo(0.0));
            Assert.That(SolreignAtmosDiagnosticEvaluator.CalculateOxygenRatioPercent(-10.0, 100.0), Is.EqualTo(0.0));
            Assert.That(SolreignAtmosDiagnosticEvaluator.CalculateOxygenRatioPercent(21.0, 0.0), Is.EqualTo(0.0));
            Assert.That(SolreignAtmosDiagnosticEvaluator.CalculateOxygenRatioPercent(21.0, -100.0), Is.EqualTo(0.0));
            Assert.That(SolreignAtmosDiagnosticEvaluator.CalculateOxygenRatioPercent(21.0, 100.0), Is.EqualTo(21.0).Within(0.0001));
            Assert.That(SolreignAtmosDiagnosticEvaluator.CalculateOxygenRatioPercent(100.0, 100.0), Is.EqualTo(100.0).Within(0.0001));
            Assert.That(SolreignAtmosDiagnosticEvaluator.CalculateOxygenRatioPercent(150.0, 100.0), Is.EqualTo(100.0).Within(0.0001));
        });
    }

    [Test]
    public void Evaluator_CalculateToxicGasRatioPercent_ValidatesBoundaries()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SolreignAtmosDiagnosticEvaluator.CalculateToxicGasRatioPercent(0.0, 100.0), Is.EqualTo(0.0));
            Assert.That(SolreignAtmosDiagnosticEvaluator.CalculateToxicGasRatioPercent(-5.0, 100.0), Is.EqualTo(0.0));
            Assert.That(SolreignAtmosDiagnosticEvaluator.CalculateToxicGasRatioPercent(1.0, 0.0), Is.EqualTo(0.0));
            Assert.That(SolreignAtmosDiagnosticEvaluator.CalculateToxicGasRatioPercent(1.0, 100.0), Is.EqualTo(1.0).Within(0.0001));
            Assert.That(SolreignAtmosDiagnosticEvaluator.CalculateToxicGasRatioPercent(50.0, 100.0), Is.EqualTo(50.0).Within(0.0001));
        });
    }

    [Test]
    public void Evaluator_DetectToxicGasPresence_EvaluatesThresholds()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SolreignAtmosDiagnosticEvaluator.DetectToxicGasPresence(0.0, 0.5f), Is.False);
            Assert.That(SolreignAtmosDiagnosticEvaluator.DetectToxicGasPresence(0.4, 0.5f), Is.False);
            Assert.That(SolreignAtmosDiagnosticEvaluator.DetectToxicGasPresence(0.5, 0.5f), Is.True);
            Assert.That(SolreignAtmosDiagnosticEvaluator.DetectToxicGasPresence(2.5, 0.5f), Is.True);
        });
    }

    [Test]
    public void Evaluator_CalculatePressureDropRate_ComputesRate()
    {
        Assert.Multiple(() =>
        {
            // Drop from 101.3 to 81.3 in 2 seconds -> (101.3 - 81.3) / 2 = 10.0 kPa/s
            Assert.That(SolreignAtmosDiagnosticEvaluator.CalculatePressureDropRate(81.3, 101.3, 2.0), Is.EqualTo(10.0).Within(0.0001));
            // Pressure increase or no change -> 0.0
            Assert.That(SolreignAtmosDiagnosticEvaluator.CalculatePressureDropRate(101.3, 101.3, 1.0), Is.EqualTo(0.0));
            Assert.That(SolreignAtmosDiagnosticEvaluator.CalculatePressureDropRate(105.0, 101.3, 1.0), Is.EqualTo(0.0));
            Assert.That(SolreignAtmosDiagnosticEvaluator.CalculatePressureDropRate(80.0, 100.0, 0.0), Is.EqualTo(0.0));
        });
    }

    [Test]
    public void Evaluator_DetectHullBreach_IdentifiesBreachAndRapidDepressurization()
    {
        Assert.Multiple(() =>
        {
            // Rapid pressure drop: drop rate 15.0 kPa/s >= 10.0 threshold
            var rapidBreach = SolreignAtmosDiagnosticEvaluator.DetectHullBreach(
                currentPressurekPa: 86.3,
                previousPressurekPa: 101.3,
                deltaTimeSeconds: 1.0,
                rapidDropRateThreshold: 10.0f,
                depressurizationThreshold: 20.0f);
            Assert.That(rapidBreach, Is.True);

            // Depressurized vacuum state (< 20 kPa)
            var vacuumBreach = SolreignAtmosDiagnosticEvaluator.DetectHullBreach(
                currentPressurekPa: 15.0,
                previousPressurekPa: 18.0,
                deltaTimeSeconds: 1.0,
                rapidDropRateThreshold: 10.0f,
                depressurizationThreshold: 20.0f);
            Assert.That(vacuumBreach, Is.True);

            // Normal pressure drop below threshold rate -> false
            var normalDrop = SolreignAtmosDiagnosticEvaluator.DetectHullBreach(
                currentPressurekPa: 96.3,
                previousPressurekPa: 101.3,
                deltaTimeSeconds: 1.0,
                rapidDropRateThreshold: 10.0f,
                depressurizationThreshold: 20.0f);
            Assert.That(normalDrop, Is.False);
        });
    }

    [Test]
    public void Evaluator_EvaluateAtmosCondition_DetectsNormalWarningCriticalAndDepressurized()
    {
        Assert.Multiple(() =>
        {
            // Normal state: 101.3 kPa, 21.0% O2, 0.0% toxic
            var normal = SolreignAtmosDiagnosticEvaluator.EvaluateAtmosCondition(
                pressurekPa: 101.3,
                o2RatioPercent: 21.0,
                toxicGasRatioPercent: 0.0,
                isHullBreach: false,
                lowPressureThreshold: 80.0f,
                criticalPressureThreshold: 50.0f,
                depressurizationThreshold: 20.0f,
                minO2Threshold: 19.5f,
                criticalO2Threshold: 12.0f,
                toxicGasThreshold: 0.5f);
            Assert.That(normal, Is.EqualTo(SolreignAtmosCondition.Normal));

            // Warning state due to low pressure (75 kPa)
            var warningPressure = SolreignAtmosDiagnosticEvaluator.EvaluateAtmosCondition(
                pressurekPa: 75.0,
                o2RatioPercent: 21.0,
                toxicGasRatioPercent: 0.0,
                isHullBreach: false,
                lowPressureThreshold: 80.0f,
                criticalPressureThreshold: 50.0f,
                depressurizationThreshold: 20.0f,
                minO2Threshold: 19.5f,
                criticalO2Threshold: 12.0f,
                toxicGasThreshold: 0.5f);
            Assert.That(warningPressure, Is.EqualTo(SolreignAtmosCondition.Warning));

            // Warning state due to mild hypoxia (18.0% O2)
            var warningO2 = SolreignAtmosDiagnosticEvaluator.EvaluateAtmosCondition(
                pressurekPa: 101.3,
                o2RatioPercent: 18.0,
                toxicGasRatioPercent: 0.0,
                isHullBreach: false,
                lowPressureThreshold: 80.0f,
                criticalPressureThreshold: 50.0f,
                depressurizationThreshold: 20.0f,
                minO2Threshold: 19.5f,
                criticalO2Threshold: 12.0f,
                toxicGasThreshold: 0.5f);
            Assert.That(warningO2, Is.EqualTo(SolreignAtmosCondition.Warning));

            // Warning state due to toxic gas presence (1.0%)
            var warningToxic = SolreignAtmosDiagnosticEvaluator.EvaluateAtmosCondition(
                pressurekPa: 101.3,
                o2RatioPercent: 21.0,
                toxicGasRatioPercent: 1.0,
                isHullBreach: false,
                lowPressureThreshold: 80.0f,
                criticalPressureThreshold: 50.0f,
                depressurizationThreshold: 20.0f,
                minO2Threshold: 19.5f,
                criticalO2Threshold: 12.0f,
                toxicGasThreshold: 0.5f);
            Assert.That(warningToxic, Is.EqualTo(SolreignAtmosCondition.Warning));

            // Critical state due to critical pressure drop (40.0 kPa)
            var criticalPressure = SolreignAtmosDiagnosticEvaluator.EvaluateAtmosCondition(
                pressurekPa: 40.0,
                o2RatioPercent: 21.0,
                toxicGasRatioPercent: 0.0,
                isHullBreach: false,
                lowPressureThreshold: 80.0f,
                criticalPressureThreshold: 50.0f,
                depressurizationThreshold: 20.0f,
                minO2Threshold: 19.5f,
                criticalO2Threshold: 12.0f,
                toxicGasThreshold: 0.5f);
            Assert.That(criticalPressure, Is.EqualTo(SolreignAtmosCondition.Critical));

            // Critical state due to severe hypoxia (10.0% O2)
            var criticalO2 = SolreignAtmosDiagnosticEvaluator.EvaluateAtmosCondition(
                pressurekPa: 101.3,
                o2RatioPercent: 10.0,
                toxicGasRatioPercent: 0.0,
                isHullBreach: false,
                lowPressureThreshold: 80.0f,
                criticalPressureThreshold: 50.0f,
                depressurizationThreshold: 20.0f,
                minO2Threshold: 19.5f,
                criticalO2Threshold: 12.0f,
                toxicGasThreshold: 0.5f);
            Assert.That(criticalO2, Is.EqualTo(SolreignAtmosCondition.Critical));

            // Depressurized state due to hull breach
            var depressurizedBreach = SolreignAtmosDiagnosticEvaluator.EvaluateAtmosCondition(
                pressurekPa: 60.0,
                o2RatioPercent: 21.0,
                toxicGasRatioPercent: 0.0,
                isHullBreach: true,
                lowPressureThreshold: 80.0f,
                criticalPressureThreshold: 50.0f,
                depressurizationThreshold: 20.0f,
                minO2Threshold: 19.5f,
                criticalO2Threshold: 12.0f,
                toxicGasThreshold: 0.5f);
            Assert.That(depressurizedBreach, Is.EqualTo(SolreignAtmosCondition.Depressurized));
        });
    }

    [Test]
    public void Evaluator_EvaluateAlertLevel_EscalatesAlertLevels()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SolreignAtmosDiagnosticEvaluator.EvaluateAlertLevel(SolreignAtmosCondition.Normal, false), Is.EqualTo(SolreignAtmosAlertLevel.None));
            Assert.That(SolreignAtmosDiagnosticEvaluator.EvaluateAlertLevel(SolreignAtmosCondition.Warning, false), Is.EqualTo(SolreignAtmosAlertLevel.Warning));
            Assert.That(SolreignAtmosDiagnosticEvaluator.EvaluateAlertLevel(SolreignAtmosCondition.Critical, false), Is.EqualTo(SolreignAtmosAlertLevel.Critical));
            Assert.That(SolreignAtmosDiagnosticEvaluator.EvaluateAlertLevel(SolreignAtmosCondition.Depressurized, false), Is.EqualTo(SolreignAtmosAlertLevel.Emergency));
            Assert.That(SolreignAtmosDiagnosticEvaluator.EvaluateAlertLevel(SolreignAtmosCondition.Warning, true), Is.EqualTo(SolreignAtmosAlertLevel.Emergency));
        });
    }

    [Test]
    public void System_InitialState_DefaultMetrics()
    {
        var sys = new SolreignAtmosDiagnosticSystem();

        Assert.Multiple(() =>
        {
            Assert.That(sys.IsEnabled, Is.False);
            Assert.That(sys.NormalPressurekPa, Is.EqualTo(101.3f));
            Assert.That(sys.LowPressureThresholdkPa, Is.EqualTo(80.0f));
            Assert.That(sys.CriticalPressureThresholdkPa, Is.EqualTo(50.0f));
            Assert.That(sys.DepressurizationThresholdkPa, Is.EqualTo(20.0f));
            Assert.That(sys.MinOxygenRatioPercent, Is.EqualTo(19.5f));
            Assert.That(sys.CriticalOxygenRatioPercent, Is.EqualTo(12.0f));
            Assert.That(sys.ToxicGasThresholdPercent, Is.EqualTo(0.5f));
            Assert.That(sys.RapidDropRateThresholdkPaPerSec, Is.EqualTo(10.0f));
            Assert.That(sys.RoomPressurekPa, Is.EqualTo(101.3).Within(0.0001));
            Assert.That(sys.OxygenRatioPercent, Is.EqualTo(21.0).Within(0.0001));
            Assert.That(sys.ToxicGasRatioPercent, Is.EqualTo(0.0));
            Assert.That(sys.ToxicGasPresent, Is.False);
            Assert.That(sys.IsHullBreach, Is.False);
            Assert.That(sys.CurrentCondition, Is.EqualTo(SolreignAtmosCondition.Normal));
            Assert.That(sys.CurrentAlertLevel, Is.EqualTo(SolreignAtmosAlertLevel.None));
        });
    }

    [Test]
    public void AtmosDiagnosticsCVar_IsReplicatedAndDefaultOn()
    {
        // Default ON + replicated: the 2026-07-24 owner activation decision (enable rather
        // than withhold; reverse at runtime if it misbehaves). This branch's original
        // dark-by-default assertion predated that ruling.
        Assert.Multiple(() =>
        {
            Assert.That(CCVars.SolreignAtmosDiagnosticsEnabled.DefaultValue, Is.True);
            Assert.That(CCVars.SolreignAtmosDiagnosticsEnabled.Flags, Is.EqualTo(CVar.SERVER));
        });
    }

    [Test]
    public void FirstSourceSample_DoesNotInventRapidDepressurization()
    {
        var sys = new SolreignAtmosDiagnosticSystem { IsEnabled = true };

        var snapshot = sys.UpdateAtmosMetricsForSource(
            new EntityUid(10),
            new EntityUid(100),
            roomPressurekPa: 75d,
            oxygenAmount: 21d,
            toxicAmount: 0d,
            totalGasAmount: 100d,
            deltaTimeSeconds: 1d);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Condition, Is.EqualTo(SolreignAtmosCondition.Warning));
            Assert.That(snapshot.IsHullBreach, Is.False);
            Assert.That(snapshot.PressureDropRatekPaPerSec, Is.Zero);
        });
    }

    [Test]
    public void SourcesKeepIndependentPressureHistory()
    {
        var sys = new SolreignAtmosDiagnosticSystem { IsEnabled = true };

        sys.UpdateAtmosMetricsForSource(
            new EntityUid(10),
            new EntityUid(100),
            101.3d,
            21d,
            0d,
            100d,
            1d);

        var secondSource = sys.UpdateAtmosMetricsForSource(
            new EntityUid(11),
            new EntityUid(100),
            75d,
            21d,
            0d,
            100d,
            1d);

        Assert.Multiple(() =>
        {
            Assert.That(secondSource.Condition, Is.EqualTo(SolreignAtmosCondition.Warning));
            Assert.That(secondSource.IsHullBreach, Is.False);
            Assert.That(secondSource.PressureDropRatekPaPerSec, Is.Zero);
        });
    }

    [Test]
    public void SourceGridChange_ResetsPressureBaseline()
    {
        var sys = new SolreignAtmosDiagnosticSystem { IsEnabled = true };
        var source = new EntityUid(10);

        sys.UpdateAtmosMetricsForSource(source, new EntityUid(100), 101.3d, 21d, 0d, 100d, 1d);
        var moved = sys.UpdateAtmosMetricsForSource(source, new EntityUid(101), 75d, 21d, 0d, 100d, 1d);

        Assert.Multiple(() =>
        {
            Assert.That(moved.Condition, Is.EqualTo(SolreignAtmosCondition.Warning));
            Assert.That(moved.IsHullBreach, Is.False);
            Assert.That(moved.PressureDropRatekPaPerSec, Is.Zero);
        });
    }

    [Test]
    public void ImmutableSpaceMixtureIsSampledAsVacuum()
    {
        var sys = new SolreignAtmosDiagnosticSystem { IsEnabled = true };

        var sampled = sys.TryUpdateAtmosMetricsForSource(
            new EntityUid(10),
            new EntityUid(100),
            GasMixture.SpaceGas,
            1d);

        Assert.Multiple(() =>
        {
            Assert.That(sampled, Is.True);
            Assert.That(sys.RoomPressurekPa, Is.Zero);
            Assert.That(sys.CurrentCondition, Is.EqualTo(SolreignAtmosCondition.Depressurized));
        });
    }

    [Test]
    public void SampleGateCoalescesSourceUpdatesWithinFiveSecondWindow()
    {
        var gate = new SolreignAtmosDiagnosticSampleGate(TimeSpan.FromSeconds(5));
        var source = new EntityUid(10);

        Assert.Multiple(() =>
        {
            Assert.That(gate.TryBeginSample(source, TimeSpan.FromSeconds(10)), Is.True);
            Assert.That(gate.TryBeginSample(source, TimeSpan.FromSeconds(12)), Is.False);
            Assert.That(gate.TryBeginSample(source, TimeSpan.FromSeconds(15)), Is.True);
            Assert.That(gate.TryBeginSample(new EntityUid(11), TimeSpan.FromSeconds(12)), Is.True);
        });
    }

    [Test]
    public void MetricThrottleDoesNotDelayHazardTransitionEvent()
    {
        var sys = new SolreignAtmosDiagnosticSystem { IsEnabled = true };
        var raisedEvents = new List<object>();
        var source = new EntityUid(10);
        var grid = new EntityUid(100);
        sys.OnEventRaised = raisedEvents.Add;

        sys.UpdateAtmosMetricsForSource(
            source,
            grid,
            101.3d,
            21d,
            0d,
            100d,
            1d,
            emitMetric: true);
        raisedEvents.Clear();

        sys.UpdateAtmosMetricsForSource(
            source,
            grid,
            75d,
            21d,
            0d,
            100d,
            1d,
            emitMetric: false);

        Assert.Multiple(() =>
        {
            Assert.That(raisedEvents, Has.None.InstanceOf<SolreignAtmosDiagnosticMetricEvent>());
            Assert.That(raisedEvents, Has.Exactly(1).InstanceOf<SolreignAtmosDisruptionEvent>());
            Assert.That(sys.CurrentCondition, Is.EqualTo(SolreignAtmosCondition.Depressurized));
        });
    }

    [Test]
    public void System_UpdateAtmosMetrics_TracksMetricsAndRaisesEvents()
    {
        var sys = new SolreignAtmosDiagnosticSystem { IsEnabled = true };
        var raisedEvents = new List<object>();
        sys.OnEventRaised = raisedEvents.Add;

        // Update with normal atmos values: 101.3 kPa, 21 mol O2 out of 100 total mol
        var snap1 = sys.UpdateAtmosMetrics(
            roomPressurekPa: 101.3,
            oxygenAmount: 21.0,
            toxicAmount: 0.0,
            totalGasAmount: 100.0,
            deltaTimeSeconds: 1.0);

        Assert.Multiple(() =>
        {
            Assert.That(sys.RoomPressurekPa, Is.EqualTo(101.3).Within(0.0001));
            Assert.That(sys.OxygenRatioPercent, Is.EqualTo(21.0).Within(0.0001));
            Assert.That(sys.ToxicGasRatioPercent, Is.EqualTo(0.0));
            Assert.That(sys.ToxicGasPresent, Is.False);
            Assert.That(sys.CurrentCondition, Is.EqualTo(SolreignAtmosCondition.Normal));
            Assert.That(sys.CurrentAlertLevel, Is.EqualTo(SolreignAtmosAlertLevel.None));
            Assert.That(snap1.IsDisrupted, Is.False);
            Assert.That(snap1.IsHullBreachAlertActive, Is.False);
            Assert.That(raisedEvents.Count, Is.EqualTo(1));
            Assert.That(raisedEvents[0], Is.InstanceOf<SolreignAtmosDiagnosticMetricEvent>());
        });

        raisedEvents.Clear();

        // Simulate rapid depressurization (hull breach alert): pressure drop from 101.3 to 75.0 in 1 second (26.3 kPa/s drop)
        var snap2 = sys.UpdateAtmosMetrics(
            roomPressurekPa: 75.0,
            oxygenAmount: 21.0,
            toxicAmount: 0.0,
            totalGasAmount: 100.0,
            deltaTimeSeconds: 1.0);

        Assert.Multiple(() =>
        {
            Assert.That(sys.RoomPressurekPa, Is.EqualTo(75.0).Within(0.0001));
            Assert.That(sys.IsHullBreach, Is.True);
            Assert.That(sys.CurrentCondition, Is.EqualTo(SolreignAtmosCondition.Depressurized));
            Assert.That(sys.CurrentAlertLevel, Is.EqualTo(SolreignAtmosAlertLevel.Emergency));
            Assert.That(snap2.IsDisrupted, Is.True);
            Assert.That(snap2.IsHullBreachAlertActive, Is.True);
            Assert.That(raisedEvents.Count, Is.EqualTo(2)); // Metric event + Disruption event
            Assert.That(raisedEvents[0], Is.InstanceOf<SolreignAtmosDiagnosticMetricEvent>());
            Assert.That(raisedEvents[1], Is.InstanceOf<SolreignAtmosDisruptionEvent>());
        });
    }

    [Test]
    public void System_ToxicGasDetection_TriggersDisruptionAlert()
    {
        var sys = new SolreignAtmosDiagnosticSystem { IsEnabled = true };
        var raisedEvents = new List<object>();
        sys.OnEventRaised = raisedEvents.Add;

        // Update with 2% toxic gas ratio -> warning level
        var snap = sys.UpdateDirectAtmosMetrics(
            roomPressurekPa: 101.3,
            oxygenRatioPercent: 21.0,
            toxicGasRatioPercent: 2.0,
            deltaTimeSeconds: 1.0);

        Assert.Multiple(() =>
        {
            Assert.That(sys.ToxicGasPresent, Is.True);
            Assert.That(sys.CurrentCondition, Is.EqualTo(SolreignAtmosCondition.Warning));
            Assert.That(sys.CurrentAlertLevel, Is.EqualTo(SolreignAtmosAlertLevel.Warning));
            Assert.That(snap.ToxicGasPresent, Is.True);
            Assert.That(raisedEvents.Count, Is.EqualTo(2));
            Assert.That(raisedEvents[1], Is.InstanceOf<SolreignAtmosDisruptionEvent>());
        });
    }

    [Test]
    public void System_DisabledState_SuppressesTracking()
    {
        var sys = new SolreignAtmosDiagnosticSystem
        {
            IsEnabled = false
        };

        var raisedEvents = new List<object>();
        sys.OnEventRaised = raisedEvents.Add;

        var snap = sys.UpdateAtmosMetrics(
            roomPressurekPa: 0.0,
            oxygenAmount: 0.0,
            toxicAmount: 50.0,
            totalGasAmount: 100.0,
            deltaTimeSeconds: 1.0);

        Assert.Multiple(() =>
        {
            Assert.That(sys.RoomPressurekPa, Is.EqualTo(101.3).Within(0.0001));
            Assert.That(snap.Enabled, Is.False);
            Assert.That(raisedEvents, Is.Empty);
        });
    }

    [Test]
    public void System_ResetMetrics_ClearsAtmosphericState()
    {
        var sys = new SolreignAtmosDiagnosticSystem { IsEnabled = true };

        sys.UpdateDirectAtmosMetrics(
            roomPressurekPa: 10.0,
            oxygenRatioPercent: 5.0,
            toxicGasRatioPercent: 10.0,
            deltaTimeSeconds: 1.0);

        Assert.That(sys.CurrentCondition, Is.EqualTo(SolreignAtmosCondition.Depressurized));

        sys.ResetMetrics();

        Assert.Multiple(() =>
        {
            Assert.That(sys.RoomPressurekPa, Is.EqualTo(101.3).Within(0.0001));
            Assert.That(sys.OxygenRatioPercent, Is.EqualTo(21.0).Within(0.0001));
            Assert.That(sys.ToxicGasRatioPercent, Is.EqualTo(0.0));
            Assert.That(sys.ToxicGasPresent, Is.False);
            Assert.That(sys.IsHullBreach, Is.False);
            Assert.That(sys.CurrentCondition, Is.EqualTo(SolreignAtmosCondition.Normal));
            Assert.That(sys.CurrentAlertLevel, Is.EqualTo(SolreignAtmosAlertLevel.None));
        });
    }

    [Test]
    public void Event_Properties_MatchConstructorArguments()
    {
        var timeStamp = TimeSpan.FromSeconds(120);
        var metricEv = new SolreignAtmosDiagnosticMetricEvent(
            timeStamp,
            101.3,
            21.0,
            0.0,
            false,
            false,
            0.0,
            SolreignAtmosCondition.Normal,
            SolreignAtmosAlertLevel.None);

        Assert.Multiple(() =>
        {
            Assert.That(metricEv.TimeStamp, Is.EqualTo(timeStamp));
            Assert.That(metricEv.RoomPressurekPa, Is.EqualTo(101.3));
            Assert.That(metricEv.OxygenRatioPercent, Is.EqualTo(21.0));
            Assert.That(metricEv.ToxicGasRatioPercent, Is.EqualTo(0.0));
            Assert.That(metricEv.ToxicGasPresent, Is.False);
            Assert.That(metricEv.IsHullBreach, Is.False);
            Assert.That(metricEv.PressureDropRatekPaPerSec, Is.EqualTo(0.0));
            Assert.That(metricEv.Condition, Is.EqualTo(SolreignAtmosCondition.Normal));
            Assert.That(metricEv.AlertLevel, Is.EqualTo(SolreignAtmosAlertLevel.None));
        });

        var disruptionEv = new SolreignAtmosDisruptionEvent(
            timeStamp,
            SolreignAtmosCondition.Normal,
            SolreignAtmosCondition.Depressurized,
            SolreignAtmosAlertLevel.Emergency,
            15.0,
            21.0,
            0.0,
            false,
            true,
            "Depressurization detected.");

        Assert.Multiple(() =>
        {
            Assert.That(disruptionEv.TimeStamp, Is.EqualTo(timeStamp));
            Assert.That(disruptionEv.PreviousCondition, Is.EqualTo(SolreignAtmosCondition.Normal));
            Assert.That(disruptionEv.NewCondition, Is.EqualTo(SolreignAtmosCondition.Depressurized));
            Assert.That(disruptionEv.AlertLevel, Is.EqualTo(SolreignAtmosAlertLevel.Emergency));
            Assert.That(disruptionEv.Details, Is.EqualTo("Depressurization detected."));
        });
    }
}
