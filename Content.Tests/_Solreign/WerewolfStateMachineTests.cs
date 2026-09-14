using System;
using Content.Server._Solreign.Antags.Werewolf;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(WerewolfStateMachine))]
public sealed class WerewolfStateMachineTests
{
    private static readonly WerewolfTimings Timings = new(
        stirring: TimeSpan.FromSeconds(30),
        transformed: TimeSpan.FromSeconds(180),
        waning: TimeSpan.FromSeconds(10));

    private static TimeSpan Secs(double s) => TimeSpan.FromSeconds(s);

    private static WerewolfState Next(
        WerewolfState state, double nowSecs, double enteredSecs, bool moon, bool cure)
        => WerewolfStateMachine.Next(state, Secs(nowSecs), Secs(enteredSecs), moon, cure, in Timings);

    // --- Dormant ---

    [Test]
    public void Dormant_NoMoon_StaysDormant()
    {
        Assert.That(Next(WerewolfState.Dormant, 100, 0, moon: false, cure: false),
            Is.EqualTo(WerewolfState.Dormant));
    }

    [Test]
    public void Dormant_MoonOpens_BeginsStirring()
    {
        Assert.That(Next(WerewolfState.Dormant, 100, 0, moon: true, cure: false),
            Is.EqualTo(WerewolfState.Stirring));
    }

    [Test]
    public void Dormant_CureApplied_ResolvesInstantly()
    {
        // Cure while in crew form skips every phase — even under an open moon.
        Assert.That(Next(WerewolfState.Dormant, 100, 0, moon: true, cure: true),
            Is.EqualTo(WerewolfState.Cured));
    }

    // --- Stirring ---

    [Test]
    public void Stirring_BeforeDurationElapses_KeepsStirring()
    {
        Assert.That(Next(WerewolfState.Stirring, 29, 0, moon: true, cure: false),
            Is.EqualTo(WerewolfState.Stirring));
    }

    [Test]
    public void Stirring_DurationElapsed_MoonStillUp_Transforms()
    {
        Assert.That(Next(WerewolfState.Stirring, 30, 0, moon: true, cure: false),
            Is.EqualTo(WerewolfState.Transformed));
    }

    [Test]
    public void Stirring_MoonClosesEarly_NearMissBackToDormant()
    {
        Assert.That(Next(WerewolfState.Stirring, 15, 0, moon: false, cure: false),
            Is.EqualTo(WerewolfState.Dormant));
    }

    [Test]
    public void Stirring_MoonClosesEarly_WithCure_ResolvesToCured()
    {
        Assert.That(Next(WerewolfState.Stirring, 15, 0, moon: false, cure: true),
            Is.EqualTo(WerewolfState.Cured));
    }

    // --- Transformed ---

    [Test]
    public void Transformed_WithinCap_MoonUp_StaysWolf()
    {
        Assert.That(Next(WerewolfState.Transformed, 100, 0, moon: true, cure: false),
            Is.EqualTo(WerewolfState.Transformed));
    }

    [Test]
    public void Transformed_DurationCapElapsed_BeginsWaning()
    {
        Assert.That(Next(WerewolfState.Transformed, 180, 0, moon: true, cure: false),
            Is.EqualTo(WerewolfState.Waning));
    }

    [Test]
    public void Transformed_MoonCloses_BeginsWaningEvenIfCapRemains()
    {
        Assert.That(Next(WerewolfState.Transformed, 60, 0, moon: false, cure: false),
            Is.EqualTo(WerewolfState.Waning));
    }

    [Test]
    public void Transformed_CureApplied_DoesNotSkipTheVisibleRevert()
    {
        // Anti-grief invariant: a cure never yanks a wolf out of play instantly — the wolf
        // stays transformed until a normal Waning boundary, then resolves through it.
        Assert.That(Next(WerewolfState.Transformed, 60, 0, moon: true, cure: true),
            Is.EqualTo(WerewolfState.Transformed));
    }

    // --- Waning ---

    [Test]
    public void Waning_BeforeDurationElapses_KeepsWaning()
    {
        Assert.That(Next(WerewolfState.Waning, 9, 0, moon: true, cure: false),
            Is.EqualTo(WerewolfState.Waning));
    }

    [Test]
    public void Waning_Elapsed_NotCured_ReturnsToDormant()
    {
        Assert.That(Next(WerewolfState.Waning, 10, 0, moon: false, cure: false),
            Is.EqualTo(WerewolfState.Dormant));
    }

    [Test]
    public void Waning_Elapsed_Cured_ResolvesToCured()
    {
        Assert.That(Next(WerewolfState.Waning, 10, 0, moon: false, cure: true),
            Is.EqualTo(WerewolfState.Cured));
    }

    // --- Cured is terminal ---

