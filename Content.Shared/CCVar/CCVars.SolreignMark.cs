using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
///     Solreign "the Mark" CVars (docs/specs/MARK-SPEC-2026-07-17-DRAFT.md §3.7), split into their
///     own partial file rather than appending to <c>CCVars.Solreign.cs</c> — the same D0
///     collision-control guidance <c>CCVars.SolreignOnboarding.cs</c> documents: a new,
///     narrowly-scoped CVar family gets its own file so a concurrent worktree editing the shared
///     CCVars files never conflicts with this one.
/// </summary>
public sealed partial class CCVars
{
    /// <summary>
    ///     Master switch for the Mark feature (Continuity Garden fixture, planting verbs, the
    ///     appended first-shift Mark stage, and the return beats — MG-W2..W4 all gate on this).
    ///     ENABLED 2026-07-25 (activation pass) — activated 2026-07-25; John flips it in live config after v13.3 settles.
    ///     Flipping back off restores today's behavior exactly: <c>mark</c> rows persist inertly and
    ///     nothing reads them while off.
    /// </summary>
    public static readonly CVarDef<bool> SolreignMarkEnabled =
        CVarDef.Create("solreign.mark.enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Physical projection capacity: how many recorded marks materialize in the garden each
    ///     round (spec §3.3.4 — records beyond capacity keep their row and get the overflow line;
    ///     seniority order, earliest planted first). At pop 1-5 the default 24 is years of headroom.
    /// </summary>
    public static readonly CVarDef<int> SolreignMarkSlots =
        CVarDef.Create("solreign.mark.slots", 24, CVar.SERVERONLY);

    /// <summary>
    ///     Sub-gate for the private return/nudge beats only (spec §3.5). Off: the garden, planting,
    ///     and the always-current examine surface stay live; PROVIDENCE just stops delivering the
    ///     growth and first-return lines.
    /// </summary>
    public static readonly CVarDef<bool> SolreignMarkReturnBeatEnabled =
        CVarDef.Create("solreign.mark.return_beat_enabled", true, CVar.SERVERONLY);
}
