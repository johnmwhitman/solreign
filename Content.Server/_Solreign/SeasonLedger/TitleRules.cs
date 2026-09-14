using System;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     Accumulated, cross-round statistics for a single account (keyed by NetUserId elsewhere).
///     Pure data — populated from the <see cref="SeasonLedgerStore"/>.
///
///     <c>StandingTotal</c> is the career sum of per-round Corporate Standing handed over from the
///     Corporate Ladder game rule (see <c>SolreignCorporateRuleSystem</c>). Defaulted to 0 so pre-existing
///     four-arg call sites keep compiling.
///
///     <c>ContractsCompleted</c> / <c>ContractScore</c> accumulate Solreign Contract completions (spec §4.1:
///     score is weighted personal=2 / department=1 / chain-final=3). Both are non-negative accumulators
///     (anti-grief rule 9), defaulted to 0 so pre-existing call sites keep compiling. <c>ContractScore</c>
///     joins the rank formula in Milestone 3 (<c>contractScore/3</c>); until then it only accumulates.
///
///     <c>HrPoints</c> is the HR Points system (Beta Feedback 01, Lane B — LJ picked the ALWAYS-CUMULATIVE
///     model over reset-per-shift): a non-negative accumulator fed only by PG-positive actions (round
///     completion, contract completion, earning a new corporate title — see <see cref="HrPointsRules"/>).
///     Never reset by anything except a full season bump (same lever every other column uses), and — unlike
///     <c>Tours</c>/<c>Title</c> — always DISPLAYED from career totals (<c>SeasonLedgerStore.GetCareerStatsAsync</c>)
///     so a season bump never makes an account's earned points visibly vanish. Defaulted to 0 so pre-existing
///     call sites keep compiling.
/// </summary>
public readonly record struct PlayerStats(
    int Tours,
    int CaptainClean,
    int AntagWins,
    int EarlyDeaths,
    int StandingTotal = 0,
    int ContractsCompleted = 0,
    int ContractScore = 0,
    int HrPoints = 0);

/// <summary>
///     What a single completed round contributes to a player's season totals.
///     Emitted by the ledger system at round end and folded into <see cref="PlayerStats"/> by the store.
///
///     <c>Standing</c> is this round's final Corporate Standing for the account (0 if the account scored
///     nothing / no Corporate rule ran). Defaulted to 0 so pre-existing call sites keep compiling.
///
///     <c>HrPointsEarned</c> is this round's HR Points payout (<see cref="HrPointsRules.ForRoundCompletion"/>),
///     folded into the account's cumulative <see cref="PlayerStats.HrPoints"/> by the store. Defaulted to 0
///     so pre-existing call sites keep compiling.
/// </summary>
public readonly record struct RoundContribution(
    bool WasCaptainClean,
    bool AntagWin,
    bool EarlyDeath,
    int RoundId = 0,
    string Gamemode = "",
    int Standing = 0,
    int ContractsCompleted = 0,
    int ContractScore = 0,
    int HrPointsEarned = 0);

/// <summary>
///     One roster member and the complete contribution captured for an atomic round-end envelope.
///     <paramref name="Connected"/> mirrors <c>RoundEndPlayerInfo.Connected</c> — disconnected minds
///     still appear in the GameTicker roster with a GUID, but streak fold must skip them (neither
///     advance nor reset). Defaults true so envelope/recovery call sites that rebuild from contribution
///     alone stay source-compatible; production <c>OnRoundEnd</c> always passes the live flag.
/// </summary>
public readonly record struct RoundEndPlayerRecord(Guid User, RoundContribution Contribution, bool Connected = true);

/// <summary>
///     One completed Solreign Contract, bound for the <c>contract_log</c> audit table (spec §4.2 — the
///     private audit feed). Season id and timestamp are stamped by the store at write time; this is not a public export.
/// </summary>
public readonly record struct ContractLogRecord(
    int RoundId,
    Guid User,
    string ContractId,
    string Scope);

/// <summary>
///     Pure, unit-testable title logic. Given accumulated <see cref="PlayerStats"/>, returns the highest-status
///     earned title (Solreign creative bible, section 3) plus the tour count. No ECS, no I/O.
/// </summary>
public static class TitleRules
{
    /// <summary>Fallback title for a fresh or unremarkable account.</summary>
    public const string DefaultTitle = "Probationary Asset";

    // --- Season-Ledger title expansion thresholds -------------------------------------------------
    // All of the following read off PlayerStats columns the store already tracks and populates every
    // round (see SeasonLedgerStore.AddRoundRecordAsync) — no new accumulator, no schema change. Every
    // threshold is a plain ">=" over a non-negative, only-ever-growing-within-a-season accumulator, so
    // (matching the existing four titles above) a title can never be earned and then silently retracted
    // by anything short of a season bump. Purely cosmetic/status — none of these unlock gameplay power.

