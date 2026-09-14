using System.Numerics;
using Content.Shared._Solreign.FX;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Server._Solreign.FX;

/// <summary>
///     Server-side <see cref="ISolreignFxAnchorResolver"/> — same contract, same never-throws
///     guarantee as <c>Content.Client._Solreign.FX.SolreignFxClientAnchorResolver</c>, backed by the
///     server's own <c>IEntityManager</c>/<c>SharedTransformSystem</c>. Used by
///     <c>SolreignFxServerSystem.RaiseCue</c>/<c>RaiseSecretRoleCue</c> to validate a caller-supplied
///     anchor before <c>SolreignFxCueV1.TryCreate</c> ever runs (spec §1.3a.2).
/// </summary>
public sealed class SolreignFxServerAnchorResolver : ISolreignFxAnchorResolver
{
    private readonly IEntityManager _entityManager;
    private readonly SharedTransformSystem _transform;

    public SolreignFxServerAnchorResolver(IEntityManager entityManager, SharedTransformSystem transform)
    {
        _entityManager = entityManager;
        _transform = transform;
    }

    public bool TryResolveCoordinatesWorldPosition(NetCoordinates coordinates, out Vector2 worldPosition)
    {
        worldPosition = default;

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
