namespace Content.Shared._Solreign.Ghost;

/// <summary>
/// Solreign — Afterlife Activities (roadmap D2.2).
/// Pure display rules for the afterlife activity list, split out for unit testing
/// (same convention as <c>RcControlMath</c>, <c>WardrobeRules</c>).
/// </summary>
public static class GhostActivityRules
{
    public const int MaxNameLength = 60;
    public const int MaxDescriptionLength = 200;

    /// <summary>
    /// Trims surrounding whitespace and clamps to <paramref name="maxLength"/>.
    /// Returns an empty string for null/whitespace input.
    /// </summary>
    public static string Sanitize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    /// <summary>
    /// Stable, case-insensitive ordinal sort by display name so the menu order is
    /// consistent between requests and clients.
    /// </summary>
    public static void SortForDisplay(List<GhostActivityInfo> activities)
    {
        activities.Sort(static (a, b) =>
            string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }
}
