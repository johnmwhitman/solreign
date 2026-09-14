using System;
using Content.Server._Solreign.PowerContractor;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Pure-logic coverage for <see cref="ProvidencePowerContractorGate"/> -- SPEC-ai-npc-phase1-v2.md
///     §4.5 (hysteresis), §3.4 (TTL + redispatch cooldown), and §5 (FSM idempotency). Same
///     no-IoC/no-engine idiom as <c>LowPopLobbyReminderGateTests</c>: the gate is fabricated
///     TimeSpans and booleans in, an enum/phase out -- everything here should run in milliseconds.
/// </summary>
[TestFixture]
[TestOf(typeof(ProvidencePowerContractorGate))]
public sealed class ProvidencePowerContractorGateTests
{
    private static readonly TimeSpan T0 = TimeSpan.Zero;

    private static TimeSpan Sec(double seconds) => TimeSpan.FromSeconds(seconds);

    [Test]
    public void FreshGate_StartsDormant()
    {
        var gate = new ProvidencePowerContractorGate();

        Assert.That(gate.Phase, Is.EqualTo(ProvidencePowerContractorPhase.Dormant));
    }

    [Test]
    public void SampleDormant_NoGap_NeverSignalsDispatch()
    {
        var gate = new ProvidencePowerContractorGate();

        for (var i = 0; i < 10; i++)
        {
            var signal = gate.SampleDormant(false, Sec(i), hysteresisTicks: 2);
            Assert.That(signal, Is.EqualTo(ProvidenceGateSignal.None));
        }

        Assert.That(gate.Phase, Is.EqualTo(ProvidencePowerContractorPhase.Dormant));
    }

    [Test]
    public void SampleDormant_SingleGapSample_BelowHysteresis_DoesNotDispatch()
    {
        // A single-frame blip must never trigger -- §4.5's core anti-thrash requirement.
        var gate = new ProvidencePowerContractorGate();

        var signal = gate.SampleDormant(true, T0, hysteresisTicks: 2);

        Assert.That(signal, Is.EqualTo(ProvidenceGateSignal.None));
        Assert.That(gate.Phase, Is.EqualTo(ProvidencePowerContractorPhase.Dormant));
    }

    [Test]
    public void SampleDormant_GapPersistsAcrossHysteresisWindow_DispatchesExactlyOnce()
    {
        var gate = new ProvidencePowerContractorGate();

        var first = gate.SampleDormant(true, Sec(0), hysteresisTicks: 2);
        Assert.That(first, Is.EqualTo(ProvidenceGateSignal.None));

        var second = gate.SampleDormant(true, Sec(5), hysteresisTicks: 2);
        Assert.That(second, Is.EqualTo(ProvidenceGateSignal.Dispatch));
        Assert.That(gate.Phase, Is.EqualTo(ProvidencePowerContractorPhase.Dispatching));

        // Sampling again while already Dispatching is an idempotent no-op (guards a caller bug from
        // double-dispatching): the gate only reacts to SampleDormant while actually Dormant.
        var third = gate.SampleDormant(true, Sec(10), hysteresisTicks: 2);
        Assert.That(third, Is.EqualTo(ProvidenceGateSignal.None));
    }

    [Test]
    public void SampleDormant_GapClearsMidStreak_ResetsTheStreak()
    {
        var gate = new ProvidencePowerContractorGate();

        gate.SampleDormant(true, Sec(0), hysteresisTicks: 2);
        gate.SampleDormant(false, Sec(5), hysteresisTicks: 2); // clears mid-streak
        var third = gate.SampleDormant(true, Sec(10), hysteresisTicks: 2);

        // Streak restarted -- one more sample is required, not zero.
        Assert.That(third, Is.EqualTo(ProvidenceGateSignal.None));
        Assert.That(gate.Phase, Is.EqualTo(ProvidencePowerContractorPhase.Dormant));

        var fourth = gate.SampleDormant(true, Sec(15), hysteresisTicks: 2);
        Assert.That(fourth, Is.EqualTo(ProvidenceGateSignal.Dispatch));
    }

