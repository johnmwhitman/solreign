using Content.Client.Overlays;
using Content.Shared._Solreign.NightVision;
using Content.Shared.NightVision;
using Content.Shared.Overlays;
using Robust.Client.Graphics;
using Robust.Client.Player;

namespace Content.Client._Solreign.NightVision;

/// <summary>
///     Client half of the after-hours compliance lenses. Two jobs, both cosmetic-side:
///
///     1. Stale-overlay cleanup — upstream <c>Content.Client.NightVision.NightVisionSystem</c>
///        only refreshes on its <c>AfterAutoHandleStateEvent</c> when the changed entity IS the
///        local player, so a server-forced shutdown of a WORN item (our dead-cell auto-off)
///        would otherwise leave the green overlay stuck on. This system periodically re-derives
///        the active night-vision source the same way upstream does and removes the overlay when
///        none remains.
///
///     2. Low-cell static — as the slotted cell empties past the component's threshold, the
///        shader noise ramps up (via <see cref="NvgPowerMath.FlickerStrength"/>), telegraphing
///        the imminent auto-off. Battery charge is networked and inferred linearly client-side
///        (see <c>SharedBatterySystem.GetCharge</c>), so no extra netcode is needed.
/// </summary>
public sealed partial class SolreignNightVisionSystem : SharedSolreignNightVisionSystem
{
    [Dependency] private IOverlayManager _overlayMan = default!;
    [Dependency] private IPlayerManager _player = default!;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.25);

    private TimeSpan _nextCheck;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (Timing.CurTime < _nextCheck)
            return;

        _nextCheck = Timing.CurTime + CheckInterval;

        if (_player.LocalEntity is not { } viewer)
            return;

        var active = FindActiveSource(viewer);
        if (active == null)
        {
            // No active night-vision source at all: clear any overlay a remote state change
            // left behind. Idempotent when the overlay is already gone.
            _overlayMan.RemoveOverlay<NightVisionOverlay>();
            return;
        }

        // Only style OUR lenses; species/antag night vision stays untouched.
        if (!TryComp<SolreignPoweredNightVisionComponent>(active.Value, out var powered))
            return;

        if (!_overlayMan.TryGetOverlay<NightVisionOverlay>(out var overlay))
            return;

        var fraction = 1f;
        if (PowerCell.TryGetBatteryFromSlot(active.Value.Owner, out var battery))
            fraction = Battery.GetChargeLevel(battery.Value.AsNullable());

        var nightVision = active.Value.Comp;
        var strength = NvgPowerMath.FlickerStrength(fraction, powered.LowChargeFraction);
        var noiseAmount = MathHelper.Lerp(nightVision.NoiseAmount, powered.LowChargeNoiseAmount, strength);
        var noiseMultiplier = MathHelper.Lerp(nightVision.NoiseMultiplier, powered.LowChargeNoiseMultiplier, strength);

        overlay.SetParameters(nightVision.OverlayColor, nightVision.LightingColor, noiseAmount, noiseMultiplier);
    }

    /// <summary>
    ///     Re-derives the night-vision source the upstream client system would pick for
    ///     <paramref name="viewer"/>: raise <see cref="RefreshNightVisionEvent"/> (directed +
    ///     inventory-relayed), then apply the same enabled/relay filtering and
    ///     prioritized-or-least-noisy selection.
    /// </summary>
    private Entity<NightVisionComponent>? FindActiveSource(EntityUid viewer)
    {
        var ev = new RefreshNightVisionEvent();
        RaiseLocalEvent(viewer, ref ev);

        Entity<NightVisionComponent>? best = null;
        var bestNoise = float.MaxValue;

        foreach (var ent in ev.Entities)
        {
            if (!ent.Comp.Enabled)
                continue;

            // Mirror upstream: intrinsic sources must not relay, worn sources must.
            if (ent.Comp.RelayOverlay == (ent.Owner == viewer))
                continue;

            if (ent.Comp.Prioritized)
                return ent;

            var noise = ent.Comp.NoiseAmount * ent.Comp.NoiseMultiplier;
            if (noise < bestNoise)
            {
                best = ent;
                bestNoise = noise;
            }
        }

        return best;
    }
}
