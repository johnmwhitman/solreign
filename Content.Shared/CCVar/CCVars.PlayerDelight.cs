using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     First-Shift Wingmates: pairs a newcomer with a veteran who has a private radio channel
    ///     to guide them, and gates the in-world beacon that opens the pairing UI.
    ///
    ///     ENABLED 2026-07-25. This was the single largest piece of finished-but-unreachable work
    ///     in the fork: the system is ~23k of working code, the beacon is placed on all 7 rotation
    ///     maps, the jobs and private radio channel shipped this week, 93 locale strings exist, and
    ///     the whole thing was switched off. Players on Reddit are explicitly asking for tutorials
    ///     and new-player help; this is that feature, already built. Shipping it dark was the
    ///     false-inventory trap in miniature.
    /// </summary>
    public static readonly CVarDef<bool> SolreignWingmatesEnabled =
        CVarDef.Create("solreign.wingmates_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Career tours a player must have before auto guide-eligibility (see
    ///     <c>WingmateSystem.HasEnoughCareerTours</c>). ALIVENESS P1 #6 (launch-weeks cold-start):
    ///     the original default of 10 meant a FRESH server had zero possible guides for ~10 shifts
    ///     — even at pop 2-5 nobody could volunteer without a per-round moderator
    ///     <c>wingmateapprove</c>, so the pairing feature was structurally dead exactly when it
    ///     mattered most. Default lowered to 1 (one completed tour: you've seen a shift, you can
    ///     show someone a shift) for the launch weeks. RE-RAISE PLAN: once the server has a stable
    ///     returning population (a bench of 10+-tour veterans actually exists), raise this back
    ///     toward 10 in LIVE CONFIG (server.toml) — the live override wins over this code default,
    ///     so the re-raise needs no cut. Moderator approve/revoke keeps overriding in either
    ///     posture.
    /// </summary>
    public static readonly CVarDef<int> SolreignWingmatesMinimumShifts =
        CVarDef.Create("solreign.wingmates_minimum_shifts", 1, CVar.SERVERONLY);

    /// <summary>
    ///     The guided First-Shift assignment track the beacon hands a newcomer: a short, ordered
    ///     set of concrete tasks with in-UI advancement, i.e. the interactive tutorial.
    ///
    ///     ENABLED 2026-07-25 alongside <see cref="SolreignWingmatesEnabled"/> — the beacon opens
    ///     the pairing UI, and this is what it gives someone who has nobody to pair with yet, which
    ///     is the common case at low population. Enabling one without the other would hand a
    ///     newcomer a beacon that shrugs.
    /// </summary>
    public static readonly CVarDef<bool> SolreignFirstShiftAssignmentsEnabled =
        CVarDef.Create("solreign.first_shift_assignments_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     The first-spawn First Shift push-prompt (<c>FirstShiftSpawnPromptSystem</c>): once per
    ///     account ever, a Tours == 0 newcomer gets one private popup + chat pointer toward the
    ///     Wingmate beacon. Built 2026-08-02 because onboarding was 100% pull-based — every other
    ///     First Shift trigger is beacon-UI-scoped, so a newcomer who never found the beacon never
    ///     learned the tutorial existed, and real arrivals bounced exactly that way.
    ///
    ///     Layered kill switch (the spawn-prompt family convention — cf.
    ///     <c>solreign.providence.first_shift_welcome</c> over <c>welcome_enabled</c>): the system
    ///     gates on this AND <see cref="SolreignFirstShiftAssignmentsEnabled"/>, because a prompt
    ///     pointing at a beacon that shrugs is worse than silence.
    /// </summary>
    public static readonly CVarDef<bool> SolreignFirstShiftSpawnPrompt =
        CVarDef.Create("solreign.first_shift_spawn_prompt", true, CVar.SERVERONLY);
}
