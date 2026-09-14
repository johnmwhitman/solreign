using System;
using System.Collections.Generic;
using Content.Server.Chat.Managers;
using Content.Server._Solreign.SeasonLedger;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Ghost.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.PlayerDelight.Mark;

/// <summary>
///     "The Mark" PROVIDENCE beat pack (docs/specs/MARK-SPEC-2026-07-17-DRAFT.md §3.5, MG-W4): the
///     return-visit growth line (C2), the once-ever first-return nudge (C7), and the overflow
///     substitution (C6 — the boundary decision below). Thin ECS glue over the pure W1 helpers
///     (<see cref="MarkAgeRules"/>, <see cref="MarkCopy"/>, <see cref="MarkBeatQueue"/>) — the same
///     split <see cref="Providence.ProvidenceWelcomeSystem"/>/<see cref="Providence.ProvidenceFirstDeathSystem"/>
///     use. A new file, disjoint from <see cref="SolreignMarkGardenSystem"/> (MG-W2) and
///     <c>FirstShiftSystem</c> (MG-W3) — runs in parallel with MG-W3 per the spec's own build
///     decomposition.
///
///     Hook: <see cref="PlayerSpawnCompleteEvent"/> — the <c>ProvidenceWelcomeSystem.LoadWelcome</c>
///     shape verbatim: <c>ev.Silent</c> early-return, a per-round per-account in-flight guard marked
///     BEFORE the await, all main-thread reads before the first await, a post-await
///     <c>Deleted()</c> re-check, async void + try/catch (a storage hiccup costs one cosmetic beat,
///     never a crash).
///
///     <b>C6 boundary decision (the W2 receipt's flagged debt):</b> the spec's §3.3.4 line ("records
///     beyond slot capacity... get the C6 overflow line on their next spawn instead of the return
///     beat") is read here as a PAYLOAD SUBSTITUTION, not a fifth unbounded beat channel. Overflow
///     is a pure comparison — <c>record.SlotIndex >= solreign.mark.slots</c> — computed straight
///     from the claimed row and the capacity CVar; it never queries live projected entities, so this
///     system stays disjoint from <see cref="SolreignMarkGardenSystem"/>'s ECS state and the
///     overflow test can never disagree with the projection's own dense-slot-order seniority law
///     (spec §9 Q3: slot 0 is the earliest planter, exactly what this comparison honors). When a
///     beat is about to fire for an overflowed account, its TEXT becomes C6 instead of C7/C2 — but
///     it still consumes the SAME write-first stamp (<c>nudge_shown</c> or the
///     <c>last_visit_stage</c> conditional advance) the un-overflowed path would have used. This is
///     the only reading that keeps the task brief's "at most 4 lines ever per account" cap intact
///     (1 nudge + 3 stage-advances, spec §6's own count) — an unbounded, un-stamped C6 channel would
///     silently blow that budget for any account that stays overflowed for the rest of its career.
/// </summary>
public sealed partial class MarkBeatsSystem : EntitySystem
{
    /// <summary>Delay behind spawn — AFTER the Welcome beat (immediate/8s) and the first-death
    /// rehire beat (6s), so all three private beats read as a sequence, not a pile (spec §3.5).</summary>
    private const float MarkBeatDelaySeconds = 10f;

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private SeasonLedgerSystem _ledger = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly MarkBeatQueue _queue = new();

    /// <summary>Accounts with a beat load currently in flight this round — gate-before-await so a
    /// rapid respawn can't double-dispatch the async read (the Welcome idiom).</summary>
    private readonly HashSet<Guid> _inFlight = new();

