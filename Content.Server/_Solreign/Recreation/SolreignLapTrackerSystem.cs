using Content.Server.Chat.Systems;
using Content.Shared.Buckle.Components;
using Content.Shared.CCVar;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Content.Shared.Trigger;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.Recreation;

/// <summary>
/// Validates checkpoint order and tracks laps for go-kart courses — gap #2 in
/// docs/specs/2026-07-11-recreation-spec.md. Checkpoints use the same generic
/// <c>TriggerOnCollide</c> primitive as the golf hole and floor traps (zero new physics/collision
/// code); the genuine gap this system fills is the stateful "did they hit checkpoints in order, how
/// many laps" logic upstream has nothing for. Ordinal validation itself is pure and lives in
/// <see cref="SolreignLapTrackerMath"/> so it's NUnit-testable without a running server.
/// </summary>
public sealed partial class SolreignLapTrackerSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;

    private bool _repeatableHeatsEnabled;

    public override void Initialize()
    {
        base.Initialize();

        // TriggerEvent is raised at the checkpoint (ent.Owner) with args.User = the entity that
        // collided with it — for a course checkpoint that's the kart itself (checkpoints are placed
        // on the kart's own drive line, not something a pedestrian would normally cross).
        SubscribeLocalEvent<SolreignLapCheckpointComponent, TriggerEvent>(OnCheckpointTrigger);
        SubscribeLocalEvent<SolreignLapTrackerComponent, StrappedEvent>(OnDriverStrapped);
        SubscribeLocalEvent<SolreignLapTrackerComponent, UnstrappedEvent>(OnDriverUnstrapped);

        Subs.CVar(
            _cfg,
            CCVars.SolreignKartRepeatableHeatsEnabled,
            OnRepeatableHeatsChanged,
            invokeImmediately: true);
    }

    private void OnRepeatableHeatsChanged(bool enabled)
    {
        _repeatableHeatsEnabled = enabled;
        if (enabled)
            return;

        var query = EntityQueryEnumerator<SolreignLapTrackerComponent>();
        while (query.MoveNext(out _, out var tracker))
        {
            // A timed heat may have completed its final circuit and be waiting for the physical
            // ordinal-0 line. Legacy mode would already be finished at that point. Normalize to
            // that invariant when the kill switch turns off so the kart never owes another lap.
            if (!tracker.Finished
                && tracker.HeatStartedAt != null
                && tracker.TotalLaps > 0
                && tracker.LapsCompleted >= tracker.TotalLaps)
            {
                tracker.Finished = true;
            }

            ClearOptionalHeatState(tracker);
        }
    }

    private static void ClearOptionalHeatState(SolreignLapTrackerComponent tracker)
    {
        tracker.HeatStartedAt = null;
        tracker.HeatDriver = null;
        tracker.FinishedElapsed = null;
        tracker.FinishingDriver = null;
    }

    private static void ResetHeat(SolreignLapTrackerComponent tracker)
    {
        tracker.NextCheckpointOrdinal = 0;
        tracker.LapsCompleted = 0;
        tracker.Finished = false;
        ClearOptionalHeatState(tracker);
    }

    private void OnDriverStrapped(Entity<SolreignLapTrackerComponent> tracker, ref StrappedEvent args)
    {
        var sameAsFinishingDriver = tracker.Comp.FinishingDriver == args.Buckle.Owner;
        if (!SolreignKartHeatRules.ShouldResetForDriver(
                _repeatableHeatsEnabled,
                tracker.Comp.Finished,
                sameAsFinishingDriver))
        {
            return;
        }

        ResetHeat(tracker.Comp);
    }

    private void OnDriverUnstrapped(
        Entity<SolreignLapTrackerComponent> tracker,
        ref UnstrappedEvent args)
    {
        if (!_repeatableHeatsEnabled ||
            tracker.Comp.Finished ||
            tracker.Comp.HeatDriver != args.Buckle.Owner)
        {
            return;
        }

        ResetHeat(tracker.Comp);
    }

    private void OnCheckpointTrigger(Entity<SolreignLapCheckpointComponent> checkpoint, ref TriggerEvent args)
    {
        if (args.User is not { } kartUid)
            return;

        if (!TryComp<SolreignLapTrackerComponent>(kartUid, out var tracker))
            return;

        if (tracker.Finished)
            return;

        if (tracker.CourseId != checkpoint.Comp.CourseId)
            return; // this kart isn't racing the course this checkpoint belongs to

        // Repeatable timing is an accountable driver result, never a kart-only result. Reject and
        // normalize any physical checkpoint crossing without a current driver before course
        // progress is computed, otherwise an unmanned kart can inherit the legacy untimed finish
        // path and hand a nearly-complete heat to somebody who buckles later.
        if (_repeatableHeatsEnabled &&
            (!TryComp<SolreignDriverSeatComponent>(kartUid, out var activeSeat) ||
             activeSeat.Driver is null))
        {
            ResetHeat(tracker);
            return;
        }

        var before = new SolreignLapTrackerMath.LapState(tracker.NextCheckpointOrdinal, tracker.LapsCompleted);
        var after = SolreignLapTrackerMath.HitCheckpoint(before, checkpoint.Comp.Ordinal, tracker.CheckpointCount);

        if (after == before)
            return; // rejected: wrong checkpoint — out of order or reverse driving

        if (SolreignKartHeatRules.ShouldStartTiming(
                _repeatableHeatsEnabled,
                tracker.TotalLaps,
                tracker.HeatStartedAt,
                before,
                after,
                checkpoint.Comp.Ordinal))
        {
            if (TryComp<SolreignDriverSeatComponent>(kartUid, out var seat) &&
                seat.Driver is { } driver)
            {
                tracker.HeatStartedAt = _timing.CurTime;
                tracker.HeatDriver = driver;
            }
        }

        tracker.NextCheckpointOrdinal = after.NextCheckpointOrdinal;

        // Timing semantics are armed once, at a valid ordinal-0 start. Merely enabling the CVar
        // during a legacy heat cannot move that heat's finish line or publish a partial time.
        var heatUsesTimedFinish = _repeatableHeatsEnabled
                                  && tracker.HeatStartedAt != null
                                  && tracker.TotalLaps > 0;

        if (SolreignKartHeatRules.IsTimedFinishLineCrossing(
                heatUsesTimedFinish,
                before,
                after,
                checkpoint.Comp.Ordinal,
                tracker.TotalLaps))
        {
            if (!TryComp<SolreignDriverSeatComponent>(kartUid, out var seat) ||
                tracker.HeatDriver is not { } heatDriver ||
                seat.Driver != heatDriver)
            {
                ResetHeat(tracker);
                return;
            }

            FinishHeat(kartUid, tracker);
            return;
        }

        if (after.LapsCompleted <= tracker.LapsCompleted)
            return;

        tracker.LapsCompleted = after.LapsCompleted;

        _popup.PopupEntity(
            Loc.GetString("solreign-kart-lap-complete", ("laps", tracker.LapsCompleted), ("total", tracker.TotalLaps)),
            kartUid,
            PopupType.Medium);
        _audio.PlayPvs(tracker.LapCompleteSound, kartUid);

        if (tracker.LapsCompleted < tracker.TotalLaps)
            return;

        // Timed heats finish where they started: on the next ordinal-0 crossing after the final
        // circuit. Default-off retains the legacy last-checkpoint finish boundary exactly.
        if (heatUsesTimedFinish)
            return;

        FinishHeat(kartUid, tracker);
    }

    private void FinishHeat(EntityUid kartUid, SolreignLapTrackerComponent tracker)
    {
        tracker.Finished = true;

        // SOLREIGN LEDGER INTEGRATION POINT (comment only — SeasonLedgerSystem is owned by the
        // Ledger team; do NOT wire from this file without their sign-off):
        //   A best-lap-time / race-win credit could land here, keyed the same way as round-end
        //   results (mirrors SolreignHotPotatoSystem.Detonate's identical hookup note). Best-time
        //   persistence is a stretch goal per the spec doc, not built in this pass.

        var finishingDriver = TryComp<SolreignDriverSeatComponent>(kartUid, out var seat)
                              && seat.Driver is { } driver
            ? driver
            : (EntityUid?) null;
        var driverName = finishingDriver is { } activeDriver
            ? Identity.Name(activeDriver, EntityManager)
            : Identity.Name(kartUid, EntityManager);

        if (_repeatableHeatsEnabled && tracker.HeatStartedAt != null)
        {
            tracker.FinishedElapsed =
                SolreignKartHeatRules.FreezeElapsed(tracker.HeatStartedAt, _timing.CurTime);
            tracker.FinishingDriver = finishingDriver;
        }
        else
        {
            ClearOptionalHeatState(tracker);
        }

        var message = tracker.FinishedElapsed is { } elapsed
            ? Loc.GetString(
                "solreign-kart-race-finished-timed",
                ("driver", driverName),
                ("laps", tracker.TotalLaps),
                ("seconds", Math.Round(elapsed.TotalSeconds, 2)))
            : Loc.GetString(
                "solreign-kart-race-finished",
                ("driver", driverName),
                ("laps", tracker.TotalLaps));

        _chat.DispatchGlobalAnnouncement(
            message,
            Loc.GetString("solreign-recreation-division-sender"),
            playSound: true,
            colorOverride: Color.FromHex("#9dfd39"));
    }
}
