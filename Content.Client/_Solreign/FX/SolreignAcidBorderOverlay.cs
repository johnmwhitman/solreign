using Content.Shared._Solreign.FX;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._Solreign.FX;

/// <summary>
///     The Solreign signature shader moment: a pulsing acid-green screen-border overlay.
///
///     Structurally this is the same shader-prototype-into-<c>DrawRect</c> idiom as upstream's
///     <c>Content.Client.CombatMode.ColoredScreenBorderOverlay</c> (which is currently unwired anywhere
///     in the repo) — we deliberately don't reuse or depend on that CombatMode-owned class, we just copy
///     its pattern with our own shader prototype (<c>SolreignAcidBorder</c>,
///     Resources/Prototypes/_Solreign/FX/shaders.yml) so Solreign FX never collides with a CombatMode
///     rework. Lifecycle (add/remove, duration) is owned by <see cref="SolreignScreenFxSystem"/>.
/// </summary>
public sealed partial class SolreignAcidBorderOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> Shader = "SolreignAcidBorder";

    /// <summary>
    ///     Design-intent border thickness in screen pixels — must match
    ///     Resources/Prototypes/_Solreign/FX/shaders.yml's <c>borderSize</c> param. Kept here (rather
    ///     than read back off the shader instance, which has no parameter getter) as the single input
    ///     to the runtime clamp in <see cref="Draw"/> below.
    /// </summary>
    private const float RequestedBorderSizePx = 30f;

    [Dependency] private IPrototypeManager _prototypeManager = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    private readonly ShaderInstance _shader;

    public SolreignAcidBorderOverlay()
    {
        IoCManager.InjectDependencies(this);

        // Unique (mutable) instance, not the shared cached one from Instance(): the shared-mechanism
        // backstop below re-clamps and re-sets `borderSize` against the live viewport every draw, and
        // ShaderPrototype.Instance() returns an instance that's already been MakeImmutable()'d.
        _shader = _prototypeManager.Index(Shader).InstanceUnique();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        // Shared-mechanism backstop (FLASH-FIX-2026-07-16): whatever the shader prototype's YAML asks
        // for, this overlay can never be told to paint more than
        // SolreignAcidBorderMath.MaxScreenCoverageFraction of the screen. Every current and future
        // caller of SolreignScreenFxEvent (earnings call, Solar Flare, Acid Storm, the merged-not-yet-
        // deployed Providence welcome pulse, Season Ledger ceremonies, Station Directives) shares this
        // one overlay instance, so fixing it here protects all of them at once.
        var clampedBorderPx = SolreignAcidBorderMath.ClampBorderSizePx(
            RequestedBorderSizePx,
            args.ViewportBounds.Width,
            args.ViewportBounds.Height);
        _shader.SetParameter("borderSize", clampedBorderPx);

        var worldHandle = args.WorldHandle;
        worldHandle.UseShader(_shader);
        var viewport = args.WorldAABB;
        worldHandle.DrawRect(viewport, Color.White);
        worldHandle.UseShader(null);
    }
}
