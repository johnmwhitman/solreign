using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
///     PROVIDENCE event-reactive CVars (feat/providence-event-reactive, PROVIDENCE-VOICE-DESIGN.md),
///     split into its own partial file rather than appending to <c>CCVars.Solreign.cs</c> or any of
///     the existing Providence-adjacent partial files — same D0 collision-control guidance those files
///     already follow: Providence edits are a hotspot across concurrent worktrees, so a new,
///     narrowly-scoped CVar gets its own file.
/// </summary>
public sealed partial class CCVars
{
    /// <summary>
    ///     Kill switch for PROVIDENCE's event-reactive dispatch
    ///     (<c>Content.Server._Solreign.Providence.ProvidenceEventReactiveSystem</c>). Default ON as
    ///     part of the reviewed activation wave: event-sourced PA commentary reads real round events
    ///     and eligible persisted Ledger history from a different round identity. Independent of the base
    ///     <see cref="SolreignProvidenceEnabled"/> voice switch (this system never selects a Providence
    ///     voice-pack category; its authored copy is text, while the global PA requests the generic
    ///     announcement cue) and of every other Providence sub-feature CVar. Flipping this off remains
    ///     the immediate per-feature rollback.
    /// </summary>
    public static readonly CVarDef<bool> SolreignProvidenceReactiveEnabled =
        CVarDef.Create("solreign.providence.reactive_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Base anti-spam cooldown (seconds) shared across every PROVIDENCE event-reactive line —
    ///     scaled per-priority by <c>ProvidenceReactiveDispatchGate.EffectiveCooldown</c> (High halves
    ///     it, Low doubles it, Normal uses it unscaled). Conservative default (90s): the whole point of
    ///     this lane is fixing a voice that fires too often on nothing, not adding a second timer that
    ///     does the same thing on something. A non-positive value collapses every tier to zero
    ///     (fires every time) rather than throwing or inverting — see
    ///     <c>ProvidenceReactiveDispatchGate.EffectiveCooldown</c>'s clamp.
    /// </summary>
    public static readonly CVarDef<float> SolreignProvidenceReactiveCooldownSeconds =
        CVarDef.Create("solreign.providence.reactive_cooldown_seconds", 90f, CVar.SERVERONLY);
}
