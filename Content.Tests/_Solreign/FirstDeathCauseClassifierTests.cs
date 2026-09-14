#nullable enable
using System.Collections.Generic;
using Content.Server._Solreign.Providence;
using Content.Shared.FixedPoint;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Table-driven coverage for the pure first-death cause classifier (spec §3.3/§7): all five
///     buckets, attacker precedence, empty/all-zero dict → UNKNOWN, tie-breaking, and the barotrauma
///     mapping RESOLVED at build (crew barotrauma deals Blunt — species_base.yml `Blunt: 0.50` ×
///     Atmospherics.LowPressureDamage 4 — so Blunt-dominant + Asphyxiation-present + no attacker is a
///     spacing, while Blunt-dominant alone stays MISADVENTURE).
/// </summary>
[TestFixture]
[TestOf(typeof(FirstDeathCauseClassifier))]
public sealed class FirstDeathCauseClassifierTests
{
    private static Dictionary<string, FixedPoint2> Damage(params (string Type, double Value)[] entries)
    {
        var dict = new Dictionary<string, FixedPoint2>();
        foreach (var (type, value) in entries)
            dict[type] = FixedPoint2.New(value);
        return dict;
    }

    // --- Attacker precedence -------------------------------------------------------------------

    [Test]
    public void AttackerPresent_AlwaysViolence_RegardlessOfDamage()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FirstDeathCauseClassifier.Classify(true, Damage(("Heat", 200))),
                Is.EqualTo(FirstDeathCause.Violence), "attacker wins over any damage profile");
            Assert.That(FirstDeathCauseClassifier.Classify(true, Damage(("Asphyxiation", 90), ("Blunt", 120))),
                Is.EqualTo(FirstDeathCause.Violence), "attacker wins over the vacuum signature");
            Assert.That(FirstDeathCauseClassifier.Classify(true, Damage()),
                Is.EqualTo(FirstDeathCause.Violence), "attacker wins even with no damage snapshot");
        });
    }

    // --- UNKNOWN --------------------------------------------------------------------------------

    [Test]
    public void EmptyDict_IsUnknown()
    {
        Assert.That(FirstDeathCauseClassifier.Classify(false, Damage()), Is.EqualTo(FirstDeathCause.Unknown));
    }

    [Test]
    public void NullDict_IsUnknown()
    {
        Assert.That(FirstDeathCauseClassifier.Classify(false, null), Is.EqualTo(FirstDeathCause.Unknown));
    }

    [Test]
    public void AllZeroDict_IsUnknown()
    {
        Assert.That(FirstDeathCauseClassifier.Classify(false, Damage(("Blunt", 0), ("Heat", 0))),
            Is.EqualTo(FirstDeathCause.Unknown), "an all-zero snapshot carries no signal");
    }

    // --- VACUUM ---------------------------------------------------------------------------------

    [TestCase("Asphyxiation")]
    [TestCase("Cold")]
    public void DominantVacuumType_IsVacuum(string type)
    {
        Assert.That(FirstDeathCauseClassifier.Classify(false, Damage((type, 80), ("Blunt", 10))),
            Is.EqualTo(FirstDeathCause.Vacuum));
    }

    [Test]
    public void BarotraumaSignature_BluntDominantWithAsphyxiation_IsVacuum()
    {
        // The resolved [needs verification] tag: a hard-vacuum death accrues 2 Blunt/s barotrauma
        // (species_base.yml) which out-accumulates the respirator's Asphyxiation — but the
        // asphyxiation is always PRESENT, and that co-presence is the vacuum signature.
        Assert.That(FirstDeathCauseClassifier.Classify(false, Damage(("Blunt", 120), ("Asphyxiation", 45))),
            Is.EqualTo(FirstDeathCause.Vacuum));
    }

    [Test]
    public void BluntDominant_WithoutAsphyxiation_IsMisadventure()
    {
        // A no-attacker crushing/fall in a pressurized hall: no asphyxiation → not a spacing.
        Assert.That(FirstDeathCauseClassifier.Classify(false, Damage(("Blunt", 120), ("Piercing", 10))),
            Is.EqualTo(FirstDeathCause.Misadventure));
    }

    [Test]
    public void BluntDominant_WithZeroAsphyxiationEntry_IsMisadventure()
    {
        // A zero-valued Asphyxiation key (the component tracks every type) is not "present".
        Assert.That(FirstDeathCauseClassifier.Classify(false, Damage(("Blunt", 120), ("Asphyxiation", 0))),
            Is.EqualTo(FirstDeathCause.Misadventure));
    }

    // --- BURN -----------------------------------------------------------------------------------

    [TestCase("Heat")]
    [TestCase("Shock")]
    public void DominantBurnType_IsBurn(string type)
    {
        Assert.That(FirstDeathCauseClassifier.Classify(false, Damage((type, 150), ("Blunt", 20))),
            Is.EqualTo(FirstDeathCause.Burn));
    }

    // --- MISADVENTURE (everything else) ----------------------------------------------------------

    [TestCase("Poison")]
    [TestCase("Radiation")]
    [TestCase("Bloodloss")]
    [TestCase("Caustic")]
    [TestCase("Slash")]
    [TestCase("Piercing")]
    [TestCase("Cellular")]
    public void DominantOtherType_NoAttacker_IsMisadventure(string type)
    {
        Assert.That(FirstDeathCauseClassifier.Classify(false, Damage((type, 90), ("Blunt", 10))),
            Is.EqualTo(FirstDeathCause.Misadventure));
    }

    // --- Tie-breaking (deterministic bucket preference: VACUUM > BURN > MISADVENTURE) ------------

    [Test]
    public void ExactTie_VacuumBeatsBurn()
    {
        Assert.That(FirstDeathCauseClassifier.Classify(false, Damage(("Cold", 50), ("Heat", 50))),
            Is.EqualTo(FirstDeathCause.Vacuum));
    }

    [Test]
    public void ExactTie_BurnBeatsMisadventure()
    {
        Assert.That(FirstDeathCauseClassifier.Classify(false, Damage(("Shock", 50), ("Poison", 50))),
            Is.EqualTo(FirstDeathCause.Burn));
    }

    [Test]
    public void ExactTie_IsInsensitiveToInsertionOrder()
    {
        var forward = FirstDeathCauseClassifier.Classify(false, Damage(("Heat", 50), ("Asphyxiation", 50)));
        var reverse = FirstDeathCauseClassifier.Classify(false, Damage(("Asphyxiation", 50), ("Heat", 50)));
        Assert.Multiple(() =>
        {
            Assert.That(forward, Is.EqualTo(FirstDeathCause.Vacuum));
            Assert.That(reverse, Is.EqualTo(forward), "dictionary enumeration order must never matter");
        });
    }

    // --- Closed-vocabulary ordinals (the copy pack's deterministic index rule depends on these) ---

    [Test]
    public void CauseOrdinals_MatchTheCopyPackContract()
    {
        Assert.Multiple(() =>
        {
            Assert.That((int) FirstDeathCause.Violence, Is.EqualTo(0));
            Assert.That((int) FirstDeathCause.Vacuum, Is.EqualTo(1));
            Assert.That((int) FirstDeathCause.Burn, Is.EqualTo(2));
            Assert.That((int) FirstDeathCause.Misadventure, Is.EqualTo(3));
            Assert.That((int) FirstDeathCause.Unknown, Is.EqualTo(4));
        });
    }
}
