using System;
using System.Collections.Generic;
using Content.Server.GameTicking.Events;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Power.NodeGroups;
using Content.Server.Power.Pow3r;
using Content.Server.Power.SMES;
using Content.Server.Station.Systems;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.Diagnostics;

/// <summary>
///     Grid power condition status under SR-REF-015.
/// </summary>
public enum SolreignPowerCondition : byte
{
    Normal = 0,
    Brownout = 1,
    Blackout = 2,
}

/// <summary>
///     Automated load-shedding alert severity levels under SR-REF-015.
/// </summary>
public enum SolreignLoadSheddingAlertLevel : byte
{
    None = 0,
    Warning = 1,      // Low battery headroom or mild brownout
    Critical = 2,     // Severe power deficit or critical headroom
    Emergency = 3,    // Complete station blackout / battery depletion
}

/// <summary>
///     Immutable snapshot of station grid power diagnostic state.
/// </summary>
public readonly record struct SolreignPowerDiagnosticSnapshot(
    bool Enabled,
    double GridPowerDrawkW,
    double GridPowerSupplykW,
    double BatteryCapacitykWh,
    double BatteryReservekWh,
    double BatteryHeadroomPercent,
    double PowerDeficitkW,
    double EstimatedReserveTimeSeconds,
    SolreignPowerCondition Condition,
    SolreignLoadSheddingAlertLevel AlertLevel,
    double RecommendedLoadSheddingkW,
    float BrownoutThresholdPercent,
    float CriticalThresholdPercent,
    float LoadSheddingThresholdkW)
{
    public bool IsDisrupted => Condition != SolreignPowerCondition.Normal;
    public bool IsLoadSheddingAlertActive => AlertLevel != SolreignLoadSheddingAlertLevel.None;
}

/// <summary>
///     Diagnostic telemetry event raised on power sample updates when <see cref="SolreignPowerDiagnosticSystem"/> is enabled.
/// </summary>
public sealed class SolreignPowerDiagnosticMetricEvent : EntityEventArgs
{
    public TimeSpan TimeStamp { get; }
    public double GridPowerDrawkW { get; }
    public double GridPowerSupplykW { get; }
    public double BatteryCapacitykWh { get; }
    public double BatteryReservekWh { get; }
    public double BatteryHeadroomPercent { get; }
    public double PowerDeficitkW { get; }
    public double EstimatedReserveTimeSeconds { get; }
    public SolreignPowerCondition Condition { get; }
    public SolreignLoadSheddingAlertLevel AlertLevel { get; }
    public double RecommendedLoadSheddingkW { get; }

    public SolreignPowerDiagnosticMetricEvent(
        TimeSpan timeStamp,
        double gridPowerDrawkW,
        double gridPowerSupplykW,
        double batteryCapacitykWh,
        double batteryReservekWh,
        double batteryHeadroomPercent,
        double powerDeficitkW,
        double estimatedReserveTimeSeconds,
        SolreignPowerCondition condition,
        SolreignLoadSheddingAlertLevel alertLevel,
        double recommendedLoadSheddingkW)
    {
        TimeStamp = timeStamp;
        GridPowerDrawkW = gridPowerDrawkW;
        GridPowerSupplykW = gridPowerSupplykW;
        BatteryCapacitykWh = batteryCapacitykWh;
        BatteryReservekWh = batteryReservekWh;
        BatteryHeadroomPercent = batteryHeadroomPercent;
        PowerDeficitkW = powerDeficitkW;
        EstimatedReserveTimeSeconds = estimatedReserveTimeSeconds;
        Condition = condition;
        AlertLevel = alertLevel;
        RecommendedLoadSheddingkW = recommendedLoadSheddingkW;
    }
}

