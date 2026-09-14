using System;
using System.Collections.Generic;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Monitor.Components;
using Content.Server.GameTicking.Events;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Station.Components;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.Diagnostics;

/// <summary>
///     Atmospheric environment condition status under SR-REF-016.
/// </summary>
public enum SolreignAtmosCondition : byte
{
    Normal = 0,
    Warning = 1,          // Mild pressure drop, hypoxia warning, or toxic gas detected
    Critical = 2,         // Severe low pressure, critical hypoxia, or high toxic gas
    Depressurized = 3,    // Hull breach vacuum or complete pressure collapse
}

/// <summary>
///     Automated atmospheric disruption alert severity levels under SR-REF-016.
/// </summary>
public enum SolreignAtmosAlertLevel : byte
{
    None = 0,
    Warning = 1,          // Minor atmospheric deviation
    Critical = 2,         // Severe environmental hazard
    Emergency = 3,        // Station hull breach / depressurization emergency
}

/// <summary>
///     Immutable snapshot of station atmospheric diagnostic state.
/// </summary>
public readonly record struct SolreignAtmosDiagnosticSnapshot(
    bool Enabled,
    double RoomPressurekPa,
    double OxygenRatioPercent,
    double ToxicGasRatioPercent,
    bool ToxicGasPresent,
    bool IsHullBreach,
    double PressureDropRatekPaPerSec,
    SolreignAtmosCondition Condition,
    SolreignAtmosAlertLevel AlertLevel,
    float LowPressureThresholdkPa,
    float CriticalPressureThresholdkPa,
    float DepressurizationThresholdkPa,
    float MinOxygenRatioPercent,
    float CriticalOxygenRatioPercent,
    float ToxicGasThresholdPercent,
    float RapidDropRateThresholdkPaPerSec)
{
    public bool IsDisrupted => Condition != SolreignAtmosCondition.Normal;
    public bool IsHullBreachAlertActive => AlertLevel == SolreignAtmosAlertLevel.Emergency || IsHullBreach || Condition == SolreignAtmosCondition.Depressurized;
}

/// <summary>
///     Diagnostic telemetry event raised on atmospheric sample updates when <see cref="SolreignAtmosDiagnosticSystem"/> is enabled.
/// </summary>
public sealed class SolreignAtmosDiagnosticMetricEvent : EntityEventArgs
{
    public TimeSpan TimeStamp { get; }
    public double RoomPressurekPa { get; }
    public double OxygenRatioPercent { get; }
    public double ToxicGasRatioPercent { get; }
    public bool ToxicGasPresent { get; }
    public bool IsHullBreach { get; }
    public double PressureDropRatekPaPerSec { get; }
    public SolreignAtmosCondition Condition { get; }
    public SolreignAtmosAlertLevel AlertLevel { get; }

    public SolreignAtmosDiagnosticMetricEvent(
        TimeSpan timeStamp,
        double roomPressurekPa,
        double oxygenRatioPercent,
        double toxicGasRatioPercent,
        bool toxicGasPresent,
        bool isHullBreach,
        double pressureDropRatekPaPerSec,
        SolreignAtmosCondition condition,
        SolreignAtmosAlertLevel alertLevel)
    {
        TimeStamp = timeStamp;
        RoomPressurekPa = roomPressurekPa;
        OxygenRatioPercent = oxygenRatioPercent;
        ToxicGasRatioPercent = toxicGasRatioPercent;
        ToxicGasPresent = toxicGasPresent;
        IsHullBreach = isHullBreach;
        PressureDropRatekPaPerSec = pressureDropRatekPaPerSec;
        Condition = condition;
        AlertLevel = alertLevel;
    }
}

/// <summary>
///     Alert event raised when atmospheric disruption or hull breach depressurization occurs.
/// </summary>
public sealed class SolreignAtmosDisruptionEvent : EntityEventArgs
{
    public TimeSpan TimeStamp { get; }
    public SolreignAtmosCondition PreviousCondition { get; }
    public SolreignAtmosCondition NewCondition { get; }
    public SolreignAtmosAlertLevel AlertLevel { get; }
    public double RoomPressurekPa { get; }
    public double OxygenRatioPercent { get; }
    public double ToxicGasRatioPercent { get; }
    public bool ToxicGasPresent { get; }
    public bool IsHullBreach { get; }
    public string Details { get; }

