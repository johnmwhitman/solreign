using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
///     Consumer-level switches for FX Language v1. These are separate from the master switch so
///     each presentation bridge can be canaried and rolled back independently.
/// </summary>
public sealed partial class CCVars
{
    /// <summary>
    ///     Enables applied-damage and successful-electrocution consumers. Enabled by the
    ///     2026-07-25 activation pass; remains independently kill-switchable.
    ///     The independent <see cref="SolreignFxCueV1Enabled"/> master gate must also be enabled.
    /// </summary>
    public static readonly CVarDef<bool> SolreignFxWorldFeedbackV1Enabled =
        CVarDef.Create("solreign.fx.world_feedback_v1", true, CVar.SERVERONLY);

    /// <summary>
    ///     Enables aggregate preactivation observation without enabling cue delivery. Ships
    ///     dormant and server-only; it never grants authority to publish metrics externally.
    ///     (Delivery above stays TRUE per the 2026-07-25 activation pass — this merge adds
    ///     the observe switch WITHOUT re-darkening delivery.)
    /// </summary>
    public static readonly CVarDef<bool> SolreignFxWorldFeedbackObserveEnabled =
        CVarDef.Create("solreign.fx.world_feedback_observe", true, CVar.SERVERONLY);
}
