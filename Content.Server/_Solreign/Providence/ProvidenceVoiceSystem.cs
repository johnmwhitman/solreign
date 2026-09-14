using Content.Server._Solreign.Effects;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.Providence;

/// <summary>
///     "Providence" — Solreign's station voice. One small central system: <see cref="PlayLine"/> takes a
///     <see cref="ProvidenceLineCategory"/>, resolves it to a <c>SoundCollectionPrototype</c> (see
///     <see cref="ProvidenceVoiceMap"/> and Resources/Prototypes/_Solreign/providence_sounds.yml) and
///     plays a random line from it station-wide via <c>PlayGlobal</c> — same idiom as
///     <c>GameTicker.AnnounceRound</c>'s <c>RoundAnnouncementPrototype</c> sound and upstream's
///     <c>announcements.yml</c> <c>RoundEnd</c>/<c>PowerOn</c> collections. Deliberately <b>not</b>
///     <c>PlayPvs</c> — Providence is a station-wide PA voice, not a positional ambience source.
///
///     Gated by <see cref="CCVars.SolreignProvidenceEnabled"/> (default on): <see cref="PlayLine"/>
///     no-ops while disabled, and <see cref="Enabled"/> is exposed so callers that need a non-voice
///     fallback (see <c>SeasonLedgerSystem.Ceremony.cs</c>'s stinger) can branch on it.
///
///     Current consumers: shift_start/shift_end (round start/end, via
///     <see cref="GameRunLevelChangedEvent"/>), title_ceremony (SeasonLedgerSystem.Ceremony.cs, voice
///     replaces the stinger with the stinger kept as the disabled-cvar fallback), event_audit (The
///     Auditor Prime's arrival, SolreignAuditorPrimeRule.Started), idle_musings (this system's own
///     Update loop, 20-40 minute random timer reusing <see cref="PeriodicEffectTiming"/>'s pure math —
///     same idiom as <c>SolreignPeriodicEffectSystem</c> but global instead of PVS-scoped, since idle
///     musings need to be heard everywhere, not just near a station entity), hot_potato (arm-time,
///     one-shot, via a virtual hook on <c>SharedSolreignHotPotatoSystem</c>), event_acid_storm,
///     death_commiseration, and new_player_welcome. The exact call sites are distributed across
///     Providence and station-identity systems; <c>providence_sounds.yml</c> is the inventory ledger.
///
///     Documented gaps (no clean hook exists this wave; documenting rather than inventing new
///     mechanics out of scope for an audio-wiring lane):
///       * zoo_breach — there is no zoo faction-flip mechanic anywhere in the codebase yet. The zoo
///         exhibit fauna (Resources/Prototypes/_Solreign/Entities/zoo_fauna.yml) is explicitly
///         documented as passive/PG with no NpcFactionMember. Wiring this line needs a faction-flip
///         game rule to be designed and built first (candidate for the pet/nature-pack lane); the
///         collection is staged in providence_sounds.yml so that future rule can call
///         <c>PlayLine(ProvidenceLineCategory.ZooBreach)</c> for free.
///       * cake_denial — no cake entity/examine handler exists in the codebase to hook.
///       * event_raid — no production caller currently selects the staged collection.
/// </summary>
public sealed partial class ProvidenceVoiceSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPlayerManager _players = default!;

    /// <summary>Lower edge of the idle-musings random window: 20 minutes.</summary>
    internal const float IdleMinSeconds = 20f * 60f;

    /// <summary>Upper edge of the idle-musings random window: 40 minutes.</summary>
    internal const float IdleMaxSeconds = 40f * 60f;

    /// <summary>
    ///     Lower edge of the low-pop idle window: 30 minutes.
    ///     REVERSED 2026-07-22 from ALIVENESS P1 #7's 8 minutes — see below.
    /// </summary>
    internal const float LowPopIdleMinSeconds = 30f * 60f;

    /// <summary>
    ///     Upper edge of the low-pop idle window: 60 minutes.
    ///     REVERSED 2026-07-22 from ALIVENESS P1 #7's 15 minutes.
    ///
    ///     ALIVENESS P1 #7 made PROVIDENCE lean IN as the station emptied (8-15 min instead of
    ///     20-40) so a quiet server still felt alive. A real player on a near-empty server
    ///     reported the opposite outcome: the voice read as "irritating… repetitive… out of
    ///     place". The arithmetic explains it — IdleMusings draws from roughly four lines, so
    ///     firing every 8-15 minutes over a shift guaranteed the same line several times.
    ///     Speeding up a shallow pool does not read as aliveness, it reads as a stuck loop.
    ///
    ///     The clamp now runs the other way: the emptier the station, the more PROVIDENCE
    ///     holds its tongue. Silence is what makes the next line land, and a near-silent
    ///     surveillance AI is more in character than a chatty one.
    /// </summary>
    internal const float LowPopIdleMaxSeconds = 60f * 60f;

    /// <summary>Cached mirror of <see cref="CCVars.SolreignProvidenceEnabled"/>, refreshed on change.</summary>
    private bool _enabled;

    /// <summary>Cached mirror of <see cref="CCVars.SolreignProvidenceLowPopCadenceThreshold"/>.</summary>
    private int _lowPopThreshold;

    /// <summary>Whether an idle-musings fire time is currently scheduled (only true during a round).</summary>
    private bool _idleScheduled;

    /// <summary>Game time the next idle musing fires, when <see cref="_idleScheduled"/> is set.</summary>
    private TimeSpan _nextIdleFireTime;

    /// <summary>Whether the station voice is currently enabled — exposed for callers that need a
    /// non-voice fallback when it isn't (see <c>SeasonLedgerSystem.Ceremony.cs</c>).</summary>
    public bool Enabled => _enabled;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.SolreignProvidenceEnabled, v => _enabled = v, invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignProvidenceLowPopCadenceThreshold, v => _lowPopThreshold = v, invokeImmediately: true);

        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
    }

    private void OnRunLevelChanged(GameRunLevelChangedEvent ev)
    {
        if (ev.Old == GameRunLevel.PreRoundLobby && ev.New == GameRunLevel.InRound)
        {
            PlayLine(ProvidenceLineCategory.ShiftStart);
            ScheduleIdle();
        }
        else if (ev.Old == GameRunLevel.InRound && ev.New == GameRunLevel.PostRound)
        {
            PlayLine(ProvidenceLineCategory.ShiftEnd);
            _idleScheduled = false;
        }
    }

    private void ScheduleIdle()
    {
        // At or below the low-pop threshold the window widens to 30-60 minutes — the station
        // voice HOLDS BACK as the station empties; above it, the default 20-40 minute cadence
        // is untouched. Re-evaluated per scheduling (round start and after every musing), so
        // rising population restores the default within one cycle. Same PlayerCount-vs-threshold
        // idiom as LowPopLobbyReminderSystem.
        //
        // 2026-07-22: this direction is REVERSED from ALIVENESS P1 #7, which clamped to 8-15
        // minutes to make the voice lean in on a quiet server. A real player reported that as
        // irritating and repetitive — with ~4 lines in the IdleMusings pool, a faster cadence
        // just replays the same line. See LowPopIdleMaxSeconds for the full rationale.
        var (minSeconds, maxSeconds) = IdleWindow(_players.PlayerCount, _lowPopThreshold);
        _nextIdleFireTime = PeriodicEffectTiming.NextFireTime(_timing.CurTime, minSeconds, maxSeconds, _random.NextDouble());
        _idleScheduled = true;
    }

    /// <summary>
    ///     Pure window-selection seam for <see cref="ScheduleIdle"/> — unit-testable without
    ///     IoC/timing (<c>ProvidenceIdleCadenceTests</c>), the <see cref="PeriodicEffectTiming"/>
    ///     idiom of keeping the decision math IoC-free.
    /// </summary>
    internal static (float MinSeconds, float MaxSeconds) IdleWindow(int playerCount, int lowPopThreshold) =>
        playerCount <= lowPopThreshold
            ? (LowPopIdleMinSeconds, LowPopIdleMaxSeconds)
            : (IdleMinSeconds, IdleMaxSeconds);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_idleScheduled)
            return;

        if (_timing.CurTime < _nextIdleFireTime)
            return;

        PlayLine(ProvidenceLineCategory.IdleMusings);
        ScheduleIdle();
    }

    /// <summary>
    ///     Plays a random line from <paramref name="category"/>'s collection, station-wide, via
    ///     <c>PlayGlobal</c> (the collection's own random pick happens inside
    ///     <c>SharedAudioSystem.ResolveSound</c> — see upstream <c>SoundCollectionSpecifier</c> handling).
    ///     No-ops entirely while <see cref="CCVars.SolreignProvidenceEnabled"/> is off.
    /// </summary>
    public void PlayLine(ProvidenceLineCategory category)
    {
        if (!_enabled)
            return;

        var collection = ProvidenceVoiceMap.CollectionFor(category);
        _audio.PlayGlobal(new SoundCollectionSpecifier(collection), Filter.Broadcast(), recordReplay: true);
    }

    /// <summary>
    ///     Plays a random line from <paramref name="category"/>'s collection to exactly ONE session,
    ///     never station-wide — for beats that are meant to feel personal (e.g. the first-shift
    ///     personal-address follow-up, <c>ProvidenceWelcomeSystem</c>) rather than a PA broadcast. Uses
    ///     the session-targeted <c>PlayGlobal</c> overload (not <c>Filter.SinglePlayer</c> +
    ///     <c>recordReplay: true</c>) so this private beat is never captured into round replays the way
    ///     the broadcast PA voice deliberately is. No-ops entirely while
    ///     <see cref="CCVars.SolreignProvidenceEnabled"/> is off, same as <see cref="PlayLine"/>.
    /// </summary>
    public void PlayLineTo(ProvidenceLineCategory category, ICommonSession recipient)
    {
        if (!_enabled)
            return;

        var collection = ProvidenceVoiceMap.CollectionFor(category);
        _audio.PlayGlobal(new SoundCollectionSpecifier(collection), recipient);
    }
}
