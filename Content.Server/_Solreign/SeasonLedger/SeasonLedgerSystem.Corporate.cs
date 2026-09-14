using System;
using System.Collections.Generic;
using Robust.Shared.Network;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     Handoff surface between the Corporate Ladder game rule (<c>SolreignCorporateRuleSystem</c>) and the
///     Season Ledger. The rule computes a per-round Corporate Standing per account during the round but does
///     not persist it; at round end it hands its final standings here, and <see cref="OnRoundEnd"/> folds each
///     account's standing into that account's <see cref="RoundContribution"/> so it accumulates into the
///     season/career ledger and compounds into the Corporate Rank (see <see cref="RankRules"/>).
///
///     Ordering (verified): the rule's <c>AppendRoundEndText</c> (a <c>RoundEndTextAppendEvent</c> handler)
///     fires BEFORE the ledger's <c>RoundEndMessageEvent</c> handler, so a submit during append is guaranteed
///     to be visible when <see cref="OnRoundEnd"/> reads it.
///
///     Threading: <see cref="SubmitRoundStandings"/> is invoked from the game rule on the main thread (game
///     events dispatch on the tick), and <see cref="OnRoundEnd"/> snapshots the map before its first
///     <c>await</c> — identical discipline to the early-death partial — so the async DB continuations never
///     read this map off-thread. Cleared on <c>RoundRestartCleanupEvent</c> via <see cref="ClearRoundStanding"/>
///     (called from the early-death cleanup handler, since a system may only subscribe an event once).
/// </summary>
public sealed partial class SeasonLedgerSystem
{
    // Final per-account Corporate Standing for the round just ended, keyed by account guid. Main-thread only.
    private readonly Dictionary<Guid, int> _roundStanding = new();

    /// <summary>
    ///     Called by the Corporate Ladder rule at round end to hand over its final per-account standings.
    ///     Replaces any prior submission for the round (last writer wins). Main-thread only.
    /// </summary>
    public void SubmitRoundStandings(IReadOnlyDictionary<NetUserId, int> standings)
    {
        _roundStanding.Clear();
        foreach (var (user, standing) in standings)
            _roundStanding[user.UserId] = standing;
    }

    /// <summary>Handoff for a single account's round standing. Main-thread only.</summary>
    public void SubmitRoundStanding(Guid user, int standing)
    {
        _roundStanding[user] = standing;
    }

    /// <summary>This account's Corporate Standing for the round just ended, or 0 if it scored nothing.</summary>
    private int RoundStandingFor(Guid user)
    {
        return _roundStanding.TryGetValue(user, out var standing) ? standing : 0;
    }

    /// <summary>Drops the per-round standings so nothing leaks into the next round.</summary>
    private void ClearRoundStanding()
    {
        _roundStanding.Clear();
    }
}
