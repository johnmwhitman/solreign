using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
///     "Cross-season World-State Consequences" CVars (SR-W-020). Split into its own partial file per
///     Solreign convention so parallel worktree waves don't collide on shared files.
/// </summary>
public sealed partial class CCVars
{
    /// <summary>
    ///     Kill switch for cross-season world-state consequences (<c>SolreignWorldStateSystem</c>). Default on.
    ///     When false, all versioned world-state decisions and next-season prototype/policy modifications
    ///     are ignored, falling back to base station defaults without mutating persistent decision files.
    /// </summary>
    public static readonly CVarDef<bool> SolreignWorldStateEnabled =
        CVarDef.Create("solreign.world_state_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Relative path to the versioned world-state decisions persistence ledger file.
    /// </summary>
    public static readonly CVarDef<string> SolreignWorldStateFilePath =
        CVarDef.Create("solreign.world_state_file_path", "world_state_decisions.json", CVar.SERVERONLY);
}
