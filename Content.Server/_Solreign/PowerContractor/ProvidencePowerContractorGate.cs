using System;

namespace Content.Server._Solreign.PowerContractor;

/// <summary>
///     What the caller (<c>ProvidencePowerContractorSystem</c>) must now DO as a result of a
///     <see cref="ProvidencePowerContractorGate"/> sample. The gate itself never touches an entity,
///     a component, or a network -- it is pure decision math, same idiom as
///     <c>LowPopLobbyReminderGate</c> / <c>ProvidenceVoiceSystem.IdleWindow</c>, directly
///     unit-testable in <c>Content.Tests</c> without IoC or engine types
///     (<c>Content.Tests/_Solreign/ProvidencePowerContractorGateTests.cs</c>).
/// </summary>
public enum ProvidenceGateSignal
{
    /// <summary>Nothing to do this sample.</summary>
    None,

    /// <summary>Caller should attempt spawn validation now (§5 Dormant -&gt; Dispatching).</summary>
    Dispatch,

    /// <summary>Caller should synchronously ClearNet() and enter RecallPending now (§5 Supplying -&gt;
    /// RecallPending).</summary>
    Recall,
}

/// <summary>
///     Pure per-zone lease state machine: hysteresis (§4.5), TTL (§3.4), redispatch cooldown (§3.4),
///     and the FSM phase (§5) itself. Free of IoC/engine types so it is directly unit-testable. One
///     instance per discovered zone (<see cref="ProvidencePowerContractorZone"/>), never shared.
///
///     Ownership contract: this class decides WHEN a transition should happen; it never performs the
///     transition's side effects (spawning, ClearNet(), deleting an entity) itself -- those live in
///     <c>ProvidencePowerContractorSystem.Lifecycle.cs</c>, which calls back into this gate
///     (<see cref="MarkSupplying"/>, <see cref="MarkDispatchFailed"/>, <see cref="MarkRecalling"/>,
///     <see cref="MarkDeleted"/>) once each side effect has actually happened. This mirrors §5's
///     framing: the FSM is the sole owner of *when*, the system is the sole owner of *how*.
/// </summary>
public sealed class ProvidencePowerContractorGate
{
    public ProvidencePowerContractorPhase Phase { get; private set; } = ProvidencePowerContractorPhase.Dormant;

    private int _gapStreak;
    private int _clearStreak;
    private TimeSpan _cooldownUntil = TimeSpan.Zero;
    private TimeSpan _ttlDeadline;

    /// <summary>True while a just-ended lease's redispatch cooldown (§3.4) hasn't elapsed yet.</summary>
    public bool InCooldown(TimeSpan now) => now < _cooldownUntil;

    /// <summary>
    ///     Review finding #4: read-only accessor for the current redispatch-cooldown deadline. Used
    ///     for two reconciliation purposes -- (a) as a deterministic tie-break when a network merge
    ///     forces a choice between two otherwise-equivalent zones (<c>ProvidencePowerContractorSystem
    ///     .PickMergeLoser</c>), and (b) to transplant an about-to-be-orphaned zone's remaining
    ///     cooldown onto a freshly-discovered anchor on the same physical grid
    ///     (<c>ProvidencePowerContractorSystem._gridCooldownMemory</c>) so a replaced/destroyed anchor
    ///     can never silently reset the anti-thrash backoff.
    /// </summary>
    public TimeSpan CooldownDeadline => _cooldownUntil;

    /// <summary>
    ///     Review finding #4: seeds a freshly-constructed (still-Dormant, never-sampled) gate with an
    ///     already-in-progress redispatch cooldown carried over from a just-orphaned zone on the same
    ///     physical grid. Only meaningful immediately after construction -- this is not a general
    ///     cooldown-extension API.
    /// </summary>
    public void SeedCooldown(TimeSpan cooldownUntil)
    {
        _cooldownUntil = cooldownUntil;
    }

