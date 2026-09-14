namespace Content.Server._Solreign.Terminator;

/// <summary>
///     One potential retrieval target, snapshotted from round state for
///     <see cref="FixationRules.SelectTarget"/>. Pure data, no engine types, so selection is directly
///     unit-testable (Content.Tests/_Solreign/FixationRulesTests.cs) — same isolation pattern as
///     <c>ZoneRules</c> and <c>TitleRules</c> elsewhere in _Solreign.
///
///     SPIKE NOTE: the system currently snapshots <c>WantedLevel</c>/<c>Demerits</c> as 0 and
///     <c>Enforcement</c>/<c>InCustody</c> as false (wiring to criminal records, Corporate Standing,
///     job department and cuffed-state is next wave); <c>MinutesOnShift</c> is live.
/// </summary>
/// <param name="WantedLevel">Security/wanted severity (0 = clean record). Dominant criterion.</param>
/// <param name="Demerits">Solreign corporate demerits (negative Standing, missed contracts…).</param>
/// <param name="MinutesOnShift">Minutes since the player spawned into the round.</param>
/// <param name="Alive">Whether the candidate is currently alive (not crit/dead).</param>
/// <param name="InCustody">Already detained — retrieval would be redundant paperwork.</param>
/// <param name="Enforcement">Security/command: compliance ENFORCERS are exempt from retrieval.</param>
public readonly record struct FixationCandidate(
    int WantedLevel,
    int Demerits,
    float MinutesOnShift,
    bool Alive = true,
    bool InCustody = false,
    bool Enforcement = false);

/// <summary>
///     Pure target-selection math for the Compliance Retrieval Unit (Terminator spike). Given a
///     snapshot of the crew, picks exactly ONE fixation target by round-state criteria:
///     most non-compliant first (wanted level, then demerits), longest-on-shift as tie-break
///     ("most overdue for review"), and a caller-supplied uniform roll to break exact ties
///     deterministically. No IoC, no I/O, no randomness of its own.
/// </summary>
public static class FixationRules
{
    /// <summary>
    ///     New joins get a protection window before they can be fixated on — nobody's first ten
    ///     minutes on the station should be a chase scene.
    /// </summary>
    public const float DefaultGraceMinutes = 10f;

    /// <summary>
    ///     Whether a candidate may be fixated on at all: alive, not already detained, not an
    ///     enforcement role, and past the new-join grace window. Negative grace (YAML
    ///     misconfiguration) clamps to zero rather than excluding everyone.
    /// </summary>
    public static bool IsEligible(in FixationCandidate c, float graceMinutes = DefaultGraceMinutes)
    {
        if (!c.Alive || c.InCustody || c.Enforcement)
            return false;

        return c.MinutesOnShift >= MathF.Max(0f, graceMinutes);
    }

    /// <summary>
    ///     Non-compliance score. Wanted level STRICTLY dominates demerits: demerits are clamped into
    ///     [0, 99] before being added to <c>wantedLevel * 100</c>, so no pile of paperwork problems
    ///     can ever outrank a genuinely wanted crew member. Negative inputs (bad upstream data)
    ///     clamp to zero.
    /// </summary>
    public static int ComplianceScore(in FixationCandidate c)
    {
        return Math.Max(0, c.WantedLevel) * 100 + Math.Clamp(c.Demerits, 0, 99);
    }

    /// <summary>
    ///     Picks the single fixation target from <paramref name="candidates"/>.
    ///     Returns the winning index, or -1 if no candidate is eligible (the unit stays dormant and
    ///     the system re-polls later).
    ///
    ///     Ordering: highest <see cref="ComplianceScore"/> wins; ties fall to the longest
    ///     <see cref="FixationCandidate.MinutesOnShift"/>; remaining exact ties are broken by
    ///     <paramref name="roll"/> (a uniform sample in [0, 1], e.g. <c>IRobustRandom.NextDouble()</c>).
    ///     Out-of-range rolls clamp into [0, 1]; a roll of exactly 1.0 selects the last tied
    ///     candidate rather than indexing out of bounds.
    /// </summary>
    public static int SelectTarget(
        IReadOnlyList<FixationCandidate> candidates,
        double roll,
        float graceMinutes = DefaultGraceMinutes)
    {
        var bestScore = int.MinValue;
        var bestShift = float.MinValue;
        var any = false;

        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            if (!IsEligible(in c, graceMinutes))
                continue;

            any = true;
            var score = ComplianceScore(in c);

            if (score > bestScore || (score == bestScore && c.MinutesOnShift > bestShift))
            {
                bestScore = score;
                bestShift = c.MinutesOnShift;
            }
        }

        if (!any)
            return -1;

        // Second pass: collect exact ties on (score, shift), then let the roll pick among them.
        var tied = new List<int>();
        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            if (!IsEligible(in c, graceMinutes))
                continue;

            if (ComplianceScore(in c) == bestScore && c.MinutesOnShift == bestShift)
                tied.Add(i);
        }

        var r = Math.Clamp(roll, 0d, 1d);
        var idx = (int) (r * tied.Count);
        if (idx >= tied.Count)
            idx = tied.Count - 1; // roll == 1.0 edge

        return tied[idx];
    }

    /// <summary>
    ///     Whether the unit must drop its current fixation and re-poll: the target died, is already
    ///     in custody (mission accomplished by other means), or left the station entirely.
    ///     A Retrieval Unit never idles on a solved problem.
    /// </summary>
    public static bool ShouldRetarget(bool targetAlive, bool targetInCustody, bool targetOnStation)
    {
        return !targetAlive || targetInCustody || !targetOnStation;
    }

    /// <summary>Whether the unit is still EMP-staggered at <paramref name="curTime"/>.</summary>
    public static bool IsStaggered(TimeSpan curTime, TimeSpan staggeredUntil)
    {
        return curTime < staggeredUntil;
    }

    /// <summary>
    ///     Computes when an EMP stagger ends. A negative duration (YAML misconfiguration) clamps to
    ///     zero rather than scheduling into the past.
    /// </summary>
    public static TimeSpan StaggerUntil(TimeSpan curTime, TimeSpan duration)
    {
        return curTime + (duration > TimeSpan.Zero ? duration : TimeSpan.Zero);
    }
}