/// <summary>
///     Alert event raised when grid power disruption occurs or load-shedding level escalates.
/// </summary>
public sealed class SolreignPowerDisruptionEvent : EntityEventArgs
{
    public TimeSpan TimeStamp { get; }
    public SolreignPowerCondition PreviousCondition { get; }
    public SolreignPowerCondition NewCondition { get; }
    public SolreignLoadSheddingAlertLevel AlertLevel { get; }
    public double GridPowerDrawkW { get; }
    public double GridPowerSupplykW { get; }
    public double BatteryHeadroomPercent { get; }
    public double PowerDeficitkW { get; }
    public double RecommendedLoadSheddingkW { get; }
    public string Details { get; }

    public SolreignPowerDisruptionEvent(
        TimeSpan timeStamp,
        SolreignPowerCondition previousCondition,
        SolreignPowerCondition newCondition,
        SolreignLoadSheddingAlertLevel alertLevel,
        double gridPowerDrawkW,
        double gridPowerSupplykW,
        double batteryHeadroomPercent,
        double powerDeficitkW,
        double recommendedLoadSheddingkW,
        string details)
    {
        TimeStamp = timeStamp;
        PreviousCondition = previousCondition;
        NewCondition = newCondition;
        AlertLevel = alertLevel;
        GridPowerDrawkW = gridPowerDrawkW;
        GridPowerSupplykW = gridPowerSupplykW;
        BatteryHeadroomPercent = batteryHeadroomPercent;
        PowerDeficitkW = powerDeficitkW;
        RecommendedLoadSheddingkW = recommendedLoadSheddingkW;
        Details = details;
    }
}

/// <summary>
///     Pure, unit-testable evaluation functions for grid power math and disruption status.
/// </summary>
public static class SolreignPowerDiagnosticEvaluator
{
    public static double CalculateHeadroomPercent(double reservekWh, double capacitykWh)
    {
        if (capacitykWh <= 0.0 || reservekWh <= 0.0)
            return 0.0;
        if (reservekWh >= capacitykWh)
            return 100.0;

        return Math.Clamp((reservekWh / capacitykWh) * 100.0, 0.0, 100.0);
    }

    public static double CalculatePowerDeficit(double drawkW, double supplykW)
    {
        if (drawkW <= 0.0)
            return 0.0;
        return Math.Max(0.0, drawkW - Math.Max(0.0, supplykW));
    }

    public static double CalculateEstimatedReserveTimeSeconds(double reservekWh, double deficitkW)
    {
        if (reservekWh <= 0.0)
            return 0.0;
        if (deficitkW <= 0.0)
            return double.PositiveInfinity;

        return (reservekWh * 3600.0) / deficitkW;
    }

    public static SolreignPowerCondition EvaluatePowerCondition(
        double drawkW,
        double supplykW,
        double reservekWh,
        double capacitykWh,
        float brownoutThresholdPercent)
    {
        var headroom = CalculateHeadroomPercent(reservekWh, capacitykWh);

        // Complete blackout requires live demand with neither supply nor stored reserve.
        // A discharged backup on an otherwise supplied grid is a reserve warning, not a blackout.
        if (supplykW <= 0.0 && drawkW > 0.0 && reservekWh <= 0.0)
            return SolreignPowerCondition.Blackout;

        // A grid with no installed reserve is not automatically unhealthy when supply meets demand.
        if (drawkW > supplykW ||
            (capacitykWh > 0.0 && headroom < brownoutThresholdPercent))
            return SolreignPowerCondition.Brownout;

        return SolreignPowerCondition.Normal;
    }

    public static SolreignLoadSheddingAlertLevel EvaluateAlertLevel(
        SolreignPowerCondition condition,
        double headroomPercent,
        double powerDeficitkW,
        float brownoutThreshold,
        float criticalThreshold,
        float loadSheddingThresholdKw)
    {
        if (condition == SolreignPowerCondition.Normal)
            return SolreignLoadSheddingAlertLevel.None;

        if (condition == SolreignPowerCondition.Blackout)
            return SolreignLoadSheddingAlertLevel.Emergency;

        if (headroomPercent <= criticalThreshold || (condition == SolreignPowerCondition.Brownout && powerDeficitkW >= loadSheddingThresholdKw))
            return SolreignLoadSheddingAlertLevel.Critical;

        if (condition == SolreignPowerCondition.Brownout || headroomPercent < brownoutThreshold)
            return SolreignLoadSheddingAlertLevel.Warning;

        return SolreignLoadSheddingAlertLevel.None;
    }

