#nullable enable
using System;
using System.Linq;
using Content.Server._Solreign.Shuttles;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Unit tests for SR-W-067 <see cref="SolreignShuttleGravitySystem"/> verifying gravity field status,
///     autopilot trajectory math, thruster dampening triggers, escrow deposits, and zero-exception execution.
/// </summary>
[TestFixture]
[TestOf(typeof(SolreignShuttleGravitySystem))]
public sealed class SolreignShuttleGravityTests
{
    private SolreignShuttleGravitySystem _system = null!;

    [SetUp]
    public void SetUp()
    {
        _system = new SolreignShuttleGravitySystem();
        _system.SetConfigValues(
            enabled: true,
            baseEscrow: 100,
            maxAutopilotSpeed: 50.0f,
            emergencyThreshold: 35.0f,
            minPowerRatio: 0.2f);
    }

    [TearDown]
    public void TearDown()
    {
        _system.ResetState();
    }

    // ==========================================
    // 1. Gravity Field Status Tests
    // ==========================================

    [Test]
    public void GravityFieldStatus_Inactive_WhenGeneratorOffOrZeroPower()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_system.CalculateGravityFieldStatus(false, 500f, 1000f), Is.EqualTo(GravityFieldStatus.Inactive));
            Assert.That(_system.CalculateGravityFieldStatus(true, 0f, 1000f), Is.EqualTo(GravityFieldStatus.Inactive));
            Assert.That(_system.CalculateGravityFieldStatus(true, -100f, 1000f), Is.EqualTo(GravityFieldStatus.Inactive));
            Assert.That(_system.CalculateGravityFieldStatus(true, 500f, 0f), Is.EqualTo(GravityFieldStatus.Inactive));
        });
    }

    [Test]
    public void GravityFieldStatus_Unstable_WhenPowerBelowMinRatio()
    {
        // minPowerRatio = 0.2f -> power below 200f on 1000f max
        var status = _system.CalculateGravityFieldStatus(true, 150f, 1000f);
        Assert.That(status, Is.EqualTo(GravityFieldStatus.Unstable));
    }

    [Test]
    public void GravityFieldStatus_Nominal_WhenPowerInNormalRange()
    {
        var status = _system.CalculateGravityFieldStatus(true, 800f, 1000f);
        Assert.That(status, Is.EqualTo(GravityFieldStatus.Nominal));
    }

    [Test]
    public void GravityFieldStatus_Overcharged_WhenPowerExceeds150Percent()
    {
        var status = _system.CalculateGravityFieldStatus(true, 1800f, 1000f);
        Assert.That(status, Is.EqualTo(GravityFieldStatus.Overcharged));
    }

    [Test]
    public void GravityFieldStatus_Inactive_WhenSystemDisabled()
    {
        _system.SetConfigValues(enabled: false, baseEscrow: 100, maxAutopilotSpeed: 50.0f, emergencyThreshold: 35.0f, minPowerRatio: 0.2f);
        var status = _system.CalculateGravityFieldStatus(true, 1000f, 1000f);
        Assert.That(status, Is.EqualTo(GravityFieldStatus.Inactive));
    }

    // ==========================================
    // 2. Autopilot Trajectory Math Tests
    // ==========================================

    [Test]
    public void CalculateAutopilotVector_ReturnsCorrectVelocityAndDistance()
    {
        var (velocity, remainingDistance) = _system.CalculateAutopilotVector(0f, 0f, 30f, 40f, 10f);

        // Distance sqrt(30^2 + 40^2) = 50
        Assert.Multiple(() =>
        {
            Assert.That(remainingDistance, Is.EqualTo(50.0f).Within(0.01f));
            Assert.That(velocity.X, Is.EqualTo(6.0f).Within(0.01f)); // 30/50 * 10
            Assert.That(velocity.Y, Is.EqualTo(8.0f).Within(0.01f)); // 40/50 * 10
            Assert.That(velocity.Length, Is.EqualTo(10.0f).Within(0.01f));
        });
    }

    [Test]
    public void CalculateAutopilotVector_ZeroDistance_ReturnsZeroVelocity()
    {
        var (velocity, remainingDistance) = _system.CalculateAutopilotVector(10f, 20f, 10f, 20f, 15f);

        Assert.Multiple(() =>
        {
            Assert.That(remainingDistance, Is.EqualTo(0f));
            Assert.That(velocity.X, Is.EqualTo(0f));
            Assert.That(velocity.Y, Is.EqualTo(0f));
        });
    }

    [Test]
    public void CalculateCourseTrajectory_GeneratesCorrectWaypointCount()
    {
        var waypoints = _system.CalculateCourseTrajectory(0f, 0f, 100f, 200f, 4);

        Assert.Multiple(() =>
        {
            Assert.That(waypoints.Count, Is.EqualTo(5)); // 4 segments -> 5 points
            Assert.That(waypoints[0].X, Is.EqualTo(0f));
            Assert.That(waypoints[0].Y, Is.EqualTo(0f));
            Assert.That(waypoints[2].X, Is.EqualTo(50f).Within(0.01f));
            Assert.That(waypoints[2].Y, Is.EqualTo(100f).Within(0.01f));
            Assert.That(waypoints[4].X, Is.EqualTo(100f).Within(0.01f));
            Assert.That(waypoints[4].Y, Is.EqualTo(200f).Within(0.01f));
        });
    }

    // ==========================================
    // 3. Escrow Fee Math Tests
    // ==========================================

    [Test]
    public void CalculateAutopilotEscrowFee_BaseFee()
    {
        var fee = _system.CalculateAutopilotEscrowFee(distance: 0f, targetSpeed: 20.0f);
        Assert.That(fee, Is.EqualTo(100)); // base escrow
    }

    [Test]
    public void CalculateAutopilotEscrowFee_DistanceAndSpeedScaling()
    {
        // base 100 + dist 100*0.5 (50) + speed (70 - 50)*2 (40) = 190
        var fee = _system.CalculateAutopilotEscrowFee(distance: 100f, targetSpeed: 70.0f);
        Assert.That(fee, Is.EqualTo(190));
    }

    [Test]
    public void CalculateAutopilotEscrowFee_EmergencyOverrideMultiplier()
    {
        // (100 + 50) * 1.5 = 225
        var fee = _system.CalculateAutopilotEscrowFee(distance: 100f, targetSpeed: 30.0f, isEmergencyOverride: true);
        Assert.That(fee, Is.EqualTo(225));
    }

    // ==========================================
    // 4. Thruster Dampening Tests
    // ==========================================

    [Test]
    public void ShouldTriggerEmergencyDampening_SpeedExceedsThreshold()
    {
        // emergencyThreshold = 35.0f
        Assert.Multiple(() =>
        {
            Assert.That(_system.ShouldTriggerEmergencyDampening(40.0f, 0.5f), Is.True);
            Assert.That(_system.ShouldTriggerEmergencyDampening(20.0f, 0.5f), Is.False);
        });
    }

    [Test]
    public void ShouldTriggerEmergencyDampening_UnstablePowerAtHighSpeed()
    {
        // minPowerRatio = 0.2f
        Assert.Multiple(() =>
        {
            Assert.That(_system.ShouldTriggerEmergencyDampening(15.0f, 0.1f), Is.True);
            Assert.That(_system.ShouldTriggerEmergencyDampening(5.0f, 0.1f), Is.False);
        });
    }

    [Test]
    public void ShouldTriggerEmergencyDampening_ManualOverrideAlwaysTriggers()
    {
        Assert.That(_system.ShouldTriggerEmergencyDampening(0.0f, 1.0f, manualOverride: true), Is.True);
    }

    [Test]
    public void CalculateThrusterDampening_ReducesVelocityCorrectly()
    {
        var result = _system.CalculateThrusterDampening(100f, 50f, dampeningFactor: 0.8f);

        // 1 - 0.8 = 0.2 multiplier
        Assert.Multiple(() =>
        {
            Assert.That(result.X, Is.EqualTo(20f).Within(0.01f));
            Assert.That(result.Y, Is.EqualTo(10f).Within(0.01f));
        });
    }

    // ==========================================
    // 5. Shuttle Lifecycle & Escrow Lifecycle Tests
    // ==========================================

    [Test]
    public void ShuttleRegistration_AndPowerUpdates()
    {
        var state = _system.RegisterShuttle(1, "Solreign-Explorer", 1000f);

        Assert.Multiple(() =>
        {
            Assert.That(state.ShuttleId, Is.EqualTo(1));
            Assert.That(state.ShuttleName, Is.EqualTo("Solreign-Explorer"));
            Assert.That(state.GravityStatus, Is.EqualTo(GravityFieldStatus.Inactive));
        });

        _system.UpdateGeneratorPower(1, active: true, currentPower: 900f);
        var updated = _system.GetShuttleState(1);

        Assert.Multiple(() =>
        {
            Assert.That(updated, Is.Not.Null);
            Assert.That(updated!.GeneratorActive, Is.True);
            Assert.That(updated.CurrentPower, Is.EqualTo(900f));
            Assert.That(updated.GravityStatus, Is.EqualTo(GravityFieldStatus.Nominal));
        });
    }

    [Test]
    public void TryEngageAutopilot_Success_WhenEscrowSufficient()
    {
        _system.RegisterShuttle(1, "Test-Shuttle");
        var requiredFee = _system.CalculateAutopilotEscrowFee(100f, 20f); // 100 + 50 = 150

        var ok = _system.TryEngageAutopilot(
            shuttleId: 1,
            currentX: 0f,
            currentY: 0f,
            targetX: 100f,
            targetY: 0f,
            targetSpeed: 20f,
            escrowFeePaid: 150,
            out var error);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True, error);
            Assert.That(error, Is.Empty);
        });

        var state = _system.GetShuttleState(1);
        Assert.Multiple(() =>
        {
            Assert.That(state!.AutopilotEngaged, Is.True);
            Assert.That(state.EscrowFeePaid, Is.EqualTo(150));
            Assert.That(state.Velocity.X, Is.EqualTo(20f).Within(0.01f));
        });
    }

    [Test]
    public void TryEngageAutopilot_Fails_WhenEscrowInsufficient()
    {
        _system.RegisterShuttle(1, "Test-Shuttle");

        var ok = _system.TryEngageAutopilot(
            shuttleId: 1,
            currentX: 0f,
            currentY: 0f,
            targetX: 100f,
            targetY: 0f,
            targetSpeed: 20f,
            escrowFeePaid: 50, // needs 150
            out var error);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(error, Does.Contain("Insufficient escrow fee"));
        });
    }

    [Test]
    public void CancelAutopilotEscrow_ReturnsFullRefund()
    {
        _system.RegisterShuttle(1, "Test-Shuttle");
        _system.TryEngageAutopilot(1, 0f, 0f, 100f, 0f, 20f, 200, out _);

        var ok = _system.CancelAutopilotEscrow(1, out var refund, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True, error);
            Assert.That(refund, Is.EqualTo(200));
        });

        var state = _system.GetShuttleState(1);
        Assert.Multiple(() =>
        {
            Assert.That(state!.AutopilotEngaged, Is.False);
            Assert.That(state.EscrowFeePaid, Is.EqualTo(0));
        });
    }

    [Test]
    public void UpdateShuttleTick_AdvancesPositionAndDisengagesAtDestination()
    {
        _system.RegisterShuttle(1, "Test-Shuttle");
        _system.TryEngageAutopilot(1, 0f, 0f, 10f, 0f, 10f, 150, out _);

        // Tick 0.5s -> moves 5 units to X=5
        _system.UpdateShuttleTick(1, 0.5f);
        var stateMid = _system.GetShuttleState(1);
        Assert.That(stateMid!.Position.X, Is.EqualTo(5f).Within(0.01f));

        // Tick 0.6s -> reaches destination X=10 -> disengages
        _system.UpdateShuttleTick(1, 0.6f);
        var stateEnd = _system.GetShuttleState(1);
        Assert.Multiple(() =>
        {
            Assert.That(stateEnd!.Position.X, Is.EqualTo(10f).Within(0.1f));
            Assert.That(stateEnd.AutopilotEngaged, Is.False);
        });
    }

    // ==========================================
    // 6. Zero-Exception Edge Case Tests
    // ==========================================

    [Test]
    public void ZeroException_NaNAndInfinityInputs_HandledGracefully()
    {
        Assert.DoesNotThrow(() =>
        {
            _system.CalculateGravityFieldStatus(true, float.NaN, float.PositiveInfinity);
            _system.CalculateAutopilotEscrowFee(float.NaN, float.NegativeInfinity);
            _system.CalculateAutopilotVector(float.NaN, float.NaN, float.PositiveInfinity, 0f, float.NaN);
            _system.CalculateCourseTrajectory(float.NaN, 0f, float.NaN, 10f, -5);
            _system.ShouldTriggerEmergencyDampening(float.NaN, float.NaN, false);
            _system.CalculateThrusterDampening(float.NaN, float.NaN, float.NaN);
            _system.UpdateShuttleTick(1, float.NaN);
        });
    }

    [Test]
    public void ZeroException_UnregisteredShuttleOperations_DoNotThrow()
    {
        Assert.DoesNotThrow(() =>
        {
            _system.UpdateGeneratorPower(999, true, 500f);
            _system.ApplyEmergencyDampening(999);
            _system.CancelAutopilotEscrow(999, out _, out _);
            _system.UpdateShuttleTick(999, 1.0f);
        });
    }
}
