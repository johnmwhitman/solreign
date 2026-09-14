using Robust.Shared.Enums;
using Content.Shared._Solreign.FX;
using Robust.Client.Graphics;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._Solreign.FX;

/// <summary>
///     `transformation`'s actor-only screen sting (spec §4's profile table, <c>full</c> profile
///     only: "targeted screen sting (actor only)"). Same screen-space border-pulse mechanism as
///     <see cref="SolreignAcidBorderOverlay"/>/<see cref="SolreignScreenFxSystem"/> (spec's own
///     precedent for "hold a screen accent for a bounded duration, actor-scoped"), but its OWN
///     shader instance/color (<c>SolreignFxTransformationSting</c>, spec §5.2's own contract that
///     a bystander's redacted <c>transformation_generic</c> cue must never carry this — enforced
///     upstream by <see cref="Content.Shared._Solreign.FX.SolreignFxRenderRecipe"/> only ever
///     setting <c>ScreenStingEnabled</c> for the real, actor-received <c>transformation</c> id) so
///     it never shares mutable shader state with the unrelated Corporate Ladder acid-green events
///     that already use <see cref="SolreignAcidBorderOverlay"/>.
/// </summary>
public sealed partial class SolreignFxTransformationStingOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> Shader = "SolreignFxTransformationSting";

    private const float RequestedBorderSizePx = 24f;

    [Dependency] private IPrototypeManager _prototypeManager = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    private readonly ShaderInstance _shader;

    private float _remaining;

    public SolreignFxTransformationStingOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _prototypeManager.Index(Shader).InstanceUnique();
    }

    /// <summary>Whether the sting is currently being held (the CueSystem rendering partial adds/removes this overlay from the <c>IOverlayManager</c> based on this, same add/remove-not-always-present idiom as <see cref="SolreignScreenFxSystem"/>).</summary>
    public bool IsActive => _remaining > 0f;

    /// <summary>Starts (or extends) the hold — overlapping transformation stings extend rather than restart/stack, same "merge, don't flicker" idiom <see cref="SolreignScreenFxSystem"/> already uses via <c>SolreignScreenFxTiming.ExtendRemaining</c>.</summary>
    public void Hold(float duration)
    {
        _remaining = SolreignScreenFxTiming.ExtendRemaining(_remaining, duration);
    }

    /// <summary>Ticks the hold down. Returns true once it reaches zero this call (the caller's cue to remove the overlay from the manager).</summary>
    public bool Tick(float frameTime)
    {
        if (_remaining <= 0f)
            return false;

        _remaining -= frameTime;
        if (_remaining > 0f)
            return false;

        _remaining = 0f;
        return true;
    }

    /// <summary>Immediately cuts the hold short (kill-switch disable / system shutdown) — does NOT dispose the underlying shader instance, since a kill-switch disable may be followed by a later re-enable of the SAME overlay object; see <see cref="DisposeShader"/> for true teardown.</summary>
    public void Clear()
    {
        _remaining = 0f;
    }

    /// <summary>Disposes the single long-lived shader instance this overlay holds for its whole lifetime — call only from true system shutdown (grk W3 round-1 review finding #12), never from a kill-switch disable that might re-enable later.</summary>
    public void DisposeShader()
    {
        _shader.Dispose();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        // Same shared-mechanism backstop as SolreignAcidBorderOverlay (FLASH-FIX-2026-07-16): never
        // let this paint more of the screen than SolreignAcidBorderMath's own coverage ceiling
        // allows, for whatever this shader is asked to draw.
        var clampedBorderPx = SolreignAcidBorderMath.ClampBorderSizePx(
            RequestedBorderSizePx,
            args.ViewportBounds.Width,
            args.ViewportBounds.Height);
        _shader.SetParameter("borderSize", clampedBorderPx);

        var worldHandle = args.WorldHandle;
        worldHandle.UseShader(_shader);
        worldHandle.DrawRect(args.WorldAABB, Color.White);
        worldHandle.UseShader(null);
    }
}
