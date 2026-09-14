using System.Numerics;
using Content.Shared._Solreign.FX;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Client._Solreign.FX;

/// <summary>
///     Client-side <see cref="ISolreignFxAnchorResolver"/> — resolves a wire anchor to a real,
///     finite world-space position using <c>IEntityManager</c>/<c>SharedTransformSystem</c>. Never
///     throws for any input, including a dangling <see cref="NetEntity"/> (deleted before arrival)
///     or nullspace/unloaded-map <see cref="NetCoordinates"/> (spec §1.3b.4) — every lookup is
///     guarded before the one call (<c>SharedTransformSystem.ToMapCoordinates</c>/<c>GetWorldPosition</c>)
///     that could otherwise throw on a missing component.
/// </summary>
public sealed class SolreignFxClientAnchorResolver : ISolreignFxAnchorResolver
{
    private readonly IEntityManager _entityManager;
    private readonly SharedTransformSystem _transform;

    public SolreignFxClientAnchorResolver(IEntityManager entityManager, SharedTransformSystem transform)
    {
        _entityManager = entityManager;
        _transform = transform;
    }

    public bool TryResolveCoordinatesWorldPosition(NetCoordinates coordinates, out Vector2 worldPosition)
    {
        worldPosition = default;

        // logError:false — a hostile/buggy server's malformed coordinates are an expected input
        // here, not an engine bug; the caller (TryValidateReceived) already aggregates/rate-limits
        // its own diagnostics (spec §1.3b's "never a per-cue log line" rule) and would otherwise be
        // doubled by this method's own per-call engine log. The NetCoordinates overload doesn't
        // expose logError, so resolve to EntityCoordinates first and use the overload that does.
        var entityCoordinates = _entityManager.GetCoordinates(coordinates);
        var map = _transform.ToMapCoordinates(entityCoordinates, logError: false);
        if (map.MapId == MapId.Nullspace)
            return false;

        if (!float.IsFinite(map.Position.X) || !float.IsFinite(map.Position.Y))
            return false;

        worldPosition = map.Position;
        return true;
    }

    public bool TryResolveEntityWorldPosition(NetEntity entity, out Vector2 worldPosition)
    {
        worldPosition = default;

        if (!_entityManager.TryGetEntity(entity, out var uid) || uid is not { } resolved)
            return false;

        if (!_entityManager.TryGetComponent<TransformComponent>(resolved, out var xform))
            return false;

        if (xform.MapUid is null || xform.MapID == MapId.Nullspace)
            return false;

        var pos = _transform.GetWorldPosition(xform);
        if (!float.IsFinite(pos.X) || !float.IsFinite(pos.Y))
            return false;

        worldPosition = pos;
        return true;
    }
}
