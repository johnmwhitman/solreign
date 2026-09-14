using System;
using System.Collections.Generic;
using Content.Server.GameTicking.Events;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     Additive per-round "early death" tracker for the Season Ledger. Watches <see cref="MobStateChangedEvent"/>
///     and records, per account, the earliest moment a player entered <see cref="MobState.Dead"/> relative to
///     round start. <see cref="OnRoundEnd"/> reads this to stamp <c>EarlyDeath</c> on each round contribution,
///     which is what unlocks the "Amortized Asset" title (3+ early deaths, see <see cref="TitleRules"/>).
///
///     Threading: every hook here runs synchronously on the main thread (game events are dispatched on the
///     tick), so the per-round map is only ever touched on-thread. <see cref="OnRoundEnd"/> snapshots its
///     decisions before its first <c>await</c> so the async DB continuations never read this map off-thread.
/// </summary>
public sealed partial class SeasonLedgerSystem
{
    /// <summary>A death within this many seconds of round start counts as "early".</summary>
    public const double EarlyDeathWindowSeconds = 300;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMindSystem _mind = default!;

    // CurTime captured when the current round started. Null between rounds / before the first start.
    private TimeSpan? _roundStart;

    // Earliest death timestamp (CurTime) per account for the current round. Main-thread only.
    private readonly Dictionary<NetUserId, TimeSpan> _earliestDeath = new();

    private void InitializeEarlyDeath()
    {
        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnRoundStarting(RoundStartingEvent ev)
    {
        // New round — stamp the start time and clear any leftover death records.
        _roundStart = _timing.CurTime;
        _earliestDeath.Clear();
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        // State is wiped before the next round; drop the per-round map so nothing leaks across rounds.
        _roundStart = null;
        _earliestDeath.Clear();

        // Also drop the handed-over Corporate standings (SeasonLedgerSystem.Corporate.cs) and the round's
        // contract completions (SeasonLedgerSystem.Contracts.cs). A system may only subscribe
        // RoundRestartCleanupEvent once, so this partial's single handler clears all the per-round maps.
        ClearRoundStanding();
        ClearRoundContracts();
    }

    private void OnMobStateChanged(MobStateChangedEvent ev)
    {
        if (ev.NewMobState != MobState.Dead)
            return;

        // Resolve the mob back to the controlling account via its mind (same key the ledger is keyed on).
        if (!_mind.TryGetMind(ev.Target, out _, out var mind))
            return;

        if (mind.UserId is not { } user)
            return;

        var now = _timing.CurTime;

        // Keep the earliest death only — a player may go Dead -> revived -> Dead again in one round.
        if (_earliestDeath.TryGetValue(user, out var existing) && existing <= now)
            return;

        _earliestDeath[user] = now;
    }

    /// <summary>
    ///     Did the player die within the early-death window of round start? Pure timing decision, unit-tested.
    ///     A death recorded before round start (defensive) or after the window does not count.
    /// </summary>
    public static bool IsEarlyDeath(TimeSpan roundStart, TimeSpan deathTime)
    {
        var elapsed = deathTime - roundStart;
        return elapsed >= TimeSpan.Zero && elapsed <= TimeSpan.FromSeconds(EarlyDeathWindowSeconds);
    }

    /// <summary>
    ///     Whether this account recorded an early death in the current round. Called synchronously from
    ///     <see cref="OnRoundEnd"/> (before any await) so the map is read on the main thread only.
    /// </summary>
    private bool HadEarlyDeath(NetUserId user)
    {
        return _roundStart is { } start
            && _earliestDeath.TryGetValue(user, out var deathTime)
            && IsEarlyDeath(start, deathTime);
    }
}
