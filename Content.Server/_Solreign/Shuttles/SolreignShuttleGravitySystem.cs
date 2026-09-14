#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Server._Solreign.Shuttles;

/// <summary>
///     SR-W-067: SS14 Solreign Shuttle Autopilot & Artificial Gravity Escrow System.
///     Tracks artificial gravity generator power state, shuttle navigation autopilot vectors,
///     emergency thruster dampening, and CVar thresholds (<c>solreign.shuttle_gravity_enabled</c>).
/// </summary>
public enum GravityFieldStatus : byte
{
    Inactive = 0,
    Unstable = 1,
    Nominal = 2,
    Overcharged = 3,
}

public sealed record ShuttleVector2D
{
    public float X { get; init; }
    public float Y { get; init; }

    public ShuttleVector2D(float x, float y)
    {
        X = float.IsNaN(x) || float.IsInfinity(x) ? 0f : x;
        Y = float.IsNaN(y) || float.IsInfinity(y) ? 0f : y;
    }

    public float Length => MathF.Sqrt((X * X) + (Y * Y));
}

public sealed record ShuttleGravityState
{
    public int ShuttleId { get; init; }
    public string ShuttleName { get; init; } = string.Empty;
    public bool GeneratorActive { get; set; }
    public float CurrentPower { get; set; }
    public float MaxPower { get; set; } = 1000.0f;
    public float PowerRatio => MaxPower > 0f ? Math.Clamp(CurrentPower / MaxPower, 0f, 2.0f) : 0f;
    public GravityFieldStatus GravityStatus { get; set; } = GravityFieldStatus.Inactive;

    public bool AutopilotEngaged { get; set; }
    public ShuttleVector2D Position { get; set; } = new(0f, 0f);
    public ShuttleVector2D TargetPosition { get; set; } = new(0f, 0f);
    public ShuttleVector2D Velocity { get; set; } = new(0f, 0f);
    public float TargetSpeed { get; set; } = 20.0f;
    public int EscrowFeePaid { get; set; }

    public bool EmergencyDampeningActive { get; set; }
    public float DampeningFactor { get; set; } = 0.8f;
}

public sealed partial class SolreignShuttleGravitySystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;

    private bool _enabled = true;
    private int _baseEscrow = 100;
    private float _maxAutopilotSpeed = 50.0f;
    private float _emergencyThreshold = 35.0f;
    private float _minPowerRatio = 0.2f;

    private readonly Dictionary<int, ShuttleGravityState> _shuttles = new();

    public bool IsEnabled => _enabled;
    public int BaseEscrow => _baseEscrow;
    public float MaxAutopilotSpeed => _maxAutopilotSpeed;
    public float EmergencyThreshold => _emergencyThreshold;
    public float MinPowerRatio => _minPowerRatio;

    public override void Initialize()
    {
        base.Initialize();

        if (_config != null)
        {
            Subs.CVar(_config, CCVars.SolreignShuttleGravityEnabled, v => _enabled = v, invokeImmediately: true);
            Subs.CVar(_config, CCVars.SolreignShuttleGravityBaseEscrow, v => _baseEscrow = v, invokeImmediately: true);
            Subs.CVar(_config, CCVars.SolreignShuttleGravityMaxAutopilotSpeed, v => _maxAutopilotSpeed = v, invokeImmediately: true);
            Subs.CVar(_config, CCVars.SolreignShuttleGravityEmergencyDampeningThreshold, v => _emergencyThreshold = v, invokeImmediately: true);
            Subs.CVar(_config, CCVars.SolreignShuttleGravityMinPowerRatio, v => _minPowerRatio = v, invokeImmediately: true);
        }
    }

    /// <summary>
    ///     Directly configure system parameters (for unit tests or standalone initialization without full IoC).
    /// </summary>
    public void SetConfigValues(bool enabled, int baseEscrow, float maxAutopilotSpeed, float emergencyThreshold, float minPowerRatio)
    {
        _enabled = enabled;
        _baseEscrow = Math.Max(0, baseEscrow);
        _maxAutopilotSpeed = Math.Max(1.0f, maxAutopilotSpeed);
        _emergencyThreshold = Math.Max(1.0f, emergencyThreshold);
        _minPowerRatio = Math.Clamp(minPowerRatio, 0.0f, 1.0f);
    }

    /// <summary>
    ///     Evaluates artificial gravity generator field status from power inputs.
    /// </summary>
    public GravityFieldStatus CalculateGravityFieldStatus(bool generatorActive, float currentPower, float maxPower)
    {
        if (!_enabled)
            return GravityFieldStatus.Inactive;

        if (!generatorActive || maxPower <= 0f || currentPower <= 0f || float.IsNaN(currentPower) || float.IsNaN(maxPower))
            return GravityFieldStatus.Inactive;

        var ratio = Math.Clamp(currentPower / maxPower, 0f, 2.0f);

        if (ratio < _minPowerRatio)
            return GravityFieldStatus.Unstable;

        if (ratio > 1.5f)
            return GravityFieldStatus.Overcharged;

        return GravityFieldStatus.Nominal;
    }

    /// <summary>
    ///     Calculates required autopilot escrow fee based on distance, speed, and emergency override status.
    /// </summary>
    public int CalculateAutopilotEscrowFee(float distance, float targetSpeed, bool isEmergencyOverride = false)
    {
        if (float.IsNaN(distance) || float.IsInfinity(distance) || distance < 0f)
            distance = 0f;

        if (float.IsNaN(targetSpeed) || float.IsInfinity(targetSpeed) || targetSpeed < 0f)
            targetSpeed = 0f;

        var fee = (double) _baseEscrow;

        if (distance > 0f)
        {
            fee += distance * 0.5f;
        }

        if (targetSpeed > _maxAutopilotSpeed)
        {
            fee += (targetSpeed - _maxAutopilotSpeed) * 2.0f;
        }

        if (isEmergencyOverride)
        {
            fee *= 1.5;
        }

        return (int) Math.Ceiling(fee);
    }

    /// <summary>
    ///     Calculates velocity vector and remaining distance to target position.
    /// </summary>
    public (ShuttleVector2D velocity, float remainingDistance) CalculateAutopilotVector(
        float currentX, float currentY, float targetX, float targetY, float speed)
    {
        currentX = float.IsNaN(currentX) || float.IsInfinity(currentX) ? 0f : currentX;
        currentY = float.IsNaN(currentY) || float.IsInfinity(currentY) ? 0f : currentY;
        targetX = float.IsNaN(targetX) || float.IsInfinity(targetX) ? 0f : targetX;
        targetY = float.IsNaN(targetY) || float.IsInfinity(targetY) ? 0f : targetY;
        speed = float.IsNaN(speed) || float.IsInfinity(speed) || speed < 0f ? 0f : speed;

        var dx = targetX - currentX;
        var dy = targetY - currentY;
        var distance = MathF.Sqrt((dx * dx) + (dy * dy));

        if (distance <= 0.001f)
        {
            return (new ShuttleVector2D(0f, 0f), 0f);
        }

        var dirX = dx / distance;
        var dirY = dy / distance;

        return (new ShuttleVector2D(dirX * speed, dirY * speed), distance);
    }

    /// <summary>
    ///     Generates waypoints along a course trajectory vector between start and target positions.
    /// </summary>
    public IReadOnlyList<ShuttleVector2D> CalculateCourseTrajectory(
        float startX, float startY, float targetX, float targetY, int waypointCount = 4)
    {
        waypointCount = Math.Clamp(waypointCount, 1, 32);
        var waypoints = new List<ShuttleVector2D>(waypointCount + 1);

        startX = float.IsNaN(startX) || float.IsInfinity(startX) ? 0f : startX;
        startY = float.IsNaN(startY) || float.IsInfinity(startY) ? 0f : startY;
        targetX = float.IsNaN(targetX) || float.IsInfinity(targetX) ? 0f : targetX;
        targetY = float.IsNaN(targetY) || float.IsInfinity(targetY) ? 0f : targetY;

        for (var i = 0; i <= waypointCount; i++)
        {
            var t = (float) i / waypointCount;
            var x = startX + (t * (targetX - startX));
            var y = startY + (t * (targetY - startY));
            waypoints.Add(new ShuttleVector2D(x, y));
        }

        return waypoints;
    }

    /// <summary>
    ///     Determines if emergency thruster dampening should trigger based on velocity, power ratio, or manual override.
    /// </summary>
    public bool ShouldTriggerEmergencyDampening(float currentSpeed, float powerRatio, bool manualOverride = false)
    {
        if (manualOverride)
            return true;

        currentSpeed = float.IsNaN(currentSpeed) || float.IsInfinity(currentSpeed) ? 0f : currentSpeed;
        powerRatio = float.IsNaN(powerRatio) || float.IsInfinity(powerRatio) ? 0f : powerRatio;

        if (currentSpeed >= _emergencyThreshold)
            return true;

        if (powerRatio < _minPowerRatio && currentSpeed > 10.0f)
            return true;

        return false;
    }

    /// <summary>
    ///     Applies thruster dampening to a velocity vector.
    /// </summary>
    public ShuttleVector2D CalculateThrusterDampening(float velocityX, float velocityY, float dampeningFactor = 0.8f)
    {
        dampeningFactor = Math.Clamp(float.IsNaN(dampeningFactor) ? 0.8f : dampeningFactor, 0.0f, 1.0f);
        velocityX = float.IsNaN(velocityX) || float.IsInfinity(velocityX) ? 0f : velocityX;
        velocityY = float.IsNaN(velocityY) || float.IsInfinity(velocityY) ? 0f : velocityY;

        var multiplier = 1.0f - dampeningFactor;
        return new ShuttleVector2D(velocityX * multiplier, velocityY * multiplier);
    }

    /// <summary>
    ///     Registers a shuttle entity for state management.
    /// </summary>
    public ShuttleGravityState RegisterShuttle(int shuttleId, string name, float maxPower = 1000.0f)
    {
        var state = new ShuttleGravityState
        {
            ShuttleId = shuttleId,
            ShuttleName = string.IsNullOrWhiteSpace(name) ? $"Shuttle-{shuttleId}" : name.Trim(),
            MaxPower = Math.Max(1.0f, maxPower),
            CurrentPower = 0.0f,
            GeneratorActive = false,
            GravityStatus = GravityFieldStatus.Inactive,
        };

        _shuttles[shuttleId] = state;
        return state;
    }

    /// <summary>
    ///     Updates a shuttle's generator power state and re-evaluates gravity status.
    /// </summary>
    public bool UpdateGeneratorPower(int shuttleId, bool active, float currentPower)
    {
        if (!_shuttles.TryGetValue(shuttleId, out var state))
            return false;

        state.GeneratorActive = active;
        state.CurrentPower = Math.Clamp(currentPower, 0.0f, state.MaxPower * 2.0f);
        state.GravityStatus = CalculateGravityFieldStatus(active, state.CurrentPower, state.MaxPower);
        return true;
    }

    /// <summary>
    ///     Attempts to engage autopilot navigation with escrow fee verification.
    /// </summary>
    public bool TryEngageAutopilot(
        int shuttleId,
        float currentX,
        float currentY,
        float targetX,
        float targetY,
        float targetSpeed,
        int escrowFeePaid,
        out string error)
    {
        error = string.Empty;

        if (!_enabled)
        {
            error = "Shuttle gravity and autopilot system is disabled.";
            return false;
        }

        if (!_shuttles.TryGetValue(shuttleId, out var state))
        {
            error = $"Shuttle ID {shuttleId} not registered.";
            return false;
        }

        var dx = targetX - currentX;
        var dy = targetY - currentY;
        var distance = MathF.Sqrt((dx * dx) + (dy * dy));

        if (distance <= 0.001f)
        {
            error = "Target position is identical to current position.";
            return false;
        }

        var requiredEscrow = CalculateAutopilotEscrowFee(distance, targetSpeed);
        if (escrowFeePaid < requiredEscrow)
        {
            error = $"Insufficient escrow fee paid. Paid: {escrowFeePaid}, Required: {requiredEscrow}.";
            return false;
        }

        state.Position = new ShuttleVector2D(currentX, currentY);
        state.TargetPosition = new ShuttleVector2D(targetX, targetY);
        state.TargetSpeed = targetSpeed;
        state.EscrowFeePaid = escrowFeePaid;
        state.AutopilotEngaged = true;

        var (velocity, _) = CalculateAutopilotVector(currentX, currentY, targetX, targetY, targetSpeed);
        state.Velocity = velocity;

        return true;
    }

    /// <summary>
    ///     Deposits additional escrow for autopilot navigation.
    /// </summary>
    public bool TryDepositAutopilotEscrow(int shuttleId, int feePaid, out string error)
    {
        error = string.Empty;

        if (!_enabled)
        {
            error = "System is disabled.";
            return false;
        }

        if (!_shuttles.TryGetValue(shuttleId, out var state))
        {
            error = $"Shuttle ID {shuttleId} not registered.";
            return false;
        }

        if (feePaid <= 0)
        {
            error = "Escrow deposit must be positive.";
            return false;
        }

        state.EscrowFeePaid += feePaid;
        return true;
    }

    /// <summary>
    ///     Cancels autopilot navigation and returns refunded escrow.
    /// </summary>
    public bool CancelAutopilotEscrow(int shuttleId, out int refundedEscrow, out string error)
    {
        refundedEscrow = 0;
        error = string.Empty;

        if (!_shuttles.TryGetValue(shuttleId, out var state))
        {
            error = $"Shuttle ID {shuttleId} not registered.";
            return false;
        }

        if (!state.AutopilotEngaged)
        {
            error = "Autopilot is not currently engaged.";
            return false;
        }

        refundedEscrow = state.EscrowFeePaid;
        state.EscrowFeePaid = 0;
        state.AutopilotEngaged = false;
        state.Velocity = new ShuttleVector2D(0f, 0f);
        return true;
    }

    /// <summary>
    ///     Applies emergency thruster dampening to slow down a shuttle.
    /// </summary>
    public bool ApplyEmergencyDampening(int shuttleId, float dampeningFactor = 0.8f)
    {
        if (!_shuttles.TryGetValue(shuttleId, out var state))
            return false;

        var dampenedVelocity = CalculateThrusterDampening(state.Velocity.X, state.Velocity.Y, dampeningFactor);
        state.Velocity = dampenedVelocity;
        state.EmergencyDampeningActive = true;
        state.DampeningFactor = dampeningFactor;
        return true;
    }

    /// <summary>
    ///     Simulates one tick step of shuttle motion under autopilot navigation.
    /// </summary>
    public void UpdateShuttleTick(int shuttleId, float deltaSeconds)
    {
        if (!_enabled || deltaSeconds <= 0f || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds))
            return;

        if (!_shuttles.TryGetValue(shuttleId, out var state) || !state.AutopilotEngaged)
            return;

        var prevDx = state.TargetPosition.X - state.Position.X;
        var prevDy = state.TargetPosition.Y - state.Position.Y;

        var currentX = state.Position.X + (state.Velocity.X * deltaSeconds);
        var currentY = state.Position.Y + (state.Velocity.Y * deltaSeconds);

        var postDx = state.TargetPosition.X - currentX;
        var postDy = state.TargetPosition.Y - currentY;

        var overshot = (prevDx * postDx + prevDy * postDy) <= 0f;

        var (newVelocity, remainingDistance) = CalculateAutopilotVector(
            currentX, currentY, state.TargetPosition.X, state.TargetPosition.Y, state.TargetSpeed);

        state.Position = new ShuttleVector2D(currentX, currentY);

        if (ShouldTriggerEmergencyDampening(state.Velocity.Length, state.PowerRatio))
        {
            ApplyEmergencyDampening(shuttleId, 0.8f);
        }
        else
        {
            state.Velocity = newVelocity;
            state.EmergencyDampeningActive = false;
        }

        if (remainingDistance <= 0.1f || overshot)
        {
            state.Position = state.TargetPosition;
            state.AutopilotEngaged = false;
            state.Velocity = new ShuttleVector2D(0f, 0f);
            state.EscrowFeePaid = 0;
        }
    }

    /// <summary>
    ///     Retrieves state for a shuttle by ID.
    /// </summary>
    public ShuttleGravityState? GetShuttleState(int shuttleId)
    {
        return _shuttles.TryGetValue(shuttleId, out var state) ? state : null;
    }

    /// <summary>
    ///     Returns all registered shuttles.
    /// </summary>
    public IReadOnlyCollection<ShuttleGravityState> GetAllShuttles()
    {
        return _shuttles.Values.ToList();
    }

    /// <summary>
    ///     Resets internal state (useful for unit tests).
    /// </summary>
    public void ResetState()
    {
        _shuttles.Clear();
    }
}
