using Content.Server._Solreign.MiniBoss;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(MiniBossPopulationGate))]
public sealed class MiniBossPopulationGateTests
{
    // --- Minimum bound ---

    [Test]
    public void BelowMinimum_Rejected()
    {
        Assert.That(MiniBossPopulationGate.InBounds(aliveCount: 5, minPopulation: 12, maxPopulation: 0), Is.False);
    }

    [Test]
    public void AtMinimum_Accepted()
    {
        Assert.That(MiniBossPopulationGate.InBounds(aliveCount: 12, minPopulation: 12, maxPopulation: 0), Is.True);
    }

    [Test]
    public void AboveMinimum_UncappedMax_Accepted()
    {
        Assert.That(MiniBossPopulationGate.InBounds(aliveCount: 60, minPopulation: 12, maxPopulation: 0), Is.True);
    }

    // --- Maximum bound ---

    [Test]
    public void WithinMaximum_Accepted()
    {
        Assert.That(MiniBossPopulationGate.InBounds(aliveCount: 15, minPopulation: 10, maxPopulation: 20), Is.True);
    }

    [Test]
    public void AtMaximum_Accepted()
    {
        Assert.That(MiniBossPopulationGate.InBounds(aliveCount: 20, minPopulation: 10, maxPopulation: 20), Is.True);
    }

    [Test]
    public void AboveMaximum_Rejected()
    {
        Assert.That(MiniBossPopulationGate.InBounds(aliveCount: 21, minPopulation: 10, maxPopulation: 20), Is.False);
    }

    // --- Zero-population edge case ---

    [Test]
    public void EmptyStation_ZeroMinimum_Accepted()
    {
        Assert.That(MiniBossPopulationGate.InBounds(aliveCount: 0, minPopulation: 0, maxPopulation: 0), Is.True);
    }

    [Test]
    public void EmptyStation_PositiveMinimum_Rejected()
    {
        Assert.That(MiniBossPopulationGate.InBounds(aliveCount: 0, minPopulation: 1, maxPopulation: 0), Is.False);
    }

    // --- Misconfiguration sanitization (YAML-sourced values, never throw) ---

    [Test]
    public void NegativeMinimum_ClampsToZero()
    {
        // A designer typo (-5) should behave like 0, not reject everything or throw.
        Assert.That(MiniBossPopulationGate.InBounds(aliveCount: 0, minPopulation: -5, maxPopulation: 0), Is.True);
    }

    [Test]
    public void NegativeMaximum_TreatedAsUncapped()
    {
        Assert.That(MiniBossPopulationGate.InBounds(aliveCount: 999, minPopulation: 0, maxPopulation: -1), Is.True);
    }
}
