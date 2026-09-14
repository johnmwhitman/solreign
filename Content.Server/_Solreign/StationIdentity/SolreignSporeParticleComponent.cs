using System.Numerics;

namespace Content.Server._Solreign.StationIdentity;

/// <summary>
///     Marks a cosmetic entity (see <c>SolreignAmbientSpore</c>,
///     Resources/Prototypes/_Solreign/Entities/StationIdentity/spores.yml) as a harmless drifting spore
///     particle spawned by <see cref="SolreignSporeDriftRule"/>. Rolls its own random drift velocity on
///     <see cref="SolreignSporeParticleSystem.OnMapInit"/> rather than having the spawning rule reach
///     into this component's fields directly — keeps the [Access] boundary clean (only
///     <see cref="SolreignSporeParticleSystem"/> ever writes <see cref="Velocity"/>) and means the spore
///     prototype is fully self-contained: spawn it anywhere (admin spawn menu included, even though it's
///     hidden by default) and it drifts on its own.
/// </summary>
[RegisterComponent, Access(typeof(SolreignSporeParticleSystem))]
public sealed partial class SolreignSporeParticleComponent : Component
{
    /// <summary>Slowest drift speed, in tiles/second.</summary>
    [DataField]
    public float MinSpeed = 0.1f;

    /// <summary>Fastest drift speed, in tiles/second.</summary>
    [DataField]
    public float MaxSpeed = 0.35f;

    /// <summary>Rolled once on <c>MapInitEvent</c> from [<see cref="MinSpeed"/>, <see cref="MaxSpeed"/>]
    /// in a uniformly random direction. Server-side visual state only; not saved, not networked (the
    /// entity's <c>TransformComponent</c> position carries the networked state instead).</summary>
    [ViewVariables]
    public Vector2 Velocity;
}
