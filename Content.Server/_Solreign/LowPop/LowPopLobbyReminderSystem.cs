using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Random;

namespace Content.Server._Solreign.LowPop;

/// <summary>
///     Churn-mitigation lane: SS14's stock lobby has no "minimum players to start" concept — see
///     <c>GameTicker.RoundFlow.cs</c>'s fixed-duration <c>_roundStartTime</c> countdown and
///     <c>GameTicker.Player.cs</c>'s <c>_roundStartCountdownHasNotStartedYetDueToNoPlayers</c> gate,
///     which only PAUSES the timer while the server is fully empty — it never lengthens or shortens
///     the countdown for player count, and the round always starts on schedule once anyone is
///     connected. Two churn risks fall out of that for a Reddit-driven server that spikes to
///     2-15 players:
///
///       1. A newcomer who joins solo (or near-solo) may not know the vanilla lobby requires clicking
///          READY in the character menu, and can simply watch the countdown expire and never spawn in
///          — a silent, confusing "the round started without me" moment (see
///          <c>GameTicker.RoundFlow.cs</c>'s <c>StartRound</c>, which only spawns players whose
///          <c>PlayerGameStatus</c> is <c>ReadyToPlay</c>).
///       2. Players may assume a quiet, low-pop round "doesn't count" and alt-tab away rather than
///          wait out the lobby timer, when in fact Season Ledger credit
///          (<c>SeasonLedgerSystem.OnRoundEnd</c> → <c>HrPointsRules.ForRoundCompletion</c>) is a flat
///          per-round bonus with NO player-count gate at all — every connected player at round end
///          already gets full credit regardless of population.
///
///     This system does not touch <c>GameTicker</c> at all — it only listens to the PUBLIC
///     <see cref="PlayerJoinedLobbyEvent"/> already raised by <c>GameTicker.Player.cs</c>'s
///     <c>PlayerJoinLobby</c>, and sends a private, text-only reminder via
///     <see cref="IChatManager.DispatchServerMessage"/>. No core round/ticker logic is modified.
///
///     Gated by <see cref="CCVars.SolreignLowPopLobbyReminderEnabled"/> (default on) and only fires
///     while the server's connected player count is at or below
///     <see cref="CCVars.SolreignLowPopLobbyReminderThreshold"/> (default 6) — see that CVar's doc
///     comment for why. Anti-fatigue: <see cref="LowPopLobbyReminderGate"/> caps it at one reminder per
///     player per round (reset on round start/restart), mirroring <c>ProvidenceWelcomeSystem</c>'s
///     gate idiom.
/// </summary>
public sealed partial class LowPopLobbyReminderSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IRobustRandom _random = default!;

    /// <summary>Loc keys for the reminder lines (lowpop-lobby-reminder.ftl), picked at random.</summary>
    private static readonly string[] ReminderKeys =
    {
        "solreign-lowpop-lobby-reminder-1",
        "solreign-lowpop-lobby-reminder-2",
        "solreign-lowpop-lobby-reminder-3",
    };

    /// <summary>
    ///     Same pool as <see cref="ReminderKeys"/> plus one extra line pointing a solo/low-pop player at
    ///     the Contracts Board (v14 quest-board extension, spec §4.1) — used ONLY while
    ///     <see cref="CCVars.SolreignContractsQuestBoardEnabled"/> is on, so flipping that CVar off
    ///     restores this system's original 3-line pool exactly, byte-for-byte.
    /// </summary>
    private static readonly string[] ReminderKeysWithContractsPointer =
    {
        "solreign-lowpop-lobby-reminder-1",
        "solreign-lowpop-lobby-reminder-2",
        "solreign-lowpop-lobby-reminder-3",
        "solreign-lowpop-lobby-reminder-contracts",
    };

    private readonly LowPopLobbyReminderGate _gate = new();

    /// <summary>Cached mirror of <see cref="CCVars.SolreignLowPopLobbyReminderEnabled"/>.</summary>
    private bool _enabled;

    /// <summary>Cached mirror of <see cref="CCVars.SolreignLowPopLobbyReminderThreshold"/>.</summary>
    private int _threshold;

    /// <summary>Cached mirror of <see cref="CCVars.SolreignContractsQuestBoardEnabled"/> — see
    /// <see cref="ReminderKeysWithContractsPointer"/> for why this system reads it too.</summary>
    private bool _contractsQuestBoardEnabled;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.SolreignLowPopLobbyReminderEnabled, v => _enabled = v, invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignLowPopLobbyReminderThreshold, v => _threshold = v, invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignContractsQuestBoardEnabled, v => _contractsQuestBoardEnabled = v, invokeImmediately: true);

        SubscribeLocalEvent<PlayerJoinedLobbyEvent>(OnPlayerJoinedLobby);
        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    private void OnRoundStarting(RoundStartingEvent ev)
    {
        _gate.Reset();
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _gate.Reset();
    }

    private void OnPlayerJoinedLobby(PlayerJoinedLobbyEvent ev)
    {
        if (!_enabled)
            return;

        // Aimed squarely at the empty-lobby churn case — stay silent at normal-to-high pop.
        if (_playerManager.PlayerCount > _threshold)
            return;

        var guid = ev.PlayerSession.UserId.UserId;

        if (!_gate.CanFire(guid))
            return;

        _gate.MarkFired(guid);

        var pool = _contractsQuestBoardEnabled ? ReminderKeysWithContractsPointer : ReminderKeys;
        var line = pool[_random.Next(pool.Length)];
        _chatManager.DispatchServerMessage(ev.PlayerSession, Loc.GetString(line));

        // Delivery goes out over DispatchServerMessage, which writes nothing to the runtime log —
        // so until this line existed there was NO way to tell "the reminder fired and they ignored
        // it" from "the reminder never fired". On 2026-08-01 an arrival was analysed for hours and
        // that question stayed unanswerable; the trigger conditions could be shown to hold, but the
        // delivery itself could not. A reminder nobody can prove was sent cannot be evaluated, and
        // an unevaluatable feature gets blamed or credited at random.
        Log.Info(
            $"lowpop lobby reminder sent to {ev.PlayerSession.Name} "
            + $"(players={_playerManager.PlayerCount}, threshold={_threshold}, key={line})");
    }
}
