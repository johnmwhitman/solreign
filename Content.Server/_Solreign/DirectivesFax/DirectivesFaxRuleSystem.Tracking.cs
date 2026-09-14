using System;
using System.Collections.Generic;
using Content.Server.GameTicking.Events;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._Solreign.DirectivesFax;

/// <summary>
///     Per-round tracking for <see cref="DirectivesFaxRuleSystem"/>: crew deaths (for the
///     zero-casualties clause) and per-account presence duration (for the "present ≥ N minutes"
///     streak gate). Same idiom as
///     <c>Content.Server._Solreign.Corporate.SolreignCorporateRuleSystem.Scoring</c> /
///     <c>Content.Server._Solreign.SeasonLedger.SeasonLedgerSystem.EarlyDeath</c>: plain per-round
///     Dictionary/HashSet state, touched only on the main thread (game events dispatch on the tick),
///     cleared on <see cref="RoundRestartCleanupEvent"/> so nothing leaks across rounds. Reading either
///     tracker at round end is O(1)/O(account count) — no per-tick scan, satisfying the "cheap to
///     evaluate" rail.
/// </summary>
public sealed partial class DirectivesFaxRuleSystem
{
    /// <summary>A death within a shift counts toward the zero-casualties clause once, regardless of
    /// revive-and-re-die — a <see cref="HashSet{T}"/> naturally dedupes.</summary>
    private readonly HashSet<NetUserId> _deadThisShift = new();

    /// <summary>Closed presence intervals accumulated so far this round, per account.</summary>
    private readonly Dictionary<NetUserId, TimeSpan> _presenceAccrued = new();

    /// <summary>Open presence interval start (CurTime) for currently-connected accounts.</summary>
    private readonly Dictionary<NetUserId, TimeSpan> _connectedSince = new();

    /// <summary>
    ///     Minimum cumulative connected time this shift for an account's compliance streak to be
    ///     touched at all. Below this, the account is simply skipped — never counted as "absent and
    ///     therefore failed", per the presence-aware discipline: absence never resets the streak.
    /// </summary>
    public static readonly TimeSpan PresenceThreshold = TimeSpan.FromMinutes(5);

    public int DeadThisShiftCount => _deadThisShift.Count;

    private void InitializeTracking()
    {
        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);

        _players.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    private void OnRoundStarting(RoundStartingEvent ev)
    {
        _deadThisShift.Clear();
        _presenceAccrued.Clear();
        _connectedSince.Clear();

        // Seed open intervals for everyone already connected when the round starts — the common case
        // (most accounts join the lobby before round start, not mid-round).
        var now = _timing.CurTime;
        foreach (var session in _players.Sessions)
        {
            if (session.Status != SessionStatus.Disconnected)
                _connectedSince[session.UserId] = now;
        }
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _deadThisShift.Clear();
        _presenceAccrued.Clear();
        _connectedSince.Clear();
    }

    private void OnMobStateChanged(MobStateChangedEvent ev)
    {
        if (ev.NewMobState != MobState.Dead)
            return;

        if (!_mind.TryGetMind(ev.Target, out _, out var mind) || mind.UserId is not { } user)
            return;

        _deadThisShift.Add(user);
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        var user = args.Session.UserId;

        if (args.NewStatus == SessionStatus.Disconnected)
        {
            CloseInterval(user);
            return;
        }

        // Any other transition (Connecting -> Connected -> InGame, ...): ensure an open interval
        // exists. Idempotent — never restart an already-open interval, which would lose time already
        // accrued this connection.
        if (!_connectedSince.ContainsKey(user))
            _connectedSince[user] = _timing.CurTime;
    }

    private void CloseInterval(NetUserId user)
    {
        if (!_connectedSince.Remove(user, out var openSince))
            return;

        var elapsed = _timing.CurTime - openSince;
        _presenceAccrued.TryGetValue(user, out var existing);
        _presenceAccrued[user] = existing + elapsed;
    }

    /// <summary>
    ///     Every account present at least <see cref="PresenceThreshold"/> cumulative time this shift —
    ///     closed intervals plus whatever open interval is still running right now. Must be called
    ///     synchronously (main thread) BEFORE any <c>await</c> in the caller, same discipline as
    ///     <c>SolreignSocialFirstsSystem</c>'s dedupe-mark: the dictionaries backing this are main-thread
    ///     only.
    /// </summary>
    private List<Guid> SnapshotPresentEligibleAccounts()
    {
        var now = _timing.CurTime;
        var eligible = new List<Guid>();
        var seen = new HashSet<NetUserId>();

        foreach (var (user, accrued) in _presenceAccrued)
        {
            seen.Add(user);
            var total = accrued;
            if (_connectedSince.TryGetValue(user, out var openSince))
                total += now - openSince;

            if (total >= PresenceThreshold)
                eligible.Add(user.UserId);
        }

        foreach (var (user, openSince) in _connectedSince)
        {
            if (!seen.Add(user))
                continue;

            if (now - openSince >= PresenceThreshold)
                eligible.Add(user.UserId);
        }

        return eligible;
    }
}
