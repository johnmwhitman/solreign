using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
///     Quest-Giver/Bounty-Board extension CVars (v14 content pick, deltav-003 + frontier-010
///     convergence) — the small, additive, low-pop/streak layer on top of the already-live, always-on
///     Solreign Contracts system (Content.Server/_Solreign/Contracts/). Contracts itself has no CVar
///     gate and doesn't need one; these gate ONLY the new extension pieces (low-pop guaranteed-easy
///     contract, the persistent contracts streak, and the lobby-reminder pointer). Split into its own
///     file per the CCVars.Solreign.cs/SolreignDirectivesFax.cs collision-control convention — a
///     narrowly-scoped CVar family gets its own file so concurrent worktrees don't collide on the same
///     source lines.
/// </summary>
public sealed partial class CCVars
{
    /// <summary>
    ///     Master switch for the low-pop guarantee + persistent contracts streak + lobby-reminder
    ///     pointer. Default ON activates this extension while retaining a runtime kill switch.
    ///     Flipping it off restores today's Contracts behavior exactly: no guaranteed easy-tier
    ///     force-issue, no streak reads/writes (contracts_streak rows sit inert), and the lobby
    ///     reminder reverts to its original 3-line pool with no Contracts-Board mention.
    /// </summary>
    public static readonly CVarDef<bool> SolreignContractsQuestBoardEnabled =
        CVarDef.Create("solreign.contracts_lowpop.enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Player-count threshold for the bounded low-pop easy-contract re-arm: at or below this many
    ///     connected players, the extension keeps an open <c>EasyTier</c> contract while the hard
    ///     MaxPersonal+2 bound has room. Mirrors SolreignLowPopLobbyReminderThreshold's own default order
    ///     of magnitude (that CVar defaults to 6; this one is intentionally tighter — the guarantee is
    ///     meant for the genuinely solo/near-solo case, not "merely quiet").
    /// </summary>
    public static readonly CVarDef<int> SolreignContractsLowPopThreshold =
        CVarDef.Create("solreign.contracts_lowpop.threshold", 4, CVar.SERVERONLY);
}
