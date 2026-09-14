using System;
using System.Collections.Generic;
using Content.Server._Solreign.Director;
using Content.Server._Solreign.SeasonLedger;
using Content.Server.Administration.Systems;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Shared.CCVar;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Ghost.Components;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.Providence;

/// <summary>
///     "The Authored First Death" (docs/specs/FIRST-DEATH-SPEC-2026-07-16-DRAFT.md): the first time a
///     given ACCOUNT ever has a player-controlled crew character die on SOLREIGN — once per account,
///     EVER — PROVIDENCE performs an authored scene instead of letting the genre's worst shame moment
///     pass in silence. Cosmetic flavor only: no change to MobState, ghosting, revival, scoring, the
///     Rivalry/Crypt telemetry channels, or the ledger's EarlyDeath tracking — all of those keep
///     firing exactly as today; this scene is layered beside them (additive-only, John ruling).
///
///     The scene (spec §2):
///       * T+0 (death tick, synchronous): gate chain → snapshot {character name via
///         <see cref="Identity.Name"/>, damage-dict copy, attacker-present bool} → async atomic claim
///         (<c>SeasonLedgerStore.TryClaimFirstDeathAsync</c> — the single exactly-once linchpin) →
///         career stats → pure cause classify (<see cref="FirstDeathCauseClassifier"/>) → pure
///         epitaph pick (<see cref="FirstDeathEpitaphPicker"/>, persisted by plate id) → schedule.
///       * T+4s (a breath after the death chaos, not during): station-wide eulogy by name
///         (<see cref="ChatSystem.DispatchGlobalAnnouncement"/>, sender "PROVIDENCE", acid green,
///         <c>playSound: false</c> so the default chime never stacks over the sting) + the staged
///         <see cref="ProvidenceLineCategory.DeathCommiseration"/> voice sting + a private line to
///         the dead player's session ("Your file remains open…" — sets up the rehire payoff).
///       * NEXT SPAWN (same round or three days later — this fork has no player self-respawn, so the
///         next successful spawn IS the return visit): private "Reinstatement Processing" beat,
///         popup + chat line, delayed a few seconds behind the Welcome beat so they read as a
///         sequence. The fee is FICTION by law — the ledger's ALWAYS-CUMULATIVE covenant
///         (<c>SeasonLedgerStore.AwardHrPointsAsync</c>: "never subtracts") means no Standing, HR
///         Points, or any ledger value is ever touched. Fires once ever (<c>rehire_shown</c> column,
///         write-before-dispatch).
///
///     Gating notes (spec §3.2):
///       * <see cref="CCVars.SolreignFirstDeathEnabled"/> (default on) is the universal kill switch,
///         re-checked at fire time (mid-flight-CVar-off guarantee).
///       * Deliberately INDEPENDENT of <see cref="ProvidenceVoiceSystem.Enabled"/>: the eulogy TEXT
///         is the primary payload (like the title-ceremony bulletin), so voice off degrades to
///         text-only rather than silencing the scene — the opposite branch choice from
///         commiseration, for the documented reason (<c>PlayLine</c> already no-ops when disabled).
///       * Crew bodies only (<see cref="HumanoidProfileComponent"/> — carried by every species via
///         BaseSpeciesMob, verified at build): a player's ghost-role mouse dying is not a state
///         funeral.
///       * Broadcast <see cref="MobStateChangedEvent"/> subscription — the directed
///         (ActorComponent, MobStateChangedEvent) pair is owned by <c>SolreignDeathTelemetrySystem</c>
///         and the event bus forbids a second directed subscriber; broadcast is multi-subscriber-safe
///         (commiseration + EarlyDeath already coexist on it).
///       * Commiseration double-beat: on a first death the independent commiseration roll may also
///         fire (private condolence, then public ceremony — two stings ~4s apart, worst case). v1
///         ACCEPTS this per spec §3.6 / open question 3 — it can happen at most once per account,
///         ever, and keeping it makes this lane zero-edit on existing systems. NO suppressor here.
///       * The attacker is NEVER named, described, or counted in any surface — the template set has
///         no attacker variable at all (closed vocabulary, spec §3.3).
/// </summary>
public sealed partial class ProvidenceFirstDeathSystem : EntitySystem
{
    /// <summary>How long after the death the public scene fires — "a breath after the death chaos,
    /// not during" (spec §2). Fixed, not randomized.</summary>
    private const float DeathBeatDelaySeconds = 4f;