    private bool _markEnabled;
    private bool _returnBeatEnabled;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.SolreignMarkEnabled, v => _markEnabled = v, invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignMarkReturnBeatEnabled, v => _returnBeatEnabled = v, invokeImmediately: true);

        SubscribeLocalEvent<RoundStartingEvent>(_ => ResetRoundState());
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => ResetRoundState());
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }

    private void ResetRoundState()
    {
        _queue.Clear();
        _inFlight.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_queue.Count == 0)
            return;

        foreach (var beat in _queue.DrainDue(_timing.CurTime))
        {
            FireBeat(beat);
        }
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        // Sub-gate only (spec §3.7): garden/planting/examine stay live under the master switch even
        // when the return-beat sub-gate is off. Both are re-checked again at fire time.
        if (!_markEnabled || !_returnBeatEnabled)
            return;

        if (ev.Silent)
            return;

        var guid = ev.Player.UserId.UserId;
        if (!_inFlight.Add(guid))
            return;

        LoadMarkBeats(ev.Mob, guid, _ticker.RoundId);
    }

    private async void LoadMarkBeats(EntityUid mob, Guid guid, int currentRoundId)
    {
        try
        {
            var record = await _ledger.GetMarkAsync(guid);

            if (Deleted(mob))
                return;

            if (record is null)
                return; // no mark — the 99% path, one indexed SELECT and done.

            if (!MarkKinds.TryParse(record.Kind, out var kind))
                return; // corrupt row (closed vocabulary) — never throw inside a spawn hook.

            if (!MarkAgeRules.TryParseLedgerUtc(record.PlantedUtc, out var planted))
                return;

            var lastVisit = planted;
            if (record.LastVisitUtc is { Length: > 0 } &&
                !MarkAgeRules.TryParseLedgerUtc(record.LastVisitUtc, out lastVisit))
                lastVisit = planted;

            var now = DateTime.UtcNow;
            var slots = _cfg.GetCVar(CCVars.SolreignMarkSlots);
            var overflow = record.SlotIndex >= slots;
            var seed = guid.GetHashCode();

            // First-return nudge (C7): once ever, only on a round LATER than the planting round —
            // the same round's planter already got the C1 confirmation, no nudge needed.
            if (record.PlantedRoundId != currentRoundId && !record.NudgeShown)
            {
                if (await _ledger.TryMarkNudgeShownAsync(guid))
                {
                    var text = overflow
                        ? Loc.GetString(MarkCopy.Pick(MarkCopy.OverflowKeys, seed))
                        : Loc.GetString(MarkCopy.Pick(MarkCopy.NudgeKeys, seed),
                            ("kind", Loc.GetString(MarkCopy.KindWordKeyFor(kind))));

                    Schedule(guid, MarkBeatKind.FirstReturnNudge, text);
                }
            }

            if (Deleted(mob))
                return;

            // Growth beat (C2): fires on STAGE ADVANCE only (not raw delta) — the anti-fatigue law,
            // at most 3 times ever per mark (stages 1/2/3).
            var stage = MarkAgeRules.StageAt(planted, now);
            if (stage > record.LastVisitStage)
            {
                if (await _ledger.TryRecordVisitAsync(guid, stage, now.ToString("o")))
                {
                    var text = overflow
                        ? Loc.GetString(MarkCopy.Pick(MarkCopy.OverflowKeys, seed ^ 1))
                        : Loc.GetString(MarkCopy.ReturnKeyFor(kind),
                            ("cm", MarkAgeRules.RenderGrowth(MarkAgeRules.GrowthDelta(planted, lastVisit, now))));

                    Schedule(guid, MarkBeatKind.GrowthReturn, text);
                }
            }
        }
        catch (Exception e)
        {
            Log.Error($"Error while loading Mark beats for {guid}:\n{e}");
        }
        finally
        {
            _inFlight.Remove(guid);
        }
    }

    private void Schedule(Guid accountId, MarkBeatKind kind, string text)
    {
        _queue.Schedule(new MarkPendingBeat(
            kind,
            accountId,
            text,
            _timing.CurTime + TimeSpan.FromSeconds(MarkBeatDelaySeconds)));
    }

    /// <summary>
    ///     Fires (or silently drops) one due beat — full re-resolution guard chain, never trusting
    ///     anything captured at schedule time (the Welcome/FirstDeath idiom verbatim): re-checks the
    ///     CVars (mid-flight-off guarantee), re-resolves the session by account, prefers the
    ///     session's CURRENTLY attached entity, and drops on ghost/dead/deleted rather than landing
    ///     a private address on the wrong body.
    /// </summary>
    private void FireBeat(MarkPendingBeat beat)
    {
        if (!_markEnabled || !_returnBeatEnabled)
            return;

        if (!_players.TryGetSessionById(new NetUserId(beat.AccountId), out var session))
            return;

        if (session.AttachedEntity is not { } target || Deleted(target))
            return;

        if (HasComp<GhostComponent>(target))
            return;

        if (TryComp<MobStateComponent>(target, out var mobState) && mobState.CurrentState == MobState.Dead)
            return;

        _popup.PopupEntity(beat.PrivateText, target, target, PopupType.Medium);
        _chatManager.DispatchServerMessage(session, beat.PrivateText);
    }

    // ---------------------------------------------------------------- test seams (house ForTests idiom)

    /// <summary>Number of not-yet-fired pending beats — integration-test visibility only.</summary>
    internal int PendingBeatCountForTests => _queue.Count;

    /// <summary>Resets round-scoped state without a real round-boundary event (pooled integration
    /// servers already spawn once during setup — the Welcome system's documented rationale).</summary>
    internal void ResetRoundStateForTests() => ResetRoundState();

    /// <summary>
    ///     Runs ONLY the inner mechanics (<see cref="LoadMarkBeats"/>) for one mob/account, with a
    ///     caller-supplied round id — it does NOT include the <see cref="OnPlayerSpawnComplete"/>
    ///     gate chain (the <c>_markEnabled</c>/<c>_returnBeatEnabled</c> CVar checks and the
    ///     <c>Silent</c> check never run). This seam exists so mechanic tests can pin exact round-id
    ///     sequences without waiting on <see cref="Robust.Server.GameTicking.GameTicker"/>'s real
    ///     round boundaries; every caller that wants those beats to actually fire must set both CVars
    ///     itself first (see the mechanic tests in this file).
    ///
    ///     MARK-DORMANCY-FIX root cause (docs/receipts/MARK-DORMANCY-FIX-2026-07-17.md): earlier
    ///     versions of this comment claimed the gates WERE included, and the dormancy tests called
    ///     this seam directly with a gate CVar off — since the gate genuinely lives only in
    ///     <see cref="OnPlayerSpawnComplete"/>, that call path always leaked exactly one queued beat
    ///     regardless of CVar state. A test-setup defect, not a production one: production spawns
    ///     only ever reach <see cref="LoadMarkBeats"/> through the real, correctly-gated
    ///     <see cref="OnPlayerSpawnComplete"/> handler. Dormancy must be proven by raising a real
    ///     <see cref="Content.Shared.GameTicking.PlayerSpawnCompleteEvent"/> through the event bus
    ///     instead — see the <c>Dormant_*</c> tests.
    /// </summary>
    internal void LoadMarkBeatsForTests(EntityUid mob, Guid guid, int currentRoundId) =>
        LoadMarkBeats(mob, guid, currentRoundId);

    /// <summary>Drains and fires every beat due at <paramref name="now"/> — lets integration tests
    /// exercise <see cref="FireBeat"/>'s full guard chain without waiting out the real delay.</summary>
    internal void FireDueBeatsForTests(TimeSpan now)
    {
        foreach (var beat in _queue.DrainDue(now))
        {
            FireBeat(beat);
        }
    }
}
