using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Let PROVIDENCE report power/atmospherics conditions the diagnostics detect. Without this
    ///     the diagnostic systems compute conditions and raise events that nothing consumes, so no
    ///     player ever learns anything from them. Default ON.
    /// </summary>
    public static readonly CVarDef<bool> SolreignDiagnosticVoiceEnabled =
        CVarDef.Create("solreign.diagnostic_voice_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Minimum seconds between diagnostic announcements. Conditions can flap across a threshold
    ///     repeatedly on a lightly-crewed station, and an AI repeating the same brownout reads as
    ///     broken. Floored at 30s in code.
    /// </summary>
    public static readonly CVarDef<int> SolreignDiagnosticVoiceCooldownSeconds =
        CVarDef.Create("solreign.diagnostic_voice_cooldown_seconds", 180, CVar.SERVERONLY);
}