    public static double CalculateRecommendedLoadSheddingkW(
        double drawkW,
        double supplykW,
        double headroomPercent,
        float brownoutThresholdPercent)
    {
        if (drawkW <= 0.0)
            return 0.0;

        var deficit = CalculatePowerDeficit(drawkW, supplykW);

        if (deficit > 0.0)
        {
            return deficit;
        }

        // If draw <= supply but headroom is low, recommend shedding enough draw to stabilize headroom
        if (headroomPercent < brownoutThresholdPercent && supplykW > 0.0)
        {
            var headroomFactor = (brownoutThresholdPercent - headroomPercent) / brownoutThresholdPercent;
            return Math.Min(drawkW, drawkW * (headroomFactor * 0.5));
        }

        return 0.0;
    }
}

internal readonly record struct SolreignPowerDiagnosticTotals(
    double DrawkW,
    double SupplykW,
    double BatteryCapacitykWh,
    double BatteryReservekWh);

/// <summary>
///     Aggregates post-solver power state without counting a shared output network once per SMES.
/// </summary>
internal sealed class SolreignPowerDiagnosticCollector
{
    private const double WattsPerKilowatt = 1_000d;
    private const double JoulesPerKilowattHour = 3_600_000d;

    private sealed class NetworkTotals
    {
        public double DrawWatts;
        public double SupplyWatts;
        public double BatteryCapacityJoules;
        public double BatteryReserveJoules;
    }

    private readonly Dictionary<PowerState.Network, NetworkTotals> _networks = new();

    public bool HasLiveSample => _networks.Count > 0;

    public void AddNetworkBattery(PowerState.Network network, PowerState.Battery battery, bool available)
    {
        if (!_networks.TryGetValue(network, out var totals))
        {
            totals = new NetworkTotals
            {
                DrawWatts = Math.Max(0d, network.LastCombinedLoad),
                SupplyWatts = Math.Max(0d, network.LastCombinedSupply),
            };
            _networks.Add(network, totals);
        }

        if (!available)
            return;

        totals.BatteryCapacityJoules += Math.Max(0d, battery.Capacity);
        totals.BatteryReserveJoules += Math.Clamp(
            battery.CurrentStorage,
            0d,
            Math.Max(0d, battery.Capacity));
    }

    public SolreignPowerDiagnosticTotals ToDiagnosticUnits()
    {
        NetworkTotals? selected = null;
        foreach (var totals in _networks.Values)
        {
            if (selected == null || IsHigherPriority(totals, selected))
                selected = totals;
        }

        if (selected == null)
            return default;

        return new SolreignPowerDiagnosticTotals(
            selected.DrawWatts / WattsPerKilowatt,
            selected.SupplyWatts / WattsPerKilowatt,
            selected.BatteryCapacityJoules / JoulesPerKilowattHour,
            selected.BatteryReserveJoules / JoulesPerKilowattHour);
    }

