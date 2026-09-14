using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
///     "Directives Fax" CVars (v14 wave-1 item #1, council C1 einstein-001 "ship first"). Split into
///     its own partial file rather than appending to <c>CCVars.Solreign.cs</c> — the same D0
///     collision-control guidance <c>CCVars.SolreignMark.cs</c>/<c>CCVars.SolreignOnboarding.cs</c>
///     document: a new, narrowly-scoped CVar family gets its own file so a concurrent worktree editing
///     the shared CCVars files never conflicts with this one.
/// </summary>
public sealed partial class CCVars
{
    /// <summary>
    ///     Master switch for the Directives Fax feature (round-start PROVIDENCE fax with the shift's
    ///     Station Directive + completable clauses, persistent compliance streaks, and the round-end
    ///     outcome line). ENABLED 2026-07-25 (activation pass) — dormant by council precondition; someone with authority flips
    ///     this on after review. Flipping back off restores today's behavior exactly: the game rule
    ///     never starts, no fax prints, no clauses evaluate, and the Season Ledger's
    ///     <c>directives_fax_streak</c> rows sit inert (nothing reads or writes them while off).
    /// </summary>
    public static readonly CVarDef<bool> SolreignDirectivesFaxEnabled =
        CVarDef.Create("solreign.directives_fax.enabled", true, CVar.SERVERONLY);
}
