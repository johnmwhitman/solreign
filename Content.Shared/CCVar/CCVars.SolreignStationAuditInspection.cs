using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
///     "Station Audit Inspection" CVars — the mandatory same-session PROVIDENCE consequence layer
///     bolted onto the already-shipped Station Audit report (see
///     <c>Content.Server._Solreign.StationAudits.StationAuditSystem</c> /
///     <c>StationAuditSystem.Inspection.cs</c>). Split into its own partial file rather than appending
///     to <c>CCVars.SolreignStationAudit.cs</c>, same collision-control idiom as
///     <c>CCVars.SolreignStationDirective.cs</c>: a new, narrowly-scoped CVar group gets its own file so
///     concurrent worktrees touching Solreign CVars don't collide on one shared file.
///
///     Council mandate this layer exists to satisfy (docs/council/2026-07-17-v14-content-adjudication.md,
///     Pope's audit-destination test): completed Station Audits must produce a PROVIDENCE consequence
///     (commendation, escalation, a quietly corrected error) within the same session, not just a filed
///     report.
/// </summary>
public sealed partial class CCVars
{
    /// <summary>
    ///     Kill switch for the self-graded inspection + PROVIDENCE consequence layer. Enabled 2026-07-25 (activation pass),
    ///     independent of <see cref="SolreignStationAuditEnabled"/> (the base audit/report/ledger-row
    ///     system, already shipped) — this lets ops flip the base report on without the newer, less-proven
    ///     consequence layer, and flip the consequence layer off instantly if PROVIDENCE's reaction
    ///     misfires, without losing the underlying shift report. Re-checked at both the assignment point
    ///     (round start) and the checkpoint fire point (mid-flight-off guarantee).
    /// </summary>
    public static readonly CVarDef<bool> SolreignStationAuditInspectionEnabled =
        CVarDef.Create("solreign.station_audit.inspection_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     How many criteria PROVIDENCE assigns per shift from the catalog. Clamped to
    ///     [0, catalog.Count] at selection time; a misconfigured 0 degenerates to "assign nothing,
    ///     consequence layer no-ops this round" rather than throwing.
    /// </summary>
    public static readonly CVarDef<int> SolreignStationAuditInspectionCount =
        CVarDef.Create("solreign.station_audit.inspection_count", 2, CVar.SERVERONLY);

    /// <summary>
    ///     Elapsed shift-minutes at which the mid-shift checkpoint self-grades the assigned criteria and
    ///     fires the mandatory same-session PROVIDENCE consequence. A round that ends before this mark
    ///     fires the same logic inline at round end instead (see
    ///     <c>StationAuditSystem.Inspection.cs</c>'s round-end fallback) so a same-session consequence
    ///     still lands even on a short shift.
    /// </summary>
    public static readonly CVarDef<int> SolreignStationAuditCheckpointMinutes =
        CVarDef.Create("solreign.station_audit.checkpoint_minutes", 15, CVar.SERVERONLY);

    /// <summary>
    ///     Flat HR Points bonus PROVIDENCE awards on a Commendation-kind consequence (checkpoint or
    ///     round-end fallback). Reuses the already-shipped, always-cumulative
    ///     <c>SeasonLedgerStore.AwardHrPointsAsync</c> — zero new payout plumbing. 0 disables the
    ///     mechanical reward while keeping the narrative one (the PA announcement still fires).
    /// </summary>
    public static readonly CVarDef<int> SolreignStationAuditCommendationHrPoints =
        CVarDef.Create("solreign.station_audit.commendation_hr_points", 5, CVar.SERVERONLY);
}
