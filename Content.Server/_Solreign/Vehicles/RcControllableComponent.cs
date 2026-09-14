namespace Content.Server._Solreign.Vehicles;

/// <summary>
/// Marks an entity (the RC car) as bindable to an <see cref="RcControllerComponent"/> handheld
/// controller. See docs/specs/2026-07-11-rc-vehicles-spike.md gap #1.
/// </summary>
[RegisterComponent]
[Access(typeof(RcControllerSystem))]
public sealed partial class RcControllableComponent : Component
{
    /// <summary>
    /// The controller item currently bound to this car, if any. Null means up for grabs.
    /// </summary>
    [DataField]
    public EntityUid? Controller;

    /// <summary>
    /// The entity whose movement input is being relayed into this car (whoever is holding the
    /// controller). Cached here purely for popups/feedback; the actual relay bookkeeping lives on
    /// <c>RelayInputMoverComponent</c>/<c>MovementRelayTargetComponent</c> via <c>SharedMoverController</c>.
    /// </summary>
    [DataField]
    public EntityUid? Driver;
}