    private static bool IsHigherPriority(NetworkTotals candidate, NetworkTotals current)
    {
        var candidateDeficit = Math.Max(0d, candidate.DrawWatts - candidate.SupplyWatts);
        var currentDeficit = Math.Max(0d, current.DrawWatts - current.SupplyWatts);
        if (candidateDeficit != currentDeficit)
            return candidateDeficit > currentDeficit;

        // Installed reserve can express a meaningful low-headroom signal. Absence of a battery
        // cannot, so only compare headroom when both networks actually have reserve capacity.
        if (candidate.BatteryCapacityJoules > 0d && current.BatteryCapacityJoules > 0d)
        {
            var candidateHeadroom = SolreignPowerDiagnosticEvaluator.CalculateHeadroomPercent(
                candidate.BatteryReserveJoules,
                candidate.BatteryCapacityJoules);
            var currentHeadroom = SolreignPowerDiagnosticEvaluator.CalculateHeadroomPercent(
                current.BatteryReserveJoules,
                current.BatteryCapacityJoules);
            if (candidateHeadroom != currentHeadroom)
                return candidateHeadroom < currentHeadroom;
        }

        if (candidate.SupplyWatts != current.SupplyWatts)
            return candidate.SupplyWatts < current.SupplyWatts;
        if (candidate.DrawWatts != current.DrawWatts)
            return candidate.DrawWatts > current.DrawWatts;

        // Equal-load networks can reconnect in a different ECS query order. Prefer the one with
        // less absolute reserve so estimated endurance remains deterministic and conservative.
        if (candidate.BatteryReserveJoules != current.BatteryReserveJoules)
            return candidate.BatteryReserveJoules < current.BatteryReserveJoules;

        // With equal zero reserve, installed depleted capacity is the stronger blackout signal.
        // If capacity is equal as well, every value exposed by this collector is equivalent.
        return candidate.BatteryCapacityJoules > current.BatteryCapacityJoules;
    }
}

