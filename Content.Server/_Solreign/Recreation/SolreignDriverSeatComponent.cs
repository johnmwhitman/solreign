namespace Content.Server._Solreign.Recreation;

/// <summary>
/// Placed on the <c>Strap</c> of a rideable recreation vehicle (e.g. <c>SolreignGoKart</c>,
/// Resources/Prototypes/_Solreign/Entities/recreation.yml) to wire "buckled in" to "steers this
/// entity" — gap #1 in docs/specs/2026-07-11-recreation-spec.md. Buckling to a plain
/// <c>Strap</c> does NOT call <c>SharedMoverController.SetRelay()</c> on its own; nothing upstream
/// does this for a bare seat (the two existing analogues, <c>CardboardBoxSystem</c> and
/// <c>SharedMechSystem</c>, wire it from their own container-insert/storage-open events instead of
/// from buckling). <see cref="SolreignDriverSeatSystem"/> is the ~15-line hookup the spec calls for.
///
/// Mirrors <c>CardboardBoxComponent.Mover</c>'s idiom of caching the current occupant so unbuckle
/// (and the defensive shutdown cleanup) can tell it's still the same entity before tearing the
/// relay down.
/// </summary>
[RegisterComponent]
[Access(typeof(SolreignDriverSeatSystem))]
public sealed partial class SolreignDriverSeatComponent : Component
{
    /// <summary>
    /// The entity currently buckled in and relaying movement input to this vehicle, if any.
    /// </summary>
    [ViewVariables]
    public EntityUid? Driver;
}