    /// <summary>How long after an eligible spawn the rehire beat fires — behind the Welcome beat's
    /// immediate popup so the two private beats read as a sequence, not a pile (spec §3.6).</summary>
    private const float RehireBeatDelaySeconds = 6f;

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ProvidenceVoiceSystem _providence = default!;
    [Dependency] private SeasonLedgerSystem _ledger = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private SolreignCryptSystem _crypt = default!;
    [Dependency] private FirstDeathObituarySystem _obituary = default!;

    /// <summary>Solreign acid green — the same brand color the HR title ceremony announces in.</summary>
    private static readonly Color EulogyColor = Color.FromHex("#39FF14");

    /// <summary>The pure per-round in-flight guard — see its own doc comment for what it is NOT.</summary>
    private readonly FirstDeathRoundGuard _guard = new();

    /// <summary>The pure delayed-beat schedule (death scene + rehire) — see its own doc comment.</summary>
    private readonly FirstDeathBeatQueue _queue = new();

    /// <summary>Cached mirror of <see cref="CCVars.SolreignFirstDeathEnabled"/>.</summary>
    private bool _enabled;

    /// <summary>How many extended crypt reports this system has composed and handed to
    /// <see cref="SolreignCryptSystem.ReportFirstDeath"/> — counted BEFORE the crypt channel's own
    /// fail-closed gating (which is deliberately independent, spec §2 "each independently gated"),
    /// so tests can pin "exactly one composition per claimed first death" with the Director off.
    /// Reset by <see cref="ResetRoundStateForTests"/>.</summary>
    private int _cryptReportsComposed;

