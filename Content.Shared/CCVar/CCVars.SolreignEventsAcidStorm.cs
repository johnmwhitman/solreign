using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
///     Kill switch for the "Acid Storm" station event (wow-wiring wave,
///     docs/receipts/wow-wiring/WOW-WIRING-2026-07-16.md) — own partial file per this codebase's D0
///     collision-control convention (see <c>CCVars.SolreignProvidenceWelcome.cs</c>'s doc comment for
///     the same reasoning: Solreign event/rule CVars are a hotspot across concurrent worktrees).
///
///     Checked at the very top of <see cref="Content.Server._Solreign.StationIdentity.SolreignAcidStormRule"/>'s
///     <c>Started</c> override: this event is admin-startable only (not yet wired into any random
///     event-selection table — same posture as its <c>SolreignSolarFlare</c>/<c>SolreignSporeDrift</c>
///     siblings), so there is no earlier "should this even be allowed to start" decision point upstream
///     of the rule itself to gate at (unlike <c>StationDirectiveLayerSystem</c>, which gates before
///     <c>GameTicker.StartGameRule</c> is ever called). <c>ForceEndSelf</c> immediately ends the rule if
///     this is off, so a disabled event never silently "runs" doing nothing for its full duration in
///     admin tooling.
/// </summary>
public sealed partial class CCVars
{
    public static readonly CVarDef<bool> SolreignAcidStormEnabled =
        CVarDef.Create("solreign.events.acid_storm", true, CVar.SERVERONLY);
}
