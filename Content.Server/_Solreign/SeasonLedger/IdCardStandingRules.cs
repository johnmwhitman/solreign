namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     Pure, unit-testable gate for the ID-card/PDA standing line — the surface where OTHER crewmembers
///     glance at a player's Season Ledger standing in-round (badge on their ID, or the PDA holding it),
///     as opposed to <see cref="PersonnelFileRules"/>'s personnel-file flourish, which only fires when
///     someone deliberately examines a crewmate's body. Churn insight: cross-round ledger persistence
///     only changes how OTHER players treat someone THIS round if it's visible somewhere people
///     routinely glance — not buried behind a deliberate "examine crewmate" action.
///
///     No ECS, no I/O: every input is already stamped on <c>SeasonTitleComponent</c> by
///     <see cref="SeasonLedgerSystem.LoadTitle"/>, so this never touches the DB and adds no new schema
///     or tracked stat — it reuses the exact same "has this account done anything" signal the personnel
///     file already uses (<see cref="PersonnelFileRules.HasRecord"/>), so a genuinely fresh account's ID
///     stays a plain, unremarkable card instead of broadcasting "Probationary Asset" to every crewmate
///     who glances at it.
/// </summary>
public static class IdCardStandingRules
{
    /// <summary>
    ///     True if this account's standing is worth stamping onto its ID card at all. Delegates to
    ///     <see cref="PersonnelFileRules.HasRecord"/> so the two surfaces (personnel file, ID badge)
    ///     can never disagree about which accounts count as "on record" — a fresh account with all-zero
    ///     tours/rank/HR-points gets neither.
    /// </summary>
    public static bool ShouldStamp(
        int tours,
        int rankIndex,
        int hrPoints,
        bool hasAdminGrant = false,
        bool hasGoldenNamePerk = false)
    {
        return PersonnelFileRules.ShouldDisplay(
            tours,
            rankIndex,
            hrPoints,
            hasAdminGrant,
            hasGoldenNamePerk);
    }
}
