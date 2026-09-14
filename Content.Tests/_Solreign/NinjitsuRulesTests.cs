using System;
using Content.Server._Solreign.MartialArts;
using Content.Server._Solreign.Ninjitsu;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(NinjitsuRules))]
public sealed class NinjitsuRulesTests
{
    private static readonly TimeSpan T0 = TimeSpan.FromSeconds(500);

    private static TimeSpan At(double seconds) => T0 + TimeSpan.FromSeconds(seconds);

    // --- CooldownReady / NextReady: shared cooldown gate for Smoke Vanish + Silent Step ---

    [Test]
    public void CooldownReady_ExactlyAtReadyTime_IsReady()
    {
        Assert.That(NinjitsuRules.CooldownReady(T0, T0), Is.True);
    }

    [Test]
    public void CooldownReady_BeforeReadyTime_IsNotReady()
    {
        Assert.That(NinjitsuRules.CooldownReady(T0, T0 + TimeSpan.FromMilliseconds(1)), Is.False);
    }

    [Test]
    public void CooldownReady_AfterReadyTime_IsReady()
    {
        Assert.That(NinjitsuRules.CooldownReady(At(1), T0), Is.True);
    }

    [Test]
    public void FreshComponent_DefaultReadyAt_IsImmediatelyReady()
    {
        // A freshly-spawned suit/ninja (default(TimeSpan) NextVanishReadyAt / NextSilentStepToggleAt)
        // must be usable immediately, same invariant as CarpComboRules.ComboReady.
        Assert.That(NinjitsuRules.CooldownReady(TimeSpan.Zero, default), Is.True);
    }

    [Test]
    public void NextReady_AddsCooldown()
    {
        Assert.That(NinjitsuRules.NextReady(T0, TimeSpan.FromSeconds(20)), Is.EqualTo(T0 + TimeSpan.FromSeconds(20)));
    }

    [Test]
    public void NextReady_NegativeCooldown_ClampsToZero()
    {
        // YAML misconfiguration shouldn't schedule the next use into the past.
        Assert.That(NinjitsuRules.NextReady(T0, TimeSpan.FromSeconds(-5)), Is.EqualTo(T0));
    }

    [Test]
    public void NextReady_ZeroCooldown_IsImmediatelyReadyAgain()
    {
        var readyAt = NinjitsuRules.NextReady(T0, TimeSpan.Zero);
        Assert.That(NinjitsuRules.CooldownReady(T0, readyAt), Is.True);
    }

    // --- VanishActive: Smoke Vanish's personal stealth + speed window ---

    [Test]
    public void VanishActive_BeforeExpiry_IsActive()
    {
        Assert.That(NinjitsuRules.VanishActive(T0, At(4)), Is.True);
    }

    [Test]
    public void VanishActive_ExactlyAtExpiry_IsNoLongerActive()
    {
        // Expiry is exclusive: unlike a combo window's inclusive edge, the vanish window ends AT
        // its expiry instant, not one tick after.
        Assert.That(NinjitsuRules.VanishActive(At(4), At(4)), Is.False);
    }

    [Test]
    public void VanishActive_AfterExpiry_IsNotActive()
    {
        Assert.That(NinjitsuRules.VanishActive(At(5), At(4)), Is.False);
    }

    [Test]
    public void VanishActive_FreshComponent_DefaultExpiresAt_IsNotActive()
    {
        // A NinjitsuVanishComponent should never exist pre-activation, but if one somehow did with
        // its default(TimeSpan) ExpiresAt, it must read as already-expired, not perpetually active.
        Assert.That(NinjitsuRules.VanishActive(TimeSpan.Zero, default), Is.False);
    }

    // --- Takedown: genuinely reuses CarpComboRules, doesn't re-derive its own copy ---

    [Test]
    public void StrikesChain_DelegatesToCarpComboRules_WithinWindow()
    {
        var window = TimeSpan.FromSeconds(1.5);

        Assert.That(NinjitsuRules.StrikesChain(T0, At(1.5), window), Is.EqualTo(CarpComboRules.WithinWindow(T0, At(1.5), window)));
        Assert.That(NinjitsuRules.StrikesChain(T0, At(1.5) + TimeSpan.FromMilliseconds(1), window), Is.EqualTo(CarpComboRules.WithinWindow(T0, At(1.5) + TimeSpan.FromMilliseconds(1), window)));
    }

    [Test]
    public void StrikesChain_InsideWindow_Chains()
    {
        Assert.That(NinjitsuRules.StrikesChain(T0, At(1), TimeSpan.FromSeconds(1.5)), Is.True);
    }

    [Test]
    public void StrikesChain_OutsideWindow_DoesNotChain()
    {
        Assert.That(NinjitsuRules.StrikesChain(T0, At(2), TimeSpan.FromSeconds(1.5)), Is.False);
    }

    [Test]
    public void Takedown_FinisherCooldown_ReusesCarpComboRulesDirectly()
    {
        // NinjitsuSystem calls CarpComboRules.ComboReady/NextComboTime directly for the takedown
        // finisher cooldown (see NinjitsuSystem.Takedown.cs) rather than wrapping it — this test
        // pins that those generic TimeSpan-only functions behave the same regardless of caller.
        var cooldown = TimeSpan.FromSeconds(4);
        Assert.That(CarpComboRules.ComboReady(T0, T0), Is.True);
        Assert.That(CarpComboRules.ComboReady(T0, At(1)), Is.False);
        Assert.That(CarpComboRules.NextComboTime(T0, cooldown), Is.EqualTo(T0 + cooldown));
    }
}
