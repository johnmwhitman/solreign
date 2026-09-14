using System;
using System.Globalization;

namespace Content.Server._Solreign.PlayerDelight.Mark;

/// <summary>
///     Pure, wall-clock aging law for the Mark (docs/specs/MARK-SPEC-2026-07-17-DRAFT.md §3.4).
///     Zero I/O, zero engine — same inputs, same stage, same scalar, forever (the epitaph-picker
///     determinism argument: it's a keepsake; determinism makes it testable and makes PROVIDENCE
///     never contradict itself).
///
///     Clock: wall-clock days since <c>planted_utc</c>. Shifts-elapsed was considered and REJECTED
///     as the primary axis (at pop 1-5, shift-driven growth stalls exactly when the server is quiet
///     — the anti-magnet); wall-clock makes "I have been watering it while you were away" literally
///     true. Thresholds are CONSTANTS, not CVars, by spec law — an engraving's aging rule shouldn't
///     be operator-twiddled; changing it later is a deliberate code change with test updates.
/// </summary>
public static class MarkAgeRules
{
    /// <summary>Days at which stage 1 begins (below this: stage 0, freshly planted).</summary>
    public const double Stage1Days = 2;

    /// <summary>Days at which stage 2 begins.</summary>
    public const double Stage2Days = 7;

    /// <summary>Days at which stage 3 (terminal) begins.</summary>
    public const double Stage3Days = 21;

    /// <summary>The terminal visual stage — stages run 0..3 (the closed 3x4 prototype table).</summary>
    public const int MaxStage = 3;

    /// <summary>Growth-scalar day cap: growth accrues for the first 60 days, then plateaus.</summary>
    public const double GrowthCapDays = 60;

    /// <summary>Growth-scalar units per day (0.7/day: day 3 renders the council's own "2.1 cm").</summary>
    public const double GrowthPerDay = 0.7;

    /// <summary>
    ///     The visual stage at <paramref name="nowUtc"/> for a mark planted at
    ///     <paramref name="plantedUtc"/>: 0 (&lt; 2 days), 1 (&gt;= 2d), 2 (&gt;= 7d), 3 (&gt;= 21d).
    ///     Monotonic in time; a negative elapsed span (clock skew, corrupt row) clamps to stage 0
    ///     rather than throwing — the projection must never fail a round over a weird timestamp.
    /// </summary>
    public static int StageAt(DateTime plantedUtc, DateTime nowUtc)
    {
        var days = (nowUtc - plantedUtc).TotalDays;
        if (days >= Stage3Days)
            return 3;
        if (days >= Stage2Days)
            return 2;
        if (days >= Stage1Days)
            return 1;
        return 0;
    }

    /// <summary>
    ///     The capped, monotonic growth scalar at <paramref name="nowUtc"/>:
    ///     <c>min(days, 60) * 0.7</c>. Identical for all kinds — the per-kind UNIT WORD (cm / lumens
    ///     / sheen) lives in the copy templates, so "2.1 cm" and "2.1 lumens" are the same number
    ///     wearing different uniforms. Negative elapsed spans clamp to 0.
    /// </summary>
    public static double GrowthScalar(DateTime plantedUtc, DateTime nowUtc)
    {
        var days = (nowUtc - plantedUtc).TotalDays;
        if (days <= 0)
            return 0;
        return Math.Min(days, GrowthCapDays) * GrowthPerDay;
    }

    /// <summary>
    ///     The growth accrued between two visits — what the return line renders
    ///     (<c>Delta(now) - Delta(last_visit)</c>, spec §3.4). Never negative: monotonicity is a law,
    ///     so out-of-order timestamps render 0 growth rather than shrinkage.
    /// </summary>
    public static double GrowthDelta(DateTime plantedUtc, DateTime lastVisitUtc, DateTime nowUtc)
    {
        var delta = GrowthScalar(plantedUtc, nowUtc) - GrowthScalar(plantedUtc, lastVisitUtc);
        return delta > 0 ? delta : 0;
    }

    /// <summary>
    ///     Renders a growth scalar to the one-decimal, invariant-culture string the templates
    ///     interpolate ("2.1", "42.0") — one render rule so the line never varies by server locale.
    /// </summary>
    public static string RenderGrowth(double scalar)
    {
        return scalar.ToString("0.0", CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     Parses the store's ISO-8601 ("o") UTC timestamp strings back to a UTC
    ///     <see cref="DateTime"/>. Returns <c>false</c> on garbage — callers skip the row (corrupt
    ///     timestamps must never throw inside a round-start projection or a spawn hook).
    /// </summary>
    public static bool TryParseLedgerUtc(string? ledgerUtc, out DateTime utc)
    {
        if (DateTime.TryParse(
                ledgerUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out utc))
        {
            return true;
        }

        utc = default;
        return false;
    }
}
