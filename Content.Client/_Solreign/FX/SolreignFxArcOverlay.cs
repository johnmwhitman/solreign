using Robust.Shared.Enums;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared._Solreign.FX;
using Robust.Client.Graphics;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Solreign.FX;

/// <summary>
///     `electrical` primitive's world-space arc/crackle overlay (spec §2 row 3: "New world-space
///     overlay class (<c>SolreignArcOverlay : Overlay</c>, <c>OverlaySpace.WorldSpace</c>) + new
///     <c>arc_distortion.swsl</c>"). Same shader-prototype-into-<c>DrawRect</c> idiom as
///     <see cref="SolreignAcidBorderOverlay"/>, but multi-instance (one activation per pooled
///     electrical slot, up to spec §3's concurrent cap of 12) and entity-anchored rather than
///     screen-space-fixed — each active slot gets its OWN unique shader instance (never the shared
///     cached one) so concurrent arcs don't fight over one shader's uniforms.
/// </summary>
public sealed partial class SolreignFxArcOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> Shader = "SolreignFxArcDistortion";

    /// <summary>World-space half-extent of each drawn arc quad, in tiles.</summary>
    private const float HalfExtent = 0.6f;

    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private readonly record struct Activation(MapCoordinates Position, Color Color, float Intensity, ShaderInstance Shader, float SeedPhase);

    private readonly Dictionary<int, Activation> _active = new();

    public SolreignFxArcOverlay()
    {
        IoCManager.InjectDependencies(this);
    }

    /// <summary>Activates or updates slot <paramref name="slotIndex"/> — called by the rendering partial on lease acquisition/merge and every frame an anchor moves.</summary>
    public void SetActivation(int slotIndex, MapCoordinates position, Color color, float intensity, uint seed)
    {
        if (!_active.TryGetValue(slotIndex, out var existing))
        {
            var instance = _prototypeManager.Index(Shader).InstanceUnique();
            existing = new Activation(position, color, intensity, instance, SolreignFxSeedMixing.Index(seed, 1000u) / 1000f * 6.2831853f);
        }

        _active[slotIndex] = existing with { Position = position, Color = color, Intensity = intensity };
    }

    /// <summary>
    ///     Deactivates and DISPOSES slot <paramref name="slotIndex"/>'s <see cref="ShaderInstance"/>
    ///     (grk W3 round-1 review finding H1: a bare dictionary <c>Remove</c> without disposal leaks
    ///     one GPU shader object per activation for the lifetime of the process — under the
    ///     electrical/cast_ring concurrent caps' natural churn over a long round, this accumulates
    ///     unboundedly). Never throws if the slot was never activated.
    /// </summary>
    public void ClearActivation(int slotIndex)
    {
        if (_active.Remove(slotIndex, out var activation))
            activation.Shader.Dispose();
    }

    /// <summary>Disposes every currently-active shader instance — called on system shutdown (grk H1: "also dispose remaining instances when the overlay is removed").</summary>
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
        var t = (float) _timing.CurTime.TotalSeconds;

        foreach (var activation in _active.Values)
        {
            if (activation.Position.MapId != args.MapId)
                continue;

            var box = new Box2(
                activation.Position.Position - new Vector2(HalfExtent, HalfExtent),
                activation.Position.Position + new Vector2(HalfExtent, HalfExtent));

            if (!args.WorldAABB.Intersects(box))
                continue;

            activation.Shader.SetParameter("arcColor", activation.Color);
            activation.Shader.SetParameter("arcIntensity", activation.Intensity);
            activation.Shader.SetParameter("seedPhase", activation.SeedPhase + t);

            worldHandle.UseShader(activation.Shader);
            worldHandle.DrawRect(box, Color.White);
        }

        worldHandle.UseShader(null);
    }
}
