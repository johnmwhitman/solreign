using System;
using System.Collections.Generic;
using Content.Shared._Solreign.FX;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Client._Solreign.FX;

/// <summary>
///     The engine-wired half of spec §2.1's "one genuinely new piece of client infrastructure" —
///     <see cref="Content.Shared._Solreign.FX.SolreignFxPool"/> (already shipped, W2) is pure
///     (category, slotIndex) bookkeeping with zero Robust entity spawning by design; THIS type owns
///     the actual <c>Spawn(SolreignFxCosmeticSprite, MapCoordinates.Nullspace)</c>/park/reposition
///     mechanics spec §2.1 assigns to "a future consumer (W3)." One <see cref="EntityUid"/> array per
///     <see cref="SolreignFxCategory"/>, sized identically to (and rebuilt in lockstep with, via
///     <see cref="SolreignFxCueSystem"/>'s <c>OnPoolRebuilt</c> hook) the logical pool's own
///     per-category capacity — a (category, slotIndex) pair means the exact same activation on both
///     sides.
///
///     Entities are spawned ONCE per (category, slotIndex) and never despawned for the lifetime of
///     the pool — only parked (moved to nullspace, hidden, light disabled, any post-shader cleared)
///     between activations, matching spec §2.1's "spawned once at pool-init... parked when idle,
///     repositioned and re-shown on cue receipt" framing exactly. Every entity carries
///     <see cref="SolreignFxPooledSpriteComponent"/> from its own prototype declaration — the marker
///     the interim budget-regression test (spec §7) queries.
/// </summary>
public sealed class SolreignFxRenderPool
{
    private const string CosmeticSpritePrototype = "SolreignFxCosmeticSprite";

    private readonly IEntityManager _entityManager;
    private readonly SpriteSystem _sprite;
    private readonly SharedPointLightSystem _light;
    private readonly SharedTransformSystem _transform;

    private readonly Dictionary<SolreignFxCategory, EntityUid[]> _entities = new();

    public SolreignFxRenderPool(
        IEntityManager entityManager,
        SpriteSystem sprite,
        SharedPointLightSystem light,
        SharedTransformSystem transform)
    {
        _entityManager = entityManager;
        _sprite = sprite;
        _light = light;
        _transform = transform;
    }

    /// <summary>
    ///     Resizes every category's entity array to match <paramref name="capacities"/> exactly.
    ///     Growing spawns new parked entities; shrinking deletes the surplus (highest-index-first —
    ///     the caller, <see cref="SolreignFxCueSystem"/>, already wipes its own active-effect list on
    ///     every rebuild, so there is never a live activation pointing at a surplus slot when this
    ///     runs).
    /// </summary>
    public void EnsureCapacities(IReadOnlyDictionary<SolreignFxCategory, int> capacities)
    {
        foreach (var (category, rawCapacity) in capacities)
        {
            var capacity = Math.Max(0, rawCapacity);
            _entities.TryGetValue(category, out var existing);
            existing ??= Array.Empty<EntityUid>();

            if (existing.Length == capacity)
                continue;

            if (existing.Length > capacity)
            {
                for (var i = capacity; i < existing.Length; i++)
                {
                    if (_entityManager.EntityExists(existing[i]))
                        _entityManager.DeleteEntity(existing[i]);
                }

                var shrunk = new EntityUid[capacity];
                Array.Copy(existing, shrunk, capacity);
                _entities[category] = shrunk;
                continue;
            }

            var grown = new EntityUid[capacity];
            Array.Copy(existing, grown, existing.Length);
            for (var i = existing.Length; i < capacity; i++)
                grown[i] = SpawnParked();

            _entities[category] = grown;
        }
    }

    private EntityUid SpawnParked()
    {
        var uid = _entityManager.SpawnEntity(CosmeticSpritePrototype, MapCoordinates.Nullspace);
        Park(uid);
        return uid;
    }

    /// <summary>Returns the pooled entity for (<paramref name="category"/>, <paramref name="slotIndex"/>), or null if the pool hasn't been sized for it yet (e.g. a race with a not-yet-applied CVar rebuild — the caller should treat this as "nothing to render this frame," never throw).</summary>
    public EntityUid? GetEntity(SolreignFxCategory category, int slotIndex)
    {
        if (!_entities.TryGetValue(category, out var slots) || slotIndex < 0 || slotIndex >= slots.Length)
            return null;

        var uid = slots[slotIndex];
        return _entityManager.EntityExists(uid) ? uid : null;
    }

    /// <summary>Total pooled entity count across every category right now — the interim budget-regression check's own denominator (spec §7: "never exceeds the declared cap").</summary>
    public int TotalCapacity()
    {
        var total = 0;
        foreach (var slots in _entities.Values)
            total += slots.Length;

        return total;
    }

    /// <summary>Moves the entity back to nullspace, hides its sprite, disables its light, and clears any post-shader — the at-rest state every pooled entity returns to between activations.</summary>
    public void Park(EntityUid uid)
    {
        if (!_entityManager.EntityExists(uid))
            return;

        // SharedTransformSystem.SetMapCoordinates always resolves its target via
        // SharedMapSystem.GetMap, which throws/KeyNotFound-fails for MapId.Nullspace (Nullspace has
        // no registered map entity to look up — it's the special "no map at all" case, not just an
        // empty one). DetachEntity is the actual sanctioned "move to nullspace" operation
        // (Spawn(protoName, MapCoordinates.Nullspace) itself works via a different code path,
        // CreateEntityUninitialized, that special-cases Nullspace directly).
        _transform.DetachEntity(uid);

        if (_entityManager.TryGetComponent<SpriteComponent>(uid, out var sprite))
        {
            _sprite.SetVisible((uid, sprite), false);
            sprite.PostShader = null;
        }

        if (_entityManager.TryGetComponent<PointLightComponent>(uid, out var light))
            _light.SetEnabled(uid, false, light);
    }

    /// <summary>Repositions a parked-or-active entity to <paramref name="coordinates"/> and shows it — the caller (the rendering partial) configures sprite/light state separately via <see cref="Sprite"/>/<see cref="Robust.Client.GameObjects.SpriteSystem"/>/<see cref="SharedPointLightSystem"/> directly.</summary>
    public void Reposition(EntityUid uid, MapCoordinates coordinates)
    {
        if (_entityManager.EntityExists(uid))
            _transform.SetMapCoordinates(uid, coordinates);
    }

    /// <summary>Deletes every pooled entity in every category and clears the table — called on system shutdown (grk W3 round-1 review finding #12: process teardown usually reclaims these anyway, but hot-reload/system-restart paths might not).</summary>
    public void DeleteAll()
    {
        foreach (var slots in _entities.Values)
        {
            foreach (var uid in slots)
            {
                if (_entityManager.EntityExists(uid))
                    _entityManager.DeleteEntity(uid);
            }
        }

        _entities.Clear();
    }
}
