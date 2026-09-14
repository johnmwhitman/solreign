using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
///     STATION-LIBRARY CVars (wave-2 item, C1 council pick; docs/council/2026-07-17-player-text-
///     safety.md's write-path/report-hide/PROVIDENCE-labeling posture is LAW where applicable —
///     retention deliberately differs, see <see cref="SolreignLibraryEnabled"/>). Split into their
///     own partial file rather than appending to <c>CCVars.Solreign.cs</c> — the same D0
///     collision-control guidance <c>CCVars.SolreignMark.cs</c>/<c>CCVars.SolreignOnboarding.cs</c>
///     document: a new, narrowly-scoped CVar family gets its own file so a concurrent worktree
///     editing the shared CCVars files never conflicts with this one.
/// </summary>
public sealed partial class CCVars
{
    /// <summary>
    ///     Master switch for the Station Archive ("PROVIDENCE Records Annex" — a bookshelf-class
    ///     structure where a player submits a written book/paper that enters the Season Ledger and
    ///     re-materializes as a readable book each round). SHIPS FALSE — dormant by mission rail.
    ///
    ///     <b>THIS FLAG ALONE IS NOT A PRODUCTION GO.</b> The C2 posture memo's write-path
    ///     discipline (submit-time fail-closed classifier screening under a dedicated surface
    ///     budget, generic private rejection that never echoes submitted text, one-tap reversible
    ///     hide-as-containment, PROVIDENCE content clearly labeled and gated through the same
    ///     screen) is LAW for this feature exactly as it is for the crew Noticeboards — the only
    ///     deliberate divergence is retention: a library has NO auto-expiry (persistence is the
    ///     point; removing a work from the ledger is a moderator hide, never a timer). Activating
    ///     this surface in production ahead of the Moderation Constitution's own Section 8 rollout
    ///     gate (staff tabletop drill + dated ACKNOWLEDGMENTS.md record — docs/MODERATION-
    ///     CONSTITUTION.md §8 in the orch-ops repo) would use a documentation gap against the
    ///     Constitution's evident purpose, for the same reason the Noticeboards CVar's doc comment
    ///     gives. Design/implementation/staging-dev testing may proceed with this CVar true in a
    ///     non-production config; flipping it true on the live box is a HUMAN gate — never an
    ///     automated or Claude-initiated flip. Submission additionally requires the master
    ///     <see cref="SolreignDirectorEnabled"/> plus a well-formed <see cref="SolreignDirectorToken"/>
    ///     (the write path's classifier round trip routes through <c>DirectorChannel</c> exactly
    ///     like Bounties/Noticeboards); round-start projection is a local Season Ledger query and
    ///     needs neither.
    /// </summary>
    public static readonly CVarDef<bool> SolreignLibraryEnabled =
        CVarDef.Create("solreign.library.enabled", false, CVar.SERVERONLY);

    /// <summary>
    ///     Physical projection capacity: how many of the most-recent non-hidden works materialize
    ///     as readable books on an Annex's shelf each round (the Continuity Garden/Mark precedent —
    ///     works beyond this cap keep their ledger row; they simply don't get a physical copy this
    ///     round, seniority order, most-recently-submitted first). Default 10 is generous headroom
    ///     for a small population and comfortably fits a <c>Bookshelf</c>-class storage grid without
    ///     visually overflowing it.
    /// </summary>
    public static readonly CVarDef<int> SolreignLibrarySlots =
        CVarDef.Create("solreign.library.slots", 10, CVar.SERVERONLY);
}
