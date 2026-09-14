using Content.Server.Popups;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.Vehicles;

/// <summary>
/// Solreign RC vehicles pack, Wave 2 (docs/specs/2026-07-11-rc-vehicles-spike.md, gap #1 + #3).
/// Binds a handheld <see cref="RcControllerComponent"/> to a car carrying
/// <see cref="RcControllableComponent"/>: point the controller at the car (AfterInteract) to bind;
/// a "let go of the wheel" verb on the controller releases it early. While bound, the holder's
/// movement input relays into the car (<c>SharedMoverController.SetRelay</c>) instead of moving the
/// holder's own body — the "pause the driver's body" gap (#3) falls out of that relay redirect for free.
///
/// Gap #2(a) — "driver sees through the car" — is handled entirely in YAML: the controller item
/// also carries a <c>SurveillanceCameraMonitor</c>/<c>ActivatableUI</c> stack (same idiom as the
/// syndicate camera bug), so the same handheld item is both the steering wheel and the screen —
/// use-in-hand opens that monitor UI. Unbinding deliberately does NOT also hang off use-in-hand:
/// <c>ActivatableUIComponent</c> already owns that event on this same item to open the monitor, and
/// two components racing over one input is exactly the kind of bug this spike is trying to avoid.
///
/// Bind drops automatically (with a popup + sound) when: the controller is dropped or leaves the
/// hand, the "let go" verb is used, the car dies or is deleted, the holder takes interrupting damage
/// ("ouch, dropped the controller"), or the pair drifts out of <see cref="RcControllerComponent.Range"/>.
/// </summary>
public sealed partial class RcControllerSystem : EntitySystem
{
    [Dependency] private SharedMoverController _mover = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RcControllerComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<RcControllerComponent, GetVerbsEvent<InteractionVerb>>(OnGetInteractionVerbs);
        SubscribeLocalEvent<RcControllerComponent, DroppedEvent>(OnDropped);
        SubscribeLocalEvent<RcControllerComponent, GotUnequippedHandEvent>(OnUnequippedHand);
        SubscribeLocalEvent<RcControllerComponent, ComponentShutdown>(OnControllerShutdown);

        SubscribeLocalEvent<RcControllableComponent, ComponentShutdown>(OnCarShutdown);
        SubscribeLocalEvent<RcControllableComponent, MobStateChangedEvent>(OnCarMobStateChanged);

        SubscribeLocalEvent<RcDrivingComponent, DamageDealtEvent>(OnDriverDamaged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<RcControllerComponent>();
        while (query.MoveNext(out var uid, out var controller))
        {
            if (controller.ControlledCar is not { } car)
                continue;

            if (now < controller.NextCheck)
                continue;

            controller.NextCheck = now + controller.CheckInterval;

            if (TerminatingOrDeleted(car) || _mobState.IsDead(car))
            {
                Unbind((uid, controller), "rc-vehicles-controller-lost-signal");
                continue;
            }

            if (!TryComp(uid, out TransformComponent? controllerXform)
                || !TryComp(car, out TransformComponent? carXform)
                || !controllerXform.Coordinates.TryDistance(EntityManager, carXform.Coordinates, out var distance))
            {
                Unbind((uid, controller), "rc-vehicles-controller-lost-signal");
                continue;
            }

            if (RcControlMath.IsOutOfRange(distance, controller.Range))
                Unbind((uid, controller), "rc-vehicles-controller-out-of-range");
        }
    }

    private void OnAfterInteract(Entity<RcControllerComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target is not { } target)
            return;

