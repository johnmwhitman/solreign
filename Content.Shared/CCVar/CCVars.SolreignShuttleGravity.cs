using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
///     CVars for SR-W-067 Solreign Shuttle Autopilot & Artificial Gravity Escrow System.
/// </summary>
public sealed partial class CCVars
{
    /// <summary>
    ///     Master toggle for the Solreign shuttle autopilot & artificial gravity escrow system.
    /// </summary>
    public static readonly CVarDef<bool> SolreignShuttleGravityEnabled =
        CVarDef.Create("solreign.shuttle_gravity_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Base escrow fee in credits required to engage shuttle autopilot navigation.
    /// </summary>
    public static readonly CVarDef<int> SolreignShuttleGravityBaseEscrow =
        CVarDef.Create("solreign.shuttle_gravity.base_escrow", 100, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum allowable autopilot velocity before thruster dampening is recommended.
    /// </summary>
    public static readonly CVarDef<float> SolreignShuttleGravityMaxAutopilotSpeed =
        CVarDef.Create("solreign.shuttle_gravity.max_autopilot_speed", 50.0f, CVar.SERVERONLY);

    /// <summary>
    ///     Velocity threshold (in m/s) above which emergency thruster dampening automatically triggers.
    /// </summary>
    public static readonly CVarDef<float> SolreignShuttleGravityEmergencyDampeningThreshold =
        CVarDef.Create("solreign.shuttle_gravity.emergency_dampening_threshold", 35.0f, CVar.SERVERONLY);

    /// <summary>
    ///     Minimum power ratio (0.0 - 1.0) required to sustain artificial gravity stability.
    /// </summary>
    public static readonly CVarDef<float> SolreignShuttleGravityMinPowerRatio =
        CVarDef.Create("solreign.shuttle_gravity.min_power_ratio", 0.2f, CVar.SERVERONLY);
}