    public SolreignAtmosDisruptionEvent(
        TimeSpan timeStamp,
        SolreignAtmosCondition previousCondition,
        SolreignAtmosCondition newCondition,
        SolreignAtmosAlertLevel alertLevel,
        double roomPressurekPa,
        double oxygenRatioPercent,
        double toxicGasRatioPercent,
        bool toxicGasPresent,
        bool isHullBreach,
        string details)
    {
        TimeStamp = timeStamp;
        PreviousCondition = previousCondition;
        NewCondition = newCondition;
        AlertLevel = alertLevel;
        RoomPressurekPa = roomPressurekPa;
        OxygenRatioPercent = oxygenRatioPercent;
        ToxicGasRatioPercent = toxicGasRatioPercent;
        ToxicGasPresent = toxicGasPresent;
        IsHullBreach = isHullBreach;
        Details = details;
    }
}

/// <summary>
///     Pure, unit-testable evaluation functions for atmospheric pressure, gas ratio, and hull breach math.
/// </summary>
public static class SolreignAtmosDiagnosticEvaluator
{
    public static double CalculateOxygenRatioPercent(double oxygenAmount, double totalGasAmount)
    {
        if (totalGasAmount <= 0.0 || oxygenAmount <= 0.0)
            return 0.0;
        if (oxygenAmount >= totalGasAmount)
            return 100.0;

        return Math.Clamp((oxygenAmount / totalGasAmount) * 100.0, 0.0, 100.0);
    }

    public static double CalculateToxicGasRatioPercent(double toxicAmount, double totalGasAmount)
    {
        if (totalGasAmount <= 0.0 || toxicAmount <= 0.0)
            return 0.0;
        if (toxicAmount >= totalGasAmount)
            return 100.0;

        return Math.Clamp((toxicAmount / totalGasAmount) * 100.0, 0.0, 100.0);
    }

    public static bool DetectToxicGasPresence(double toxicGasRatioPercent, float toxicThresholdPercent)
    {
        if (toxicThresholdPercent < 0.0f)
            return toxicGasRatioPercent > 0.0;

        return toxicGasRatioPercent >= toxicThresholdPercent;
    }

    public static double CalculatePressureDropRate(double currentPressurekPa, double previousPressurekPa, double deltaTimeSeconds)
    {
        if (deltaTimeSeconds <= 0.0 || previousPressurekPa <= currentPressurekPa)
            return 0.0;

        return (previousPressurekPa - currentPressurekPa) / deltaTimeSeconds;
    }

    public static bool DetectHullBreach(
        double currentPressurekPa,
        double previousPressurekPa,
        double deltaTimeSeconds,
        float rapidDropRateThreshold,
        float depressurizationThreshold)
    {
        if (currentPressurekPa <= depressurizationThreshold)
            return true;

        var dropRate = CalculatePressureDropRate(currentPressurekPa, previousPressurekPa, deltaTimeSeconds);
        return dropRate >= rapidDropRateThreshold && rapidDropRateThreshold > 0.0f;
    }

    public static SolreignAtmosCondition EvaluateAtmosCondition(
        double pressurekPa,
        double o2RatioPercent,
        double toxicGasRatioPercent,
        bool isHullBreach,
        float lowPressureThreshold,
        float criticalPressureThreshold,
        float depressurizationThreshold,
        float minO2Threshold,
        float criticalO2Threshold,
        float toxicGasThreshold)
    {
        if (isHullBreach || pressurekPa <= depressurizationThreshold)
            return SolreignAtmosCondition.Depressurized;

        if (pressurekPa <= criticalPressureThreshold || o2RatioPercent <= criticalO2Threshold || toxicGasRatioPercent >= (toxicGasThreshold * 5.0f))
            return SolreignAtmosCondition.Critical;

        if (pressurekPa <= lowPressureThreshold || o2RatioPercent < minO2Threshold || toxicGasRatioPercent >= toxicGasThreshold)
            return SolreignAtmosCondition.Warning;

        return SolreignAtmosCondition.Normal;
    }

    public static SolreignAtmosAlertLevel EvaluateAlertLevel(SolreignAtmosCondition condition, bool isHullBreach)
    {
        if (condition == SolreignAtmosCondition.Depressurized || isHullBreach)
            return SolreignAtmosAlertLevel.Emergency;

        if (condition == SolreignAtmosCondition.Critical)
            return SolreignAtmosAlertLevel.Critical;

        if (condition == SolreignAtmosCondition.Warning)
            return SolreignAtmosAlertLevel.Warning;

        return SolreignAtmosAlertLevel.None;
    }
}

