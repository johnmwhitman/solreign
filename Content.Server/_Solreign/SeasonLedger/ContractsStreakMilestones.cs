using System.Collections.Generic;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     The closed set of streak counts PROVIDENCE acknowledges with a milestone toast for the Solreign
///     Contracts consecutive-shift streak (v14 quest-board extension, spec §3.2) — structurally cloned
///     from <c>DirectivesFaxStreakMilestones</c>, same threshold set and the same
///     <c>SolreignAwardPopup.ShowMilestone</c> voice. Pure data — one loc key per threshold in
///     <c>contracts_streak.ftl</c>; a streak count not in this set is silently unremarked.
/// </summary>
public static class ContractsStreakMilestones
{
    /// <summary>Loc key (reason text, popup + chat mirror share it) per milestone streak count.</summary>
    public static readonly IReadOnlyDictionary<int, string> ReasonLocKeyByStreak = new Dictionary<int, string>
    {
        [3] = "solreign-contracts-streak-milestone-3",
        [5] = "solreign-contracts-streak-milestone-5",
        [10] = "solreign-contracts-streak-milestone-10",
        [25] = "solreign-contracts-streak-milestone-25",
        [50] = "solreign-contracts-streak-milestone-50",
    };

    /// <summary>Whether this streak count is a milestone worth acknowledging.</summary>
    public static bool IsMilestone(int streak) => ReasonLocKeyByStreak.ContainsKey(streak);
}
