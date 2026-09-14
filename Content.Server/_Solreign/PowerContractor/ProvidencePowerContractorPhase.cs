namespace Content.Server._Solreign.PowerContractor;

/// <summary>
///     The lifecycle FSM from SPEC-ai-npc-phase1-v2.md §5. This is the sole owner of every
///     safety-relevant transition for one zone's lease — an optional decorative HTN layer (§6, not
///     built in this cut) may only ever *request* a transition, never mutate this directly.
/// </summary>
public enum ProvidencePowerContractorPhase
{
    /// <summary>No lease, no live entity. The zone is idle, waiting on a hysteresis-confirmed gap.</summary>
    Dormant,

    /// <summary>Spawn validation is in progress. Transient — resolves to Supplying or back to Dormant
    /// the same tick it's entered, never persists across ticks.</summary>
    Dispatching,

    /// <summary>A contractor entity is live and its PowerSupplierComponent is (or is about to be, next
    /// solver tick) part of the target network's Supplies.</summary>
    Supplying,

    /// <summary>Lease revoked (ClearNet() already fired), blocking new work, about to play despawn
    /// FX/lines. Entered at most once per lease -- idempotency guard for double-recall races.</summary>
    RecallPending,

    /// <summary>Despawn FX/lines are playing. Nothing left running -- no DoAfter, no in-flight
    /// interaction (§5's "why no DoAfter wait" note).</summary>
    Recalling,
}
