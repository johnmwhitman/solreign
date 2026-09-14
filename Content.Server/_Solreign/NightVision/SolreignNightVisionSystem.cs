using Content.Shared._Solreign.NightVision;
using Content.Shared.Overlays;
using Content.Shared.PowerCell.Components;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Solreign.NightVision;

/// <summary>
///     Server half of the after-hours compliance lenses: a low-frequency sync loop that keeps the
///     power-cell draw in lockstep with the night-vision toggle, plays the toggle click, and
///     force-disables lenses that have run out of charge.
///
///     Upstream's <c>ToggleNightVisionEvent</c> handler (SharedNightVisionSystem) flips
///     <c>NightVisionComponent.Enabled</c> without raising any follow-up event, so instead of
///     racing it on the broadcast action event this system reconciles the authoritative state a
///     couple of times a second — the same accumulator shape as
///     <c>SolreignPeriodicEffectSystem</c> (Content.Server/_Solreign/Effects). The same reconcile
///     pass doubles as the toggle-click detector (Phase2 A4 game-feel sweep): manual toggles used
///     to flip the fullscreen overlay in total silence, no click, no cue.
/// </summary>
public sealed partial class SolreignNightVisionSystem : SharedSolreignNightVisionSystem
{
    [Dependency] private SharedAudioSystem _audio = default!;

    private static readonly TimeSpan SyncInterval = TimeSpan.FromSeconds(0.5);

    private TimeSpan _nextSync;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (Timing.CurTime < _nextSync)
            return;

        _nextSync = Timing.CurTime + SyncInterval;

        var query = EntityQueryEnumerator<SolreignPoweredNightVisionComponent, NightVisionComponent, PowerCellDrawComponent>();
        while (query.MoveNext(out var uid, out var powered, out var nightVision, out var draw))
        {
            // Lenses on => cell drains; lenses off => cell rests. SetDrawEnabled no-ops when
            // already in the right state, so this is cheap.
            PowerCell.SetDrawEnabled((uid, draw), nightVision.Enabled);

            // Toggle edge since the last sync: this is the only place that can tell "the wearer
            // just flipped it" from "it's been sitting in this state" (see class doc), so it is
            // also where the click has to live.
            if (powered.WasEnabled != nightVision.Enabled)
            {
                _audio.PlayPvs(nightVision.Enabled ? powered.ToggleOnSound : powered.ToggleOffSound, uid);
                powered.WasEnabled = nightVision.Enabled;
            }

            if (!nightVision.Enabled)
                continue;

            // Out of juice (or toggled on with a dead/absent cell): force the auto-off.
            // PowerCellSlotEmptyEvent already covers the common case instantly; this is the
            // authoritative backstop, so a dead-cell activation only ever blinks for at most
            // one sync interval.
            if (!PowerCell.HasDrawCharge(uid))
                ShutDown((uid, nightVision), popup: true);
        }
    }
}
