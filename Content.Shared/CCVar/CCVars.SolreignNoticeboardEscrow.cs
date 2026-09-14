using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
///     CVars for SR-W-066 Solreign Dynamic Station Noticeboard & Broadcast Escrow System.
/// </summary>
public sealed partial class CCVars
{
    /// <summary>
    ///     Master toggle for the Solreign noticeboard escrow & station broadcast system.
    /// </summary>
    public static readonly CVarDef<bool> SolreignNoticeboardEscrowEnabled =
        CVarDef.Create("solreign.noticeboard_escrow_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Base fee in credits for pinning standard flyers to station noticeboards.
    /// </summary>
    public static readonly CVarDef<int> SolreignNoticeboardEscrowBasePinFee =
        CVarDef.Create("solreign.noticeboard_escrow.base_pin_fee", 50, CVar.SERVERONLY);

    /// <summary>
    ///     Multiplier applied to fees for urgent emergency announcements.
    /// </summary>
    public static readonly CVarDef<float> SolreignNoticeboardEscrowUrgentMultiplier =
        CVarDef.Create("solreign.noticeboard_escrow.urgent_multiplier", 2.5f, CVar.SERVERONLY);

    /// <summary>
    ///     Minimum escrow required for broad-channel station announcements.
    /// </summary>
    public static readonly CVarDef<int> SolreignNoticeboardEscrowBroadcastMinEscrow =
        CVarDef.Create("solreign.noticeboard_escrow.broadcast_min_escrow", 200, CVar.SERVERONLY);

    /// <summary>
    ///     Decay percentage rate per hour for pinned notice visibility/health.
    /// </summary>
    public static readonly CVarDef<float> SolreignNoticeboardEscrowDecayRatePerHour =
        CVarDef.Create("solreign.noticeboard_escrow.decay_rate_per_hour", 10.0f, CVar.SERVERONLY);
}
