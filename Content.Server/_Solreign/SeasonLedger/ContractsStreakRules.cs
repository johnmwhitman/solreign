using System;
using System.Collections.Generic;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     Pure fold decisions for the Solreign Contracts consecutive-shift streak (v14 quest-board
///     extension, spec §3) — kept free of IoC so unit tests can pin cross-scope and connectivity
///     rules without spinning up the ledger store or round-end envelope.
/// </summary>
public static class ContractsStreakRules
{
    /// <summary>Contract-log scope label for Personal completions (see <c>ContractsSystem.ScopeLabel</c>).</summary>
    public const string PersonalScope = "personal";

    /// <summary>
    ///     Disconnected roster members must not advance or reset a streak — only <paramref name="connected"/>
    ///     accounts (honoring <c>RoundEndPlayerInfo.Connected</c>) are folded.
    /// </summary>
    public static bool ShouldFoldAccount(bool connected) => connected;

    /// <summary>
    ///     True when this account completed at least one Personal-scope contract this round.
    ///     Salvage-raid (and other non-personal) completions must not advance the Personal streak.
    /// </summary>
    public static bool CompletedPersonalThisRound(int personalCompletions) => personalCompletions > 0;

    /// <summary>Whether a contract-log scope row counts toward the Personal streak.</summary>
    public static bool IsPersonalScope(string scope) =>
        string.Equals(scope, PersonalScope, StringComparison.Ordinal);

    /// <summary>
    ///     Counts Personal-scope completions for <paramref name="user"/> from an already-snapshotted
    ///     contract log (main-thread-before-await). Aggregate <c>ContractsCompleted</c> is deliberately
    ///     not used — it includes salvage participants who deposited nothing.
    /// </summary>
    public static int CountPersonalCompletions(IReadOnlyList<ContractLogRecord> contractLog, Guid user)
    {
        var count = 0;
        foreach (var row in contractLog)
        {
            if (row.User != user)
                continue;
            if (!IsPersonalScope(row.Scope))
                continue;
            count++;
        }

        return count;
    }

    /// <summary>
    ///     Whether a milestone toast should fire for this fold result. Same/older-round replay returns
    ///     the existing streak but must never re-notify — callers plumb <paramref name="wasReplay"/> from
    ///     the store and suppress delivery when true.
    /// </summary>
    public static bool ShouldNotifyMilestone(bool completedThisRound, bool wasReplay, int currentStreak)
    {
        return completedThisRound && !wasReplay && ContractsStreakMilestones.IsMilestone(currentStreak);
    }
}
