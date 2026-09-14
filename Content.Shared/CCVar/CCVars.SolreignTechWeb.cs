using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     SR-W-063: Master kill switch for Solreign Research Tech Web & Department Point Escrow System.
    ///     When true (default), experimental node unlocking, research point generation, and department milestone rewards are active.
    /// </summary>
    public static readonly CVarDef<bool> SolreignTechWebEnabled =
        CVarDef.Create("solreign.tech_web_enabled", true, CVar.SERVERONLY);
}
