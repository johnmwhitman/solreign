using System;
using System.Collections.Generic;
using System.Linq;

namespace Content.Server._Solreign.StationAudits;

/// <summary>
///     Pure, unit-testable selection logic for the inspection layer — no ECS, no I/O, no loc. Kept
///     separate from <see cref="StationAuditSystem"/> so it can be exercised without spinning up the
///     game, same split as <c>StationDirectiveSelection</c>.
/// </summary>
public static class StationAuditInspectionSelection
{
    /// <summary>
    ///     Picks which catalog indices PROVIDENCE assigns this shift: round-robin by round id (same
    ///     deterministic-window idiom as <c>StationDirectiveSelection.SelectDirectiveIndex</c>, extended
    ///     to a window of size <paramref name="assignCount"/> instead of a single index), with a curation
    ///     rule (spec §2.3): if the round-robin window happens to contain zero always-eligible criteria,
    ///     the last slot is swapped for one, so PROVIDENCE never assigns an inspection that reads as a
    ///     guaranteed no-op.
    ///
    ///     Degenerate inputs never throw: a non-positive <paramref name="catalogCount"/> or
    ///     <paramref name="assignCount"/> returns an empty list ("assign nothing, consequence layer
    ///     no-ops this round"); <paramref name="assignCount"/> greater than <paramref name="catalogCount"/>
    ///     clamps to the whole catalog; a negative <paramref name="roundId"/> folds into a valid window
    ///     start the same way <c>StationDirectiveSelection</c> folds a negative round id into a valid index.
    /// </summary>
    public static IReadOnlyList<int> SelectAssigned(
        int roundId,
        int catalogCount,
        int assignCount,
        IReadOnlySet<int> alwaysEligibleIndices)
    {
        if (catalogCount <= 0 || assignCount <= 0)
            return Array.Empty<int>();

        var clampedCount = Math.Min(assignCount, catalogCount);

        var start = roundId % catalogCount;
        if (start < 0)
            start += catalogCount;

        var picked = new List<int>(clampedCount);
        for (var offset = 0; offset < clampedCount; offset++)
            picked.Add((start + offset) % catalogCount);

        // Curation rule: guarantee at least one always-eligible criterion in the assigned set whenever
        // the catalog actually has one to offer. Deterministic replacement (lowest always-eligible
        // index) rather than random, so the same (roundId, catalogCount, assignCount) input always
        // produces the same assignment.
        if (alwaysEligibleIndices.Count > 0 && !picked.Any(alwaysEligibleIndices.Contains))
            picked[^1] = alwaysEligibleIndices.Min();

        return picked.Distinct().ToList();
    }
}
