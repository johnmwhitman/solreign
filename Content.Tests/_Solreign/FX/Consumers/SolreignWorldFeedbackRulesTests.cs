#nullable enable
using Content.Shared._Solreign.FX.Consumers;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using NUnit.Framework;
using Robust.Shared.Configuration;

namespace Content.Tests._Solreign.FX.Consumers;

[TestFixture]
[TestOf(typeof(SolreignWorldFeedbackRules))]
public sealed class SolreignWorldFeedbackRulesTests
{
    /// <summary>
    ///     The world-feedback consumer is its own SERVERONLY switch, and ships ON.
    /// </summary>
    /// <remarks>
    ///     Was <c>ConsumerCVar_IsExactDormantServerOnlySwitch</c> and asserted a default of FALSE.
    ///     That was the dormant-on-arrival stance; the 2026-07-25 activation pass turned this
    ///     consumer on deliberately. The assertion outlived the decision, which is how a suite
    ///     starts being ignored.
    ///
    ///     Still pinned, because these are the parts that can break something: the exact CVar name
    ///     (box config references it) and SERVERONLY (a client must not decide its own FX policy).
    ///     The default now asserts TRUE on purpose — turning it back off should be a visible edit
    ///     here, not a silent drift.
    /// </remarks>
    [Test]
    public void ConsumerCVar_IsExactServerOnlySwitch_AndShipsEnabled()
    {
        var cvar = CCVars.SolreignFxWorldFeedbackV1Enabled;

        Assert.That(cvar.Name, Is.EqualTo("solreign.fx.world_feedback_v1"));
        Assert.That(cvar.DefaultValue, Is.True,
            "World feedback ships ON as of the 2026-07-25 activation pass. If it is being turned "
            + "off, change it here too so the decision is recorded rather than inferred.");
        Assert.That(cvar.Flags, Is.EqualTo(CVar.SERVERONLY));
    }

    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void ClassifyAppliedDamage_NonPositiveOrNonFinite_ReturnsNone(float appliedPositiveDamage)
    {
        Assert.That(
            SolreignWorldFeedbackRules.ClassifyAppliedDamage(appliedPositiveDamage),
            Is.EqualTo(SolreignImpactCue.None));
    }

    [Test]
    public void ClassifyAppliedDamage_BelowThreshold_ReturnsLight()
    {
        Assert.That(
            SolreignWorldFeedbackRules.ClassifyAppliedDamage(19.99f),
            Is.EqualTo(SolreignImpactCue.Light));
    }

    [Test]
    public void ClassifyAppliedDamage_AtThreshold_ReturnsHeavy()
    {
        Assert.That(
            SolreignWorldFeedbackRules.ClassifyAppliedDamage(20f),
            Is.EqualTo(SolreignImpactCue.Heavy));
    }

    [Test]
    [TestCase("Blunt")]
    [TestCase("Slash")]
    [TestCase("Piercing")]
    [TestCase("Structural")]
    public void AppliedPositiveDamage_AllowedPhysicalType_ReturnsPositiveSubtotal(string damageType)
    {
        var damage = new DamageSpecifier();
        damage.DamageDict[damageType] = FixedPoint2.New(22);

        Assert.That(SolreignWorldFeedbackRules.AppliedPositiveDamage(damage), Is.EqualTo(22f));
    }

    [TestCase("Shock")]
    [TestCase("Holy")]
    [TestCase("Heat")]
    [TestCase("Asphyxiation")]
    public void AppliedPositiveDamage_ReleasedNonPhysicalType_ReturnsZero(string damageType)
    {
        var damage = new DamageSpecifier();
        damage.DamageDict[damageType] = FixedPoint2.New(22);

        Assert.That(SolreignWorldFeedbackRules.AppliedPositiveDamage(damage), Is.Zero);
    }

    [Test]
    public void AppliedPositiveDamage_UnknownEmptyAndDefaultTypes_ReturnZero()
    {
        var unknown = new DamageSpecifier();
        unknown.DamageDict["UnreleasedDamageType"] = FixedPoint2.New(22);

        var empty = new DamageSpecifier();
        empty.DamageDict[string.Empty] = FixedPoint2.New(22);

        var defaultType = new DamageSpecifier();
        defaultType.DamageDict[default] = FixedPoint2.New(22);

        Assert.Multiple(() =>
        {
            Assert.That(SolreignWorldFeedbackRules.AppliedPositiveDamage(unknown), Is.Zero);
            Assert.That(SolreignWorldFeedbackRules.AppliedPositiveDamage(empty), Is.Zero);
            Assert.That(SolreignWorldFeedbackRules.AppliedPositiveDamage(defaultType), Is.Zero);
        });
    }