    [Test]
    public void SampleDormant_DuringRedispatchCooldown_NeverDispatches_EvenWithHysteresisSatisfied()
    {
        var gate = new ProvidencePowerContractorGate();

        // Complete one full lease so a cooldown is active.
        gate.SampleDormant(true, Sec(0), hysteresisTicks: 1);
        gate.MarkSupplying(Sec(0), leaseTtl: Sec(100));
        gate.MarkDeleted(Sec(1), cooldown: Sec(60));

        Assert.That(gate.InCooldown(Sec(30)), Is.True);

        // Gap re-confirms immediately and stays confirmed well past the hysteresis window -- must
        // still not dispatch until the cooldown itself elapses (§3.4's anti-thrash backstop, layered
        // ON TOP of hysteresis).
        var duringCooldown1 = gate.SampleDormant(true, Sec(2), hysteresisTicks: 1);
        var duringCooldown2 = gate.SampleDormant(true, Sec(10), hysteresisTicks: 1);
        Assert.That(duringCooldown1, Is.EqualTo(ProvidenceGateSignal.None));
        Assert.That(duringCooldown2, Is.EqualTo(ProvidenceGateSignal.None));

        Assert.That(gate.InCooldown(Sec(61)), Is.False);
        var afterCooldown = gate.SampleDormant(true, Sec(61), hysteresisTicks: 1);
        Assert.That(afterCooldown, Is.EqualTo(ProvidenceGateSignal.Dispatch));
    }

    [Test]
    public void MarkDispatchFailed_ReturnsToDormant_AndRetriesImmediatelyNextSample()
    {
        // §7.2: "fails cleanly ... re-attempt next monitor tick" -- the gap streak must NOT be lost
        // on a failed spawn attempt, else a persistently-obstructed DeployTile would force rebuilding
        // the whole hysteresis window every single tick and never actually retry promptly.
        var gate = new ProvidencePowerContractorGate();

        gate.SampleDormant(true, Sec(0), hysteresisTicks: 2);
        gate.SampleDormant(true, Sec(5), hysteresisTicks: 2); // -> Dispatching
        Assert.That(gate.Phase, Is.EqualTo(ProvidencePowerContractorPhase.Dispatching));

        gate.MarkDispatchFailed();
        Assert.That(gate.Phase, Is.EqualTo(ProvidencePowerContractorPhase.Dormant));

        // A single additional sample immediately re-dispatches -- streak was preserved.
        var retry = gate.SampleDormant(true, Sec(6), hysteresisTicks: 2);
        Assert.That(retry, Is.EqualTo(ProvidenceGateSignal.Dispatch));
    }

    [Test]
    public void MarkSupplying_SetsPhaseAndStartsTtl()
    {
        var gate = new ProvidencePowerContractorGate();
        gate.SampleDormant(true, Sec(0), hysteresisTicks: 1);

        gate.MarkSupplying(Sec(0), leaseTtl: Sec(300));

        Assert.That(gate.Phase, Is.EqualTo(ProvidencePowerContractorPhase.Supplying));
    }

    [Test]
    public void SampleSupplying_GapStillHeld_StaysSupplying()
    {
        var gate = new ProvidencePowerContractorGate();
        gate.SampleDormant(true, Sec(0), hysteresisTicks: 1);
        gate.MarkSupplying(Sec(0), leaseTtl: Sec(1000));

        var signal = gate.SampleSupplying(true, Sec(5), hysteresisTicks: 2);

        Assert.That(signal, Is.EqualTo(ProvidenceGateSignal.None));
        Assert.That(gate.Phase, Is.EqualTo(ProvidencePowerContractorPhase.Supplying));
    }

    [Test]
    public void SampleSupplying_GapClearsAcrossHysteresisWindow_RecallsExactlyOnce()
    {
        // The human-takeover / deficit-cleared path (§8 steps 7-8): coverage or deficit clearing must
        // hold for more than one consecutive sample before recall, same anti-thrash discipline as
        // dispatch.
        var gate = new ProvidencePowerContractorGate();
        gate.SampleDormant(true, Sec(0), hysteresisTicks: 1);
        gate.MarkSupplying(Sec(0), leaseTtl: Sec(1000));

        var first = gate.SampleSupplying(false, Sec(5), hysteresisTicks: 2);
        Assert.That(first, Is.EqualTo(ProvidenceGateSignal.None));
        Assert.That(gate.Phase, Is.EqualTo(ProvidencePowerContractorPhase.Supplying));

        var second = gate.SampleSupplying(false, Sec(10), hysteresisTicks: 2);
        Assert.That(second, Is.EqualTo(ProvidenceGateSignal.Recall));
        Assert.That(gate.Phase, Is.EqualTo(ProvidencePowerContractorPhase.RecallPending));
    }

