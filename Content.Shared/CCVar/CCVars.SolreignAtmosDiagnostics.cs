using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

// SR-REF-016 SS14 Solreign station atmospheric pressure & oxygen disruption diagnostic system.
// Own partial file per Solreign convention so parallel worktree waves don't collide.
public sealed partial class CCVars
{
    /// <summary>
    ///     Master switch for Solreign atmospheric pressure & oxygen disruption diagnostics.
    ///     Default ON. Activated 2026-07-24 by owner decision (enable rather than
    ///     withhold; reverse at runtime if it misbehaves). Set false to disable.
    /// </summary>
    public static readonly CVarDef<bool> SolreignAtmosDiagnosticsEnabled =
        CVarDef.Create("solreign.atmos_diagnostics_enabled", true, CVar.SERVER);

    /// <summary>
    ///     Target normal room atmospheric pressure in kPa.
    ///     Default 101.3 kPa.
    /// </summary>
    public static readonly CVarDef<float> SolreignAtmosNormalPressurekPa =
        CVarDef.Create("solreign.atmos_normal_pressure_kpa", 101.3f, CVar.SERVER);

    /// <summary>
    ///     Low pressure threshold in kPa below which a pressure warning alert is issued.
    ///     Default 80.0 kPa.
    /// </summary>
    public static readonly CVarDef<float> SolreignAtmosLowPressureThresholdkPa =
        CVarDef.Create("solreign.atmos_low_pressure_threshold_kpa", 80.0f, CVar.SERVER);

    /// <summary>
    ///     Critical pressure threshold in kPa below which a critical pressure drop alert is issued.
    ///     Default 50.0 kPa.
    /// </summary>
    public static readonly CVarDef<float> SolreignAtmosCriticalPressureThresholdkPa =
        CVarDef.Create("solreign.atmos_critical_pressure_threshold_kpa", 50.0f, CVar.SERVER);

    /// <summary>
    ///     Depressurization / vacuum threshold in kPa indicating severe hull breach or station depressurization.
    ///     Default 20.0 kPa.
    /// </summary>
    public static readonly CVarDef<float> SolreignAtmosDepressurizationThresholdkPa =
        CVarDef.Create("solreign.atmos_depressurization_threshold_kpa", 20.0f, CVar.SERVER);

    /// <summary>
    ///     Minimum safe oxygen ratio percentage (0-100%) below which hypoxia warning is issued.
    ///     Default 19.5%.
    /// </summary>
    public static readonly CVarDef<float> SolreignAtmosMinOxygenRatioPercent =
        CVarDef.Create("solreign.atmos_min_oxygen_ratio_percent", 19.5f, CVar.SERVER);

    /// <summary>
    ///     Critical oxygen ratio percentage (0-100%) below which severe hypoxia alert is issued.
    ///     Default 12.0%.
    /// </summary>
    public static readonly CVarDef<float> SolreignAtmosCriticalOxygenRatioPercent =
        CVarDef.Create("solreign.atmos_critical_oxygen_ratio_percent", 12.0f, CVar.SERVER);

    /// <summary>
    ///     Toxic gas ratio percentage (0-100%) threshold at or above which toxic gas presence is flagged.
    ///     Default 0.5%.
    /// </summary>
    public static readonly CVarDef<float> SolreignAtmosToxicGasThresholdPercent =
        CVarDef.Create("solreign.atmos_toxic_gas_threshold_percent", 0.5f, CVar.SERVER);

    /// <summary>
    ///     Rate of pressure drop in kPa/sec threshold for rapid depressurization / hull breach detection.
    ///     Default 10.0 kPa/s.
    /// </summary>
    public static readonly CVarDef<float> SolreignAtmosRapidDropRateThresholdkPaPerSec =
        CVarDef.Create("solreign.atmos_rapid_drop_rate_threshold_kpa_per_sec", 10.0f, CVar.SERVER);
}