        if (TryBind(ent, target, args.User, args.CanReach))
            args.Handled = true;
    }

    private void OnGetInteractionVerbs(Entity<RcControllerComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null || ent.Comp.ControlledCar == null)
            return;

        args.Verbs.Add(new InteractionVerb
        {
            Act = () => Unbind(ent, "rc-vehicles-controller-released"),
            Message = Loc.GetString("rc-vehicles-controller-release-verb-message"),
            Text = Loc.GetString("rc-vehicles-controller-release-verb-text"),
        });
    }

    private void OnDropped(Entity<RcControllerComponent> ent, ref DroppedEvent args)
    {
        if (ent.Comp.ControlledCar != null)
            Unbind(ent, "rc-vehicles-controller-dropped");
    }

    private void OnUnequippedHand(Entity<RcControllerComponent> ent, ref GotUnequippedHandEvent args)
    {
        if (ent.Comp.ControlledCar != null)
            Unbind(ent, "rc-vehicles-controller-dropped");
    }

    private void OnControllerShutdown(Entity<RcControllerComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.ControlledCar != null)
            Unbind(ent, null);
    }

    private void OnCarShutdown(Entity<RcControllableComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Controller is { } controllerUid && TryComp<RcControllerComponent>(controllerUid, out var controller))
            Unbind((controllerUid, controller), "rc-vehicles-controller-lost-signal");
    }

    private void OnCarMobStateChanged(Entity<RcControllableComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        if (ent.Comp.Controller is { } controllerUid && TryComp<RcControllerComponent>(controllerUid, out var controller))
            Unbind((controllerUid, controller), "rc-vehicles-controller-lost-signal");
    }

    private void OnDriverDamaged(Entity<RcDrivingComponent> ent, ref DamageDealtEvent args)
    {
        if (!args.InterruptsDoAfters)
            return;

        if (!TryComp<RcControllerComponent>(ent.Comp.Controller, out var controller))
            return;

        Unbind((ent.Comp.Controller, controller), "rc-vehicles-controller-flinch");
    }

    /// <summary>
    /// Attempts to bind <paramref name="controllerEnt"/> to <paramref name="target"/>, popping
    /// feedback to <paramref name="user"/> on success and on most failure paths. Returns false only
    /// when <paramref name="target"/> isn't an RC car at all, so the interaction is left unhandled
    /// for some other AfterInteract subscriber.
    /// </summary>
    private bool TryBind(Entity<RcControllerComponent> controllerEnt, EntityUid target, EntityUid user, bool canReach)
    {
        var targetIsControllable = TryComp<RcControllableComponent>(target, out var carComp);

        var result = RcControlMath.CanBind(
            targetIsControllable,
            carAlreadyHasController: targetIsControllable && carComp!.Controller != null,
            controllerAlreadyHasCar: controllerEnt.Comp.ControlledCar != null,
            carUnavailable: targetIsControllable && (TerminatingOrDeleted(target) || _mobState.IsDead(target)),
            canReach: canReach,
            driverAlreadyDriving: HasComp<RcDrivingComponent>(user));

        switch (result)
        {
            case RcControlMath.BindResult.NotControllable:
                return false;

            case RcControlMath.BindResult.ControllerAlreadyBound:
                _popup.PopupEntity(Loc.GetString("rc-vehicles-controller-already-bound"), user, user);
                return true;

            case RcControlMath.BindResult.DriverAlreadyDriving:
                _popup.PopupEntity(Loc.GetString("rc-vehicles-controller-driver-busy"), user, user);
                return true;

            case RcControlMath.BindResult.CarAlreadyBound:
                _popup.PopupEntity(Loc.GetString("rc-vehicles-controller-car-taken"), user, user);
                return true;

            case RcControlMath.BindResult.CarUnavailable:
                _popup.PopupEntity(Loc.GetString("rc-vehicles-controller-car-dead"), user, user);
                return true;

            case RcControlMath.BindResult.OutOfReach:
                // Standard interact-range failure; the base interaction system already gives
                // feedback for this, so no extra popup here.
                return true;

            case RcControlMath.BindResult.Ok:
                Bind(controllerEnt, (target, carComp!), user);
                return true;

            default:
                return true;
        }
    }

    private void Bind(Entity<RcControllerComponent> controllerEnt, Entity<RcControllableComponent> carEnt, EntityUid user)
    {
        controllerEnt.Comp.ControlledCar = carEnt.Owner;
        controllerEnt.Comp.Driver = user;
        controllerEnt.Comp.NextCheck = _timing.CurTime + controllerEnt.Comp.CheckInterval;

        carEnt.Comp.Controller = controllerEnt.Owner;
        carEnt.Comp.Driver = user;

        var driving = EnsureComp<RcDrivingComponent>(user);
        driving.Controller = controllerEnt.Owner;
        driving.Car = carEnt.Owner;

        _mover.SetRelay(user, carEnt.Owner);

        _popup.PopupEntity(Loc.GetString("rc-vehicles-controller-bound", ("car", carEnt.Owner)), user, user);
        _audio.PlayPvs(controllerEnt.Comp.ConnectSound, controllerEnt.Owner);
    }

    /// <summary>
    /// Drops an active bind. <paramref name="locKey"/> is popped to the driver if given, and is left
    /// null for the "the controller itself is going away" shutdown path where there's no one left to
    /// pop a message at (`OnControllerShutdown` running mid-deletion of the controller, not the driver).
    /// </summary>
    private void Unbind(Entity<RcControllerComponent> controllerEnt, string? locKey)
    {
        var car = controllerEnt.Comp.ControlledCar;
        var driver = controllerEnt.Comp.Driver;

        controllerEnt.Comp.ControlledCar = null;
        controllerEnt.Comp.Driver = null;

        if (car is { } carUid && TryComp<RcControllableComponent>(carUid, out var carComp))
        {
            carComp.Controller = null;
            carComp.Driver = null;
        }

        if (driver is { } driverUid && !TerminatingOrDeleted(driverUid))
        {
            RemCompDeferred<RcDrivingComponent>(driverUid);

            // Only release the relay if it's still pointed at our car — something else may have
            // already taken it over in the meantime (e.g. the driver got scooped into a mech).
            if (TryComp<RelayInputMoverComponent>(driverUid, out var relay) && relay.RelayEntity == car)
                RemCompDeferred<RelayInputMoverComponent>(driverUid);

            if (locKey != null)
                _popup.PopupEntity(Loc.GetString(locKey), driverUid, driverUid);
        }

        _audio.PlayPvs(controllerEnt.Comp.DisconnectSound, controllerEnt.Owner);
    }
}
