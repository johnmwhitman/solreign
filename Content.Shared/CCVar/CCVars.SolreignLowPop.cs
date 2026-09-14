using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
///     Low-pop lobby reminder CVars (churn-mitigation lane: reminds a newly-joined player to ready up
///     and reassures them that a quiet-shift round still counts toward Season Ledger standing). Split
///     into its own file rather than appending to <c>CCVars.Solreign.cs</c> or another fork CVar file —
///     same D0 collision-control guidance those files already follow: a narrowly-scoped CVar gets its
///     own file so concurrent worktrees don't collide on the same source lines.
/// </summary>
public sealed partial class CCVars
{
    /// <summary>
    ///     Kill switch for the low-pop lobby reminder
    ///     (<c>Content.Server._Solreign.LowPop.LowPopLobbyReminderSystem</c>). Default on. Purely a
    ///     private, text-only chat message to the joining player — never gameplay-affecting, never
    ///     blocks/replaces ahelp, admin bwoink, or the vanilla lobby join flow.
    /// </summary>
    public static readonly CVarDef<bool> SolreignLowPopLobbyReminderEnabled =
        CVarDef.Create("solreign.lowpop.lobby_reminder_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Only remind a joining player when the server's current connected player count is at or below
    ///     this many players — the reminder is aimed squarely at the empty-lobby churn case (a solo or
    ///     near-solo newcomer who might not know to ready up, or might assume a quiet round "doesn't
    ///     count"). At normal-to-high pop the vanilla lobby UI and other players already make this
    ///     obvious, so the reminder stays silent rather than adding noise.
    /// </summary>
    public static readonly CVarDef<int> SolreignLowPopLobbyReminderThreshold =
        CVarDef.Create("solreign.lowpop.lobby_reminder_threshold", 6, CVar.SERVERONLY);
}
