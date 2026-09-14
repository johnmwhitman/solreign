using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Jobs;
using Content.Server._Solreign.FX.Consumers;
using Content.Shared._Solreign.FX.Consumers;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.Analyzers;

namespace Content.Benchmarks._Solreign.FX.Consumers;

public enum WorldFeedbackDamageRecipe : byte
{
    PhysicalSubtotal = 0,
    NonKinetic,
    Mixed,
}

/// <summary>
///     Compares the released-v1 positive-entry scan and threshold mapping from
///     403512014f16aaceba53bb90ef435c2393dca2e4 with the production v1.1 closed classifier.
///     Specifiers are constructed once in setup; both measured methods return the cue so the JIT
///     cannot discard the scan.
/// </summary>
[MemoryDiagnoser]
[CategoriesColumn]
[SimpleJob(RuntimeMoniker.Net10_0, warmupCount: 6, iterationCount: 15, invocationCount: 1_000_000)]
[Virtual]
public class SolreignWorldFeedbackClassifierBenchmarks
{
    // Source-bound to released v1 commit 403512014f16aaceba53bb90ef435c2393dca2e4.
    // Never reference the current production constant from the frozen baseline.
    private const float ReleasedV1HeavyImpactThreshold = 20f;

    private static readonly string[] PhysicalOrder =
    {
        "Blunt",
        "Slash",
        "Piercing",
        "Structural",
        "Heat",
        "Shock",
        "Holy",
        "Asphyxiation",
    };

    private static readonly string[] NonKineticOrder =
    {
        "Heat",
        "Shock",
        "Holy",
        "Asphyxiation",
        "Radiation",
        "Cold",
        "Cellular",
        "Caustic",
    };

    private static readonly string[] MixedOrder =
    {
        "Blunt",
        "Heat",
        "Slash",
        "Shock",
        "Piercing",
        "Holy",
        "Structural",
        "Asphyxiation",
    };

    private DamageSpecifier _damage = default!;

    [Params(0, 1, 3, 8)]
    public int EntryCount { get; set; }

