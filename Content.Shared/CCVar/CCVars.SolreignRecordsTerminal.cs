using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
///     Solreign Personnel Records Terminal CVar (wave-2 item einstein-016 — mine rank #8), split into
///     its own partial file rather than appending to <c>CCVars.Solreign.cs</c> — the same D0
///     collision-control guidance <c>CCVars.SolreignOnboarding.cs</c>/<c>CCVars.SolreignMark.cs</c>
///     document: a new, narrowly-scoped CVar family gets its own file so a concurrent worktree editing
///     the shared CCVars files never conflicts with this one.
/// </summary>
public sealed partial class CCVars
{
    /// <summary>
    ///     Master switch for the Personnel Records Terminal (the "PROVIDENCE Personnel Records" console
    ///     — a player-reachable read of their own Season Ledger: title, tours, standing, social firsts,
    ///     first-death commemoration, and Mark status). ENABLED 2026-07-25 (activation pass) — activated 2026-07-25; content
    ///     restriction was lifted the night this landed, but activation is still John's call. Flipping
    ///     back off restores today's behavior exactly: the console (if ever spawned) closes any opened
    ///     window immediately and pushes no data — the ledger tables it reads are untouched either way.
    /// </summary>
    public static readonly CVarDef<bool> SolreignRecordsTerminalEnabled =
        CVarDef.Create("solreign.records_terminal.enabled", true, CVar.SERVERONLY);
}
