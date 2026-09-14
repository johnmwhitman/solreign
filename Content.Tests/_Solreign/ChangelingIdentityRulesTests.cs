using System;
using Content.Server._Solreign.Changeling;
using Content.Shared.Mobs;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(ChangelingIdentityRules))]
public sealed class ChangelingIdentityRulesTests
{
    private static TimeSpan Secs(double s) => TimeSpan.FromSeconds(s);

    // --- CanAbsorb: precondition ordering + refusal reasons (spec §1, §3) ---

    [Test]
    public void CanAbsorb_TargetNotCritical_Denied()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                ChangelingIdentityRules.CanAbsorb(MobState.Alive, false, Secs(0), Secs(0), 0, 6),
                Is.EqualTo(AbsorbDenialReason.TargetNotCritical));

            Assert.That(
                ChangelingIdentityRules.CanAbsorb(MobState.Dead, false, Secs(0), Secs(0), 0, 6),
                Is.EqualTo(AbsorbDenialReason.TargetNotCritical));
        });
    }

    [Test]
    public void CanAbsorb_CriticalTarget_Allowed()
    {
        Assert.That(
            ChangelingIdentityRules.CanAbsorb(MobState.Critical, false, Secs(0), Secs(0), 0, 6),
            Is.EqualTo(AbsorbDenialReason.None));
    }

    [Test]
    public void CanAbsorb_AlreadyAbsorbedSource_Denied()
    {
        Assert.That(
            ChangelingIdentityRules.CanAbsorb(MobState.Critical, true, Secs(100), Secs(0), 0, 6),
            Is.EqualTo(AbsorbDenialReason.TargetAlreadyAbsorbed));
    }

    [Test]
    public void CanAbsorb_OnCooldown_Denied()
    {
        Assert.That(
            ChangelingIdentityRules.CanAbsorb(MobState.Critical, false, Secs(10), Secs(20), 0, 6),
            Is.EqualTo(AbsorbDenialReason.OnCooldown));
    }

    [Test]
    public void CanAbsorb_CooldownJustExpired_Allowed()
    {
        Assert.That(
            ChangelingIdentityRules.CanAbsorb(MobState.Critical, false, Secs(20), Secs(20), 0, 6),
            Is.EqualTo(AbsorbDenialReason.None));
    }

    [Test]
    public void CanAbsorb_AliasLimitReached_Denied()
    {
        Assert.That(
            ChangelingIdentityRules.CanAbsorb(MobState.Critical, false, Secs(0), Secs(0), 6, 6),
            Is.EqualTo(AbsorbDenialReason.AliasLimitReached));
    }

    [Test]
    public void CanAbsorb_BelowAliasLimit_Allowed()
    {
        Assert.That(
            ChangelingIdentityRules.CanAbsorb(MobState.Critical, false, Secs(0), Secs(0), 5, 6),
            Is.EqualTo(AbsorbDenialReason.None));
    }

    [Test]
    public void CanAbsorb_ChecksNotCriticalBeforeOtherReasons()
    {
        // Even if every OTHER gate would also fail, "not critical" should win — it's checked first,
        // and it's the one PG-rule-critical reason (spec §1) that must never be silently overridden.
        Assert.That(
            ChangelingIdentityRules.CanAbsorb(MobState.Dead, true, Secs(10), Secs(20), 6, 6),
            Is.EqualTo(AbsorbDenialReason.TargetNotCritical));
    }

    // --- NextAbsorbAllowedAt ---

    [Test]
    public void NextAbsorbAllowedAt_AddsCooldownToNow()
    {
        Assert.That(ChangelingIdentityRules.NextAbsorbAllowedAt(Secs(100), 20f), Is.EqualTo(Secs(120)));
    }

    // --- CanTransform / MostRecentAliasIndex (spec §2.2) ---

    [Test]
    public void CanTransform_NoAliases_False()
    {
        Assert.That(ChangelingIdentityRules.CanTransform(0), Is.False);
    }

    [Test]
    public void CanTransform_HasAliases_True()
    {
        Assert.That(ChangelingIdentityRules.CanTransform(3), Is.True);
    }

    [Test]
    public void MostRecentAliasIndex_ReturnsLastIndex()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ChangelingIdentityRules.MostRecentAliasIndex(1), Is.EqualTo(0));
            Assert.That(ChangelingIdentityRules.MostRecentAliasIndex(4), Is.EqualTo(3));
        });
    }

    [Test]
    public void MostRecentAliasIndex_NoAliases_ReturnsSentinel()
    {
        Assert.That(ChangelingIdentityRules.MostRecentAliasIndex(0), Is.EqualTo(-1));
    }
}
