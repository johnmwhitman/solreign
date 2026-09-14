using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     SR-W-062: Master kill switch for Solreign Cargo Escrow & Automated Delivery System.
    ///     When true (default), automated shuttle dispatches, credit escrow deposits, and bounty payout calculations are active.
    /// </summary>
    public static readonly CVarDef<bool> SolreignCargoEscrowEnabled =
        CVarDef.Create("solreign.cargo_escrow_enabled", true, CVar.SERVERONLY);
}
