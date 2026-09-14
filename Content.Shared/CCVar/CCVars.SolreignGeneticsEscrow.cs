using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
///     CVar threshold definitions for SR-W-068: Solreign Genetics Mutation &amp; DNA Sample Escrow System.
/// </summary>
public sealed partial class CCVars
{
    /// <summary>
    ///     Kill switch for the Solreign Genetics Mutation &amp; DNA Sample Escrow System.
    ///     When disabled, escrow submissions are rejected and sequencing processing is inert.
    /// </summary>
    public static readonly CVarDef<bool> SolreignGeneticsEscrowEnabled =
        CVarDef.Create("solreign.genetics_escrow_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Base deposit (credits) required per DNA sample escrow submission.
    /// </summary>
    public static readonly CVarDef<int> SolreignGeneticsEscrowBaseDeposit =
        CVarDef.Create("solreign.genetics_escrow_base_deposit", 100, CVar.SERVERONLY);

    /// <summary>
    ///     Radiation hazard threshold (rads) above which mutation stability degrades and sample hazard increases.
    /// </summary>
    public static readonly CVarDef<float> SolreignGeneticsEscrowRadThreshold =
        CVarDef.Create("solreign.genetics_escrow_rad_threshold", 50.0f, CVar.SERVERONLY);

    /// <summary>
    ///     Minimum mutation stability ratio required for partial/full escrow payout eligibility (0.0 to 1.0).
    /// </summary>
    public static readonly CVarDef<float> SolreignGeneticsEscrowMinStability =
        CVarDef.Create("solreign.genetics_escrow_min_stability", 0.4f, CVar.SERVERONLY);
}
