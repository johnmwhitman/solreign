using Content.Server._Solreign.Vehicles;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(RcControlMath))]
public sealed class RcControlMathTests
{
    // --- CanBind ---

    [Test]
    public void CanBind_AllClear_ReturnsOk()
    {
        var result = RcControlMath.CanBind(
            targetIsControllable: true,
            carAlreadyHasController: false,
            controllerAlreadyHasCar: false,
            carUnavailable: false,
            canReach: true,
            driverAlreadyDriving: false);

        Assert.That(result, Is.EqualTo(RcControlMath.BindResult.Ok));
    }

    [Test]
    public void CanBind_TargetNotControllable_ReturnsNotControllable()
    {
        // Everything else about the situation looks fine — the only thing wrong is the target
        // isn't an RC car at all. This must win over every other check so the interaction is left
        // unhandled for some other AfterInteract subscriber.
        var result = RcControlMath.CanBind(
            targetIsControllable: false,
            carAlreadyHasController: true,
            controllerAlreadyHasCar: true,
            carUnavailable: true,
            canReach: false,
            driverAlreadyDriving: true);

        Assert.That(result, Is.EqualTo(RcControlMath.BindResult.NotControllable));
    }

    [Test]
    public void CanBind_ControllerAlreadyBound_ReturnsControllerAlreadyBound()
    {
        var result = RcControlMath.CanBind(
            targetIsControllable: true,
            carAlreadyHasController: false,
            controllerAlreadyHasCar: true,
            carUnavailable: false,
            canReach: true,
            driverAlreadyDriving: false);

        Assert.That(result, Is.EqualTo(RcControlMath.BindResult.ControllerAlreadyBound));
    }

    [Test]
    public void CanBind_DriverAlreadyDriving_ReturnsDriverAlreadyDriving()
    {
        // Distinct from ControllerAlreadyBound: this controller is fresh (unbound), but the
        // would-be driver is holding a *different* bound controller already.
        var result = RcControlMath.CanBind(
            targetIsControllable: true,
            carAlreadyHasController: false,
            controllerAlreadyHasCar: false,
            carUnavailable: false,
            canReach: true,
            driverAlreadyDriving: true);

        Assert.That(result, Is.EqualTo(RcControlMath.BindResult.DriverAlreadyDriving));
    }

    [Test]
    public void CanBind_DriverAlreadyDriving_TakesPriorityOverCarAlreadyBound()
    {
        // Both are true; DriverAlreadyDriving should win so the popup blames the driver's own
        // hands being full rather than implying the *target* car is the problem.
        var result = RcControlMath.CanBind(
            targetIsControllable: true,
            carAlreadyHasController: true,
            controllerAlreadyHasCar: false,
            carUnavailable: false,
            canReach: true,
            driverAlreadyDriving: true);

        Assert.That(result, Is.EqualTo(RcControlMath.BindResult.DriverAlreadyDriving));
    }

    [Test]
    public void CanBind_CarAlreadyBound_ReturnsCarAlreadyBound()
    {
        var result = RcControlMath.CanBind(
            targetIsControllable: true,
            carAlreadyHasController: true,
            controllerAlreadyHasCar: false,
            carUnavailable: false,
            canReach: true,
            driverAlreadyDriving: false);

        Assert.That(result, Is.EqualTo(RcControlMath.BindResult.CarAlreadyBound));
    }

    [Test]
    public void CanBind_CarUnavailable_ReturnsCarUnavailable()
    {
        var result = RcControlMath.CanBind(
            targetIsControllable: true,
            carAlreadyHasController: false,
            controllerAlreadyHasCar: false,
            carUnavailable: true,
            canReach: true,
            driverAlreadyDriving: false);

        Assert.That(result, Is.EqualTo(RcControlMath.BindResult.CarUnavailable));
    }

    [Test]
    public void CanBind_CarUnavailable_TakesPriorityOverOutOfReach()
    {
        // A dead car ten tiles away should report "it's dead", not "too far away" — the dead
        // report is the more actionable/honest one (walking closer won't help).
        var result = RcControlMath.CanBind(
            targetIsControllable: true,
            carAlreadyHasController: false,
            controllerAlreadyHasCar: false,
            carUnavailable: true,
            canReach: false,
            driverAlreadyDriving: false);

        Assert.That(result, Is.EqualTo(RcControlMath.BindResult.CarUnavailable));
    }

    [Test]
    public void CanBind_OutOfReach_ReturnsOutOfReach()
    {
        var result = RcControlMath.CanBind(
            targetIsControllable: true,
            carAlreadyHasController: false,
            controllerAlreadyHasCar: false,
            carUnavailable: false,
            canReach: false,
            driverAlreadyDriving: false);

        Assert.That(result, Is.EqualTo(RcControlMath.BindResult.OutOfReach));
    }

    // --- IsOutOfRange ---

    [Test]
    public void IsOutOfRange_WellWithinRange_IsFalse()
    {
        Assert.That(RcControlMath.IsOutOfRange(5f, 200f), Is.False);
    }

    [Test]
    public void IsOutOfRange_ExactlyAtRange_IsFalse()
    {
        Assert.That(RcControlMath.IsOutOfRange(200f, 200f), Is.False);
    }

    [Test]
    public void IsOutOfRange_JustPastRange_IsTrue()
    {
        Assert.That(RcControlMath.IsOutOfRange(200.01f, 200f), Is.True);
    }

    [Test]
    public void IsOutOfRange_ZeroDistance_IsFalse()
    {
        Assert.That(RcControlMath.IsOutOfRange(0f, 200f), Is.False);
    }

    [Test]
    public void IsOutOfRange_PositiveInfinity_IsTrue()
    {
        // Cross-map / unresolvable coordinates should never read as "still in range".
        Assert.That(RcControlMath.IsOutOfRange(float.PositiveInfinity, 200f), Is.True);
    }

    [Test]
    public void IsOutOfRange_NaN_IsTrue()
    {
        Assert.That(RcControlMath.IsOutOfRange(float.NaN, 200f), Is.True);
    }

    [Test]
    public void IsOutOfRange_ZeroRange_AnyPositiveDistanceIsOutOfRange()
    {
        Assert.That(RcControlMath.IsOutOfRange(0.01f, 0f), Is.True);
    }
}