/// <summary>
///     Bounds routine metric publication per sensor while leaving state and transition detection live.
/// </summary>
internal sealed class SolreignAtmosDiagnosticSampleGate
{
    private readonly TimeSpan _interval;
    private readonly Dictionary<EntityUid, TimeSpan> _nextSamples = new();

    public SolreignAtmosDiagnosticSampleGate(TimeSpan interval)
    {
        _interval = interval;
    }

    public bool TryBeginSample(EntityUid source, TimeSpan now)
    {
        if (_nextSamples.TryGetValue(source, out var next) && now < next)
            return false;

        _nextSamples[source] = now + _interval;
        return true;
    }

    public void Remove(EntityUid source)
    {
        _nextSamples.Remove(source);
    }

    public void Clear()
    {
        _nextSamples.Clear();
    }
}

/// <summary>
///     SR-REF-016: SS14 Solreign station atmospheric pressure &amp; oxygen disruption diagnostic system.
///     Tracks room pressure (kPa), oxygen ratio (%), toxic gas presence, and hull breach depressurization alerts.
///     The public snapshot is the latest station-sensor sample; per-sensor history exists to prevent
///     cross-room pressure comparisons, not to provide a room-indexed query API.
/// </summary>
public sealed partial class SolreignAtmosDiagnosticSystem : EntitySystem
{
    private static readonly TimeSpan MetricSampleInterval = TimeSpan.FromSeconds(5);

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AtmosphereSystem _atmosphere = default!;

    private bool _enabled;
    private float _normalPressurekPa = 101.3f;
    private float _lowPressureThresholdkPa = 80.0f;
    private float _criticalPressureThresholdkPa = 50.0f;
    private float _depressurizationThresholdkPa = 20.0f;
    private float _minOxygenRatioPercent = 19.5f;
    private float _criticalOxygenRatioPercent = 12.0f;
    private float _toxicGasThresholdPercent = 0.5f;
    private float _rapidDropRateThresholdkPaPerSec = 10.0f;

    private double _roomPressurekPa = 101.3;
    private double _previousPressurekPa = 101.3;
    private double _oxygenRatioPercent = 21.0;
    private double _toxicGasRatioPercent = 0.0;
    private double _pressureDropRatekPaPerSec = 0.0;
    private bool _toxicGasPresent;
    private bool _isHullBreach;

    private SolreignAtmosCondition _currentCondition = SolreignAtmosCondition.Normal;
    private SolreignAtmosAlertLevel _currentAlertLevel = SolreignAtmosAlertLevel.None;
    private readonly Dictionary<EntityUid, SourceState> _sourceStates = new();
    private readonly SolreignAtmosDiagnosticSampleGate _metricSampleGate = new(MetricSampleInterval);

    private readonly record struct SourceState(
        EntityUid Grid,
        double PressurekPa,
        SolreignAtmosCondition Condition,
        SolreignAtmosAlertLevel AlertLevel);

    public bool IsEnabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public float NormalPressurekPa
    {
        get => _normalPressurekPa;
        set => _normalPressurekPa = Math.Max(0.0f, value);
    }

    public float LowPressureThresholdkPa
    {
        get => _lowPressureThresholdkPa;
        set => _lowPressureThresholdkPa = Math.Max(0.0f, value);
    }

    public float CriticalPressureThresholdkPa
    {
        get => _criticalPressureThresholdkPa;
        set => _criticalPressureThresholdkPa = Math.Max(0.0f, value);
    }

    public float DepressurizationThresholdkPa
    {
        get => _depressurizationThresholdkPa;
        set => _depressurizationThresholdkPa = Math.Max(0.0f, value);
    }

    public float MinOxygenRatioPercent
    {
        get => _minOxygenRatioPercent;
        set => _minOxygenRatioPercent = Math.Clamp(value, 0.0f, 100.0f);
    }

    public float CriticalOxygenRatioPercent
    {
        get => _criticalOxygenRatioPercent;
        set => _criticalOxygenRatioPercent = Math.Clamp(value, 0.0f, 100.0f);
    }

    public float ToxicGasThresholdPercent
    {
        get => _toxicGasThresholdPercent;
        set => _toxicGasThresholdPercent = Math.Clamp(value, 0.0f, 100.0f);
    }

    public float RapidDropRateThresholdkPaPerSec
    {
        get => _rapidDropRateThresholdkPaPerSec;
        set => _rapidDropRateThresholdkPaPerSec = Math.Max(0.0f, value);
    }

