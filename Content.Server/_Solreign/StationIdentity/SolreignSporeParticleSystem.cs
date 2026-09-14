using System.Numerics;
using Robust.Shared.Random;

namespace Content.Server._Solreign.StationIdentity;

/// <summary>
///     Drives every <see cref="SolreignSporeParticleComponent"/>: rolls a random drift velocity on
///     spawn, then nudges the entity's local position along it every tick using
///     <see cref="StationMoodMath.AdvanceDrift"/> until <c>TimedDespawn</c> (declared on the
///     <c>SolreignAmbientSpore</c> prototype, not here) removes it. Update-loop shape follows upstream
///     <c>EmitSoundSystem</c>/this wave's own <see cref="SolreignStationMoodSystem"/>: a plain
///     <c>EntityQueryEnumerator</c> (skips paused entities) with no per-tick allocation.
/// </summary>
public sealed partial class SolreignSporeParticleSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SolreignSporeParticleComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<SolreignSporeParticleComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.Velocity = _random.NextVector2(ent.Comp.MinSpeed, ent.Comp.MaxSpeed);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SolreignSporeParticleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (comp.Velocity == Vector2.Zero)
                continue;

            var next = StationMoodMath.AdvanceDrift(xform.LocalPosition, comp.Velocity, frameTime);
            _transform.SetLocalPosition(uid, next, xform);
        }
    }
}
