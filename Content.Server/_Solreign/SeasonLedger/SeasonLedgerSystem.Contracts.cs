using System;
using System.Collections.Generic;
using System.Linq;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     Handoff surface between the Solreign Contracts system (<c>ContractsSystem</c>) and the Season
///     Ledger — the same shape as the Corporate Standing handoff (<c>SeasonLedgerSystem.Corporate</c>).
///     Contract payouts call <see cref="SubmitContractCompletion"/> the moment a contract completes
///     (instant in-round feedback lives in the Contracts system; persistence lives here); at round end
///     <c>OnRoundEnd</c> folds the per-account totals into each <see cref="RoundContribution"/> and appends
///     the audit rows to <c>contract_log</c>.
///
///     Threading: <see cref="SubmitContractCompletion"/> is invoked from contract payout on the main thread
///     (deposits are interaction events, dispatched on the tick), and <c>OnRoundEnd</c> snapshots both
///     collections before its first <c>await</c> — identical discipline to the early-death and Corporate
///     partials — so the async DB continuations never read them off-thread. Cleared on
///     <c>RoundRestartCleanupEvent</c> via <see cref="ClearRoundContracts"/> (called from the early-death
///     cleanup handler, since a system may only subscribe an event once).
///
///     Anti-grief rule 9 (never-demote): completions only ever ADD non-negative counts/scores; a failed,
///     skipped or abandoned contract writes nothing at all.
/// </summary>
public sealed partial class SeasonLedgerSystem
{
    // Per-account contract totals for the current round: completions + weighted score. Main-thread only.
    private readonly Dictionary<Guid, (int Completed, int Score)> _roundContracts = new();

    // One entry per completion per contributor, bound for contract_log at round end. Main-thread only.
    private readonly List<(Guid User, string ContractId, string Scope)> _roundContractLog = new();

    /// <summary>
    ///     Records one contract completion for an account. <paramref name="score"/> is the weighted
    ///     contract_score contribution (spec §4.1: personal=2, department=1, chain-final=3); negative
    ///     scores are clamped to 0 so the ledger's never-demote invariant can't be violated from here.
    ///     Main-thread only.
    /// </summary>
    public void SubmitContractCompletion(Guid user, string contractId, string scope, int score)
    {
        if (string.IsNullOrWhiteSpace(contractId))
            throw new ArgumentException("Contract ID must be nonempty.", nameof(contractId));
        if (string.IsNullOrWhiteSpace(scope))
            throw new ArgumentException("Contract scope must be nonempty.", nameof(scope));

        if (score < 0)
            score = 0;

        _roundContracts.TryGetValue(user, out var current);
        _roundContracts[user] = (current.Completed + 1, current.Score + score);
        _roundContractLog.Add((user, contractId, scope));
    }

    /// <summary>Current in-round contract totals for exact-once verification. Main-thread only.</summary>
    internal (int Completed, int Score) CurrentContractTotals(Guid user)
    {
        return _roundContracts.GetValueOrDefault(user);
    }

    /// <summary>Current in-round contract-log count for one account. Main-thread only.</summary>
    internal int CurrentContractLogCount(Guid user)
    {
        return _roundContractLog.Count(entry => entry.User == user);
    }

    /// <summary>
    ///     This account's contract totals for the round just ended, or zeroes. Called synchronously from
    ///     <c>OnRoundEnd</c> (before any await) so the map is read on the main thread only.
    /// </summary>
    private (int Completed, int Score) RoundContractsFor(Guid user)
    {
        return _roundContracts.TryGetValue(user, out var totals) ? totals : (0, 0);
    }

    /// <summary>
    ///     Snapshots the round's completion audit rows into store-ready records. Called synchronously from
    ///     <c>OnRoundEnd</c> before its first await (main-thread-before-await contract).
    /// </summary>
    private List<ContractLogRecord> SnapshotContractLog(int roundId)
    {
        var records = new List<ContractLogRecord>(_roundContractLog.Count);
        foreach (var (user, contractId, scope) in _roundContractLog)
            records.Add(new ContractLogRecord(roundId, user, contractId, scope));

        return records;
    }

    /// <summary>Drops the per-round contract records so nothing leaks into the next round.</summary>
    private void ClearRoundContracts()
    {
        _roundContracts.Clear();
        _roundContractLog.Clear();
    }
}
