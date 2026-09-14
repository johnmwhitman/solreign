using Content.Server._Solreign.Effects;
using Content.Server.GameTicking.Events;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Popups;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.Providence;

/// <summary>
///     Delight-eggs batch (feat/delight-eggs): Providence's "second condolence" — a rare, delayed
///     follow-up to <see cref="ProvidenceCommiserationSystem"/>'s death-commiseration line. Never a
///     stand-alone trigger: <see cref="MaybeSchedule"/> is only ever called by
///     <see cref="ProvidenceCommiserationSystem"/>, right after a player's FIRST commiseration this
///     round has already fired, so a player can never get a second condolence without having gotten a
///     first one. Cosmetic flavor only — never affects gameplay, revival, or scoring, same house rule
///     as the base commiseration beat.
///
///     Two independent gates, same shape as <see cref="ProvidenceCommiserationGate"/>:
///       1. A coin flip (<see cref="CCVars.SolreignProvidenceSecondCondolenceChance"/>, default 30% —
///          stricter than the base line's 50%, so a follow-up reads as rarer than the beat that
///          unlocked it) decides whether a follow-up gets scheduled AT ALL.
///       2. If scheduled, the actual fire time is a random delay
///          (<see cref="DelayMinSeconds"/>-<see cref="DelayMaxSeconds"/> later) reusing
///          <see cref="PeriodicEffectTiming.NextFireTime"/>'s pure interpolation math, so it reads as
///          Providence "remembering" rather than an instant double-tap.
///     A player can only ever have one scheduled follow-up (the per-round cap on the FIRST
///     commiseration already guarantees this — <see cref="MaybeSchedule"/> is called at most once per
///     player per round).
///
///     Gated by <see cref="CCVars.SolreignProvidenceSecondCondolenceEnabled"/> AND the base station
///     voice's <see cref="ProvidenceVoiceSystem.Enabled"/> switch — either being off means
///     <see cref="MaybeSchedule"/> never schedules anything.
/// </summary>
public sealed partial class ProvidenceSecondCondolenceSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ProvidenceVoiceSystem _providence = default!;

    /// <summary>Lower edge of the follow-up delay window: 5 minutes after the first condolence.</summary>
    private const float DelayMinSeconds = 5f * 60f;

    /// <summary>Upper edge of the follow-up delay window: 15 minutes after the first condolence.</summary>
    private const float DelayMaxSeconds = 15f * 60f;

    /// <summary>
    ///     The 4 follow-up lines, distinct from <c>ProvidenceCommiserationSystem.LineKeys</c> — a
    ///     follow-up should read as Providence circling back, not repeating itself.
    /// </summary>
    private static readonly string[] LineKeys =
    {
        "solreign-providence-second-condolence-1",
        "solreign-providence-second-condolence-2",
        "solreign-providence-second-condolence-3",
        "solreign-providence-second-condolence-4",
    };

    private bool _enabled;

    /// <summary>Scheduled fire times for players awaiting a follow-up, keyed by their account GUID.</summary>
    private readonly Dictionary<Guid, TimeSpan> _scheduled = new();

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.SolreignProvidenceSecondCondolenceEnabled, v => _enabled = v, invokeImmediately: true);

        SubscribeLocalEvent<RoundStartingEvent>(OnRoundBoundary);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundBoundary);
    }

    private void OnRoundBoundary(RoundStartingEvent ev)
    {
        _scheduled.Clear();
    }

    private void OnRoundBoundary(RoundRestartCleanupEvent ev)
    {
        _scheduled.Clear();
    }

    /// <summary>
    ///     Rolls whether <paramref name="player"/>'s just-fired first commiseration also earns a
    ///     scheduled follow-up. A miss here is silent and permanent for the round — there is no second
    ///     roll, since <see cref="ProvidenceCommiserationSystem"/> only ever calls this once per player
    ///     per round (right after its own once-per-round cap admits them).
    /// </summary>
    public void MaybeSchedule(Guid player)
    {
        if (!_enabled || !_providence.Enabled)
            return;

        if (_scheduled.ContainsKey(player))
            return;

        var chance = _cfg.GetCVar(CCVars.SolreignProvidenceSecondCondolenceChance);
        if (!ProvidenceCommiserationGate.ShouldFire(_random.NextDouble(), chance))
            return;

        _scheduled[player] = PeriodicEffectTiming.NextFireTime(_timing.CurTime, DelayMinSeconds, DelayMaxSeconds, _random.NextDouble());
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_scheduled.Count == 0)
            return;

        var curTime = _timing.CurTime;
        List<Guid>? due = null;

        foreach (var (player, fireTime) in _scheduled)
        {
            if (curTime < fireTime)
                continue;

            due ??= new List<Guid>();
            due.Add(player);
        }

        if (due is null)
            return;

        foreach (var player in due)
        {
            _scheduled.Remove(player);
            Fire(player);
        }
    }

    /// <summary>
    ///     Shows the follow-up popup if the player is still connected with a live body — a player who
    ///     disconnected or has no attached entity by the time their follow-up comes due simply never
    ///     sees it (no queued/replayed delivery); this is a flavor beat, not a guaranteed notification.
    /// </summary>
    private void Fire(Guid player)
    {
        if (!_players.TryGetSessionById(new NetUserId(player), out var session))
            return;

        if (session.AttachedEntity is not { } entity || !Exists(entity))
            return;

        var line = LineKeys[_random.Next(LineKeys.Length)];
        _popup.PopupEntity(Loc.GetString(line), entity, entity, PopupType.Medium);
        _providence.PlayLine(ProvidenceLineCategory.DeathCommiseration);
    }
}
