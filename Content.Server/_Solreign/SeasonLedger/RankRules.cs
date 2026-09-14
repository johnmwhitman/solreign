namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     The corporate ladder an account climbs across its whole career (Solreign creative bible, section 4).
///     Higher index = more prestigious. Purely ordinal — the enum values double as the ascending rank index.
/// </summary>
public enum CorporateRank
{
    ProbationaryAsset = 0,
    Associate = 1,
    SeniorAssociate = 2,
    Manager = 3,
    Director = 4,
    VicePresident = 5,
    BoardMember = 6,

    // Executive tiers (Beta Feedback 01, Lane B — "more corporate ranks, gated on time-played + HR
    // points"). Appended ABOVE the frozen 0-6 ladder rather than spliced in between existing rungs, so no
    // rank a live account already holds ever gets renumbered. Most prestigious last.
    SeniorBoardMember = 7,
    ChiefExecutiveOfficer = 8,
    ChairmanOfTheBoard = 9,
    FounderEmeritus = 10,
}

/// <summary>
///     Pure, unit-testable corporate-rank logic. Given accumulated <see cref="PlayerStats"/>, returns the
///     earned rank via a monotonic career score. No ECS, no I/O.
///
///     Unlike <see cref="TitleRules"/> (which reflects the current season), rank is a career ladder: it is
///     computed from all-time totals and, by construction, can only ever go up — a single bad round must
///     never demote an account (so <c>EarlyDeaths</c> carries zero weight in the score).
/// </summary>
public static class RankRules
{
    /// <summary>Human-readable name for a fresh account, matching the lowest rung of the ladder.</summary>
    public const string DefaultRank = "Probationary Asset";

    /// <summary>How many points of career Corporate Standing are worth one point of rank score.</summary>
    public const int StandingPerScorePoint = 5;

    /// <summary>
    ///     A monotonic career score. Tours are worth 1; clean captaincies and antag wins are worth 2 each
    ///     (they are harder-earned). Career Corporate Standing (kills folded up from the Corporate Ladder rule)
    ///     contributes at <c>StandingTotal / StandingPerScorePoint</c> — a small, non-negative weight: five
    ///     round-standing points buy one rank point, so a productive career compounds into the ladder without
    ///     letting a single big round leapfrog tenure. <c>EarlyDeaths</c> is deliberately weighted 0, and
    ///     <c>StandingTotal</c> is only ever non-negative (the rule awards +1s, never penalties), so a bad
    ///     round can never lower the score, and therefore never demote the account.
    /// </summary>
    public static int Score(PlayerStats s) =>
        s.Tours + 2 * s.CaptainClean + 2 * s.AntagWins + s.StandingTotal / StandingPerScorePoint;

    /// <summary>
    ///     Career tenure floor (Tours) for each executive tier above Board Member. "Time-played" has no
    ///     wall-clock signal in this pure/testable layer — Tours (completed rounds) is the existing
    ///     career-tenure proxy the whole ladder already runs on, so the executive tiers reuse it rather than
    ///     inventing a second notion of tenure.
    /// </summary>
    private const int SeniorBoardMemberTours = 30;
    private const int ChiefExecutiveOfficerTours = 45;
    private const int ChairmanOfTheBoardTours = 65;
    private const int FounderEmeritusTours = 90;

    /// <summary>HR Points floor (see <see cref="HrPointsRules"/>) for each executive tier above Board Member.</summary>
    private const int SeniorBoardMemberPoints = 400;
    private const int ChiefExecutiveOfficerPoints = 700;
    private const int ChairmanOfTheBoardPoints = 1100;
    private const int FounderEmeritusPoints = 1600;

    /// <summary>
    ///     Computes the earned rank for a player. Highest threshold met wins; falls back to
    ///     <see cref="CorporateRank.ProbationaryAsset"/>. Returns the enum, its display name, and its
    ///     ordinal index (identical to the enum value).
    /// </summary>
    public static (CorporateRank Rank, string Name, int Index) Compute(PlayerStats s)
    {
        var score = Score(s);

        // Threshold ladder — most prestigious first. First match wins. UNCHANGED from the original 7-tier
        // ladder: the executive tiers below never lower this bar, only build on top of it.
        var rank =
            score >= 120 ? CorporateRank.BoardMember
            : score >= 80 ? CorporateRank.VicePresident
            : score >= 50 ? CorporateRank.Director
            : score >= 30 ? CorporateRank.Manager
            : score >= 15 ? CorporateRank.SeniorAssociate
            : score >= 5 ? CorporateRank.Associate
            : CorporateRank.ProbationaryAsset;

        // Executive tiers (Beta Feedback 01, Lane B): once an account has already cleared the Board Member
        // score bar, four further tiers open up — each gated on BOTH career tenure (Tours) AND
        // always-cumulative HR Points, so neither axis alone can buy an executive title: a decades-long
        // career with no HR standing stalls at Board Member, and a single HR-Points-heavy round can't vault
        // a fresh account past it either. Most prestigious first, first match wins.
        if (rank == CorporateRank.BoardMember)
        {
            rank =
                s.Tours >= FounderEmeritusTours && s.HrPoints >= FounderEmeritusPoints ? CorporateRank.FounderEmeritus
                : s.Tours >= ChairmanOfTheBoardTours && s.HrPoints >= ChairmanOfTheBoardPoints ? CorporateRank.ChairmanOfTheBoard
                : s.Tours >= ChiefExecutiveOfficerTours && s.HrPoints >= ChiefExecutiveOfficerPoints ? CorporateRank.ChiefExecutiveOfficer
                : s.Tours >= SeniorBoardMemberTours && s.HrPoints >= SeniorBoardMemberPoints ? CorporateRank.SeniorBoardMember
                : rank;
        }

        return (rank, Name(rank), (int) rank);
    }

    /// <summary>Display name for a rank (spaced, title-cased).</summary>
    private static string Name(CorporateRank rank) => rank switch
    {
        CorporateRank.FounderEmeritus => "Founder Emeritus",
        CorporateRank.ChairmanOfTheBoard => "Chairman of the Board",
        CorporateRank.ChiefExecutiveOfficer => "Chief Executive Officer",
        CorporateRank.SeniorBoardMember => "Senior Board Member",
        CorporateRank.BoardMember => "Board Member",
        CorporateRank.VicePresident => "Vice President",
        CorporateRank.Director => "Director",
        CorporateRank.Manager => "Manager",
        CorporateRank.SeniorAssociate => "Senior Associate",
        CorporateRank.Associate => "Associate",
        _ => DefaultRank,
    };
}
