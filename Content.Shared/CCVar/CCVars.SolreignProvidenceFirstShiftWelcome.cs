using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
///     Kill switch for Providence's DELAYED, personally-addressed first-shift beat (player-delight
///     lane, "wow wiring" wave — docs/receipts/wow-wiring/WOW-WIRING-2026-07-16.md) — own partial file
///     rather than appending to <c>CCVars.SolreignProvidenceWelcome.cs</c>, same D0 collision-control
///     guidance that file already documents (Providence is a hotspot across concurrent worktrees).
///
///     Distinct from and independent of <see cref="SolreignProvidenceWelcomeEnabled"/>: that CVar gates
///     the EXISTING immediate, station-wide-VO welcome
///     (<c>Content.Server._Solreign.Providence.ProvidenceWelcomeSystem.OnPlayerSpawnComplete</c>'s
///     unconditional first branch). THIS CVar gates only the additive layer that fires a few seconds
///     later: a private, targeted (not station-wide) VO line plus a chat line addressing the player by
///     character name plus a subtle single-player screen-fx pulse. If the base welcome CVar is off, this
///     layer never schedules in the first place (no hook point to reach); if only this CVar is off, the
///     immediate beat still fires exactly as before and only the delayed personal beat is suppressed.
///
///     <see cref="CVar.REPLICATED"/> per this wave's explicit spec (mirrors
///     <see cref="SolreignMovementBobEnabled"/>'s replicated-kill-switch shape) rather than this codebase's
///     usual <see cref="CVar.SERVERONLY"/> default for Solreign CVars — replicated so a future
///     client-side affordance (e.g. a settings toggle mirroring server state) can read it without a
///     round trip; the server remains authoritative over whether the effects actually fire.
/// </summary>
public sealed partial class CCVars
{
    public static readonly CVarDef<bool> SolreignProvidenceFirstShiftWelcome =
        CVarDef.Create("solreign.providence.first_shift_welcome", true, CVar.REPLICATED | CVar.SERVER);
}