    [ParamsAllValues]
    public WorldFeedbackDamageRecipe Recipe { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        ValidateFrozenBaseline();
        _damage = BuildDamage(EntryCount, Recipe);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ClassifierPair")]
    public SolreignDamageFeedbackCue ReleasedV1PositiveScanAndThreshold()
    {
        return ReleasedV1Classify(_damage);
    }

    [Benchmark]
    [BenchmarkCategory("ClassifierPair")]
    public SolreignDamageFeedbackCue ProductionV11ClosedClassifier()
    {
        return SolreignWorldFeedbackRules.ClassifyDamage(_damage);
    }

    private static DamageSpecifier BuildDamage(int entryCount, WorldFeedbackDamageRecipe recipe)
    {
        var order = recipe switch
        {
            WorldFeedbackDamageRecipe.PhysicalSubtotal => PhysicalOrder,
            WorldFeedbackDamageRecipe.NonKinetic => NonKineticOrder,
            WorldFeedbackDamageRecipe.Mixed => MixedOrder,
            _ => throw new ArgumentOutOfRangeException(nameof(recipe), recipe, null),
        };

        if (entryCount < 0 || entryCount > order.Length)
            throw new ArgumentOutOfRangeException(nameof(entryCount));

        var damage = new DamageSpecifier();
        for (var i = 0; i < entryCount; i++)
        {
            // The physical recipe's final four entries are deliberately non-positive scan load:
            // DamageSpecifier has only four distinct allowlisted physical IDs.
            var value = recipe == WorldFeedbackDamageRecipe.PhysicalSubtotal && i >= 4
                ? FixedPoint2.Zero
                : FixedPoint2.New(7);
            damage.DamageDict[order[i]] = value;
        }

        return damage;
    }

    private static SolreignDamageFeedbackCue ReleasedV1Classify(DamageSpecifier damage)
    {
        var total = FixedPoint2.Zero;
        foreach (var value in damage.DamageDict.Values)
        {
            if (value > FixedPoint2.Zero)
                total += value;
        }

        var appliedPositiveDamage = total.Float();
        if (!float.IsFinite(appliedPositiveDamage) || appliedPositiveDamage <= 0f)
            return SolreignDamageFeedbackCue.None;

        return appliedPositiveDamage >= ReleasedV1HeavyImpactThreshold
            ? SolreignDamageFeedbackCue.KineticHeavy
            : SolreignDamageFeedbackCue.KineticLight;
    }

    private static void ValidateFrozenBaseline()
    {
        if (SolreignWorldFeedbackRules.HeavyImpactThreshold != 20f)
            throw new InvalidOperationException("Current production threshold changed; revalidate v1.1 independently.");

        var nonPositive = new DamageSpecifier();
        nonPositive.DamageDict["Blunt"] = FixedPoint2.New(-1);

        var belowThreshold = new DamageSpecifier();
        belowThreshold.DamageDict["Blunt"] = FixedPoint2.New(19.99);

        var atThreshold = new DamageSpecifier();
        atThreshold.DamageDict["Blunt"] = FixedPoint2.New(20);

        if (ReleasedV1Classify(new DamageSpecifier()) != SolreignDamageFeedbackCue.None ||
            ReleasedV1Classify(nonPositive) != SolreignDamageFeedbackCue.None ||
            ReleasedV1Classify(belowThreshold) != SolreignDamageFeedbackCue.KineticLight ||
            ReleasedV1Classify(atThreshold) != SolreignDamageFeedbackCue.KineticHeavy ||
            SolreignWorldFeedbackRules.ClassifyDamage(new DamageSpecifier()) != SolreignDamageFeedbackCue.None ||
            SolreignWorldFeedbackRules.ClassifyDamage(nonPositive) != SolreignDamageFeedbackCue.None ||
            SolreignWorldFeedbackRules.ClassifyDamage(belowThreshold) != SolreignDamageFeedbackCue.KineticLight ||
            SolreignWorldFeedbackRules.ClassifyDamage(atThreshold) != SolreignDamageFeedbackCue.KineticHeavy)
        {
            throw new InvalidOperationException("World Feedback classifier baseline fixtures drifted.");
        }
    }
}

/// <summary>
///     Isolated production routing and fixed-child metric costs. These are not compared against
///     the classifier baseline because they represent different operations.
/// </summary>
[MemoryDiagnoser]
[CategoriesColumn]
[SimpleJob(RuntimeMoniker.Net10_0, warmupCount: 6, iterationCount: 15, invocationCount: 1_000_000)]
[Virtual]
public class SolreignWorldFeedbackRoutingBenchmarks
{
    [Benchmark]
    [BenchmarkCategory("DisabledGate")]
    public bool DisabledGate()
    {
        return SolreignWorldFeedbackDispatchRules.ShouldHandle(
            deliveryEnabled: false,
            observeEnabled: false);
    }

    [Benchmark]
    [BenchmarkCategory("DeliveryCandidate")]
    public (string EffectId, float Intensity, float Duration) DeliveryCandidate()
    {
        if (!SolreignWorldFeedbackDispatchRules.TryGetImpactCandidate(
                SolreignDamageFeedbackCue.KineticHeavy,
                out var candidate))
        {
            throw new InvalidOperationException("The production heavy-impact route was filtered.");
        }

        return (candidate.EffectId, candidate.Intensity, candidate.Duration);
    }

    [Benchmark]
    [BenchmarkCategory("ObserveOnlyCounter")]
    public void ObserveOnlyFixedCachedCounter()
    {
        SolreignWorldFeedbackMetrics.Record(SolreignWorldFeedbackObservation.KineticLightShadow);
    }
}

/// <summary>
///     Single-threaded cached-child plumbing throughput only. This is not a contention, live
///     server p95/p99, scrape, or retention benchmark.
/// </summary>
[MemoryDiagnoser]
[CategoriesColumn]
[SimpleJob(RuntimeMoniker.Net10_0, warmupCount: 6, iterationCount: 15, invocationCount: 1_024)]
[Virtual]
public class SolreignWorldFeedbackMetricBurstBenchmarks
{
    private const int BurstSize = 10_000;

    [Benchmark(OperationsPerInvoke = BurstSize)]
    [BenchmarkCategory("MetricBurst")]
    public void CachedCounterBurst()
    {
        for (var i = 0; i < BurstSize; i++)
        {
            SolreignWorldFeedbackMetrics.Record(
                SolreignWorldFeedbackObservation.KineticLightShadow);
        }
    }
}
