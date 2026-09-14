using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
///     "Station Directive" CVars — the per-round Solreign HR corporate goal + PA ticker (see
///     <c>Content.Server._Solreign.StationDirective.StationDirectiveRuleSystem</c>). Split into its own
///     partial file rather than appending to <c>CCVars.Solreign.cs</c>, same collision-control idiom as
///     <c>CCVars.SolreignProvidenceWelcome.cs</c>: a new, narrowly-scoped CVar pair gets its own file so
///     concurrent worktrees touching Solreign CVars don't collide on one shared file.
/// </summary>
public sealed partial class CCVars
{
    /// <summary>
    ///     Kill switch for the Station Directive layer (<c>StationDirectiveLayerSystem</c>). Default on.
    ///     Cosmetic/flavor only — the same "additive, zero-core-edit" idiom as the Corporate Ladder and
    ///     Providence welcome beat. Checked once at round start; flipping this mid-round does not retract
    ///     an already-started directive for the current round.
    /// </summary>
    public static readonly CVarDef<bool> SolreignStationDirectiveEnabled =
        CVarDef.Create("solreign.station_directive.enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Seconds between Station Directive PA ticker lines. Deliberately conservative (default 5
    ///     minutes) — this is atmospheric flavor, not a channel that should compete with real
    ///     announcements. Read live via <c>Subs.CVar</c>, so an admin can retune cadence mid-round.
    /// </summary>
    public static readonly CVarDef<float> SolreignStationDirectiveTickerIntervalSeconds =
        CVarDef.Create("solreign.station_directive.ticker_interval_seconds", 300f, CVar.SERVERONLY);
}
