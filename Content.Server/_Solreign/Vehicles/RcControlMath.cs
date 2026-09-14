namespace Content.Server._Solreign.Vehicles;

/// <summary>
/// Pure bind/range rules for the RC car controller (Wave 2 of the RC vehicles pack,
/// see docs/specs/2026-07-11-rc-vehicles-spike.md gap #1). Kept dependency-free (no EntityUid,
/// no components) so the policy is unit-testable without a running server — <see cref="RcControllerSystem"/>
/// just reads its own ECS state into plain booleans/floats and calls into here.
/// </summary>
public static class RcControlMath
{
    public enum BindResult
    {
        /// <summary>Bind is allowed.</summary>
        Ok,

        /// <summary>The interact target doesn't have <c>RcControllableComponent</c> at all — not our business, let some other AfterInteract handler try.</summary>
        NotControllable,

        /// <summary>This car already has a different controller bound to it.</summary>
        CarAlreadyBound,

        /// <summary>This controller is already bound to a (possibly different) car; release it first.</summary>
        ControllerAlreadyBound,

        /// <summary>The car is dead/terminating — a wreck earns no driver.</summary>
        CarUnavailable,

        /// <summary>Standard interact-range failure (too far to point the controller at it).</summary>
        OutOfReach,

        /// <summary>
        /// The would-be driver is already driving a different car with another controller
        /// (e.g. holding two controllers). Only one relay target per mover is meaningful, so this
        /// is rejected explicitly rather than silently stealing the driver out from under the first bind.
        /// </summary>
        DriverAlreadyDriving,
    }

    /// <summary>
    /// Decides whether a controller may bind to a candidate car.
    /// </summary>
    public static BindResult CanBind(
        bool targetIsControllable,
        bool carAlreadyHasController,
        bool controllerAlreadyHasCar,
        bool carUnavailable,
        bool canReach,
        bool driverAlreadyDriving)
    {
        if (!targetIsControllable)
            return BindResult.NotControllable;

        if (controllerAlreadyHasCar)
            return BindResult.ControllerAlreadyBound;

        if (driverAlreadyDriving)
            return BindResult.DriverAlreadyDriving;

        if (carAlreadyHasController)
            return BindResult.CarAlreadyBound;

        if (carUnavailable)
            return BindResult.CarUnavailable;

        if (!canReach)
            return BindResult.OutOfReach;

        return BindResult.Ok;
    }

    /// <summary>
    /// Whether an active bind should be dropped this tick because the controller has drifted out
    /// of range of its car. Any non-finite distance (e.g. the car left the map, coordinates don't
    /// resolve) counts as out of range rather than silently staying bound forever.
    /// </summary>
    public static bool IsOutOfRange(float distance, float maxRange)
    {
        if (float.IsNaN(distance) || float.IsPositiveInfinity(distance))
            return true;

        return distance > maxRange;
    }
}
