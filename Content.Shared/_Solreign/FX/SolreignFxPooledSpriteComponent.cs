using Robust.Shared.GameObjects;

namespace Content.Shared._Solreign.FX;

/// <summary>
///     Marker component on every entity <see cref="SolreignFxRenderPool"/> pre-spawns for FX
///     Language v1's pooled cosmetic sprite category (spec §2.1's <c>SolreignFxCosmeticSprite</c>
///     entity, spawned once at pool-init and parked/reshown, never despawned). Deliberately empty —
///     its only job is to give the interim budget-regression test (spec §7's "Budget regression
///     (interim, pre-profiler)" row) something to <c>EntityQuery</c> over:
///     <c>EntityQuery&lt;SolreignFxPooledSpriteComponent&gt;</c> count must never exceed the sum of
///     every category's <see cref="Content.Shared._Solreign.FX.SolreignFxCategoryTable"/> concurrent
///     cap (scaled by the current pool-cap CVar/profile multiplier) — a real, profiler-derived
///     replacement for this interim check is future work per spec §3's own revalidation requirement,
///     not silently treated as final.
/// </summary>
[RegisterComponent]
public sealed partial class SolreignFxPooledSpriteComponent : Component
{
}