/// <summary>
///     SR-REF-015: Solreign station grid load and power disruption diagnostic system.
///     Tracks grid power draw, battery reserve headroom, estimated reserve endurance,
///     and automated load-shedding alerts under emergency blackout/brownout conditions.
/// </summary>
public sealed partial class SolreignPowerDiagnosticSystem : EntitySystem
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(5);

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private StationSystem _station = default!;

    private bool _enabled;
    private float _brownoutHeadroomThresholdPercent = 20.0f;
    private float _criticalHeadroomThresholdPercent = 5.0f;
    private float _loadSheddingThresholdKw = 50.0f;

    private double _gridPowerDrawkW;
    private double _gridPowerSupplykW;
    private double _batteryCapacitykWh;
    private double _batteryReservekWh;

    private SolreignPowerCondition _currentCondition = SolreignPowerCondition.Normal;
    private SolreignLoadSheddingAlertLevel _currentAlertLevel = SolreignLoadSheddingAlertLevel.None;
    private TimeSpan _nextSample;

    public bool IsEnabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public float BrownoutHeadroomThresholdPercent
    {
        get => _brownoutHeadroomThresholdPercent;
        set => _brownoutHeadroomThresholdPercent = Math.Clamp(value, 0.0f, 100.0f);
    }

    public float CriticalHeadroomThresholdPercent
    {
        get => _criticalHeadroomThresholdPercent;
        set => _criticalHeadroomThresholdPercent = Math.Clamp(value, 0.0f, 100.0f);
    }

    public float LoadSheddingThresholdKw
    {
        get => _loadSheddingThresholdKw;
        set => _loadSheddingThresholdKw = Math.Max(0.0f, value);
    }

    public double GridPowerDrawkW => _gridPowerDrawkW;
    public double GridPowerSupplykW => _gridPowerSupplykW;
    public double BatteryCapacitykWh => _batteryCapacitykWh;
    public double BatteryReservekWh => _batteryReservekWh;
    public double BatteryHeadroomPercent => SolreignPowerDiagnosticEvaluator.CalculateHeadroomPercent(_batteryReservekWh, _batteryCapacitykWh);
    public double PowerDeficitkW => SolreignPowerDiagnosticEvaluator.CalculatePowerDeficit(_gridPowerDrawkW, _gridPowerSupplykW);
    public double EstimatedReserveTimeSeconds => SolreignPowerDiagnosticEvaluator.CalculateEstimatedReserveTimeSeconds(_batteryReservekWh, PowerDeficitkW);

    public SolreignPowerCondition CurrentCondition => _currentCondition;
    public SolreignLoadSheddingAlertLevel CurrentAlertLevel => _currentAlertLevel;

    public Action<object>? OnEventRaised { get; set; }

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.SolreignPowerDiagnosticsEnabled, OnEnabledChanged, invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignPowerBrownoutHeadroomThreshold, v => _brownoutHeadroomThresholdPercent = Math.Clamp(v, 0.0f, 100.0f), invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignPowerCriticalHeadroomThreshold, v => _criticalHeadroomThresholdPercent = Math.Clamp(v, 0.0f, 100.0f), invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignPowerLoadSheddingThresholdKw, v => _loadSheddingThresholdKw = Math.Max(0.0f, v), invokeImmediately: true);

        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        SubscribeLocalEvent<NetworkBatteryPostSync>(OnNetworkBatteryPostSync);
    }

    private void OnEnabledChanged(bool value)
    {
        _enabled = value;
        _nextSample = TimeSpan.Zero;

        if (!value)
            ResetMetrics();
    }

    private void OnRoundStarting(RoundStartingEvent ev) => ResetMetrics();
    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev) => ResetMetrics();

    private void OnNetworkBatteryPostSync(NetworkBatteryPostSync ev)
    {
        if (!_enabled || _timing.CurTime < _nextSample)
            return;

        _nextSample = _timing.CurTime + SampleInterval;
        var collector = new SolreignPowerDiagnosticCollector();
        var query = EntityQueryEnumerator<
            SmesComponent,
            BatteryDischargerComponent,
            PowerNetworkBatteryComponent,
            TransformComponent>();

        while (query.MoveNext(out var uid, out _, out var discharger, out var battery, out var xform))
        {
            if (_station.GetOwningStation(uid, xform) == null ||
                !battery.Enabled ||
                battery.NetworkBattery.Paused ||
                discharger.Net is not PowerNet net ||
                net.Removed ||
                net.Remaking ||
                !net.IsConnectedNetwork)
            {
                continue;
            }

            collector.AddNetworkBattery(
                net.NetworkNode,
                battery.NetworkBattery,
                battery.CanDischarge);
        }

        var totals = collector.ToDiagnosticUnits();
        if (!collector.HasLiveSample)
            return;

        UpdateGridPower(
            totals.DrawkW,
            totals.SupplykW,
            totals.BatteryCapacitykWh,
            totals.BatteryReservekWh);
    }

    private void RaiseDiagnosticEvent<T>(T ev) where T : notnull
    {
        OnEventRaised?.Invoke(ev);

        if (EntityManager != null)
        {
            RaiseLocalEvent(ev);
        }
    }

    /// <summary>
    ///     Update current grid metrics and evaluate disruption / load shedding status.
    /// </summary>
    public SolreignPowerDiagnosticSnapshot UpdateGridPower(
        double drawkW,
        double supplykW,
        double batteryCapacitykWh,
        double batteryReservekWh)
    {
        if (!_enabled)
            return GetSnapshot();

        _gridPowerDrawkW = Math.Max(0.0, drawkW);
        _gridPowerSupplykW = Math.Max(0.0, supplykW);
        _batteryCapacitykWh = Math.Max(0.0, batteryCapacitykWh);
        _batteryReservekWh = Math.Clamp(batteryReservekWh, 0.0, _batteryCapacitykWh);

        return RecordSample();
    }

    /// <summary>
    ///     Record power diagnostic sample, evaluate conditions, and raise events if state changed.
    /// </summary>
    public SolreignPowerDiagnosticSnapshot RecordSample()
    {
        if (!_enabled)
            return GetSnapshot();

        var headroom = BatteryHeadroomPercent;
        var deficit = PowerDeficitkW;
        var reserveTime = EstimatedReserveTimeSeconds;

        var newCondition = SolreignPowerDiagnosticEvaluator.EvaluatePowerCondition(
            _gridPowerDrawkW,
            _gridPowerSupplykW,
            _batteryReservekWh,
            _batteryCapacitykWh,
            _brownoutHeadroomThresholdPercent);

        var newAlertLevel = SolreignPowerDiagnosticEvaluator.EvaluateAlertLevel(
            newCondition,
            headroom,
            deficit,
            _brownoutHeadroomThresholdPercent,
            _criticalHeadroomThresholdPercent,
            _loadSheddingThresholdKw);

        var recShedding = SolreignPowerDiagnosticEvaluator.CalculateRecommendedLoadSheddingkW(
            _gridPowerDrawkW,
            _gridPowerSupplykW,
            headroom,
            _brownoutHeadroomThresholdPercent);

        var prevCondition = _currentCondition;
        var prevAlertLevel = _currentAlertLevel;

        _currentCondition = newCondition;
        _currentAlertLevel = newAlertLevel;

        var timeStamp = _timing != null ? _timing.CurTime : TimeSpan.Zero;

        var metricEv = new SolreignPowerDiagnosticMetricEvent(
            timeStamp,
            _gridPowerDrawkW,
            _gridPowerSupplykW,
            _batteryCapacitykWh,
            _batteryReservekWh,
            headroom,
            deficit,
            reserveTime,
            newCondition,
            newAlertLevel,
            recShedding);

        RaiseDiagnosticEvent(metricEv);

        if (newCondition != prevCondition || (newAlertLevel != prevAlertLevel && newAlertLevel != SolreignLoadSheddingAlertLevel.None))
        {
            var details = $"Grid state transitioned from {prevCondition} (Alert: {prevAlertLevel}) to {newCondition} (Alert: {newAlertLevel}). Deficit: {deficit:F1}kW, Reserve Headroom: {headroom:F1}%. Recommended Shedding: {recShedding:F1}kW.";
            var disruptionEv = new SolreignPowerDisruptionEvent(
                timeStamp,
                prevCondition,
                newCondition,
                newAlertLevel,
                _gridPowerDrawkW,
                _gridPowerSupplykW,
                headroom,
                deficit,
                recShedding,
                details);

            RaiseDiagnosticEvent(disruptionEv);
        }

        return GetSnapshot();
    }

    /// <summary>
    ///     Reset grid metrics and condition state back to defaults.
    /// </summary>
    public void ResetMetrics()
    {
        _nextSample = TimeSpan.Zero;
        _gridPowerDrawkW = 0.0;
        _gridPowerSupplykW = 0.0;
        _batteryCapacitykWh = 0.0;
        _batteryReservekWh = 0.0;
        _currentCondition = SolreignPowerCondition.Normal;
        _currentAlertLevel = SolreignLoadSheddingAlertLevel.None;
    }

    /// <summary>
    ///     Get an immutable snapshot of current grid power diagnostic metrics.
    /// </summary>
    public SolreignPowerDiagnosticSnapshot GetSnapshot()
    {
        var headroom = BatteryHeadroomPercent;
        var deficit = PowerDeficitkW;
        var reserveTime = EstimatedReserveTimeSeconds;
        var recShedding = SolreignPowerDiagnosticEvaluator.CalculateRecommendedLoadSheddingkW(
            _gridPowerDrawkW,
            _gridPowerSupplykW,
            headroom,
            _brownoutHeadroomThresholdPercent);

        return new SolreignPowerDiagnosticSnapshot(
            _enabled,
            _gridPowerDrawkW,
            _gridPowerSupplykW,
            _batteryCapacitykWh,
            _batteryReservekWh,
            headroom,
            deficit,
            reserveTime,
            _currentCondition,
            _currentAlertLevel,
            recShedding,
            _brownoutHeadroomThresholdPercent,
            _criticalHeadroomThresholdPercent,
            _loadSheddingThresholdKw);
    }
}
