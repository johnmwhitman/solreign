namespace Content.Server._Solreign.Vehicles;

/// <summary>
/// Added to the entity holding an <see cref="RcControllerComponent"/> while it is actively driving
/// a car, so <see cref="RcControllerSystem"/> can catch "ouch, let go of the controller" damage
/// interrupts on that specific holder without subscribing DamageDealtEvent on every mob repo-wide.
/// Removed the moment the bind drops, for any reason.
/// </summary>
[RegisterComponent]
[Access(typeof(RcControllerSystem))]
public sealed partial class RcDrivingComponent : Component
{
    /// <summary>
    /// The controller item this driver is holding.
    /// </summary>
    [DataField]
    public EntityUid Controller;

    /// <summary>
    /// The car currently receiving this driver's movement input.
    /// </summary>
    [DataField]
    public EntityUid Car;
}
