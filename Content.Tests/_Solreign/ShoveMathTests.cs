using Content.Shared._Solreign.Combat;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(ShoveMath))]
public sealed class ShoveMathTests
{
    // --- PushDistance ---

    [Test]
    public void PushDistance_ZeroProbability_IsMinimum()
    {
        Assert.That(ShoveMath.PushDistance(0f), Is.EqualTo(ShoveMath.MinPushDistance).Within(1e-6));
    }

    [Test]
    public void PushDistance_FullProbability_IsMaximum()
    {
        Assert.That(ShoveMath.PushDistance(1f), Is.EqualTo(ShoveMath.MaxPushDistance).Within(1e-6));
    }

    [Test]
    public void PushDistance_HalfProbability_IsMidpoint()
    {
        var expected = (ShoveMath.MinPushDistance + ShoveMath.MaxPushDistance) / 2f;
        Assert.That(ShoveMath.PushDistance(0.5f), Is.EqualTo(expected).Within(1e-6));
    }

    [Test]
    public void PushDistance_OverOne_ClampsToMaximum()
    {
        Assert.That(ShoveMath.PushDistance(5f), Is.EqualTo(ShoveMath.MaxPushDistance).Within(1e-6));
    }

    [Test]
    public void PushDistance_Negative_ClampsToMinimum()
    {
        Assert.That(ShoveMath.PushDistance(-2f), Is.EqualTo(ShoveMath.MinPushDistance).Within(1e-6));
    }

    // --- PushSpeed ---

    [Test]
    public void PushSpeed_ZeroProbability_IsMinimum()
    {
        Assert.That(ShoveMath.PushSpeed(0f), Is.EqualTo(ShoveMath.MinPushSpeed).Within(1e-6));
    }

    [Test]
    public void PushSpeed_FullProbability_IsMaximum()
    {
        Assert.That(ShoveMath.PushSpeed(1f), Is.EqualTo(ShoveMath.MaxPushSpeed).Within(1e-6));
    }

    [Test]
    public void PushSpeed_OverOne_ClampsToMaximum()
    {
        Assert.That(ShoveMath.PushSpeed(1.5f), Is.EqualTo(ShoveMath.MaxPushSpeed).Within(1e-6));
    }

    [Test]
    public void PushSpeed_Negative_ClampsToMinimum()
    {
        Assert.That(ShoveMath.PushSpeed(-1f), Is.EqualTo(ShoveMath.MinPushSpeed).Within(1e-6));
    }

    // --- BonusStaminaDamage ---

    [Test]
    public void BonusStaminaDamage_ZeroProbability_IsZero()
    {
        Assert.That(ShoveMath.BonusStaminaDamage(0f), Is.EqualTo(0f));
    }

    [Test]
    public void BonusStaminaDamage_FullProbability_IsMaximum()
    {
        Assert.That(ShoveMath.BonusStaminaDamage(1f), Is.EqualTo(ShoveMath.MaxBonusStaminaDamage).Within(1e-6));
    }

    [Test]
    public void BonusStaminaDamage_HalfProbability_IsHalfMaximum()
    {
        Assert.That(ShoveMath.BonusStaminaDamage(0.5f), Is.EqualTo(ShoveMath.MaxBonusStaminaDamage / 2f).Within(1e-6));
    }

    [Test]
    public void BonusStaminaDamage_OverOne_ClampsToMaximum()
    {
        Assert.That(ShoveMath.BonusStaminaDamage(3f), Is.EqualTo(ShoveMath.MaxBonusStaminaDamage).Within(1e-6));
    }

    [Test]
    public void BonusStaminaDamage_Negative_ClampsToZero()
    {
        Assert.That(ShoveMath.BonusStaminaDamage(-1f), Is.EqualTo(0f));
    }

    // --- Cross-check: a cleaner disarm should never push, throw slower, or hit softer than a
    // messier one that still succeeded. ---

    [Test]
    public void CleanerShove_NeverWeakerThanMessierShove()
    {
        Assert.That(ShoveMath.PushDistance(0.9f), Is.GreaterThanOrEqualTo(ShoveMath.PushDistance(0.2f)));
        Assert.That(ShoveMath.PushSpeed(0.9f), Is.GreaterThanOrEqualTo(ShoveMath.PushSpeed(0.2f)));
        Assert.That(ShoveMath.BonusStaminaDamage(0.9f), Is.GreaterThanOrEqualTo(ShoveMath.BonusStaminaDamage(0.2f)));
    }
}
