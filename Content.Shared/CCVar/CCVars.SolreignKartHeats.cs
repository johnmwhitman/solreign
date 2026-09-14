using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
/// Server-owned controls for repeatable SOLREIGN kart heats.
/// </summary>
public sealed partial class CCVars
{
    /// <summary>
    /// Allows a finished kart to begin a fresh timed heat when a different driver straps in.
    /// Ships disabled so existing one-race-per-kart behavior remains unchanged until reviewed.
    /// </summary>
    public static readonly CVarDef<bool> SolreignKartRepeatableHeatsEnabled =
        CVarDef.Create("solreign.kart_repeatable_heats_enabled", false, CVar.SERVERONLY);
}