    public double RoomPressurekPa => _roomPressurekPa;
    public double OxygenRatioPercent => _oxygenRatioPercent;
    public double ToxicGasRatioPercent => _toxicGasRatioPercent;
    public bool ToxicGasPresent => _toxicGasPresent;
    public bool IsHullBreach => _isHullBreach;
    public double PressureDropRatekPaPerSec => _pressureDropRatekPaPerSec;

    public SolreignAtmosCondition CurrentCondition => _currentCondition;
    public SolreignAtmosAlertLevel CurrentAlertLevel => _currentAlertLevel;

    public Action<object>? OnEventRaised { get; set; }

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.SolreignAtmosDiagnosticsEnabled, OnEnabledChanged, invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignAtmosNormalPressurekPa, v => _normalPressurekPa = Math.Max(0.0f, v), invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignAtmosLowPressureThresholdkPa, v => _lowPressureThresholdkPa = Math.Max(0.0f, v), invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignAtmosCriticalPressureThresholdkPa, v => _criticalPressureThresholdkPa = Math.Max(0.0f, v), invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignAtmosDepressurizationThresholdkPa, v => _depressurizationThresholdkPa = Math.Max(0.0f, v), invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignAtmosMinOxygenRatioPercent, v => _minOxygenRatioPercent = Math.Clamp(v, 0.0f, 100.0f), invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignAtmosCriticalOxygenRatioPercent, v => _criticalOxygenRatioPercent = Math.Clamp(v, 0.0f, 100.0f), invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignAtmosToxicGasThresholdPercent, v => _toxicGasThresholdPercent = Math.Clamp(v, 0.0f, 100.0f), invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignAtmosRapidDropRateThresholdkPaPerSec, v => _rapidDropRateThresholdkPaPerSec = Math.Max(0.0f, v), invokeImmediately: true);

        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        // AtmosMonitorSystem owns the AtmosMonitorComponent subscription for this event.
        // Subscribe through the already-attached AtmosDeviceComponent and filter to monitors
        // so Robust's directed-event bus does not reject a duplicate component/event pair.
        SubscribeLocalEvent<AtmosDeviceComponent, AtmosDeviceUpdateEvent>(OnAtmosDeviceUpdate);
        SubscribeLocalEvent<AtmosMonitorComponent, ComponentShutdown>(OnMonitorShutdown);
    }

    private void OnEnabledChanged(bool value)
    {
        _enabled = value;

        if (!value)
            ResetMetrics();
    }

    private void OnRoundStarting(RoundStartingEvent ev) => ResetMetrics();
    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev) => ResetMetrics();

    private void OnAtmosDeviceUpdate(Entity<AtmosDeviceComponent> ent, ref AtmosDeviceUpdateEvent args)
    {
        if (!_enabled || !TryComp<AtmosMonitorComponent>(ent.Owner, out var monitor))
            return;

        if (monitor.MonitorsPipeNet ||
            args.Grid is not { } grid ||
            !HasComp<StationMemberComponent>(grid.Owner))
        {
            _sourceStates.Remove(ent.Owner);
            _metricSampleGate.Remove(ent.Owner);
            return;
        }

        var xform = Transform(ent.Owner);
        var mixture = _atmosphere.GetContainingMixture(
            (ent.Owner, xform),
            args.Grid,
            args.Map,
            ignoreExposed: true,
            excite: false);

        if (mixture == null)
        {
            _sourceStates.Remove(ent.Owner);
            _metricSampleGate.Remove(ent.Owner);
            return;
        }

        TryUpdateAtmosMetricsForSource(
            ent.Owner,
            grid.Owner,
            mixture,
            args.dt,
            _metricSampleGate.TryBeginSample(ent.Owner, _timing.CurTime));
    }

    private void OnMonitorShutdown(Entity<AtmosMonitorComponent> ent, ref ComponentShutdown args)
    {
        _sourceStates.Remove(ent.Owner);
        _metricSampleGate.Remove(ent.Owner);
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
    ///     Update current room atmospheric metrics and evaluate pressure, oxygen, toxic gas, and hull breach status.
    /// </summary>
    public SolreignAtmosDiagnosticSnapshot UpdateAtmosMetrics(
        double roomPressurekPa,
        double oxygenAmount,
        double toxicAmount,
        double totalGasAmount,
        double deltaTimeSeconds = 1.0)
    {
        return UpdateAtmosMetricsForSource(
            EntityUid.Invalid,
            EntityUid.Invalid,
            roomPressurekPa,
            oxygenAmount,
            toxicAmount,
            totalGasAmount,
            deltaTimeSeconds);
    }

    internal SolreignAtmosDiagnosticSnapshot UpdateAtmosMetricsForSource(
        EntityUid source,
        EntityUid grid,
        double roomPressurekPa,
        double oxygenAmount,
        double toxicAmount,
        double totalGasAmount,
        double deltaTimeSeconds = 1.0,
        bool emitMetric = true)
    {
        if (!_enabled)
            return GetSnapshot();

        var hasContinuousHistory =
            _sourceStates.TryGetValue(source, out var previous) &&
            previous.Grid == grid;

        _previousPressurekPa = hasContinuousHistory
            ? previous.PressurekPa
            : Math.Max(0.0, roomPressurekPa);
        _roomPressurekPa = Math.Max(0.0, roomPressurekPa);

        _oxygenRatioPercent = SolreignAtmosDiagnosticEvaluator.CalculateOxygenRatioPercent(oxygenAmount, totalGasAmount);
        _toxicGasRatioPercent = SolreignAtmosDiagnosticEvaluator.CalculateToxicGasRatioPercent(toxicAmount, totalGasAmount);

        return RecordSampleForSource(
            source,
            grid,
            deltaTimeSeconds,
            hasContinuousHistory ? previous.Condition : SolreignAtmosCondition.Normal,
            hasContinuousHistory ? previous.AlertLevel : SolreignAtmosAlertLevel.None,
            emitMetric);
    }

    internal bool TryUpdateAtmosMetricsForSource(
        EntityUid source,
        EntityUid grid,
        GasMixture? mixture,
        double deltaTimeSeconds,
        bool emitMetric = true)
    {
        if (!_enabled || mixture == null)
            return false;

        UpdateAtmosMetricsForSource(
            source,
            grid,
            mixture.Pressure,
            mixture.GetMoles(Gas.Oxygen),
            mixture.GetMoles(Gas.Plasma),
            mixture.TotalMoles,
            deltaTimeSeconds,
            emitMetric);
        return true;
    }

    /// <summary>
    ///     Direct update of room atmospheric ratios and pressure metrics.
    /// </summary>
    public SolreignAtmosDiagnosticSnapshot UpdateDirectAtmosMetrics(
        double roomPressurekPa,
        double oxygenRatioPercent,
        double toxicGasRatioPercent,
        double deltaTimeSeconds = 1.0)
    {
        if (!_enabled)
            return GetSnapshot();

        var hasContinuousHistory =
            _sourceStates.TryGetValue(EntityUid.Invalid, out var previous) &&
            previous.Grid == EntityUid.Invalid;

        _previousPressurekPa = hasContinuousHistory
            ? previous.PressurekPa
            : Math.Max(0.0, roomPressurekPa);
        _roomPressurekPa = Math.Max(0.0, roomPressurekPa);

        _oxygenRatioPercent = Math.Clamp(oxygenRatioPercent, 0.0, 100.0);
        _toxicGasRatioPercent = Math.Clamp(toxicGasRatioPercent, 0.0, 100.0);

        return RecordSampleForSource(
            EntityUid.Invalid,
            EntityUid.Invalid,
            deltaTimeSeconds,
            hasContinuousHistory ? previous.Condition : SolreignAtmosCondition.Normal,
            hasContinuousHistory ? previous.AlertLevel : SolreignAtmosAlertLevel.None,
            emitMetric: true);
    }

    /// <summary>
    ///     Record atmospheric diagnostic sample, evaluate conditions, and raise events if state changed.
    /// </summary>
    public SolreignAtmosDiagnosticSnapshot RecordSample(double deltaTimeSeconds = 1.0)
    {
        return RecordSampleForSource(
            EntityUid.Invalid,
            EntityUid.Invalid,
            deltaTimeSeconds,
            _currentCondition,
            _currentAlertLevel,
            emitMetric: true);
    }

    private SolreignAtmosDiagnosticSnapshot RecordSampleForSource(
        EntityUid source,
        EntityUid grid,
        double deltaTimeSeconds,
        SolreignAtmosCondition previousCondition,
        SolreignAtmosAlertLevel previousAlertLevel,
        bool emitMetric)
    {
        if (!_enabled)
            return GetSnapshot();

        _toxicGasPresent = SolreignAtmosDiagnosticEvaluator.DetectToxicGasPresence(_toxicGasRatioPercent, _toxicGasThresholdPercent);
        _pressureDropRatekPaPerSec = SolreignAtmosDiagnosticEvaluator.CalculatePressureDropRate(_roomPressurekPa, _previousPressurekPa, deltaTimeSeconds);

        _isHullBreach = SolreignAtmosDiagnosticEvaluator.DetectHullBreach(
            _roomPressurekPa,
            _previousPressurekPa,
            deltaTimeSeconds,
            _rapidDropRateThresholdkPaPerSec,
            _depressurizationThresholdkPa);

        var newCondition = SolreignAtmosDiagnosticEvaluator.EvaluateAtmosCondition(
            _roomPressurekPa,
            _oxygenRatioPercent,
            _toxicGasRatioPercent,
            _isHullBreach,
            _lowPressureThresholdkPa,
            _criticalPressureThresholdkPa,
            _depressurizationThresholdkPa,
            _minOxygenRatioPercent,
            _criticalOxygenRatioPercent,
            _toxicGasThresholdPercent);

        var newAlertLevel = SolreignAtmosDiagnosticEvaluator.EvaluateAlertLevel(newCondition, _isHullBreach);

        _currentCondition = newCondition;
        _currentAlertLevel = newAlertLevel;
        _sourceStates[source] = new SourceState(grid, _roomPressurekPa, newCondition, newAlertLevel);

        var timeStamp = _timing != null ? _timing.CurTime : TimeSpan.Zero;

        if (emitMetric)
        {
            var metricEv = new SolreignAtmosDiagnosticMetricEvent(
                timeStamp,
                _roomPressurekPa,
                _oxygenRatioPercent,
                _toxicGasRatioPercent,
                _toxicGasPresent,
                _isHullBreach,
                _pressureDropRatekPaPerSec,
                newCondition,
                newAlertLevel);

            RaiseDiagnosticEvent(metricEv);
        }

        if (newCondition != previousCondition || (newAlertLevel != previousAlertLevel && newAlertLevel != SolreignAtmosAlertLevel.None))
        {
            var details = $"Atmospheric state transitioned from {previousCondition} (Alert: {previousAlertLevel}) to {newCondition} (Alert: {newAlertLevel}). Pressure: {_roomPressurekPa:F1}kPa, O2 Ratio: {_oxygenRatioPercent:F1}%, Toxic: {_toxicGasRatioPercent:F1}%, Breach: {_isHullBreach}.";
            var disruptionEv = new SolreignAtmosDisruptionEvent(
                timeStamp,
                previousCondition,
                newCondition,
                newAlertLevel,
                _roomPressurekPa,
                _oxygenRatioPercent,
                _toxicGasRatioPercent,
                _toxicGasPresent,
                _isHullBreach,
                details);

            RaiseDiagnosticEvent(disruptionEv);
        }

        return GetSnapshot();
    }

    /// <summary>
    ///     Reset atmospheric metrics and condition state back to defaults.
    /// </summary>
    public void ResetMetrics()
    {
        _sourceStates.Clear();
        _metricSampleGate.Clear();
        _roomPressurekPa = _normalPressurekPa;
        _previousPressurekPa = _normalPressurekPa;
        _oxygenRatioPercent = 21.0;
        _toxicGasRatioPercent = 0.0;
        _pressureDropRatekPaPerSec = 0.0;
        _toxicGasPresent = false;
        _isHullBreach = false;
        _currentCondition = SolreignAtmosCondition.Normal;
        _currentAlertLevel = SolreignAtmosAlertLevel.None;
    }

    /// <summary>
    ///     Get an immutable snapshot of current atmospheric diagnostic metrics.
    /// </summary>
    public SolreignAtmosDiagnosticSnapshot GetSnapshot()
    {
        return new SolreignAtmosDiagnosticSnapshot(
            _enabled,
            _roomPressurekPa,
            _oxygenRatioPercent,
            _toxicGasRatioPercent,
            _toxicGasPresent,
            _isHullBreach,
            _pressureDropRatekPaPerSec,
            _currentCondition,
            _currentAlertLevel,
            _lowPressureThresholdkPa,
            _criticalPressureThresholdkPa,
            _depressurizationThresholdkPa,
            _minOxygenRatioPercent,
            _criticalOxygenRatioPercent,
            _toxicGasThresholdPercent,
            _rapidDropRateThresholdkPaPerSec);
    }
}