    /// <summary>
    ///     Set true for the remainder of the round the first time THIS round's death claim succeeds
    ///     (i.e. this shift is the one where the once-per-account-EVER scene actually fired) —
    ///     exposed read-only for Station Audits' "deaths (+ whether commemorated)" section (v14
    ///     wave-1 #3). Reset on round start/cleanup, same as every other per-round flag in this
    ///     system. Deliberately NOT "has this account ever had a first death" (that is career-scoped,
    ///     answered by <c>SeasonLedgerStore.GetFirstDeathAsync</c>) — the audit cares about THIS
    ///     shift only.
    /// </summary>
    internal bool FirstDeathCommemoratedThisRound { get; private set; }

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.SolreignFirstDeathEnabled, v => _enabled = v, invokeImmediately: true);

        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }

    private void OnRoundStarting(RoundStartingEvent ev)
    {
        _guard.Reset();
        _queue.Clear();
        ResetCryptBackfillState();
        FirstDeathCommemoratedThisRound = false;

        // FD-W3.5: scan for banked memorials (claims whose crypt plaque never minted — v13.3-era
        // rows, or gates-closed claims) and queue their paced backfill. Fail-closed no-op unless
        // the crypt channel is actually ready — see ProvidenceFirstDeathSystem.CryptBackfill.cs.
        StartCryptBackfill();
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _guard.Reset();
        _queue.Clear();
        ResetCryptBackfillState();
        FirstDeathCommemoratedThisRound = false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        PumpCryptBackfill();

        if (_queue.Count == 0)
            return;

        foreach (var beat in _queue.DrainDue(_timing.CurTime))
        {
            FireBeat(beat);
        }
    }

    // --- Death side (spec §3.2 gate chain — all synchronous, then one async hop) -------------------

    private void OnMobStateChanged(MobStateChangedEvent ev)
    {
        if (!_enabled)
            return;

        // Active rounds only, gated at CLAIM time, not just fire time (review F1): a death during
        // PostRound (the post-round brawl) must not consume the once-per-account claim — the beat
        // would be dropped by FireBeat's own InRound guard and the scene lost forever. Not claiming
        // here preserves the player's authored first death for a round where it can actually play.
        if (_ticker.RunLevel != GameRunLevel.InRound)
            return;

        if (ev.NewMobState != MobState.Dead)
            return;

        // Crew bodies only — a ghost-role mouse death is not a state funeral.
        if (!HasComp<HumanoidProfileComponent>(ev.Target))
            return;

        // Resolve the mob back to the controlling account via its mind — the exact resolution the
        // commiseration beat and the EarlyDeath tracker use, so all three agree on identity.
        if (!_mind.TryGetMind(ev.Target, out _, out var mind))
            return;

        if (mind.UserId is not { } user)
            return;

        // Per-round in-flight guard, marked BEFORE the first await (Welcome idiom) — stops a
        // die → revive → die-again sequence from double-dispatching the async claim in one round.
        var guid = user.UserId;
        if (!_guard.CanFire(guid))
            return;

        _guard.MarkFired(guid);

        // Snapshot NOW, synchronously, on the death tick (threading discipline per
        // SeasonLedgerSystem.EarlyDeath.cs): everything the composed scene needs, before any await.
        var name = Identity.Name(ev.Target, EntityManager);

        var damage = new Dictionary<string, FixedPoint2>();
        if (TryComp<DamageableComponent>(ev.Target, out var damageable))
        {
            // GetAllDamage is the sanctioned read surface (the component's Damage member is
            // Access-restricted) — same call BarotraumaSystem itself uses; it hands back a copy.
            foreach (var (type, value) in _damageable.GetAllDamage((ev.Target, damageable)).DamageDict)
            {
                damage[type] = value;
            }
        }

        // Attacker predicate: mirror DeathAttribution.TryResolveAttackerGuid (the crypt channel's
        // resolution) so the two channels never disagree about whether a killer existed — plus a
        // self-kill exclusion: a player who shot themselves is not a "workplace dispute".
        var attackerPresent = DeathAttribution.TryResolveAttackerGuid(EntityManager, ev.Origin, out var attackerGuid)
                              && attackerGuid != guid.ToString();

        // FD-W4 (spec §6.2): connected players AT DEATH TIME — the Discord obituary's player-gate
        // count (the recap-law analogue: never advertise an empty station). Snapshotted here,
        // synchronously with the rest, never re-read after the await.
        var playerCount = _players.PlayerCount;

        ClaimAndSchedule(guid, name, damage, attackerPresent, _ticker.RoundId, playerCount);
    }

    /// <summary>
    ///     The one async hop: atomic claim → career stats → pure compose → schedule the T+4s beat.
    ///     async void + try/catch (house threading contract) — never blocks the tick; a storage
    ///     failure silently costs one cosmetic scene, never a crash. The atomic claim row is the
    ///     exactly-once linchpin: two deaths racing in the same tick both reach the claim; SQLite
    ///     hands it to exactly one.
    /// </summary>
    private async void ClaimAndSchedule(
        Guid user,
        string name,
        IReadOnlyDictionary<string, FixedPoint2> damage,
        bool attackerPresent,
        int roundId,
        int playerCountAtDeath)
    {
        try
        {
            var cause = FirstDeathCauseClassifier.Classify(attackerPresent, damage);

            // Career-cumulative stats (spec §2 timeline): {tours} + the title derived from them.
            var career = await _ledger.GetCareerStatsAsync(user);
            var (title, _) = TitleRules.Compute(career);
            var tours = career.Tours;

            // Pure, deterministic epitaph — persisted by plate id, not free text (spec §3.1).
            var epitaph = FirstDeathEpitaphPicker.Pick(tours, cause, title);

            var claimed = await _ledger.TryClaimFirstDeathAsync(
                user, roundId, name, cause.ToString().ToUpperInvariant(), tours, title, epitaph.Id);

            // Not claimed (row already exists / lost the race) → done; the scene never fires.
            if (!claimed)
                return;

            // v14 wave-1 #3: this shift is the one where the once-per-account-EVER scene actually
            // fired — Station Audits' "whether commemorated" reads this, read-only, for the rest of
            // the round.
            FirstDeathCommemoratedThisRound = true;

            // FD-W3 side artifact (spec §5): the extended crypt report — composed from exactly the
            // values just persisted into the claim row, handed to the crypt channel's own
            // fail-closed gate (crypt CVar + master + token + rate limit; daemon off → silently
            // skipped, the scene unaffected). Fired HERE, at claim time, rather than with the T+4s
            // beat: the claim row is the exactly-once linchpin, and the permanent public memorial
            // must not share the beat's accepted swallow window (round-end race, mid-flight CVar
            // flip) — the plaque mints whenever the claim mints. Deviation from the spec §2
            // timeline's T+4s placement, recorded in the FD-W3 receipt.
            _cryptReportsComposed++;
            try
            {
                // FD-W3.5: stamp crypt_reported on a successful HAND-OFF (and only then) — the
                // sole dedupe against the round-start plaque backfill (the daemon mints per POST,
                // not per victim). A refused hand-off (crypt gates closed, rate-limited) leaves
                // the row unstamped, and the backfill recovers the memorial in a later round —
                // where pre-W3.5 it was silently lost forever. Stamping law + tradeoff:
                // ProvidenceFirstDeathSystem.CryptBackfill.cs and the FD-W3.5 receipt.
                if (_crypt.ReportFirstDeath(FirstDeathCryptReport.Build(user, name, epitaph.Text, cause, tours, title))
                    && !await _ledger.TryMarkFirstDeathCryptReportedAsync(user))
                {
                    Log.Warning($"First-death claim-time crypt stamp for {user} found the row already "
                                + "stamped — a duplicate plaque may have been minted.");
                }
            }
            catch (Exception e)
            {
                // Review fix (grk r1 #5): the crypt leg must never share fate with the authored
                // scene — a compose/serialize/stamp failure here costs the plaque, not the eulogy.
                Log.Error($"First-death crypt report failed (scene unaffected): {e}");
            }

            // FD-W4 side artifact (spec §6): the Discord obituary leg — composed from the same
            // claimed snapshot, handed to its own fully-gated system (webhook CVar SHIPS EMPTY =
            // inert manual-paste mode; player gate; own rate-limit channel). Always appends the
            // ready-to-paste block to data/first_deaths.jsonl (write-before-dispatch); with the
            // shipping defaults it never egresses. Same claim-time placement and fate isolation
            // as the crypt leg: a failure here costs the obituary, never the eulogy.
            try
            {
                _obituary.Record(new FirstDeathObituary(
                    DateTimeOffset.UtcNow, roundId, name, title, tours, cause, playerCountAtDeath));
            }
            catch (Exception e)
            {
                Log.Error($"First-death Discord obituary leg failed (scene unaffected): {e}");
            }

            var eulogy = Loc.GetString(FirstDeathCopy.EulogyKeyFor(cause), ("name", name));
            var privateLine = Loc.GetString(FirstDeathCopy.PrivateLineKey, ("name", name));

            _queue.Schedule(new FirstDeathPendingBeat(
                FirstDeathBeatKind.DeathScene,
                user,
                eulogy,
                privateLine,
                _timing.CurTime + TimeSpan.FromSeconds(DeathBeatDelaySeconds)));
        }
        catch (Exception e)
        {
            Log.Error($"Error while claiming/scheduling first-death scene for {user}:\n{e}");
        }
    }

    // --- Rehire side (spec §4) ----------------------------------------------------------------------

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        if (!_enabled)
            return;

        // Same Silent check the vanilla join greeting and the Welcome beat respect — a silent spawn
        // (e.g. a cryo-storage return) opted out of greetings entirely.
        if (ev.Silent)
            return;

        LoadRehire(ev.Player.UserId.UserId);
    }

    /// <summary>
    ///     Async rehire check: claimed row with <c>rehire_shown == 0</c>? → mark shown FIRST
    ///     (write-before-dispatch — a racing double-spawn loses the conditional UPDATE and delivers
    ///     nothing; worst case a beat is swallowed by a disconnect, never duplicated), then schedule
    ///     the private beat a few seconds out.
    /// </summary>
    private async void LoadRehire(Guid user)
    {
        try
        {
            var record = await _ledger.GetFirstDeathAsync(user);
            if (record is null || record.RehireShown)
                return;

            if (!await _ledger.TryMarkRehireShownAsync(user))
                return;

            // The claim row's cause string round-trips back to the enum for the deterministic pick;
            // an unparseable value (never expected — closed vocabulary) degrades to Unknown flavor.
            if (!Enum.TryParse<FirstDeathCause>(record.Cause, ignoreCase: true, out var cause))
                cause = FirstDeathCause.Unknown;

            var key = FirstDeathCopy.RehireKeyFor(record.ToursAtDeath, record.TitleAtDeath, cause);
            var line = Loc.GetString(key,
                ("fee", Loc.GetString(FirstDeathCopy.FeeKey)),
                ("title", record.TitleAtDeath),
                ("tours", record.ToursAtDeath));

            _queue.Schedule(new FirstDeathPendingBeat(
                FirstDeathBeatKind.Rehire,
                user,
                string.Empty,
                line,
                _timing.CurTime + TimeSpan.FromSeconds(RehireBeatDelaySeconds)));
        }
        catch (Exception e)
        {
            Log.Error($"Error while loading first-death rehire beat for {user}:\n{e}");
        }
    }

    // --- Delivery ------------------------------------------------------------------------------------

    /// <summary>
    ///     Fires (or silently drops) one due beat. Main-thread only (called from <see cref="Update"/>).
    ///     Every guard is re-checked at fire time — never trust anything but the beat's own composed
    ///     text across the delay.
    /// </summary>
    private void FireBeat(FirstDeathPendingBeat beat)
    {
        // Mid-flight-CVar-off guarantee: a flip to off after scheduling still drops the beat.
        if (!_enabled)
            return;

        // Active rounds only (the title-ceremony guard): no eulogies over the lobby or post-round.
        if (_ticker.RunLevel != GameRunLevel.InRound)
            return;

        switch (beat.Kind)
        {
            case FirstDeathBeatKind.DeathScene:
                FireDeathScene(beat);
                break;

            case FirstDeathBeatKind.Rehire:
                FireRehire(beat);
                break;
        }
    }

    private void FireDeathScene(FirstDeathPendingBeat beat)
    {
        // 1. Station-wide eulogy by name. playSound: false — the voice sting below is the audio;
        //    the default announcement chime must not stack over it (spec §3.4).
        _chat.DispatchGlobalAnnouncement(
            beat.PublicText,
            Loc.GetString(FirstDeathCopy.SenderKey),
            playSound: false,
            colorOverride: EulogyColor);

        // 2. Voice sting — the staged DeathCommiseration collection, tonally pre-cleared for death
        //    beats. No-ops by itself when Providence's voice is off: text-only degradation, the
        //    scene's primary payload is the bulletin above.
        _providence.PlayLine(ProvidenceLineCategory.DeathCommiseration);

        // 3. Private line to the dead player's session (they're a ghost watching chat). Re-resolved
        //    by account at fire time — a disconnect between death and T+4s silently drops only this
        //    private half; the public ceremony still honors them.
        if (_players.TryGetSessionById(new NetUserId(beat.AccountId), out var session))
            _chatManager.DispatchServerMessage(session, beat.PrivateText);
    }

    private void FireRehire(FirstDeathPendingBeat beat)
    {
        // No PA, no broadcast — this beat is intimacy (Levine's escalation ladder). Full Welcome-
        // style re-resolution guard chain: never trust anything captured before the delay.
        if (!_players.TryGetSessionById(new NetUserId(beat.AccountId), out var session))
            return;

        if (session.AttachedEntity is not { } target || Deleted(target))
            return;

        // A rehire address has no business landing on a ghost's view or a dead body — if the player
        // died again within the delay, the beat is silently swallowed (never re-queued: rehire_shown
        // is already stamped, and a swallowed beat is always preferable to a duplicated one).
        if (HasComp<GhostComponent>(target))
            return;

        if (TryComp<MobStateComponent>(target, out var mobState) && mobState.CurrentState == MobState.Dead)
            return;

        // Popup + private chat line, so it survives popup-blindness and can be screenshotted from
        // chat history (spec §4.3).
        _popup.PopupEntity(beat.PrivateText, target, target, PopupType.Medium);
        _chatManager.DispatchServerMessage(session, beat.PrivateText);
    }

    // --- Test seams (the ProvidenceWelcomeSystem idiom) ----------------------------------------------

    /// <summary>Number of not-yet-fired pending beats — integration-test visibility only.</summary>
    internal int PendingBeatCountForTests => _queue.Count;

    /// <summary>Pending beats of one kind — integration-test visibility only.</summary>
    internal int PendingBeatCountOfKindForTests(FirstDeathBeatKind kind) => _queue.CountOf(kind);

    /// <summary>Extended crypt-report compositions since the last reset (see the field's doc
    /// comment) — integration-test visibility only.</summary>
    internal int CryptReportsComposedForTests => _cryptReportsComposed;

    /// <summary>
    ///     Resets both round-scoped stores without broadcasting a real round-boundary event — pooled
    ///     integration servers go through a REAL spawn during setup, so every test must start from
    ///     the clean slate a genuinely fresh round would have (the Welcome system's documented
    ///     rationale, verbatim).
    /// </summary>
    internal void ResetRoundStateForTests()
    {
        _guard.Reset();
        _queue.Clear();
        _cryptReportsComposed = 0;
        ResetCryptBackfillState();
    }

    /// <summary>
    ///     Drains and fires every pending beat due at <paramref name="now"/> — lets integration tests
    ///     exercise <see cref="FireBeat"/>'s full guard chain without waiting out the real delays.
    /// </summary>
    internal void FireDueBeatsForTests(TimeSpan now)
    {
        foreach (var beat in _queue.DrainDue(now))
        {
            FireBeat(beat);
        }
    }
}