    [Test]
    public void SampleSupplying_ClearingBlip_ResetsClearStreak()
    {
        var gate = new ProvidencePowerContractorGate();
        gate.SampleDormant(true, Sec(0), hysteresisTicks: 1);
        gate.MarkSupplying(Sec(0), leaseTtl: Sec(1000));

        gate.SampleSupplying(false, Sec(5), hysteresisTicks: 2); // clear streak = 1
        gate.SampleSupplying(true, Sec(10), hysteresisTicks: 2); // gap re-confirms -- resets clear streak
        var third = gate.SampleSupplying(false, Sec(15), hysteresisTicks: 2);

        Assert.That(third, Is.EqualTo(ProvidenceGateSignal.None));
        Assert.That(gate.Phase, Is.EqualTo(ProvidencePowerContractorPhase.Supplying));
    }

    [Test]
    public void SampleSupplying_TtlExpiry_ForcesRecall_EvenWhileGapStillHeld()
    {
        // §5's FSM diagram: TTL expiry recalls "regardless of whether the trigger condition still
        // holds." This must NOT be subject to hysteresis at all -- it fires on the very sample where
        // now >= ttlDeadline, gapConditionHeld=true notwithstanding.
        var gate = new ProvidencePowerContractorGate();
        gate.SampleDormant(true, Sec(0), hysteresisTicks: 1);
        gate.MarkSupplying(Sec(0), leaseTtl: Sec(300));

        var beforeTtl = gate.SampleSupplying(true, Sec(299), hysteresisTicks: 2);
        Assert.That(beforeTtl, Is.EqualTo(ProvidenceGateSignal.None));

        var atTtl = gate.SampleSupplying(true, Sec(300), hysteresisTicks: 2);
        Assert.That(atTtl, Is.EqualTo(ProvidenceGateSignal.Recall));
        Assert.That(gate.Phase, Is.EqualTo(ProvidencePowerContractorPhase.RecallPending));
    }

    [Test]
    public void TryForceRecall_FromSupplying_SucceedsOnce_ThenIsIdempotentNoOp()
    {
        var gate = new ProvidencePowerContractorGate();
        gate.SampleDormant(true, Sec(0), hysteresisTicks: 1);
        gate.MarkSupplying(Sec(0), leaseTtl: Sec(1000));

        var first = gate.TryForceRecall();
        Assert.That(first, Is.True);
        Assert.That(gate.Phase, Is.EqualTo(ProvidencePowerContractorPhase.RecallPending));

        // Double-recall race (e.g. round-end AND a same-tick hysteresis-confirmed clear) must be a
        // harmless no-op -- §5: "RecallPending is entered at most once per lease."
        var second = gate.TryForceRecall();
        Assert.That(second, Is.False);
        Assert.That(gate.Phase, Is.EqualTo(ProvidencePowerContractorPhase.RecallPending));
    }

    [Test]
    public void MarkRecalling_OnlyTransitions_FromRecallPending()
    {
        var gate = new ProvidencePowerContractorGate();

        // No-op while Dormant.
        gate.MarkRecalling();
        Assert.That(gate.Phase, Is.EqualTo(ProvidencePowerContractorPhase.Dormant));

        gate.SampleDormant(true, Sec(0), hysteresisTicks: 1);
        gate.MarkSupplying(Sec(0), leaseTtl: Sec(1000));
        gate.TryForceRecall();

        gate.MarkRecalling();
        Assert.That(gate.Phase, Is.EqualTo(ProvidencePowerContractorPhase.Recalling));
    }

    [Test]
    public void MarkDeleted_ResetsToDormant_AndStartsCooldown()
    {
        var gate = new ProvidencePowerContractorGate();
        gate.SampleDormant(true, Sec(0), hysteresisTicks: 1);
        gate.MarkSupplying(Sec(0), leaseTtl: Sec(1000));
        gate.TryForceRecall();
        gate.MarkRecalling();

        gate.MarkDeleted(Sec(50), cooldown: Sec(20));

        Assert.That(gate.Phase, Is.EqualTo(ProvidencePowerContractorPhase.Dormant));
        Assert.That(gate.InCooldown(Sec(60)), Is.True);
        Assert.That(gate.InCooldown(Sec(70)), Is.False);
    }

