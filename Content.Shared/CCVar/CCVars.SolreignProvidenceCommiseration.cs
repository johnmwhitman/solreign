using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
///     Providence death-commiseration CVars (roadmap D2.2), split into its own partial file rather than
///     appending to the shared <c>CCVars.Solreign.cs</c>. Per the fleet workboard's D0 collision-control
///     guidance ("prefer a separate partial CVar file over colliding with held AGX changes in
///     CCVars.Solreign.cs — Map and Providence edits are hotspots"), a new Providence-adjacent CVar
///     lives here instead, so a concurrent worktree editing CCVars.Solreign.cs never conflicts with
///     this one on the same file.
/// </summary>
public sealed partial class CCVars
{
    /// <summary>
    ///     Kill switch for Providence's death commiseration line
    ///     (<c>Content.Server._Solreign.Providence.ProvidenceCommiserationSystem</c>). Default on.
    ///     Independent of the base <see cref="SolreignProvidenceEnabled"/> voice switch, but the system
    ///     also no-ops whenever that switch is off — this CVar exists so operators can silence just the
    ///     commiseration flavor beat without disabling shift-start/shift-end/idle-musings/etc.
    /// </summary>
    public static readonly CVarDef<bool> SolreignProvidenceCommiserationEnabled =
        CVarDef.Create("solreign.providence.commiseration_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Anti-fatigue probability (0-1) that an eligible death fires the commiseration line, on top of
    ///     the hard once-per-player-per-round cap enforced in code
    ///     (<c>ProvidenceCommiserationGate.CanFire</c>/<c>MarkFired</c>). Conservative default (50%) so
    ///     most deaths pass silently and the line reads as a rare, special beat rather than a scripted
    ///     death-screen ritual. Values outside [0, 1] are clamped defensively at the call site
    ///     (<c>ProvidenceCommiserationGate.ShouldFire</c>), so a misconfigured value fails toward "fires
    ///     less often", never toward a crash or a guaranteed/negative-odds fire.
    /// </summary>
    public static readonly CVarDef<float> SolreignProvidenceCommiserationChance =
        CVarDef.Create("solreign.providence.commiseration_chance", 0.5f, CVar.SERVERONLY);
}
