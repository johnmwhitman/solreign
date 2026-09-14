using System.Collections.Generic;

namespace Content.Server._Solreign.DirectivesFax;

/// <summary>
///     The closed set of streak counts PROVIDENCE acknowledges with an escalating-deadpan milestone
///     toast (council instruction: "PROVIDENCE acknowledges streak milestones... with escalating
///     deadpan", the <c>SolreignAwardPopup.ShowMilestone</c> voice). Pure data — one loc key per
///     threshold in <c>directives_fax.ftl</c>; a streak count not in this set is silently unremarked.
/// </summary>
public static class DirectivesFaxStreakMilestones
{
    /// <summary>Loc key (reason text, popup + chat mirror share it) per milestone streak count.</summary>
    public static readonly IReadOnlyDictionary<int, string> ReasonLocKeyByStreak = new Dictionary<int, string>
    {
        [3] = "solreign-directives-fax-streak-milestone-3",
        [5] = "solreign-directives-fax-streak-milestone-5",
        [10] = "solreign-directives-fax-streak-milestone-10",
        [25] = "solreign-directives-fax-streak-milestone-25",
        [50] = "solreign-directives-fax-streak-milestone-50",
    };

    /// <summary>Whether this streak count is a milestone worth acknowledging.</summary>
    public static bool IsMilestone(int streak) => ReasonLocKeyByStreak.ContainsKey(streak);
}
