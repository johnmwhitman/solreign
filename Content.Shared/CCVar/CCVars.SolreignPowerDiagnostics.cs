using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

// SR-REF-015 SS14 Solreign station grid load & power disruption diagnostic system.
// Own partial file per Solreign convention so parallel worktree waves don't collide.
public sealed partial class CCVars
{
    /// <summary>
    ///     Master switch for Solreign grid power diagnostics and load-shedding alerts.
    ///     Default ON. Activated 2026-07-24 by owner decision (enable rather than
    ///     withhold; reverse at runtime if it misbehaves). Set false to disable.
    /// </summary>
    public static readonly CVarDef<bool> SolreignPowerDiagnosticsEnabled =
        CVarDef.Create("solreign.power_diagnostics_enabled", true, CVar.SERVER);

    /// <summary>
    ///     Battery reserve headroom percentage threshold (0-100%) below which grid is evaluated in Brownout.
    ///     Default 20.0%.
    /// </summary>
    public static readonly CVarDef<float> SolreignPowerBrownoutHeadroomThreshold =
        CVarDef.Create("solreign.power_brownout_headroom_threshold", 20.0f, CVar.SERVER);

    /// <summary>
    ///     Battery reserve headroom percentage threshold (0-100%) below which grid alert level is Critical.
    ///     Default 5.0%.
    /// </summary>
    public static readonly CVarDef<float> SolreignPowerCriticalHeadroomThreshold =
        CVarDef.Create("solreign.power_critical_headroom_threshold", 5.0f, CVar.SERVER);

    /// <summary>
    ///     Minimum power deficit in kW required to trigger automated load-shedding alerts.
    ///     Default 50.0 kW.
    /// </summary>
    public static readonly CVarDef<float> SolreignPowerLoadSheddingThresholdKw =
        CVarDef.Create("solreign.power_load_shedding_threshold_kw", 50.0f, CVar.SERVER);
}
