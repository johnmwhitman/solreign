using System;
using Content.Server._Solreign.SeasonLedger;
using Content.Server.GameTicking.Events;
using Content.Shared._Solreign.FX;
using Content.Shared.CCVar;
using Content.Shared.Ghost.Components;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.Providence;

/// <summary>
///     Providence's "first-shift welcome" player-delight beat: reinforces the station's "this station
///     remembers you" identity by privately greeting each player, once per round, on their first
///     eligible spawn. Cosmetic flavor only — never affects gameplay, jobs, or scoring.
///
///     Personalization reads the Season Ledger's CAREER-cumulative stats (<c>SeasonLedgerSystem.
///     GetCareerStatsAsync</c>, the same store call site <c>SeasonLedgerSystem.LoadTitle</c> uses for
///     rank) — no new DB column, no new accumulator:
///       * <c>career.Tours == 0</c> means this account has never had a round recorded at round end
///         (see <c>SeasonLedgerStore.AddRoundRecordAsync</c>, which increments <c>tours</c> for every
///         connected player every round) — a reliable, already-populated "brand new asset" signal.
///         These get a first-shift induction line, plus Providence's staged
///         <see cref="ProvidenceLineCategory.NewPlayerWelcome"/> voice sting (the only Providence
///         voice-pack category actually named/recorded for this beat — see
///         <c>providence_sounds.yml</c>'s <c>new_player_welcome_*.ogg</c> files, wired here for the
///         first time).
///       * Otherwise, they get a "welcome back" line referencing their career shift count and the
///         title <see cref="TitleRules.Compute"/> derives from those same career totals — no separate
///         season-scoped query needed, and no voice sting (no dedicated "welcome back" audio category
///         exists in the voice pack, unlike the true first-shift case).
///
///     Anti-fatigue lives entirely in the pure <see cref="ProvidenceWelcomeGate"/>, unit-tested in
///     isolation (Content.Tests/_Solreign/ProvidenceWelcomeGateTests.cs) — same thin-ECS-glue split as
///     <c>ProvidenceCommiserationGate</c>/<c>ProvidenceCommiserationSystem</c>: a hard cap of one
///     welcome per player per round (a player may respawn — death, cryo return, ghost-role swap — more
///     than once), reset on every round start/restart.
///
///     Gated by <see cref="CCVars.SolreignProvidenceWelcomeEnabled"/> (default on). Respects
///     <see cref="PlayerSpawnCompleteEvent.Silent"/> — the same flag that suppresses the vanilla
///     "job-greet-station-name" join greeting (<c>GameTicker.Spawning.cs</c>) — so a silent spawn
///     (e.g. a cryo-storage return) never doubles up on greetings.
///
///     ADDITIVE LAYER (wow-wiring wave, docs/receipts/wow-wiring/WOW-WIRING-2026-07-16.md): on the same
///     <c>career.Tours == 0</c> branch, also schedules ONE delayed, personally-addressed follow-up beat
///     (see <see cref="ScheduleFirstShiftPersonal"/>/<see cref="Update"/>/<see cref="FireFirstShiftPersonal"/>)
///     a few seconds later — a private chat line addressing the player by character name, the SAME
///     <see cref="ProvidenceLineCategory.NewPlayerWelcome"/> VO but played TARGETED to just that session
///     (<see cref="ProvidenceVoiceSystem.PlayLineTo"/>, not the immediate beat's station-wide
///     <c>PlayLine</c>), and one single-player screen-fx pulse
///     (<see cref="SolreignScreenFxEvent"/> raised via the session-targeted <c>RaiseNetworkEvent</c>
///     overload — safe: the client's overlay is per-client-local state, and its
///     <c>SubscribeAllEvent&lt;SolreignScreenFxEvent&gt;</c> reacts to whatever the server sends it
///     regardless of whether delivery was broadcast or targeted). Gated independently by
///     <see cref="CCVars.SolreignProvidenceFirstShiftWelcome"/> (default on) — the delayed layer never
///     schedules if the base welcome CVar above is off (no hook point to reach), but can be
///     independently killed without touching the existing immediate beat.
///
///     Scheduling never stores an <c>ICommonSession</c> or trusts the spawn-time mob at fire time — see
///     <see cref="ProvidenceFirstShiftPersonalQueue"/>'s own doc comment for why, and
///     <see cref="FireFirstShiftPersonal"/> for the full re-resolution/ghost/dead/deleted guard chain.
/// </summary>
public sealed partial class ProvidenceWelcomeSystem : EntitySystem
{
    /// <summary>How long after the immediate welcome the personal follow-up fires — "a few seconds,
    /// after the spawn rush", per this wave's spec. Not randomized; a fixed, predictable beat.</summary>
    private const float FirstShiftPersonalDelaySeconds = 8f;

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ProvidenceVoiceSystem _providence = default!;
    [Dependency] private SeasonLedgerSystem _ledger = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;