    /// <summary>ContractScore floor for <c>Scope Overachiever</c> (spec §4.1 weighted score: personal=2/department=1/chain-final=3).</summary>
    public const int ScopeOverachieverContractScore = 21;

    /// <summary>Season HrPoints floor for <c>Board's Favorite</c>.</summary>
    public const int BoardsFavoriteHrPoints = 150;

    /// <summary>Season Corporate Standing floor for <c>Quarterly Standout</c>.</summary>
    public const int QuarterlyStandoutStanding = 25;

    /// <summary>Solreign Contract completion floor for <c>Chain Closer</c>.</summary>
    public const int ChainCloserContracts = 10;

    /// <summary>Tour floor for <c>Compliant Asset</c> (paired with zero early deaths — see below).</summary>
    public const int CompliantAssetTours = 8;

    /// <summary>
    ///     Computes the earned title for a player. Highest-status condition wins; falls back to
    ///     <see cref="DefaultTitle"/>. <c>Tours</c> simply echoes the accumulated tour count.
    /// </summary>
    public static (string Title, int Tours) Compute(PlayerStats s)
    {
        var title = DefaultTitle;

        // Priority ladder — most prestigious first. First match wins.
        if (s.CaptainClean >= 3)
            // Bible #14: complete rounds as Captain without firing a weapon.
            title = "Brand Ambassador";
        else if (s.AntagWins >= 3)
            // Bible #10: repeated successful corporate "restructuring" (antag wins).
            title = "Restructuring Specialist";
        else if (s.ContractScore >= ScopeOverachieverContractScore)
            // NEW: a season of heavyweight Solreign Contract work — the weighted score (spec §4.1) is a
            // harder bar than raw completions, so this outranks Chain Closer below.
            title = "Scope Overachiever";
        else if (s.HrPoints >= BoardsFavoriteHrPoints)
            // NEW: sustained PG-positive conduct this season (HrPointsRules) — HR notices a repeat performer.
            title = "Board's Favorite";
        else if (s.StandingTotal >= QuarterlyStandoutStanding)
            // NEW: a season of strong Corporate Standing hand-offs from the Corporate Ladder rule.
            title = "Quarterly Standout";
        else if (s.ContractsCompleted >= ChainCloserContracts)
            // NEW: raw Solreign Contract completion volume this season.
            title = "Chain Closer";
        else if (s.EarlyDeaths >= 3)
            // Bible #1: die in the first five minutes of three (consecutive) rounds.
            title = "Amortized Asset";
        else if (s.Tours >= CompliantAssetTours && s.EarlyDeaths == 0)
            // NEW: real tenure with a spotless (zero early-death) record — the "nothing to audit here" asset.
            title = "Compliant Asset";
        else if (s.AntagWins >= 1)
            // NEW: a single successful "restructuring" — below Restructuring Specialist's veteran bar.
            title = "Independent Contractor";
        else if (s.CaptainClean >= 1)
            // NEW: at least one clean captaincy — below Brand Ambassador's repeat-performer bar.
            title = "Poster Child";
        else if (s.Tours >= 5 && s.CaptainClean == 0 && s.AntagWins == 0)
            // Bible #5: a long-tenured asset with nothing notable to show for it.
            title = "Sub-optimal Contributor";

        return (title, s.Tours);
    }

    /// <summary>
    ///     Ceremony gate: true only when a freshly computed title is a genuinely NEW grant — a real earned
    ///     title (never the probationary default) that differs from the last title already announced for
    ///     the account. Respawns/reconnects with an unchanged title stay silent; so does sliding back to
    ///     the default after a season reset. Pure — no ECS, no I/O — so it is unit-testable.
    /// </summary>
    public static bool IsNewGrant(string? previousTitle, string newTitle)
    {
        if (string.IsNullOrEmpty(newTitle) || newTitle == DefaultTitle)
            return false;

        return !string.Equals(previousTitle, newTitle, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The station-wide HR bulletin body for a title ceremony (Solreign brand voice). Pure formatting
    ///     so the copy is unit-testable; the ECS layer (<c>SeasonLedgerSystem.Ceremony</c>) owns dispatch.
    /// </summary>
    public static string FormatCeremonyBulletin(string name, string title)
    {
        return $"SOLREIGN HR BULLETIN: Asset {name} has been designated {title.ToUpperInvariant()}. Compliance is its own reward.";
    }
}
