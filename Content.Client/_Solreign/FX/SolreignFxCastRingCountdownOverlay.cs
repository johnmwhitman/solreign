using Robust.Shared.Enums;
using System.Collections.Generic;
using System.Numerics;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Client._Solreign.FX;

/// <summary>
///     `cast_ring`'s <c>cosmetic_minimal</c> fallback (spec §4: "ring replaced by a static icon +
///     countdown text above the caster; this is gameplay-critical... never dropped, only
///     re-rendered non-visually-dense" — and spec §4.0's Observer legibility rule: "must also serve
///     players WATCHING the affected entity... render above the affected entity in world, not
///     solely in the actor's private HUD"). Screen-space (world-anchored text requires a
///     world-to-screen projection every frame, same as SS14's own status-icon/speech-bubble
///     idiom) — procedural only, no new icon texture: a small flat-colored square plus the
///     remaining-seconds countdown via the engine's default font, both drawn directly with
///     <see cref="DrawingHandleScreen"/> primitives.
/// </summary>
public sealed partial class SolreignFxCastRingCountdownOverlay : Overlay
{
    private const float IconHalfSizePx = 7f;

    [Dependency] private IResourceCache _resourceCache = default!;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    private readonly record struct Activation(MapCoordinates Position, Color Color, float RemainingSeconds);

    private readonly Dictionary<int, Activation> _active = new();
    private Font? _font;

    public SolreignFxCastRingCountdownOverlay()
    {
        IoCManager.InjectDependencies(this);
    }

    public void SetActivation(int slotIndex, MapCoordinates position, Color color, float remainingSeconds)
    {
        _active[slotIndex] = new Activation(position, color, remainingSeconds);
    }

    public void ClearActivation(int slotIndex)
    {
        _active.Remove(slotIndex);
    }

    /// <summary>Clears every activation — no <see cref="ShaderInstance"/> to dispose here (this overlay draws plain screen-space primitives), but the dictionary itself should not survive a kill-switch disable / shutdown.</summary>
    public void Clear()
    {
        _active.Clear();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_active.Count == 0 || args.ViewportControl is null)
            return;

        _font ??= _resourceCache.GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", 10);

        var screenHandle = args.ScreenHandle;

        foreach (var activation in _active.Values)
        {
            if (activation.Position.MapId != args.MapId)
                continue;

            var screenPos = args.ViewportControl.WorldToScreen(activation.Position.Position);
            var iconBox = new UIBox2(
                screenPos.X - IconHalfSizePx, screenPos.Y - IconHalfSizePx - 18f,
                screenPos.X + IconHalfSizePx, screenPos.Y + IconHalfSizePx - 18f);

            screenHandle.DrawRect(iconBox, activation.Color);

            var seconds = System.MathF.Max(0f, activation.RemainingSeconds);
            var label = $"{seconds:0.0}s";
            screenHandle.DrawString(_font, new Vector2(iconBox.Right + 4f, iconBox.Top), label, Color.White);
        }
    }
}
