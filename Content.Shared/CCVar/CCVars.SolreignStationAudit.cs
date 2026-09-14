using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
///     "Station Audit" CVars — PROVIDENCE's end-of-shift deadpan corporate assessment (see
///     <c>Content.Server._Solreign.StationAudits.StationAuditSystem</c>). Split into its own partial
///     file rather than appending to <c>CCVars.Solreign.cs</c>, same collision-control idiom as
///     <c>CCVars.SolreignStationDirective.cs</c>: a new, narrowly-scoped CVar gets its own file so
///     concurrent worktrees touching Solreign CVars don't collide on one shared file.
/// </summary>
public sealed partial class CCVars
{
    /// <summary>
    ///     Kill switch for Station Audits. Enabled 2026-07-25 (activation pass) (council precondition, v14 wave-1 #3): the
    ///     feature must ship dormant. When true, PROVIDENCE compiles an end-of-shift audit from real
    ///     observed round state (shift duration, crew count, deaths, directive on file if any,
    ///     stipends/bounty activity if those systems are enabled, notable-event count, a
    ///     tracked-stats "Commendation of the Shift", and a fictional deadpan "Item of Concern"),
    ///     prints a keepsake paper at a station comms console, appends the text to the round-end
    ///     summary screen, and appends a compact row to the <c>station_audit_log</c> Season Ledger
    ///     table. Re-checked at fire time (mid-flight-CVar-off guarantee) — flipping this off stops
    ///     the very next round-end append, paper spawn, and ledger row.
    /// </summary>
    public static readonly CVarDef<bool> SolreignStationAuditEnabled =
        CVarDef.Create("solreign.station_audit.enabled", true, CVar.SERVERONLY);
}
