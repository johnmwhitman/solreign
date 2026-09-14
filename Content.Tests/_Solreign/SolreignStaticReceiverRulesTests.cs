using System;
using Content.Server._Solreign.EasterEggs;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(SolreignStaticReceiverRules))]
public sealed class SolreignStaticReceiverRulesTests
{
    private const uint SaltA = 0x5EA51DE1u;
    private const uint SaltB = 0xC0FFEE42u;

    private static readonly TimeSpan Minute = TimeSpan.FromSeconds(60);

    // --- TimeBucket ---

    [Test]
    public void TimeBucket_SameWindow_SameBucket()
    {
        var a = SolreignStaticReceiverRules.TimeBucket(TimeSpan.FromSeconds(120), Minute);
        var b = SolreignStaticReceiverRules.TimeBucket(TimeSpan.FromSeconds(179), Minute);

        Assert.That(a, Is.EqualTo(b));
    }

    [Test]
    public void TimeBucket_NextWindow_DifferentBucket()
    {
        var a = SolreignStaticReceiverRules.TimeBucket(TimeSpan.FromSeconds(179), Minute);
        var b = SolreignStaticReceiverRules.TimeBucket(TimeSpan.FromSeconds(180), Minute);

        Assert.That(a, Is.Not.EqualTo(b));
    }

    [Test]
    public void TimeBucket_NonPositiveBucketLength_FallsBackToOneMinute_DoesNotThrow()
    {
        var viaZero = SolreignStaticReceiverRules.TimeBucket(TimeSpan.FromSeconds(120), TimeSpan.Zero);
        var viaMinute = SolreignStaticReceiverRules.TimeBucket(TimeSpan.FromSeconds(120), Minute);

        Assert.That(viaZero, Is.EqualTo(viaMinute));
    }

    // --- Roll: determinism + independence + range ---

    [Test]
    public void Roll_SameInputs_AlwaysSameOutput()
    {
        var a = SolreignStaticReceiverRules.Roll(1234, 7, 42, SaltA);
        var b = SolreignStaticReceiverRules.Roll(1234, 7, 42, SaltA);

        Assert.That(a, Is.EqualTo(b));
    }

    [Test]
    public void Roll_DifferentBucket_DifferentOutput()
    {
        var a = SolreignStaticReceiverRules.Roll(1234, 7, 42, SaltA);
        var b = SolreignStaticReceiverRules.Roll(1234, 7, 43, SaltA);

        Assert.That(a, Is.Not.EqualTo(b));
    }

    [Test]
    public void Roll_DifferentRound_DifferentOutput()
    {
        var a = SolreignStaticReceiverRules.Roll(1234, 7, 42, SaltA);
        var b = SolreignStaticReceiverRules.Roll(1234, 8, 42, SaltA);

        Assert.That(a, Is.Not.EqualTo(b));
    }

    [Test]
    public void Roll_DifferentSalt_IndependentOutput()
    {
        var a = SolreignStaticReceiverRules.Roll(1234, 7, 42, SaltA);
        var b = SolreignStaticReceiverRules.Roll(1234, 7, 42, SaltB);

        Assert.That(a, Is.Not.EqualTo(b));
    }

    [Test]
    public void Roll_ManySamples_AllInUnitInterval()
    {
        for (var bucket = 0L; bucket < 1000; bucket++)
        {
            var roll = SolreignStaticReceiverRules.Roll(99, 3, bucket, SaltA);
            Assert.That(roll, Is.GreaterThanOrEqualTo(0d).And.LessThan(1d), $"bucket {bucket}");
        }
    }

    [Test]
    public void Roll_ManySamples_RoughlyUniform()
    {
        // Coarse sanity: over 2000 buckets, the fraction under 0.6 should be near 0.6 — this is
        // the property the nearby-crackle chance actually rides on. Deterministic (no test flake):
        // same hash, same inputs, same count every run.
        var under = 0;
        const int n = 2000;
        for (var bucket = 0L; bucket < n; bucket++)
        {
            if (SolreignStaticReceiverRules.Roll(555, 12, bucket, SaltA) < 0.6)
                under++;
        }

        Assert.That(under / (double) n, Is.EqualTo(0.6).Within(0.05));
    }