    /// <summary>
    ///     One monitor sample while <see cref="ProvidencePowerContractorPhase.Dormant"/>. Idempotent
    ///     no-op if called while not actually Dormant (guards a caller bug from double-dispatching).
    /// </summary>
    /// <param name="gapConditionHeld">This tick's raw §4.3+§4.4 "failing AND uncovered" result.</param>
    /// <param name="now">Current game time.</param>
    /// <param name="hysteresisTicks">Consecutive samples required before acting (§4.5).</param>
    public ProvidenceGateSignal SampleDormant(bool gapConditionHeld, TimeSpan now, int hysteresisTicks)
    {
        if (Phase != ProvidencePowerContractorPhase.Dormant)
            return ProvidenceGateSignal.None;

        if (!gapConditionHeld)
        {
            _gapStreak = 0;
            return ProvidenceGateSignal.None;
        }

        _gapStreak++;

        if (_gapStreak < hysteresisTicks)
            return ProvidenceGateSignal.None;

        if (now < _cooldownUntil)
            return ProvidenceGateSignal.None;

        Phase = ProvidencePowerContractorPhase.Dispatching;
        return ProvidenceGateSignal.Dispatch;
    }

    /// <summary>
    ///     Call after spawn validation (§7) actually succeeded and the contractor is anchored. Starts
    ///     the TTL clock (§3.4) and resets the clear-streak for the new lease.
    /// </summary>
    public void MarkSupplying(TimeSpan now, TimeSpan leaseTtl)
    {
        Phase = ProvidencePowerContractorPhase.Supplying;
        _ttlDeadline = now + leaseTtl;
        _clearStreak = 0;
    }

    /// <summary>
    ///     Call after spawn validation (§7) failed clean. Returns to Dormant WITHOUT resetting the gap
    ///     streak, so the very next monitor sample retries immediately rather than re-waiting through
    ///     the full hysteresis window (§7.2: "fails cleanly ... re-attempt next monitor tick").
    /// </summary>
    public void MarkDispatchFailed()
    {
        Phase = ProvidencePowerContractorPhase.Dormant;
    }

    /// <summary>
    ///     One monitor sample while <see cref="ProvidencePowerContractorPhase.Supplying"/>. TTL expiry
    ///     is NOT subject to hysteresis -- it forces recall even while the gap condition still holds
    ///     (§5's FSM diagram, TTL branch). Idempotent no-op if called while not actually Supplying.
    /// </summary>
    public ProvidenceGateSignal SampleSupplying(bool gapConditionHeld, TimeSpan now, int hysteresisTicks)
    {
        if (Phase != ProvidencePowerContractorPhase.Supplying)
            return ProvidenceGateSignal.None;

        if (now >= _ttlDeadline)
        {
            Phase = ProvidencePowerContractorPhase.RecallPending;
            return ProvidenceGateSignal.Recall;
        }

        if (gapConditionHeld)
        {
            _clearStreak = 0;
            return ProvidenceGateSignal.None;
        }

        _clearStreak++;

        if (_clearStreak < hysteresisTicks)
            return ProvidenceGateSignal.None;

        Phase = ProvidencePowerContractorPhase.RecallPending;
        return ProvidenceGateSignal.Recall;
    }

    /// <summary>
    ///     Admin/round-end/destruction/grid-removal forced recall (§5, §7.5, §8's implicit-recall
    ///     paths). Returns false (no-op) if a recall is already in flight -- the idempotency guard
    ///     that makes a double-recall race harmless (§5: "RecallPending is entered at most once per
    ///     lease").
    /// </summary>
    public bool TryForceRecall()
    {
        if (Phase is ProvidencePowerContractorPhase.RecallPending or ProvidencePowerContractorPhase.Recalling)
            return false;

        Phase = ProvidencePowerContractorPhase.RecallPending;
        return true;
    }

    /// <summary>Call once despawn FX/lines have started playing.</summary>
    public void MarkRecalling()
    {
        if (Phase != ProvidencePowerContractorPhase.RecallPending)
            return;

        Phase = ProvidencePowerContractorPhase.Recalling;
    }

    /// <summary>
    ///     Terminal transition: the contractor entity is gone. Resets to Dormant and starts the
    ///     redispatch cooldown (§3.4) -- applies uniformly whether the lease ended by clean recall,
    ///     destruction, or grid removal (§5's "Redispatch backoff" note).
    /// </summary>
    public void MarkDeleted(TimeSpan now, TimeSpan cooldown)
    {
        Phase = ProvidencePowerContractorPhase.Dormant;
        _gapStreak = 0;
        _clearStreak = 0;
        _cooldownUntil = now + cooldown;
    }
}
