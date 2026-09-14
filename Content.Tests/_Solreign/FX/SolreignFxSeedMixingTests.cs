using Content.Shared._Solreign.FX;
using NUnit.Framework;

namespace Content.Tests._Solreign.FX;

/// <summary>
///     Determinism and safety coverage for the frozen <see cref="SolreignFxSeedMixing"/> finalizer
///     (spec §1.2, cdx #18) — the mission's explicitly-named "seed determinism" test requirement.
///     Renderer code is prohibited from <c>Math.Abs(seed) % n</c>/signed-modulo indexing precisely
///     because those are corruption hazards; this proves the sanctioned replacement
///     (<see cref="SolreignFxSeedMixing.Index"/>) is deterministic, bounded, and never divides by
///     zero.
/// </summary>
[TestFixture]
[TestOf(typeof(SolreignFxSeedMixing))]
public sealed class SolreignFxSeedMixingTests
{
    [Test]
    public void Mix_SameInput_AlwaysProducesSameOutput()
    {
        var a = SolreignFxSeedMixing.Mix(12345u);
        var b = SolreignFxSeedMixing.Mix(12345u);

        Assert.That(a, Is.EqualTo(b));
    }

    [Test]
    public void Mix_DifferentInputs_TypicallyProduceDifferentOutputs()
    {
        // Not a formal avalanche/distribution proof — just a sanity check that adjacent seeds
        // don't collide trivially (e.g. an identity/no-op "mix").
        var results = new System.Collections.Generic.HashSet<uint>();
        for (uint i = 0; i < 256; i++)
            results.Add(SolreignFxSeedMixing.Mix(i));

        Assert.That(results.Count, Is.EqualTo(256), "expected 256 distinct outputs for 256 distinct small seeds");
    }

    [Test]
    public void Mix_ZeroSeed_DoesNotThrowAndIsDeterministic()
    {
        var first = SolreignFxSeedMixing.Mix(0u);
        var second = SolreignFxSeedMixing.Mix(0u);
        Assert.That(first, Is.EqualTo(second));
    }

    [Test]
    public void Mix_MaxValueSeed_DoesNotThrowAndIsDeterministic()
    {
        var first = SolreignFxSeedMixing.Mix(uint.MaxValue);
        var second = SolreignFxSeedMixing.Mix(uint.MaxValue);
        Assert.That(first, Is.EqualTo(second));
    }

    // --- Index: the ONLY sanctioned way to turn a seed into a bounded index ---

    [Test]
    public void Index_NIsZero_ReturnsZeroRatherThanDividingByZero()
    {
        uint result = 999;
        Assert.DoesNotThrow(() => result = SolreignFxSeedMixing.Index(12345u, 0u));
        Assert.That(result, Is.EqualTo(0u));
    }

    [TestCase(1u)]
    [TestCase(2u)]
    [TestCase(7u)]
    [TestCase(256u)]
    public void Index_IsAlwaysWithinBounds(uint n)
    {
        for (uint seed = 0; seed < 1000; seed++)
        {
            var index = SolreignFxSeedMixing.Index(seed, n);
            Assert.That(index, Is.LessThan(n), $"seed={seed}, n={n}");
        }
    }

    [Test]
    public void Index_SameSeedAndN_AlwaysProducesSameIndex()
    {
        var first = SolreignFxSeedMixing.Index(42u, 6u);
        var second = SolreignFxSeedMixing.Index(42u, 6u);

        Assert.That(first, Is.EqualTo(second));
    }

    [Test]
    public void Index_MaxValueSeedAndN_NeverThrows()
    {
        Assert.DoesNotThrow(() => SolreignFxSeedMixing.Index(uint.MaxValue, uint.MaxValue));
    }
}