    // --- ShouldCrackle: the noisy-signal contract ---

    [Test]
    public void ShouldCrackle_SourceNearby_UsesNearbyChance()
    {
        Assert.That(SolreignStaticReceiverRules.ShouldCrackle(true, 0.59, 0.6f, 0.12f), Is.True);
        Assert.That(SolreignStaticReceiverRules.ShouldCrackle(true, 0.61, 0.6f, 0.12f), Is.False);
    }

    [Test]
    public void ShouldCrackle_NearbySource_CanMiss()
    {
        // The redesign's core anti-wallhack property: a source in range does NOT guarantee a crackle.
        Assert.That(SolreignStaticReceiverRules.ShouldCrackle(true, 0.99, 0.6f, 0.12f), Is.False);
    }

    [Test]
    public void ShouldCrackle_NoSource_CanFalsePositive()
    {
        // And the inverse: silence does not guarantee safety — an empty room can still crackle.
        Assert.That(SolreignStaticReceiverRules.ShouldCrackle(false, 0.11, 0.6f, 0.12f), Is.True);
        Assert.That(SolreignStaticReceiverRules.ShouldCrackle(false, 0.13, 0.6f, 0.12f), Is.False);
    }

    [Test]
    public void ShouldCrackle_ChancesOutsideUnitInterval_ClampDefensively()
    {
        // chance > 1 clamps to always-fire; negative clamps to never-fire.
        Assert.That(SolreignStaticReceiverRules.ShouldCrackle(true, 0.999, 5f, 0.12f), Is.True);
        Assert.That(SolreignStaticReceiverRules.ShouldCrackle(false, 0.0, 0.6f, -1f), Is.False);
    }

    // --- CrackleVariant: tiered text ---

    [Test]
    public void CrackleVariant_UnderRareChance_IsRareVariant()
    {
        Assert.That(SolreignStaticReceiverRules.CrackleVariant(0.10, 0.15f), Is.EqualTo(2));
    }

    [Test]
    public void CrackleVariant_CommonRange_SplitsBetweenFirstAndSecond()
    {
        // rare = 0.15 -> [0.15, 0.575) is variant 0, [0.575, 1) is variant 1.
        Assert.That(SolreignStaticReceiverRules.CrackleVariant(0.2, 0.15f), Is.EqualTo(0));
        Assert.That(SolreignStaticReceiverRules.CrackleVariant(0.5, 0.15f), Is.EqualTo(0));
        Assert.That(SolreignStaticReceiverRules.CrackleVariant(0.6, 0.15f), Is.EqualTo(1));
        Assert.That(SolreignStaticReceiverRules.CrackleVariant(0.99, 0.15f), Is.EqualTo(1));
    }

    [Test]
    public void CrackleVariant_ZeroRareChance_NeverRare()
    {
        Assert.That(SolreignStaticReceiverRules.CrackleVariant(0.0, 0f), Is.Not.EqualTo(2));
        Assert.That(SolreignStaticReceiverRules.CrackleVariant(0.49, 0f), Is.EqualTo(0));
        Assert.That(SolreignStaticReceiverRules.CrackleVariant(0.51, 0f), Is.EqualTo(1));
    }

    [Test]
    public void CrackleVariant_RareChanceAboveOne_ClampsToAlwaysRare()
    {
        Assert.That(SolreignStaticReceiverRules.CrackleVariant(0.999, 5f), Is.EqualTo(2));
    }

    [Test]
    public void CrackleVariant_AllRolls_YieldValidIndices()
    {
        for (var i = 0; i < 100; i++)
        {
            var variant = SolreignStaticReceiverRules.CrackleVariant(i / 100d, 0.15f);
            Assert.That(variant, Is.InRange(0, 2), $"roll {i / 100d}");
        }
    }
}
