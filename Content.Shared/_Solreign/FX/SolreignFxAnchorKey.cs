using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Shared._Solreign.FX;

/// <summary>
///     A cue's anchor identity for merge/coalesce-by-(EffectId, anchor) purposes (spec §3's pool
///     merge rule AND spec §3.1's egress-budget coalescing rule — both sides of the wire need the
///     exact same "is this the same anchor" notion, hence one shared type rather than a
///     client-local and a server-local copy). Wraps exactly one of
///     <see cref="NetEntity"/>/<see cref="NetCoordinates"/> — the same one-of shape
///     <see cref="SolreignFxCueV1"/> itself carries. Two coordinate anchors compare equal only on
///     exact position match (no epsilon) — a merge/coalesce is meant for the same repeated trigger
///     point, not "nearby."
/// </summary>
public readonly struct SolreignFxAnchorKey : IEquatable<SolreignFxAnchorKey>
{
    private readonly NetEntity? _entity;
    private readonly NetCoordinates? _coordinates;

    private SolreignFxAnchorKey(NetEntity? entity, NetCoordinates? coordinates)
    {
        _entity = entity;
        _coordinates = coordinates;
    }

    public static SolreignFxAnchorKey FromEntity(NetEntity entity) => new(entity, null);
    public static SolreignFxAnchorKey FromCoordinates(NetCoordinates coordinates) => new(null, coordinates);

    public bool Equals(SolreignFxAnchorKey other)
    {
        if (_entity.HasValue != other._entity.HasValue ||
            _coordinates.HasValue != other._coordinates.HasValue)
        {
            return false;
        }

        return (!_entity.HasValue ||
                _entity.GetValueOrDefault().Equals(other._entity.GetValueOrDefault()))
            && (!_coordinates.HasValue ||
                _coordinates.GetValueOrDefault().Equals(other._coordinates.GetValueOrDefault()));
    }

    public override bool Equals(object? obj) => obj is SolreignFxAnchorKey other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_entity, _coordinates);
}
