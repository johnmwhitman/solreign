using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
///     Solreign Shift Archive CVars, split into their own partial file rather than appending to
///     <c>CCVars.Solreign.cs</c> — the same D0 collision-control guidance
///     <c>CCVars.SolreignMark.cs</c> documents: a new, narrowly-scoped CVar family gets its own
///     file so a concurrent worktree editing the shared CCVars files never conflicts with this one.
/// </summary>
public sealed partial class CCVars
{
    /// <summary>
    ///     Master switch for the Shift Archive board: a wall console that lists the most recent
    ///     shifts as PROVIDENCE recorded them, straight from the Season Ledger's
    ///     <c>station_audit_log</c> (plus per-round completed-contract counts). ENABLED 2026-07-25 (activation pass) —
    ///     canary law: the board renders an "archive synchronization offline" state until the
    ///     switch is flipped, so map placement and the feature flip are independent steps.
    ///     Off: nothing reads the ledger for archive purposes, zero behavior change from today.
    /// </summary>
    public static readonly CVarDef<bool> SolreignShiftArchiveEnabled =
        CVarDef.Create("solreign.shift_archive.enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     How many recent shifts the board lists, newest first. The read is capped at the store
    ///     (`GetRecentStationAuditsAsync(limit)`); rows beyond the cap are simply never returned.
    ///     8 by default per the archive design brief ("last 8-10 real events") — a longer scroll
    ///     stops reading as "recent history" and starts reading as a log dump.
    /// </summary>
    public static readonly CVarDef<int> SolreignShiftArchiveEntries =
        CVarDef.Create("solreign.shift_archive.entries", 8, CVar.SERVERONLY);
}