    [Test]
    public void AppliedPositiveDamage_NonPositivePhysicalValues_ReturnZero()
    {
        var damage = new DamageSpecifier();
        damage.DamageDict["Blunt"] = FixedPoint2.Zero;
        damage.DamageDict["Slash"] = FixedPoint2.New(-10);

        Assert.That(SolreignWorldFeedbackRules.AppliedPositiveDamage(damage), Is.Zero);
    }

    [Test]
    public void AppliedPositiveDamage_MixedPhysicalAndInternalDamage_ReturnsPhysicalSubtotal()
    {
        var damage = new DamageSpecifier();
        damage.DamageDict["Blunt"] = FixedPoint2.New(19.99);
        damage.DamageDict["Heat"] = FixedPoint2.New(20);

        Assert.That(SolreignWorldFeedbackRules.AppliedPositiveDamage(damage), Is.EqualTo(19.99f));
    }

    [Test]
    public void AppliedPositiveDamage_HealingAndPositivePhysicalDamage_ReturnsPositivePhysicalSubtotal()
    {
        var damage = new DamageSpecifier();
        damage.DamageDict["Blunt"] = FixedPoint2.New(22);
        damage.DamageDict["Heat"] = FixedPoint2.New(-50);

        Assert.That(SolreignWorldFeedbackRules.AppliedPositiveDamage(damage), Is.EqualTo(22f));
    }

    [Test]
    public void AppliedPositiveDamage_MultiplePositivePhysicalEntries_ReturnsCombinedSubtotal()
    {
        var damage = new DamageSpecifier();
        damage.DamageDict["Blunt"] = FixedPoint2.New(10);
        damage.DamageDict["Slash"] = FixedPoint2.New(7);
        damage.DamageDict["Piercing"] = FixedPoint2.New(3);

        Assert.That(SolreignWorldFeedbackRules.AppliedPositiveDamage(damage), Is.EqualTo(20f));
    }

    [TestCase("Blunt", 0)]
    [TestCase("Slash", -1)]
    public void ClassifyDamage_NoPositiveEntries_ReturnsNone(string damageType, int amount)
    {
        var damage = new DamageSpecifier();
        damage.DamageDict[damageType] = FixedPoint2.New(amount);

        Assert.That(SolreignWorldFeedbackRules.ClassifyDamage(damage), Is.EqualTo(SolreignDamageFeedbackCue.None));
    }

    [Test]
    public void ClassifyDamage_EmptyDamage_ReturnsNone()
    {
        Assert.That(
            SolreignWorldFeedbackRules.ClassifyDamage(new DamageSpecifier()),
            Is.EqualTo(SolreignDamageFeedbackCue.None));
    }

    [TestCase("UnreleasedDamageType")]
    [TestCase("Heat")]
    [TestCase("Shock")]
    public void ClassifyDamage_PositiveNonKineticDamage_ReturnsNonKinetic(string damageType)
    {
        var damage = new DamageSpecifier();
        damage.DamageDict[damageType] = FixedPoint2.New(22);

        Assert.That(SolreignWorldFeedbackRules.ClassifyDamage(damage), Is.EqualTo(SolreignDamageFeedbackCue.NonKinetic));
    }

    [TestCase("Blunt", 19.99, SolreignDamageFeedbackCue.KineticLight)]
    [TestCase("Slash", 19.99, SolreignDamageFeedbackCue.KineticLight)]
    [TestCase("Piercing", 19.99, SolreignDamageFeedbackCue.KineticLight)]
    [TestCase("Structural", 19.99, SolreignDamageFeedbackCue.KineticLight)]
    [TestCase("Blunt", 20, SolreignDamageFeedbackCue.KineticHeavy)]
    public void ClassifyDamage_PositivePhysicalDamage_ReturnsThresholdCue(
        string damageType,
        double amount,
        SolreignDamageFeedbackCue expected)
    {
        var damage = new DamageSpecifier();
        damage.DamageDict[damageType] = FixedPoint2.New(amount);
        damage.DamageDict["Heat"] = FixedPoint2.New(-50);

        Assert.That(SolreignWorldFeedbackRules.ClassifyDamage(damage), Is.EqualTo(expected));
    }

    [Test]
    public void ClassifyDamage_MixedPositiveDamage_UsesPhysicalSubtotal()
    {
        var damage = new DamageSpecifier();
        damage.DamageDict["Blunt"] = FixedPoint2.New(10);
        damage.DamageDict["Heat"] = FixedPoint2.New(50);

        Assert.That(SolreignWorldFeedbackRules.ClassifyDamage(damage), Is.EqualTo(SolreignDamageFeedbackCue.KineticLight));
    }
}
