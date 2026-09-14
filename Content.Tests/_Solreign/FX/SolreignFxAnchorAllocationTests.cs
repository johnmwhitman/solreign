#nullable enable
using System;
using System.Runtime.CompilerServices;
using Content.Shared._Solreign.FX;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Tests._Solreign.FX;

/// <summary>
///     Guards the shared anchor identity used by both client pool matching and server egress
///     coalescing. Equality is a hot scan path, so matching anchors must not allocate.
/// </summary>
[TestFixture]
[NonParallelizable]
[TestOf(typeof(SolreignFxAnchorKey))]
public sealed class SolreignFxAnchorAllocationTests
{
    private const int WarmupIterations = 100_000;
    private const int MeasuredIterations = 100_000;
    private const int MeasurementWindows = 3;

    [Test]
    public void Equals_PreservesAnchorIdentitySemantics()
    {
        var defaultAnchor = default(SolreignFxAnchorKey);
        var entity = EntityAnchor(7);
        var sameEntity = EntityAnchor(7);
        var differentEntity = EntityAnchor(8);
        var coordinates = CoordinateAnchor(5f);
        var sameCoordinates = CoordinateAnchor(5f);
        var nearbyCoordinates = CoordinateAnchor(5.0001f);

        Assert.Multiple(() =>
        {
            Assert.That(defaultAnchor.Equals(defaultAnchor), Is.True);
            Assert.That(defaultAnchor.Equals(entity), Is.False);
            Assert.That(defaultAnchor.Equals(coordinates), Is.False);
            Assert.That(entity.Equals(sameEntity), Is.True);
            Assert.That(entity.Equals(differentEntity), Is.False);
            Assert.That(entity.Equals(coordinates), Is.False);
            Assert.That(coordinates.Equals(sameCoordinates), Is.True);
            Assert.That(coordinates.Equals(nearbyCoordinates), Is.False);
            Assert.That(entity.GetHashCode(), Is.EqualTo(sameEntity.GetHashCode()));
            Assert.That(coordinates.GetHashCode(), Is.EqualTo(sameCoordinates.GetHashCode()));
        });
    }

    [Test]
    public void Equals_EntityAnchors_AllocatesNoManagedBytes()
    {
        AssertComparisonAllocatesNoBytes(EntityAnchor(7), EntityAnchor(7), expected: true, "equal entity");
        AssertComparisonAllocatesNoBytes(EntityAnchor(7), EntityAnchor(8), expected: false, "unequal entity");
    }

    [Test]
    public void Equals_CoordinateAnchors_AllocatesNoManagedBytes()
    {
        AssertComparisonAllocatesNoBytes(CoordinateAnchor(5f), CoordinateAnchor(5f), expected: true, "equal coordinates");
        AssertComparisonAllocatesNoBytes(CoordinateAnchor(5f), CoordinateAnchor(5.0001f), expected: false, "unequal coordinates");
    }

    [Test]
    public void Equals_CrossKindAnchors_AllocatesNoManagedBytes()
    {
        AssertComparisonAllocatesNoBytes(EntityAnchor(7), CoordinateAnchor(5f), expected: false, "entity versus coordinates");
        AssertComparisonAllocatesNoBytes(CoordinateAnchor(5f), EntityAnchor(7), expected: false, "coordinates versus entity");
    }

    private static SolreignFxAnchorKey EntityAnchor(int id)
    {
        return SolreignFxAnchorKey.FromEntity(new NetEntity(id));
    }

    private static SolreignFxAnchorKey CoordinateAnchor(float x)
    {
        return SolreignFxAnchorKey.FromCoordinates(new NetCoordinates(new NetEntity(1), x, 0f));
    }

    private static void AssertComparisonAllocatesNoBytes(
        SolreignFxAnchorKey left,
        SolreignFxAnchorKey right,
        bool expected,
        string scenario)
    {
        var expectedMatches = expected ? MeasuredIterations : 0;

        var warmupMatches = CountMatches(in left, in right, WarmupIterations);
        Assert.That(warmupMatches, Is.EqualTo(expected ? WarmupIterations : 0));

        for (var window = 0; window < MeasurementWindows; window++)
        {
            var threadId = Environment.CurrentManagedThreadId;
            var before = GC.GetAllocatedBytesForCurrentThread();
            var matches = CountMatches(in left, in right, MeasuredIterations);
            var after = GC.GetAllocatedBytesForCurrentThread();

            Assert.Multiple(() =>
            {
                Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(threadId),
                    $"{scenario}, window {window}: allocation counters cannot be compared across threads");
                Assert.That(matches, Is.EqualTo(expectedMatches),
                    $"{scenario}, window {window}: the comparison result changed");
                Assert.That(after - before, Is.Zero,
                    $"{scenario}, window {window}: equality allocated {after - before} managed bytes");
            });
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Compare(in SolreignFxAnchorKey left, in SolreignFxAnchorKey right)
    {
        return left.Equals(right);
    }

    private static int CountMatches(
        in SolreignFxAnchorKey left,
        in SolreignFxAnchorKey right,
        int iterations)
    {
        var matches = 0;

        for (var i = 0; i < iterations; i++)
        {
            if (Compare(in left, in right))
                matches++;
        }

        return matches;
    }
}
