using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
///     UX-SIMPLE FIX 1: posture switch for Wingmates guide eligibility. Split into its own partial
///     file rather than appending to <c>CCVars.PlayerDelight.cs</c> — same collision-control
///     rationale as <c>CCVars.SolreignProvidenceWelcome.cs</c> (Wingmates is a hotspot across
///     concurrent worktrees).
///
///     Before this CVar existed, the ONLY way for a player to become guide-eligible was a
///     moderator running <c>wingmateapprove &lt;player&gt;</c> — with no in-game explanation when
///     that hadn't happened, pairing was structurally impossible for two ordinary players (see
///     DIAG-ORACLE-WINGMATE-UX-2026-07-16.md, Investigation B). Default TRUE per the owner's
///     standing enable-max ruling: an account meeting the basic automatic criteria
///     (<c>WingmateSystem.MeetsAutoEligibilityCriteria</c> — alive, not a ghost/observer, and at
///     or above <see cref="CCVars.SolreignWingmatesMinimumShifts"/> career shifts) is
///     automatically guide-eligible with no moderator action required.
///
///     Flip to FALSE to restore the pre-fix MODERATED posture (guide eligibility requires an
///     explicit <c>wingmateapprove</c>) — e.g. for a low-pop canary with an admin present, or if a
///     moderation policy later requires a human gate on who can be alone in a teaching role with a
///     newcomer. <c>wingmateapprove</c>/<c>wingmaterevoke</c> keep working as an
///     override/revoke path in EITHER posture: a moderator can always approve someone the auto
///     gate would reject (e.g. below the shift floor) or revoke someone the auto gate would
///     otherwise keep re-granting.
///
///     REPLICATED so the client can render an accurate "why can't I volunteer" explanation
///     (<c>wingmates-guide-ineligible-manual</c> vs the auto-mode-specific reasons) without a
///     round trip; SERVER because only the server's posture is authoritative (the client cannot
///     set this).
/// </summary>
public sealed partial class CCVars
{
    public static readonly CVarDef<bool> SolreignWingmatesAutoGuideEligibility =
        CVarDef.Create("solreign.wingmates.auto_guide_eligibility", true, CVar.REPLICATED | CVar.SERVER);
}
