using Content.Shared._Solreign.FX;
using NUnit.Framework;

namespace Content.Tests._Solreign.FX;

/// <summary>
///     Unit coverage for the pure, engine-free <see cref="SolreignFxTokenBucket"/> rate limiter
///     (spec §3.1 scaffolding) — W2's egress budget and client intake cap both wire this shape.
///     Its own class remarks promise it ships "already-tested"; this file is what makes that true.
/// </summary>
[TestFixture]
[TestOf(typeof(SolreignFxTokenBucket))]
public sealed class SolreignFxTokenBucketTests
{
    [Test]
    public void StartsFullyToppedUp()
    {
        var bucket = new SolreignFxTokenBucket(refillPerSecond: 2f, burstCapacity: 10f, nowSeconds: 0d);

        Assert.That(bucket.Available(0d), Is.EqualTo(10f));
    }

    [Test]
    public void TryConsume_WithinBudget_DeductsAndSucceeds()
    {
        var bucket = new SolreignFxTokenBucket(refillPerSecond: 2f, burstCapacity: 10f, nowSeconds: 0d);

        var ok = bucket.TryConsume(0d, 4f);

        Assert.That(ok, Is.True);
        Assert.That(bucket.Available(0d), Is.EqualTo(6f));
    }

    [Test]
    public void TryConsume_OverBudget_FailsAndLeavesBucketUntouched()
    {
        var bucket = new SolreignFxTokenBucket(refillPerSecond: 2f, burstCapacity: 10f, nowSeconds: 0d);

        var ok = bucket.TryConsume(0d, 11f);

        Assert.That(ok, Is.False);
        Assert.That(bucket.Available(0d), Is.EqualTo(10f), "a failed consume must never partially deduct");
    }

    [Test]
    public void Refill_AccruesOverTime_CappedAtBurstCapacity()
    {
        var bucket = new SolreignFxTokenBucket(refillPerSecond: 2f, burstCapacity: 10f, nowSeconds: 0d);
        bucket.TryConsume(0d, 10f); // drain to zero

        Assert.That(bucket.Available(1d), Is.EqualTo(2f)); // 1s * 2/s refill
        Assert.That(bucket.Available(10d), Is.EqualTo(10f), "refill must cap at burst capacity, never overshoot");
    }

    [Test]
    public void Refill_NoElapsedTime_DoesNotDoubleGrant()
    {
        var bucket = new SolreignFxTokenBucket(refillPerSecond: 2f, burstCapacity: 10f, nowSeconds: 0d);
        bucket.TryConsume(0d, 5f);

        var firstCheck = bucket.Available(0d);
        var secondCheck = bucket.Available(0d);

        Assert.That(firstCheck, Is.EqualTo(secondCheck));
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    [TestCase(-1f)]
    public void TryConsume_NonFiniteOrNegativeCost_FailsClosedWithoutThrowing(float poisonCost)
    {
        var bucket = new SolreignFxTokenBucket(refillPerSecond: 2f, burstCapacity: 10f, nowSeconds: 0d);

        bool ok = true;
        Assert.DoesNotThrow(() => ok = bucket.TryConsume(0d, poisonCost));
        Assert.That(ok, Is.False);
        Assert.That(bucket.Available(0d), Is.EqualTo(10f), "a rejected poisoned cost must never grant free tokens or partially deduct");
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void Constructor_NonFiniteRefillOrBurst_FailsClosedToZeroWithoutThrowing(float poison)
    {
        SolreignFxTokenBucket bucket = default;
        Assert.DoesNotThrow(() => bucket = new SolreignFxTokenBucket(poison, poison, 0d));

        Assert.That(bucket.RefillPerSecond, Is.EqualTo(0f));
        Assert.That(bucket.BurstCapacity, Is.EqualTo(0f));
    }

    [Test]
    public void Available_NonFiniteNow_DoesNotThrowAndSkipsRefill()
    {
        var bucket = new SolreignFxTokenBucket(refillPerSecond: 2f, burstCapacity: 10f, nowSeconds: 0d);
        bucket.TryConsume(0d, 5f);

        float result = -1f;
        Assert.DoesNotThrow(() => result = bucket.Available(double.NaN));
        Assert.That(result, Is.EqualTo(5f));
    }
}
