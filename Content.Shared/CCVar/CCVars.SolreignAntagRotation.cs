using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Spread antag turns across the roster instead of re-picking the same few accounts.
    ///     Default ON. The gate is capped so it can only reorder who draws antag, never prevent
    ///     antags existing (see SolreignAntagRotationGate). Set false to disable at runtime.
    /// </summary>
    public static readonly CVarDef<bool> SolreignAntagRotationEnabled =
        CVarDef.Create("solreign.antag_rotation_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     How many rounds an account is skipped for after drawing antag. Small on purpose: with a
    ///     handful of regulars a long window would gate everyone, and the cap would just ignore it.
    /// </summary>
    public static readonly CVarDef<int> SolreignAntagRotationCooldownRounds =
        CVarDef.Create("solreign.antag_rotation_cooldown_rounds", 2, CVar.SERVERONLY);
}