    [Test]
    public void SampleSupplying_WhileNotSupplying_IsIdempotentNoOp()
    {
        var gate = new ProvidencePowerContractorGate();

        var signal = gate.SampleSupplying(false, Sec(0), hysteresisTicks: 1);

        Assert.That(signal, Is.EqualTo(ProvidenceGateSignal.None));
        Assert.That(gate.Phase, Is.EqualTo(ProvidencePowerContractorPhase.Dormant));
    }

    [Test]
    public void CooldownDeadline_ReflectsMarkDeletedsCooldown()
    {
        // Review finding #4: CooldownDeadline is the read-only accessor the reconciliation logic
        // (merge tie-breaks, grid-cooldown-continuity transplant) relies on.
        var gate = new ProvidencePowerContractorGate();
        gate.SampleDormant(true, Sec(0), hysteresisTicks: 1);
        gate.MarkSupplying(Sec(0), leaseTtl: Sec(100));

        gate.MarkDeleted(Sec(50), cooldown: Sec(20));

        Assert.That(gate.CooldownDeadline, Is.EqualTo(Sec(70)));
    }

    [Test]
    public void SeedCooldown_OnFreshGate_PutsItInCooldownImmediately()
    {
        // Review finding #4: a freshly-discovered zone on a grid with an orphaned, still-running
        // cooldown must inherit it via SeedCooldown -- proving it actually blocks an immediate
        // dispatch even though this gate itself has never sampled before.
        var gate = new ProvidencePowerContractorGate();

        gate.SeedCooldown(Sec(60));

        Assert.That(gate.InCooldown(Sec(30)), Is.True);
        Assert.That(gate.CooldownDeadline, Is.EqualTo(Sec(60)));

        // Hysteresis satisfied but still within the seeded cooldown window -- must not dispatch.
        gate.SampleDormant(true, Sec(10), hysteresisTicks: 1);
        var duringCooldown = gate.SampleDormant(true, Sec(20), hysteresisTicks: 1);
        Assert.That(duringCooldown, Is.EqualTo(ProvidenceGateSignal.None));

        Assert.That(gate.InCooldown(Sec(61)), Is.False);
        var afterCooldown = gate.SampleDormant(true, Sec(61), hysteresisTicks: 1);
        Assert.That(afterCooldown, Is.EqualTo(ProvidenceGateSignal.Dispatch));
    }

    [Test]
    public void FullLifecycle_DispatchSupplyHumanTakeoverRecall_MatchesAcceptanceNarrative()
    {
        // Mirrors §8's data-flow walkthrough at the pure decision-math level (the engine-level half --
        // actual spawn/ClearNet/entity-delete -- is covered by
        // ProvidencePowerContractorSystemIntegrationTest).
        var gate = new ProvidencePowerContractorGate();

        gate.SampleDormant(true, Sec(0), hysteresisTicks: 2);
        var dispatchSignal = gate.SampleDormant(true, Sec(5), hysteresisTicks: 2);
        Assert.That(dispatchSignal, Is.EqualTo(ProvidenceGateSignal.Dispatch));

        gate.MarkSupplying(Sec(5), leaseTtl: Sec(300));
        Assert.That(gate.Phase, Is.EqualTo(ProvidencePowerContractorPhase.Supplying));

        gate.SampleSupplying(true, Sec(10), hysteresisTicks: 2); // still no human -- stays Supplying

        gate.SampleSupplying(false, Sec(15), hysteresisTicks: 2); // human arrives
        var recallSignal = gate.SampleSupplying(false, Sec(20), hysteresisTicks: 2);
        Assert.That(recallSignal, Is.EqualTo(ProvidenceGateSignal.Recall));
        Assert.That(gate.Phase, Is.EqualTo(ProvidencePowerContractorPhase.RecallPending));

        gate.MarkRecalling();
        gate.MarkDeleted(Sec(21), cooldown: Sec(180));

        Assert.That(gate.Phase, Is.EqualTo(ProvidencePowerContractorPhase.Dormant));
        Assert.That(gate.InCooldown(Sec(200)), Is.True);
    }
}
