using System;
using Content.Server._Solreign.PlayerDelight.FirstShift;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking.Events;
using Content.Shared._Solreign.PlayerDelight.Vista;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Ghost.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.PlayerDelight.Vista;

/// <summary>
///     The first-shift vista beat (council memo docs/council/2026-07-16-design-magnetism.md, item 5:
///     "route the first-shift path past one composed vista with a PROVIDENCE line"): when a player
///     with an ACTIVE First Shift assignment (see <see cref="FirstShiftSystem.HasActiveAssignment"/> —
///     the same round-local state the First Shift beacon UI drives) first walks into the proximity of
///     their station's one <see cref="SolreignVistaMarkerComponent"/>, PROVIDENCE privately delivers
///     that marker's map-specific line: a popup at the player plus the same line as a private chat
///     message (chat persists after the popup fades — the FirstShiftSystem.OnComplete /
///     LowPopLobbyReminderSystem delivery idiom). Cosmetic flavor only — never affects gameplay.
///
///     Once per player per round, tracked by the pure <see cref="VistaBeatGate"/> (unit-tested in
///     Content.Tests/_Solreign/VistaBeatGateTests.cs), reset on every round start/restart — the same
///     thin-ECS-glue split as ProvidenceWelcomeGate/ProvidenceWelcomeSystem.
///
///     Proximity is a throttled server-side distance scan (<see cref="ScanIntervalSeconds"/>), not a
///     physics trigger: markers are anchored, fixtureless MarkerBase children, there is exactly one
///     per map (asserted by SolreignVistaMarkerMapPlacementIntegrationTest), and the eligible-player
///     set (active first-shift assignments) is tiny, so a 1 Hz radius check is both cheap and immune
///     to tunneling/step-trigger edge cases.
///
///     Gated by <see cref="CCVars.SolreignFirstShiftVistaBeat"/> (default on; see that CVar's doc for
///     why it is not folded into solreign.social_cheap_adds).
/// </summary>
public sealed partial class SolreignVistaBeatSystem : EntitySystem
{
    /// <summary>How often the proximity scan runs. 1s is far finer than the time it takes to walk
    /// through a 3-tile delivery radius.</summary>
    private const float ScanIntervalSeconds = 1f;

    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private FirstShiftSystem _firstShift = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>The pure once-per-player-per-round gate — see its own doc comment.</summary>
    private readonly VistaBeatGate _gate = new();

    /// <summary>Cached mirror of <see cref="CCVars.SolreignFirstShiftVistaBeat"/>.</summary>
    private bool _enabled;

    private TimeSpan _nextScan = TimeSpan.Zero;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.SolreignFirstShiftVistaBeat, v => _enabled = v, invokeImmediately: true);

        SubscribeLocalEvent<RoundStartingEvent>(_ => ResetRound());
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => ResetRound());
    }

    private void ResetRound()
    {
        _gate.Reset();
        _nextScan = TimeSpan.Zero;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_enabled)
            return;

        var now = _timing.CurTime;
        if (now < _nextScan)
            return;

        _nextScan = now + TimeSpan.FromSeconds(ScanIntervalSeconds);
        Scan();
    }

    /// <summary>
    ///     One proximity pass. Markers outer (there is at most a handful in the world — one per loaded
    ///     station map), eligible sessions inner. Every guard is a silent skip — a missed beat is
    ///     always preferable to a line landing on a ghost, a corpse, or the wrong body:
    ///       1. Once-per-round gate (cheapest check, and the hard cap).
    ///       2. Active First Shift assignment — the beat only exists ON the first-shift path.
    ///       3. A live attached entity on the same map as the marker.
    ///       4. Not a ghost/observer, not dead (same guard chain as
    ///          ProvidenceWelcomeSystem.FireFirstShiftPersonal).
    ///       5. Inside the marker's radius, and the marker actually has a line configured.
    /// </summary>
    private void Scan()
    {
        var query = EntityQueryEnumerator<SolreignVistaMarkerComponent, TransformComponent>();
        while (query.MoveNext(out var markerUid, out var marker, out var markerXform))
        {
            if (marker.LineId.Length == 0 || markerXform.MapID == MapId.Nullspace)
                continue;

            var markerPos = _transform.GetWorldPosition(markerUid);
            var rangeSquared = marker.Range * marker.Range;

            foreach (var session in _players.Sessions)
            {
                if (session.Status != SessionStatus.InGame)
                    continue;

                if (!_gate.CanFire(session.UserId.UserId))
                    continue;

                if (!_firstShift.HasActiveAssignment(session.UserId))
                    continue;

                if (session.AttachedEntity is not { } target || Deleted(target))
                    continue;

                var targetXform = Transform(target);
                if (targetXform.MapID != markerXform.MapID)
                    continue;

                if (HasComp<GhostComponent>(target))
                    continue;

                if (TryComp<MobStateComponent>(target, out var mobState) && mobState.CurrentState == MobState.Dead)
                    continue;

                if ((_transform.GetWorldPosition(target) - markerPos).LengthSquared() > rangeSquared)
                    continue;

                // Mark BEFORE delivering — the same mark-first idiom as ProvidenceWelcomeSystem's
                // gate, so nothing that throws mid-delivery can ever double-fire next scan.
                _gate.MarkDelivered(session.UserId.UserId);

                var line = Loc.GetString(marker.LineId);
                _popup.PopupEntity(line, target, target, PopupType.Medium);
                _chatManager.DispatchServerMessage(session, line);
            }
        }
    }

    /// <summary>Runs one proximity pass immediately, bypassing only the scan-interval throttle. The
    /// enabled mirror is deliberately NOT bypassed — integration tests flip the CVar to exercise the
    /// disabled case.</summary>
    internal void ScanForTests()
    {
        if (_enabled)
            Scan();
    }

    /// <summary>Resets the round-scoped gate without a real round-boundary event — same reasoning as
    /// ProvidenceWelcomeSystem.ResetRoundStateForTests.</summary>
    internal void ResetRoundStateForTests() => ResetRound();

    /// <summary>Whether <paramref name="player"/> could still receive a vista line this round —
    /// integration-test visibility only.</summary>
    internal bool CanFireForTests(Guid player) => _gate.CanFire(player);
}
