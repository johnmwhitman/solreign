using System;
using System.Collections.Generic;
using Content.Server._Solreign.SeasonLedger;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Robust.Shared.Configuration;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.Providence;

/// <summary>
///     PROVIDENCE's event-reactive voice (feat/providence-event-reactive —
///     docs/PROVIDENCE-VOICE-DESIGN.md, written after a real player called the station voice
///     "repetitive... out of place" and the design doc diagnosed why: PROVIDENCE had a REGISTER, not a
///     CHARACTER — every existing line (<see cref="ProvidenceVoiceSystem"/>'s idle-musings pool)
///     fires on a TIMER and comments on nothing that happened. This system is the fix the design doc
///     asked for: lines that fire because something specific just happened, sourced where possible
///     from the Season Ledger's permanent player history — the tagline is "the Ledger is watching. It
///     never forgets"; this is the first surface where a line actually demonstrates that.
///
///     Deliberately NO PROVIDENCE VOICE-PACK CATEGORY
///     (<see cref="ChatSystem.DispatchGlobalAnnouncement"/>, same idiom as
///     <c>StationDirectiveRuleSystem</c>/<c>ProvidenceFirstDeathSystem</c>'s eulogy): authored PA text
///     requests the generic announcement cue, but never calls
///     <see cref="ProvidenceVoiceSystem.PlayLine"/> for an existing voice-pack category. Every
///     existing category is audio recorded for a DIFFERENT beat (idle chatter, the first-ever-death
///     eulogy, the coin-flip commiseration); repurposing one of those recordings for this system's
///     text would either misrepresent what that recording was made for or double up with the system
///     that already owns it on the same death (<c>ProvidenceCommiserationSystem</c>'s independent
///     coin-flip also fires on <see cref="MobStateChangedEvent"/>). No new audio is recorded in this
///     lane — that is out of scope for a dispatch-and-rails wave.
///
///     Two event sources, both real, both already raised elsewhere in the codebase:
///       * <see cref="MobStateChangedEvent"/> (broadcast — multi-subscriber-safe; three other systems
///         already coexist on it: <c>ProvidenceFirstDeathSystem</c>, <c>ProvidenceCommiserationSystem</c>,
///         <c>SeasonLedgerSystem.EarlyDeath.cs</c>) for death commentary, classified into
///         <see cref="ProvidenceReactiveDeathLineClass"/> by <see cref="ProvidenceReactiveCopy.ClassifyDeath"/>:
///         Generic (no history), Repeat (3rd+ death this shift), or Memory (an eligible persisted
///         first-death record from a different round identity exists for this account — read-only against
///         <see cref="SeasonLedgerSystem.GetFirstDeathAsync"/>, requirement 2 of the design doc).
///       * <see cref="GameRunLevelChangedEvent"/> (already subscribed elsewhere, e.g.
///         <see cref="ProvidenceVoiceSystem"/> itself, for shift-start/shift-end audio) for a one-shot
///         round-end death-toll summary.
///
///     Anti-spam: a SINGLE shared <see cref="ProvidenceReactiveDispatchGate"/> cooldown across every
///     line this system can produce (not a per-category cooldown, which a burst of mixed event types
///     would trivially route around) — see that class's doc comment for why priority scales rather
///     than bypasses the cooldown. A candidate that loses to the cooldown is DROPPED, never queued to
///     replay later: silence is the point (design doc rule 5), and a delayed replay of a stale event
///     would read as even more of a non-sequitur than the current timer-driven lines do.
///
///     Rails (non-negotiable per the lane brief):
///       * Every player-authored string (character name) is passed through
///         <see cref="ProvidenceNameSanitizer.Sanitize"/> before it can reach a line. A name that fails
///         to sanitize to anything usable silently drops the whole line (fail-closed to silence, never
///         a broken/blank announcement).
///       * The Ledger-memory line degrades to silence (falls through to Generic/Repeat) when there is
///         no genuinely-prior history — see <see cref="ProvidenceReactiveDeathMemory"/>.
///       * Every async continuation re-checks <see cref="_enabled"/> and the round state at fire time
///         (the mid-flight-CVar-off guarantee every other Providence system already follows) and is
///         wrapped in try/catch — a Ledger read failure costs one line, never a crash.
///       * Scripted only: no LLM call anywhere in this system (out of scope per the lane brief; the
///         Director/Oracle path the design doc describes as "mostly already built" is a follow-up
///         wave's decision, not this one's).
///
///     Gated by <see cref="CCVars.SolreignProvidenceReactiveEnabled"/>, defaulting TRUE under the
///     reviewed activation contract. The CVar remains the immediate per-feature rollback.
/// </summary>
public sealed partial class ProvidenceEventReactiveSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private SeasonLedgerSystem _ledger = default!;

    /// <summary>Solreign acid green — same brand color the first-death eulogy and title-ceremony
    /// bulletins already announce in, so this reads as the same voice, not a new one.</summary>
    private static readonly Color ReactiveColor = Color.FromHex("#39FF14");

    /// <summary>The shared anti-spam gate — see its own doc comment.</summary>
    private readonly ProvidenceReactiveDispatchGate _gate = new();

    /// <summary>Per-account death count THIS round, main-thread only — same Dictionary-then-Clear-on-
    /// round-boundary idiom as <c>SeasonLedgerSystem.EarlyDeath.cs</c>'s <c>_earliestDeath</c> map.
    /// Tracked here rather than reused from elsewhere: nothing else in the codebase counts ALL deaths
    /// per account per round (EarlyDeath only remembers the earliest one).</summary>
    private readonly Dictionary<Guid, int> _deathCountThisRound = new();

    /// <summary>Total deaths this round, station-wide — feeds the round-end summary.</summary>
    private int _totalDeathsThisRound;

    /// <summary>Cached mirror of <see cref="CCVars.SolreignProvidenceReactiveEnabled"/>.</summary>
    private bool _enabled;

    /// <summary>Cached mirror of <see cref="CCVars.SolreignProvidenceReactiveCooldownSeconds"/>.</summary>
    private TimeSpan _cooldown;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.SolreignProvidenceReactiveEnabled, v => _enabled = v, invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignProvidenceReactiveCooldownSeconds,
            v => _cooldown = TimeSpan.FromSeconds(v), invokeImmediately: true);

        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
    }

    private void OnRoundStarting(RoundStartingEvent ev)
    {
        ResetRoundState();
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        ResetRoundState();
    }

    private void ResetRoundState()
    {
        _gate.Reset();
        _deathCountThisRound.Clear();
        _totalDeathsThisRound = 0;
    }

    // --- Death commentary ----------------------------------------------------------------------------

    private void OnMobStateChanged(MobStateChangedEvent ev)
    {
        if (!_enabled)
            return;

        if (_ticker.RunLevel != GameRunLevel.InRound)
            return;

        if (ev.NewMobState != MobState.Dead)
            return;

        // Crew bodies only — same predicate ProvidenceFirstDeathSystem uses; a ghost-role mouse dying
        // is not "aboutness", it's noise.
        if (!HasComp<HumanoidProfileComponent>(ev.Target))
            return;

        if (!_mind.TryGetMind(ev.Target, out _, out var mind))
            return;

        if (mind.UserId is not { } user)
            return;

        var guid = user.UserId;
        _deathCountThisRound.TryGetValue(guid, out var priorCount);
        var deathCountThisRound = priorCount + 1;
        _deathCountThisRound[guid] = deathCountThisRound;
        _totalDeathsThisRound++;

        // Rail: sanitize the player-authored name BEFORE anything else touches it. A name that fails
        // to sanitize to anything usable drops this event's line entirely — fail closed to silence.
        var rawName = Identity.Name(ev.Target, EntityManager);
        var name = ProvidenceNameSanitizer.Sanitize(rawName);
        if (name is null)
            return;

        var roundId = _ticker.RoundId;
        HandleDeathAsync(guid, name, deathCountThisRound, roundId);
    }

    /// <summary>
    ///     The one async hop: read-only Ledger lookup for the memory line, then classify + gate + fire.
    ///     async void + try/catch (house threading contract, same as every other Providence async
    ///     continuation) — a Ledger failure costs one line, never a crash.
    /// </summary>
    private async void HandleDeathAsync(Guid guid, string name, int deathCountThisRound, int roundId)
    {
        try
        {
            FirstDeathRecord? record;
            try
            {
                record = await _ledger.GetFirstDeathAsync(guid);
            }
            catch (Exception e)
            {
                // A Ledger read failure must never fabricate a memory line and must never block the
                // (cheaper, no-I/O) generic/repeat line either — degrade to "no memory" and continue.
                Log.Error($"Providence reactive: first-death lookup failed for {guid} (degrading to no memory):\n{e}");
                record = null;
            }

            var hasMemory = ProvidenceReactiveDeathMemory.IsEligible(record, roundId);
            var lineClass = ProvidenceReactiveCopy.ClassifyDeath(hasMemory, deathCountThisRound);
            var priority = ProvidenceReactiveCopy.PriorityFor(lineClass);

            // Mid-flight-CVar-off guarantee + round-state re-check: a flip to off, or the round ending,
            // between the death tick and this continuation resuming must still drop the line silently.
            if (!_enabled)
                return;

            if (_ticker.RunLevel != GameRunLevel.InRound)
                return;

            var now = _timing.CurTime;
            if (!_gate.CanFire(priority, now, _cooldown))
                return;

            string text;
            switch (lineClass)
            {
                case ProvidenceReactiveDeathLineClass.Memory when record is not null:
                {
                    var cause = Enum.TryParse<FirstDeathCause>(record.Cause, ignoreCase: true, out var parsed)
                        ? parsed
                        : FirstDeathCause.Unknown;
                    var causeLabel = FirstDeathCopy.CauseLabelFor(cause);
                    var key = ProvidenceReactiveCopy.DeathMemoryKeys[_random.Next(ProvidenceReactiveCopy.DeathMemoryKeys.Length)];
                    text = Loc.GetString(key, ("name", name), ("cause", causeLabel), ("title", record.TitleAtDeath));
                    break;
                }

                case ProvidenceReactiveDeathLineClass.Repeat:
                    text = Loc.GetString(ProvidenceReactiveCopy.DeathRepeatKey, ("name", name), ("count", deathCountThisRound));
                    break;

                default:
                {
                    var key = ProvidenceReactiveCopy.DeathGenericKeys[_random.Next(ProvidenceReactiveCopy.DeathGenericKeys.Length)];
                    text = Loc.GetString(key, ("name", name));
                    break;
                }
            }

            _gate.MarkFired(now);
            _lastDeathLineClass = lineClass;
            Dispatch(text);
        }
        catch (Exception e)
        {
            Log.Error($"Error while composing Providence reactive death line for {guid}:\n{e}");
        }
        finally
        {
            _deathContinuationCompletionCount++;
        }
    }

    // --- Round-end summary ----------------------------------------------------------------------------

    private void OnRunLevelChanged(GameRunLevelChangedEvent ev)
    {
        if (!_enabled)
            return;

        if (ev.Old != GameRunLevel.InRound || ev.New != GameRunLevel.PostRound)
            return;

        const ProvidenceReactivePriority priority = ProvidenceReactivePriority.High;
        var now = _timing.CurTime;
        if (!_gate.CanFire(priority, now, _cooldown))
            return;

        var key = ProvidenceReactiveCopy.RoundEndKeyFor(_totalDeathsThisRound);
        var text = _totalDeathsThisRound <= 0
            ? Loc.GetString(key)
            : Loc.GetString(key, ("count", _totalDeathsThisRound));

        _gate.MarkFired(now);
        Dispatch(text);
    }

    // --- Delivery --------------------------------------------------------------------------------------

    private void Dispatch(string text)
    {
        _reactiveDispatchCount++;
        _lastDispatchText = text;

        _chat.DispatchGlobalAnnouncement(
            text,
            Loc.GetString(ProvidenceReactiveCopy.SenderKey),
            playSound: true,
            colorOverride: ReactiveColor);
    }

    // --- Test seams (the ProvidenceCommiserationSystem/ProvidenceFirstDeathSystem idiom) --------------

    private int _reactiveDispatchCount;
    private int _deathContinuationCompletionCount;
    private string? _lastDispatchText;
    private ProvidenceReactiveDeathLineClass? _lastDeathLineClass;

    /// <summary>How many event-reactive lines this system selected and composed for a dispatch attempt
    /// — integration-test visibility only (the CVar-off / cooldown / burst no-op proofs assert this
    /// stays at 0 or 1). This does not prove client receipt.</summary>
    internal int ReactiveDispatchCountForTests => _reactiveDispatchCount;

    /// <summary>How many asynchronous death continuations reached a terminal path — integration-test
    /// visibility only, so negative assertions wait for the actual continuation instead of elapsed ticks.</summary>
    internal int DeathContinuationCompletionCountForTests => _deathContinuationCompletionCount;

    /// <summary>The text most recently composed for a dispatch attempt, or null if none yet —
    /// integration-test visibility only.</summary>
    internal string? LastDispatchTextForTests => _lastDispatchText;

    /// <summary>The class of the most recent death line selected for a dispatch attempt, or null when
    /// no death line has reached that boundary — integration-test visibility only.</summary>
    internal ProvidenceReactiveDeathLineClass? LastDeathLineClassForTests => _lastDeathLineClass;

    /// <summary>Per-account death count this round, for a given account — integration-test visibility only.</summary>
    internal int DeathCountThisRoundForTests(Guid guid) => _deathCountThisRound.TryGetValue(guid, out var count) ? count : 0;

    /// <summary>
    ///     Resets round-scoped state AND the dispatch counters without broadcasting a real round-
    ///     boundary event — same rationale as every other Providence system's test seam: a
    ///     pool-connected session already goes through a real spawn/round before the test body runs.
    /// </summary>
    internal void ResetRoundStateForTests()
    {
        ResetRoundState();
        _reactiveDispatchCount = 0;
        _deathContinuationCompletionCount = 0;
        _lastDispatchText = null;
        _lastDeathLineClass = null;
    }
}
