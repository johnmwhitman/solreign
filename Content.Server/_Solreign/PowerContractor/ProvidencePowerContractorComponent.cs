using Robust.Shared.GameObjects;

namespace Content.Server._Solreign.PowerContractor;

/// <summary>
///     Marks an entity as a live PROVIDENCE power-contractor lease (SPEC-ai-npc-phase1-v2.md §3-§5).
///     Attached programmatically at spawn by <c>ProvidencePowerContractorSystem</c> -- never placed
///     by a mapper, never present on any prototype's default component list except the contractor's
///     own (<c>ProvidencePowerContractor</c>, <c>save: false</c>, §7.3).
///
///     Deliberately data-only. The FSM phase, TTL, and hysteresis state all live in
///     <see cref="ProvidencePowerContractorZone.Gate"/> on the server-side monitor, not here -- this
///     component exists so the system can find its way FROM an entity (e.g. an
///     <c>EntityTerminatingEvent</c>, or a test asserting the spawned entity's identity) BACK TO the
///     owning zone, not to duplicate authority over lease state.
/// </summary>
[RegisterComponent]
public sealed partial class ProvidencePowerContractorComponent : Component
{
    /// <summary>The anchor entity (SMES) whose zone dispatched this contractor -- the key into
    /// <c>ProvidencePowerContractorSystem</c>'s zone dictionary.</summary>
    public EntityUid AnchorUid;
}