    /// <summary>Loc keys for the 4 "welcome back" lines (providence-welcome.ftl), parameterized by tours/title.</summary>
    private static readonly string[] WelcomeBackKeys =
    {
        "solreign-providence-welcome-back-1",
        "solreign-providence-welcome-back-2",
        "solreign-providence-welcome-back-3",
        "solreign-providence-welcome-back-4",
    };

    /// <summary>Loc keys for the 4 first-shift induction lines (providence-welcome.ftl), no parameters.</summary>
    private static readonly string[] FirstShiftKeys =
    {
        "solreign-providence-welcome-first-1",
        "solreign-providence-welcome-first-2",
        "solreign-providence-welcome-first-3",
        "solreign-providence-welcome-first-4",
    };

    /// <summary>Loc keys for the delayed personal-address follow-up (providence-welcome.ftl),
    /// parameterized by <c>$name</c> — the character's <c>MetaData.EntityName</c>.</summary>
    private static readonly string[] PersonalKeys =
    {
        "solreign-providence-first-personal-1",
        "solreign-providence-first-personal-2",
        "solreign-providence-first-personal-3",
        "solreign-providence-first-personal-4",
    };

    /// <summary>Fallback for <see cref="PersonalKeys"/> in the (extremely unlikely) case the resolved
    /// entity has no usable name — no parameters, so it can never format-fail.</summary>
    private static readonly string[] PersonalKeysNameless =
    {
        "solreign-providence-first-personal-nameless",
    };

    /// <summary>The pure anti-fatigue gate — see its own doc comment for the full rule.</summary>
    private readonly ProvidenceWelcomeGate _gate = new();

    /// <summary>The pure delayed-personal-beat schedule — see its own doc comment.</summary>
    private readonly ProvidenceFirstShiftPersonalQueue _personalQueue = new();

    /// <summary>Cached mirror of <see cref="CCVars.SolreignProvidenceWelcomeEnabled"/>.</summary>
    private bool _enabled;