    [Test]
    public void Cured_IsTerminal_UnderEveryInput()
    {
        foreach (var moon in new[] { false, true })
        foreach (var cure in new[] { false, true })
        {
            Assert.That(Next(WerewolfState.Cured, 9999, 0, moon, cure),
                Is.EqualTo(WerewolfState.Cured));
        }
    }

    // --- Re-entrancy: a second moon window restarts the cycle ---

    [Test]
    public void Dormant_SecondMoonWindow_StirsAgain()
    {
        // Full first episode already resolved back to Dormant; a fresh window re-triggers.
        var state = Next(WerewolfState.Dormant, 1000, 500, moon: true, cure: false);
        Assert.That(state, Is.EqualTo(WerewolfState.Stirring));
    }

    // --- Sanitization: YAML can be hostile ---

    [Test]
    public void NegativeTimings_ClampToZero_NoThrow()
    {
        var hostile = new WerewolfTimings(Secs(-5), Secs(-5), Secs(-5));

        // Zero stirring: moon-open dormant → stirring, then transforms immediately.
        var afterStirring = WerewolfStateMachine.Next(
            WerewolfState.Stirring, Secs(0), Secs(0), true, false, in hostile);
        Assert.That(afterStirring, Is.EqualTo(WerewolfState.Transformed));

        // Zero waning: exits immediately.
        var afterWaning = WerewolfStateMachine.Next(
            WerewolfState.Waning, Secs(0), Secs(0), false, false, in hostile);
        Assert.That(afterWaning, Is.EqualTo(WerewolfState.Dormant));
    }

    // --- Helpers ---

    [Test]
    public void IsWolfForm_OnlyWhileTransformed()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WerewolfStateMachine.IsWolfForm(WerewolfState.Transformed), Is.True);
            Assert.That(WerewolfStateMachine.IsWolfForm(WerewolfState.Dormant), Is.False);
            Assert.That(WerewolfStateMachine.IsWolfForm(WerewolfState.Stirring), Is.False);
            Assert.That(WerewolfStateMachine.IsWolfForm(WerewolfState.Waning), Is.False);
            Assert.That(WerewolfStateMachine.IsWolfForm(WerewolfState.Cured), Is.False);
        });
    }

    [Test]
    public void ShowsFur_DuringTransformedAndWaning()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WerewolfStateMachine.ShowsFur(WerewolfState.Transformed), Is.True);
            Assert.That(WerewolfStateMachine.ShowsFur(WerewolfState.Waning), Is.True);
            Assert.That(WerewolfStateMachine.ShowsFur(WerewolfState.Dormant), Is.False);
            Assert.That(WerewolfStateMachine.ShowsFur(WerewolfState.Stirring), Is.False);
            Assert.That(WerewolfStateMachine.ShowsFur(WerewolfState.Cured), Is.False);
        });
    }
}

/// <summary>
///     Build-pass extension (docs/specs/2026-07-11-werewolf-vampire-spec.md §3.3-3.5): the maul-gating
///     pure logic that backs <c>SolreignWerewolfSystem.Transform.OnMeleeHit</c>.
/// </summary>
[TestFixture]
[TestOf(typeof(WerewolfMaulRules))]
public sealed class WerewolfMaulRulesTests
{
    private static TimeSpan Secs(double s) => TimeSpan.FromSeconds(s);

    [Test]
    public void CanMaul_OffCooldown_NotTouched_Allowed()
    {
        Assert.That(WerewolfMaulRules.CanMaul(Secs(10), Secs(5), targetIsMoonTouched: false), Is.True);
    }

    [Test]
    public void CanMaul_ExactlyAtCooldownBoundary_Allowed()
    {
        // now >= nextMaulAllowed is inclusive.
        Assert.That(WerewolfMaulRules.CanMaul(Secs(5), Secs(5), targetIsMoonTouched: false), Is.True);
    }

    [Test]
    public void CanMaul_StillOnCooldown_Refused()
    {
        Assert.That(WerewolfMaulRules.CanMaul(Secs(4), Secs(5), targetIsMoonTouched: false), Is.False);
    }

    [Test]
    public void CanMaul_TargetAlreadyMoonTouched_Refused_EvenOffCooldown()
    {
        // Anti-grief rule 2: the immunity window wins even when the cooldown alone would allow it.
        Assert.That(WerewolfMaulRules.CanMaul(Secs(10), Secs(0), targetIsMoonTouched: true), Is.False);
    }

    [Test]
    public void NextMaulAllowedAt_AddsCooldownToNow()
    {
        Assert.That(WerewolfMaulRules.NextMaulAllowedAt(Secs(10), 5f), Is.EqualTo(Secs(15)));
    }

    [Test]
    public void NextMaulAllowedAt_NegativeCooldown_ClampsToNoCooldown()
    {
        // Sanitize, don't throw (house doctrine) — hostile YAML never schedules into the past either.
        Assert.That(WerewolfMaulRules.NextMaulAllowedAt(Secs(10), -5f), Is.EqualTo(Secs(10)));
    }
}
