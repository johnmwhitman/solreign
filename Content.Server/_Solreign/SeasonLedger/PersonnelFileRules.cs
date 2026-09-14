namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     Pure, unit-testable formatting rules for the Season Ledger's on-examine "personnel file" —
///     the discovery-delight flourish players get when they examine a crewmember with ledger history.
///     No ECS, no I/O: every input is already stamped on <c>SeasonTitleComponent</c> by
///     <see cref="SeasonLedgerSystem.LoadTitle"/>, so this never touches the DB and adds no new schema.
/// </summary>
public static class PersonnelFileRules
{
    /// <summary>
    ///     True if the account has ANY ledger history worth surfacing — a non-zero season tour count
    ///     (<see cref="SeasonTitleComponent.Tours"/>), a career rank above the probationary floor
    ///     (<see cref="SeasonTitleComponent.RankIndex"/>), or any lifetime HR Points
    ///     (<see cref="SeasonTitleComponent.HrPoints"/>). All three are already tracked, non-negative,
    ///     only-ever-growing accumulators (see <see cref="TitleRules"/> / <see cref="RankRules"/> /
    ///     <see cref="HrPointsRules"/>), so a genuinely fresh account — one that has never completed a
    ///     round in any season — is the only case where all three read zero.
    /// </summary>
    public static bool HasRecord(int tours, int rankIndex, int hrPoints)
    {
        return tours > 0 || rankIndex > 0 || hrPoints > 0;
    }

    /// <summary>
    /// A persisted cosmetic grant is itself visible standing, even before the account completes a tour.
    /// </summary>
    public static bool ShouldDisplay(
        int tours,
        int rankIndex,
        int hrPoints,
        bool hasAdminGrant,
        bool hasGoldenNamePerk)
    {
        return HasRecord(tours, rankIndex, hrPoints) ||
               hasAdminGrant ||
               hasGoldenNamePerk;
    }

    /// <summary>
    ///     A single flavor word for the account's career standing, derived purely from the already-computed
    ///     <see cref="RankRules.CorporateRank"/> index — no new tracking, no schema change. Deliberately a
    ///     different word than the literal rank name (see <c>solreign-personnel-file-detail-examine</c>)
    ///     so the personnel-file line reads as commentary on the rank, not a duplicate of it.
    ///     Not to be confused with the separate "Corporate Standing" per-round mechanic
    ///     (<see cref="PlayerStats.StandingTotal"/>) that game-rule scoring uses — this is purely cosmetic
    ///     copy for the examine flourish.
    /// </summary>
    public static string DescribeCareerStanding(int rankIndex)
    {
        // Clamp defensively: RankIndex is always CorporateRank's 0-10 ordinal in practice, but examine
        // must never throw over a stale/out-of-range value.
        var clamped = rankIndex < 0 ? 0 : rankIndex > 10 ? 10 : rankIndex;

        return clamped switch
        {
            0 => "unverified", // Probationary Asset
            1 => "provisional", // Associate
            2 => "satisfactory", // Senior Associate
            3 => "commendable", // Manager
            4 => "exemplary", // Director
            5 => "distinguished", // Vice President
            6 => "exceptional", // Board Member
            7 => "elite", // Senior Board Member
            8 => "immaculate", // Chief Executive Officer
            9 => "legendary", // Chairman of the Board
            _ => "immortalized", // Founder Emeritus (10)
        };
    }
}