    /// <summary>Cached mirror of <see cref="CCVars.SolreignProvidenceFirstShiftWelcome"/>.</summary>
    private bool _personalEnabled;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.SolreignProvidenceWelcomeEnabled, v => _enabled = v, invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignProvidenceFirstShiftWelcome, v => _personalEnabled = v, invokeImmediately: true);

        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }

    private void OnRoundStarting(RoundStartingEvent ev)
    {
        _gate.Reset();
        _personalQueue.Clear();
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _gate.Reset();
        _personalQueue.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_personalQueue.Count == 0)
            return;

        foreach (var pending in _personalQueue.DrainDue(_timing.CurTime))
        {
            FireFirstShiftPersonal(pending);
        }
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        if (!_enabled)
            return;

        // Mirrors the vanilla "job-greet-station-name" greeting's own Silent check (GameTicker.
        // Spawning.cs) — a silent spawn (e.g. a cryo-storage return) opted out of join greetings
        // entirely, so Providence's welcome shouldn't fire either.
        if (ev.Silent)
            return;

        var guid = ev.Player.UserId.UserId;

        // Hard cap first (cheap, no store round-trip spent) — if this player already got their one
        // welcome this round, stop before even querying the ledger.
        if (!_gate.CanFire(guid))
            return;

        // Recorded BEFORE the async ledger read resolves — same "mark first" idiom as
        // SeasonLedgerSystem.LoadTitle's ceremony gate, so a racing double-spawn can never double-greet.
        _gate.MarkFired(guid);

        LoadWelcome(ev.Mob, guid);
    }

    private async void LoadWelcome(EntityUid mob, Guid guid)
    {
        try
        {
            var career = await _ledger.GetCareerStatsAsync(guid);

            if (Deleted(mob))
                return;

            if (career.Tours == 0)
            {
                var line = FirstShiftKeys[_random.Next(FirstShiftKeys.Length)];
                _popup.PopupEntity(Loc.GetString(line), mob, mob, PopupType.Medium);
                _providence.PlayLine(ProvidenceLineCategory.NewPlayerWelcome);

                if (_personalEnabled)
                    ScheduleFirstShiftPersonal(mob, guid);
            }
            else
            {
                var (title, _) = TitleRules.Compute(career);
                var line = WelcomeBackKeys[_random.Next(WelcomeBackKeys.Length)];
                _popup.PopupEntity(Loc.GetString(line, ("tours", career.Tours), ("title", title)), mob, mob, PopupType.Medium);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Error while loading Providence welcome for {guid}:\n{e}");
        }
    }

    /// <summary>
    ///     Enqueues the delayed personal follow-up beat — <see cref="FirstShiftPersonalDelaySeconds"/>
    ///     from now, keyed by the account's stable <see cref="Guid"/> (never the session, which may not
    ///     survive that long) and the current spawn mob (a best-effort fallback only — see
    ///     <see cref="FireFirstShiftPersonal"/>, which always prefers the LIVE session's currently
    ///     attached entity over this snapshot).
    /// </summary>
    private void ScheduleFirstShiftPersonal(EntityUid mob, Guid guid)
    {
        _personalQueue.Schedule(guid, mob, _timing.CurTime + TimeSpan.FromSeconds(FirstShiftPersonalDelaySeconds));
    }

    /// <summary>
    ///     Fires (or silently drops) one delayed personal beat. Every step is a silent no-op on failure
    ///     — a missed personal beat is always preferable to a thrown exception or a message landing on
    ///     the wrong entity:
    ///       1. Re-resolve the session by account <see cref="Guid"/> — never trust a session captured at
    ///          schedule time (disconnect/reconnect between scheduling and firing is expected and must
    ///          not throw or double-fire; reconnecting yields a NEW session object for the SAME guid).
    ///       2. Prefer the session's CURRENTLY attached entity over the spawn-time mob snapshot — the
    ///          player may have died/respawned/swapped bodies in the intervening few seconds. If the
    ///          session has NO attached entity at all (lobby, disconnected-but-not-yet-reaped, admin
    ///          ghost with no body), drop entirely rather than falling back to the stale spawn mob — a
    ///          personal address has no business landing on a body nobody is currently playing.
    ///       3. Ghost/observer check (<see cref="GhostComponent"/>) — a personal "onboarding logged"
    ///          address has no business landing on a ghost's view or competing with the unrelated death-
    ///          commiseration beat.
    ///       4. Dead-body check (<see cref="MobStateComponent"/>) — same reasoning, belt-and-suspenders
    ///          for the case where the mind hasn't transferred to a ghost entity yet.
    ///     <see cref="_personalEnabled"/> IS re-checked here (re-read live from the cached mirror, not
    ///     just at schedule time) — this is the mid-flight-CVar-off guarantee: a CVar flip to off after
    ///     scheduling but before this fires must still silently drop the beat.
    /// </summary>
    private void FireFirstShiftPersonal(ProvidenceFirstShiftPersonalPending pending)
    {
        if (!_personalEnabled)
            return;

        if (!_players.TryGetSessionById(new NetUserId(pending.AccountId), out var session))
            return;

        if (session.AttachedEntity is not { } target || Deleted(target))
            return;

        if (HasComp<GhostComponent>(target))
            return;

        if (TryComp<MobStateComponent>(target, out var mobState) && mobState.CurrentState == MobState.Dead)
            return;

        var name = MetaData(target).EntityName;
        var line = string.IsNullOrWhiteSpace(name)
            ? Loc.GetString(PersonalKeysNameless[_random.Next(PersonalKeysNameless.Length)])
            : Loc.GetString(PersonalKeys[_random.Next(PersonalKeys.Length)], ("name", name));

        _popup.PopupEntity(line, target, target, PopupType.Medium);
        _providence.PlayLineTo(ProvidenceLineCategory.NewPlayerWelcome, session);
        RaiseNetworkEvent(new SolreignScreenFxEvent(), session);
    }

    /// <summary>Number of not-yet-fired delayed personal beats — integration-test visibility only.</summary>
    internal int PersonalQueueCountForTests => _personalQueue.Count;

    /// <summary>
    ///     Resets both round-scoped stores (same two calls <see cref="OnRoundStarting"/>/
    ///     <see cref="OnRoundRestartCleanup"/> make) without broadcasting a real round-boundary event to
    ///     the rest of the entity system graph. Integration tests need this because a pool-connected
    ///     session (<c>PoolSettings.Connected = true, DummyTicker = false</c>) already goes through a
    ///     REAL spawn during pool setup, before the test body runs — which, with this feature's CVars at
    ///     their default (on), already schedules a real pending personal beat the test never asked for.
    ///     Call this first, before touching any CVar or firing a synthetic spawn, so every test starts
    ///     from the same clean slate a genuinely fresh round would have.
    /// </summary>
    internal void ResetRoundStateForTests()
    {
        _gate.Reset();
        _personalQueue.Clear();
    }

    /// <summary>
    ///     Drains and fires every delayed personal beat due at <paramref name="now"/> — lets integration
    ///     tests exercise <see cref="FireFirstShiftPersonal"/>'s full guard chain (session re-resolution,
    ///     ghost/dead/deleted checks) without waiting <see cref="FirstShiftPersonalDelaySeconds"/> of
    ///     real server ticks.
    /// </summary>
    internal void FireDuePersonalForTests(TimeSpan now)
    {
        foreach (var pending in _personalQueue.DrainDue(now))
        {
            FireFirstShiftPersonal(pending);
        }
    }
}
