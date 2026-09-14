using System.Collections.Generic;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Robust.Shared.Network;

namespace Content.Server._Solreign.Corporate;

/// <summary>
///     Per-round Corporate Standing tracker for <see cref="SolreignCorporateRuleSystem"/>. Cosmetic score only.
///
///     Threading (mirrors <c>SeasonLedgerSystem.EarlyDeath</c>): every hook here runs synchronously on the main
///     thread, so the per-round maps are only ever touched on-thread. Both maps are cleared on round restart so
///     nothing leaks across rounds. Accounts are keyed on <see cref="NetUserId"/>, resolved via the killer's
///     mind — the same key the Season Ledger uses.
/// </summary>
public sealed partial class SolreignCorporateRuleSystem
{
    /// <summary>Corporate Standing per account for the current round. Main-thread only.</summary>
    private readonly Dictionary<NetUserId, int> _standing = new();

    /// <summary>
    ///     Last-seen display name per account, captured at scoring time so the scoreboard can name people who
    ///     have since disconnected or been gibbed. Main-thread only.
    /// </summary>
    private readonly Dictionary<NetUserId, string> _names = new();

    /// <summary>True only between our rule's <c>Started</c> and round teardown — gates kill scoring.</summary>
    private bool _active;

    private void InitializeScoring()
    {
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _standing.Clear();
        _names.Clear();
        _active = false;
    }

    /// <summary>
    ///     Simplest trackable "productivity" signal: when a mob enters <see cref="MobState.Dead"/>, the entity
    ///     that caused it (<c>Origin</c>) — if it resolves to an account and isn't the deceased themselves — is
    ///     credited +1 Corporate Standing. Anything we can't attribute to an account (environmental death,
    ///     weapon-as-origin, suicide, mindless mob) is simply skipped. No penalties, no compulsion.
    /// </summary>
    private void OnMobStateChanged(MobStateChangedEvent ev)
    {
        if (!_active || ev.NewMobState != MobState.Dead)
            return;

        if (ev.Origin is not { } origin || origin == ev.Target)
            return;

        // Resolve the killer's mob back to the controlling account via its mind.
        if (!_mind.TryGetMind(origin, out _, out var mind) || mind.UserId is not { } killer)
            return;

        Add(killer, 1);
        _names[killer] = MetaData(origin).EntityName;
    }

    /// <summary>
    ///     Credits Corporate Standing from outside the kill-attribution path — the Solreign Contracts
    ///     payout pipeline (spec §3.3: "standing via existing Corporate pipeline"). Deliberately NOT gated
    ///     on <see cref="_active"/>: a contract completed while the rule idles still counts, and the
    ///     standing flows to the Season Ledger through the same round-end handoff as kill standing.
    ///     Non-positive deltas are ignored (anti-grief rule 9: standing only ever goes up).
    /// </summary>
    public void AwardStanding(NetUserId user, int delta, string name)
    {
        if (delta <= 0)
            return;

        Add(user, delta);
        _names[user] = name;
    }

    /// <summary>Current in-round Standing for contract/economy verification. Main-thread only.</summary>
    internal int CurrentStanding(NetUserId user)
    {
        return _standing.GetValueOrDefault(user);
    }

    /// <summary>Adds <paramref name="delta"/> to an account's standing (creating the entry if absent).</summary>
    private void Add(NetUserId user, int delta)
    {
        _standing.TryGetValue(user, out var current);
        _standing[user] = current + delta;
    }

    /// <summary>
    ///     Snapshots the current standings into the pure-helper input shape, resolving each account to its
    ///     last-seen display name (falling back to the raw user id if we never captured one). Main-thread only.
    /// </summary>
    private List<CorporateStanding> SnapshotStandings()
    {
        var snapshot = new List<CorporateStanding>(_standing.Count);
        foreach (var (user, score) in _standing)
        {
            var name = _names.TryGetValue(user, out var known) ? known : user.ToString();
            snapshot.Add(new CorporateStanding(name, score));
        }

        return snapshot;
    }
}
