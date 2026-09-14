using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public partial class CCVars
{
    public static readonly CVarDef<bool> SolreignSiliconBatteryEnabled =
        CVarDef.Create("solreign.silicon_battery_enabled", true, CVar.SERVERONLY);
}
