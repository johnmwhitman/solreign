using Robust.Shared.Enums;
using System.Collections.Generic;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._Solreign.FX;

/// <summary>
///     `cast_ring` primitive's entity-anchored windup/telegraph ring (spec §2 row 6:
///     "<see cref="SolreignAcidBorderOverlay"/>'s Overlay-subclass pattern, retargeted from
///     screen-space-fixed to entity-anchored world-space — New <c>SolreignCastRingOverlay</c>,
///     anchored to <c>EntityAnchor</c> each frame instead of the full viewport"). Multi-instance
///     (up to spec §3's concurrent cap of 8), each with its own unique shader instance.
///
///     This is the FULL/LOW_VFX/REDUCED_MOTION rendering path (spec §4's profile table:
///     "animated ring + light" / "ring animation frozen to a slow linear fill" / "simplified
///     single-color fill" — all three still draw the ring, only the animation style differs).
///     `cosmetic_minimal` never uses this overlay at all — see
///     <see cref="SolreignFxCastRingCountdownOverlay"/> for that profile's "static icon + countdown
///     text" replacement (spec §4: "this is gameplay-critical... never dropped, only re-rendered
///     non-visually-dense").
/// </summary>
public sealed partial class SolreignFxCastRingOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> Shader = "SolreignFxCastRing";
    private const float HalfExtent = 0.9f;

    [Dependency] private IPrototypeManager _prototypeManager = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private readonly record struct Activation(MapCoordinates Position, Color Color, float FillProgress, bool Animated, ShaderInstance Shader);

    private readonly Dictionary<int, Activation> _active = new();

    public SolreignFxCastRingOverlay()
    {
        IoCManager.InjectDependencies(this);
    }

    /// <summary>
    ///     Activates or updates slot <paramref name="slotIndex"/>. <paramref name="fillProgress"/>
    ///     (0..1) is the windup's elapsed/duration ratio, recomputed every frame by the rendering
    ///     partial — the ring itself IS the telegraphed countdown, so it must track real elapsed
    ///     time, never a fixed animation clip. <paramref name="animated"/> false freezes the fill to
    ///     a static linear look with no pulse (spec: reduced_motion's "no pulsing/rotation").
    /// </summary>
    public void SetActivation(int slotIndex, MapCoordinates position, Color color, float fillProgress, bool animated)
    {
        if (!_active.TryGetValue(slotIndex, out var existing))
            existing = new Activation(position, color, fillProgress, animated, _prototypeManager.Index(Shader).InstanceUnique());

        _active[slotIndex] = existing with { Position = position, Color = color, FillProgress = fillProgress, Animated = animated };
    }

    /// <summary>Deactivates and DISPOSES slot <paramref name="slotIndex"/>'s <see cref="ShaderInstance"/> (grk W3 round-1 review finding H1 — see <see cref="SolreignFxArcOverlay.ClearActivation"/>'s remarks for the full rationale).</summary>
    public void ClearActivation(int slotIndex)
    {
        if (_active.Remove(slotIndex, out var activation))
            activation.Shader.Dispose();
    }

    /// <summary>Disposes every currently-active shader instance — called on system shutdown.</summary>
    public void ClearAllActivations()
    {
        foreach (var activation in _active.Values)
            activation.Shader.Dispose();

        _active.Clear();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_active.Count == 0)
            return;

        var worldHandle = args.WorldHandle;

        foreach (var activation in _active.Values)
        {
            if (activation.Position.MapId != args.MapId)
                continue;

            var box = new Box2(
                activation.Position.Position - new Vector2(HalfExtent, HalfExtent),
                activation.Position.Position + new Vector2(HalfExtent, HalfExtent));

            if (!args.WorldAABB.Intersects(box))
                continue;

            activation.Shader.SetParameter("ringColor", activation.Color);
            activation.Shader.SetParameter("fillProgress", activation.FillProgress);
            activation.Shader.SetParameter("pulseEnabled", activation.Animated ? 1f : 0f);

            worldHandle.UseShader(activation.Shader);
            worldHandle.DrawRect(box, Color.White);
        }

        worldHandle.UseShader(null);
    }
}
