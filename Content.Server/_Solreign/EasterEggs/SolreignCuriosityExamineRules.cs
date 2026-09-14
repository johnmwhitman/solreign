namespace Content.Server._Solreign.EasterEggs;

/// <summary>
///     Pure tier/roll math for <see cref="SolreignCuriosityExamineSystem"/>. Kept free of IoC/engine
///     types so it is directly unit-testable
///     (Content.Tests/_Solreign/SolreignCuriosityExamineRulesTests.cs), same split as
///     <c>SolreignWristOrganizerRules</c>/<c>SolreignWristOrganizerSystem</c>.
/// </summary>
public static class SolreignCuriosityExamineRules
{
    /// <summary>
    ///     Highest tier index unlocked by <paramref name="examineCount"/> against the ascending
    ///     <paramref name="thresholds"/> list, or -1 if the count hasn't reached even the first
    ///     threshold (or the list is empty). A threshold of 0 or negative unlocks immediately —
    ///     defensive against a YAML author writing a non-positive first threshold, not something the
    ///     mechanism relies on.
    /// </summary>
    public static int TierIndexForCount(int examineCount, IReadOnlyList<int> thresholds)
    {
        var tier = -1;

        for (var i = 0; i < thresholds.Count; i++)
        {
            if (examineCount >= thresholds[i])
                tier = i;
            else
                break;
        }

        return tier;
    }

    /// <summary>
    ///     Whether the rare top-tier aside should show this examine: only ever true once
    ///     <paramref name="tierIndex"/> has reached <paramref name="topTierIndex"/> (never fires early,
    ///     and never fires if there is no top tier at all — <paramref name="topTierIndex"/> &lt; 0), and
    ///     then gated by a per-examine coin flip. <paramref name="chance"/> is clamped into [0, 1] so a
    ///     misconfigured value fails toward "shows less often", never a crash or guaranteed/negative
    ///     odds — same defensive-clamp idiom as <c>ProvidenceCommiserationGate.ShouldFire</c>.
    /// </summary>
    public static bool ShouldShowRareAside(int tierIndex, int topTierIndex, double roll, float chance)
    {
        if (topTierIndex < 0 || tierIndex < topTierIndex)
            return false;

        var clampedChance = Math.Clamp(chance, 0f, 1f);
        return roll < clampedChance;
    }
}
