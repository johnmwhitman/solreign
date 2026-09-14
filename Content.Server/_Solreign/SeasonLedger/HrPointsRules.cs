namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     Pure, unit-testable HR Points logic (Beta Feedback 01, Lane B — the HR points system). LJ picked the
///     ALWAYS-CUMULATIVE model over the alternative (reset-per-shift-then-totalled): points accumulate every
///     round and are never zeroed except by a full season bump (the same lever every other Season Ledger
///     column already uses), and are always DISPLAYED from career totals so they read as a running lifetime
///     score, never a per-shift meter.
///
///     Points reward PG-positive account behavior ONLY, reusing existing ledger hooks — no new tracking is
///     introduced:
///       * completing a round at all (a "tour" — <see cref="SeasonLedgerSystem.OnRoundEnd"/>)
///       * completing a Solreign Contract (<see cref="SeasonLedgerSystem.SubmitContractCompletion"/>)
///       * earning a genuinely NEW corporate title (the ceremony gate in <see cref="SeasonLedgerSystem.LoadTitle"/>,
///         see <see cref="TitleRules.IsNewGrant"/>)
///
///     Deliberately NO kill/combat tracking feeds this — the Solreign HR voice rewards compliance and
///     productivity, not violence.
/// </summary>
public static class HrPointsRules
{
    /// <summary>Points awarded for completing a round (a "tour") — the flat per-shift HR bonus.</summary>
    public const int RoundCompletionPoints = 10;

    /// <summary>Points awarded per Solreign Contract completed during the round.</summary>
    public const int ContractCompletionPoints = 5;

    /// <summary>Bonus points awarded the moment a NEW corporate title is earned (ceremony-gated, never a reload/respawn re-fire).</summary>
    public const int TitleEarnedPoints = 25;

    /// <summary>
    ///     HR points earned for a single round: the flat completion bonus plus a per-contract bonus for
    ///     however many Solreign Contracts were completed that round. Never negative — <paramref name="contractsCompleted"/>
    ///     is itself a non-negative accumulator (anti-grief rule 9: completions only ever add), and this
    ///     guards defensively anyway so the HR Points total can never be pushed backward from this call site.
    /// </summary>
    public static int ForRoundCompletion(int contractsCompleted)
    {
        var contracts = contractsCompleted > 0 ? contractsCompleted : 0;
        return RoundCompletionPoints + contracts * ContractCompletionPoints;
    }
}
