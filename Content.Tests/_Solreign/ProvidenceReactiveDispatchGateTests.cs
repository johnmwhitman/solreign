using System;
using Content.Server._Solreign.Providence;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(ProvidenceReactiveDispatchGate))]
public sealed class ProvidenceReactiveDispatchGateTests
{
    private static readonly TimeSpan BaseCooldown = TimeSpan.FromSeconds(90);

    [Test]
    public void CanFire_IsTrueBeforeAnythingHasEverFired()
    {
        var gate = new ProvidenceReactiveDispatchGate();

        Assert.That(gate.CanFire(ProvidenceReactivePriority.Normal, TimeSpan.Zero, BaseCooldown), Is.True);
    }

    [Test]
    public void BurstOfNormalPriorityEvents_OnlyTheFirstFires()
    {
        // The debris-cloud spam failure mode (PLAYER-FEEDBACK-2026-07-22.md): many candidate events
        // arriving well inside the cooldown window must collapse to at most one actual fire.
        var gate = new ProvidenceReactiveDispatchGate();
        var now = TimeSpan.Zero;
        var fireCount = 0;

        for (var i = 0; i < 20; i++)
        {
            var t = now + TimeSpan.FromSeconds(i); // 20 candidates, one per second — all inside 90s
            if (gate.CanFire(ProvidenceReactivePriority.Normal, t, BaseCooldown))
            {
                fireCount++;
                gate.MarkFired(t);
            }
        }

        Assert.That(fireCount, Is.EqualTo(1), "A burst inside the cooldown window must fire exactly once, not once per event.");
    }

    [Test]
    public void AfterMarkFired_CanFire_IsFalseImmediately()
    {
        var gate = new ProvidenceReactiveDispatchGate();
        var now = TimeSpan.FromSeconds(10);

        gate.MarkFired(now);

        Assert.That(gate.CanFire(ProvidenceReactivePriority.Normal, now, BaseCooldown), Is.False);
    }

    [Test]
    public void AfterMarkFired_CanFire_IsTrueOnceTheBaseCooldownElapses_ForNormalPriority()
    {
        var gate = new ProvidenceReactiveDispatchGate();
        var fired = TimeSpan.FromSeconds(10);
        gate.MarkFired(fired);

        Assert.That(gate.CanFire(ProvidenceReactivePriority.Normal, fired + BaseCooldown - TimeSpan.FromSeconds(1), BaseCooldown), Is.False);
        Assert.That(gate.CanFire(ProvidenceReactivePriority.Normal, fired + BaseCooldown, BaseCooldown), Is.True);
    }

    [Test]
    public void HighPriority_CanFireSoonerThanNormal_AfterAPriorFire()
    {
        var gate = new ProvidenceReactiveDispatchGate();
        var fired = TimeSpan.FromSeconds(10);
        gate.MarkFired(fired);

        // Halfway through the base cooldown: Normal must still be blocked, High must already be allowed.
        var halfway = fired + TimeSpan.FromSeconds(BaseCooldown.TotalSeconds / 2);

        Assert.Multiple(() =>
        {
            Assert.That(gate.CanFire(ProvidenceReactivePriority.Normal, halfway, BaseCooldown), Is.False,
                "Normal priority should still be waiting out the full base cooldown.");
            Assert.That(gate.CanFire(ProvidenceReactivePriority.High, halfway, BaseCooldown), Is.True,
                "High priority should be allowed after half the base cooldown.");
        });
    }

    [Test]
    public void LowPriority_MustWaitLongerThanNormal_AfterAPriorFire()
    {
        var gate = new ProvidenceReactiveDispatchGate();
        var fired = TimeSpan.FromSeconds(10);
        gate.MarkFired(fired);

        var atBaseCooldown = fired + BaseCooldown;

        Assert.Multiple(() =>
        {
            Assert.That(gate.CanFire(ProvidenceReactivePriority.Normal, atBaseCooldown, BaseCooldown), Is.True,
                "Normal priority should be allowed once the base cooldown has fully elapsed.");
            Assert.That(gate.CanFire(ProvidenceReactivePriority.Low, atBaseCooldown, BaseCooldown), Is.False,
                "Low priority should still be waiting — it needs double the base cooldown.");
        });
    }

    [Test]
    public void EvenHighPriority_IsBlockedImmediatelyAfterAnotherHighPriorityFire()
    {
        // Priority scales the cooldown, it does not bypass it outright — a High-priority line firing
        // right after another High-priority line must still be blocked.
        var gate = new ProvidenceReactiveDispatchGate();
        var fired = TimeSpan.FromSeconds(10);
        gate.MarkFired(fired);

        Assert.That(gate.CanFire(ProvidenceReactivePriority.High, fired + TimeSpan.FromSeconds(1), BaseCooldown), Is.False);
    }

    [Test]
    public void Reset_ClearsTheFiredClock_SoAnyPriorityCanFireImmediately()
    {
        var gate = new ProvidenceReactiveDispatchGate();
        gate.MarkFired(TimeSpan.FromSeconds(10));

        gate.Reset();

        Assert.That(gate.CanFire(ProvidenceReactivePriority.Low, TimeSpan.FromSeconds(10), BaseCooldown), Is.True,
            "A round boundary must never let cooldown state leak into the next round.");
    }

    [TestCase(ProvidenceReactivePriority.High, 45)]
    [TestCase(ProvidenceReactivePriority.Normal, 90)]
    [TestCase(ProvidenceReactivePriority.Low, 180)]
    public void EffectiveCooldown_ScalesByPriority(ProvidenceReactivePriority priority, double expectedSeconds)
    {
        var effective = ProvidenceReactiveDispatchGate.EffectiveCooldown(priority, BaseCooldown);

        Assert.That(effective, Is.EqualTo(TimeSpan.FromSeconds(expectedSeconds)));
    }

    [Test]
    public void EffectiveCooldown_ClampsANonPositiveBaseCooldownToZero_NeverNegative()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProvidenceReactiveDispatchGate.EffectiveCooldown(ProvidenceReactivePriority.High, TimeSpan.Zero), Is.EqualTo(TimeSpan.Zero));
            Assert.That(ProvidenceReactiveDispatchGate.EffectiveCooldown(ProvidenceReactivePriority.Low, TimeSpan.FromSeconds(-5)), Is.EqualTo(TimeSpan.Zero));
        });
    }
}
