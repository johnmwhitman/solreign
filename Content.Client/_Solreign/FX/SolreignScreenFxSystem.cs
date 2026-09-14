using Content.Shared._Solreign.FX;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;

namespace Content.Client._Solreign.FX;

/// <summary>
///     Client reaction to <see cref="SolreignScreenFxEvent"/>: holds the acid-green
///     <see cref="SolreignAcidBorderOverlay"/> up for the requested duration, then removes it.
///     Overlapping triggers extend the hold instead of restarting or stacking the overlay (see
///     <see cref="SolreignScreenFxTiming.ExtendRemaining"/>) — a burst of Solreign events reads as one
///     held sting, not a flicker.
///
///     FLASH-FIX-2026-07-16: gated behind the client's <c>accessibility.reduced_motion</c> CVar (same
///     precedent as <see cref="Content.Client._Solreign.MovementBob.MovementBobSystem"/>'s movement-bob
///     gating). A pulsing full-screen-edge flash is exactly the kind of motion/photosensitivity trigger
///     reduced-motion mode exists to suppress, and unlike movement-bob there's no reduced-fidelity
///     fallback that still serves the "mark this moment" intent, so reduced-motion clients simply never
///     see the overlay — events are no-ops for them entirely (see <see cref="OnScreenFx"/>), not just
///     overlay-suppressed, so <c>_remaining</c> can't go stale while reduced motion is on and then
///     bleed into (inflate) the next post-toggle-off trigger via <see cref="SolreignScreenFxTiming.ExtendRemaining"/>
///     (grk r1 caught this: <c>Update</c> only ticks <c>_remaining</c> down while <c>_active</c>, so
///     letting <c>OnScreenFx</c> keep calling <c>ExtendRemaining</c> under reduced motion — with the
///     overlay never active to tick it back down — would accumulate a stale hold).
/// </summary>
public sealed partial class SolreignScreenFxSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayMan = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    private readonly SolreignAcidBorderOverlay _overlay = new();
    private float _remaining;
    private bool _active;
    private bool _reducedMotion;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeAllEvent<SolreignScreenFxEvent>(OnScreenFx);
        Subs.CVar(_cfg, CCVars.ReducedMotion, OnReducedMotionChanged, invokeImmediately: true);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        if (_active)
            _overlayMan.RemoveOverlay(_overlay);

        _active = false;
        _remaining = 0f;
    }

    private void OnReducedMotionChanged(bool value)
    {
        _reducedMotion = value;

        // Mid-hold toggle: cut the overlay immediately rather than waiting for it to expire, same
        // bidirectional handling as MovementBobSystem.RefreshActive.
        if (_reducedMotion && _active)
        {
            _active = false;
            _remaining = 0f;
            _overlayMan.RemoveOverlay(_overlay);
        }
    }

    private void OnScreenFx(SolreignScreenFxEvent ev)
    {
        // Reduced-motion clients no-op entirely here: skip the ExtendRemaining bookkeeping too, not
        // just the overlay. Update() only ticks _remaining down while _active, and the overlay is
        // never active under reduced motion, so if we let ExtendRemaining keep raising _remaining on
        // every event it would accumulate (capped at MaxDuration) with nothing ever counting it back
        // down — then bleed into and inflate the very next trigger after the player switches reduced
        // motion back off.
        if (_reducedMotion)
            return;

        _remaining = SolreignScreenFxTiming.ExtendRemaining(_remaining, ev.Duration);

        if (_active)
            return;

        _active = true;
        _overlayMan.AddOverlay(_overlay);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_active)
            return;

        _remaining -= frameTime;

        if (_remaining > 0f)
            return;

        _active = false;
        _remaining = 0f;
        _overlayMan.RemoveOverlay(_overlay);
    }
}
