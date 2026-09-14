using Robust.Shared.Audio;

namespace Content.Server._Solreign.Vehicles;

/// <summary>
/// Solreign RC vehicles pack, Wave 2 — see docs/specs/2026-07-11-rc-vehicles-spike.md gap #1/#2(a).
/// Marks a handheld item as an RC car controller. Point it at (AfterInteract) an entity carrying
/// <see cref="RcControllableComponent"/> to bind: the holder's movement input then relays into the
/// car via <c>SharedMoverController.SetRelay</c>. A "let go of the wheel" verb releases the car
/// early (deliberately not use-in-hand — <c>ActivatableUIComponent</c> already owns that event on
/// this same item to open the camera monitor UI; see <see cref="RcControllerSystem"/> for why).
/// This item is also expected to carry a <c>SurveillanceCameraMonitor</c> stack in YAML (gap #2(a))
/// so the same controller doubles as the "see through the car" handheld monitor.
/// </summary>
[RegisterComponent]
[Access(typeof(RcControllerSystem))]
public sealed partial class RcControllerComponent : Component
{
    /// <summary>
    /// The car currently bound to this controller, if any.
    /// </summary>
    [DataField]
    public EntityUid? ControlledCar;

    /// <summary>
    /// Who is holding this controller while bound. Cached so the periodic range check doesn't need
    /// a hands lookup every tick.
    /// </summary>
    [DataField]
    public EntityUid? Driver;

    /// <summary>
    /// Max distance (in tiles) before the link auto-drops. Defaults to the same envelope as the
    /// car's own wireless camera range (see rc_car.yml) so "close enough to watch" and "close enough
    /// to drive" line up.
    /// </summary>
    [DataField]
    public float Range = 200f;

    /// <summary>
    /// How often, while bound, to re-check range and that the car is still alive.
    /// </summary>
    [DataField]
    public TimeSpan CheckInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Next time (game time) the periodic check should run. Advances by <see cref="CheckInterval"/>.
    /// </summary>
    [DataField]
    public TimeSpan NextCheck = TimeSpan.Zero;

    /// <summary>
    /// Played on this controller when a bind succeeds.
    /// </summary>
    [DataField]
    public SoundSpecifier? ConnectSound = new SoundPathSpecifier("/Audio/Machines/beep.ogg");

    /// <summary>
    /// Played on this controller whenever the bind drops, for any reason.
    /// </summary>
    [DataField]
    public SoundSpecifier? DisconnectSound = new SoundPathSpecifier("/Audio/Machines/button.ogg");
}
