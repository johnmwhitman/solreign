using Content.Server._Solreign.Terminator;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(ComplianceHunterSpawnRules))]
public sealed class ComplianceHunterSpawnRulesTests
{
    // --- Default single-hunter cap ---

    [Test]
    public void EmptyStation_DefaultMax_Accepted()
    {
        Assert.That(ComplianceHunterSpawnRules.MaySpawn(existingCount: 0), Is.True);
    }

    [Test]
    public void OneLive_DefaultMax_Rejected()
    {
        Assert.That(ComplianceHunterSpawnRules.MaySpawn(existingCount: 1), Is.False);
    }

    [Test]
    public void ManyLive_DefaultMax_Rejected()
    {
        Assert.That(ComplianceHunterSpawnRules.MaySpawn(existingCount: 5), Is.False);
    }

    // --- Explicit max concurrent ---

    [Test]
    public void BelowMax_Accepted()
    {
        Assert.That(ComplianceHunterSpawnRules.MaySpawn(existingCount: 1, maxConcurrent: 3), Is.True);
    }

    [Test]
    public void AtMax_Rejected()
    {
        Assert.That(ComplianceHunterSpawnRules.MaySpawn(existingCount: 3, maxConcurrent: 3), Is.False);
    }

    [Test]
    public void AboveMax_Rejected()
    {
        Assert.That(ComplianceHunterSpawnRules.MaySpawn(existingCount: 4, maxConcurrent: 3), Is.False);
    }

    // --- Zero-max edge (always reject) ---

    [Test]
    public void ZeroMax_AlwaysRejected()
    {
        Assert.That(ComplianceHunterSpawnRules.MaySpawn(existingCount: 0, maxConcurrent: 0), Is.False);
    }

    // --- Misconfiguration sanitization (YAML-sourced values, never throw) ---

    [Test]
    public void NegativeExisting_ClampsToZero_Accepted()
    {
        Assert.That(ComplianceHunterSpawnRules.MaySpawn(existingCount: -1, maxConcurrent: 1), Is.True);
    }

    [Test]
    public void NegativeMax_ClampsToZero_Rejected()
    {
        // A designer typo (-1) must not unlock an uncapped flood of hunters.
        Assert.That(ComplianceHunterSpawnRules.MaySpawn(existingCount: 0, maxConcurrent: -1), Is.False);
    }

    // --- Population scaling math ---

    [Test]
    public void PopScaling_ZeroPop_ReturnsBaseMax()
    {
        Assert.That(ComplianceHunterSpawnRules.CalculateMaxConcurrent(0, 15, 1), Is.EqualTo(1));
    }

    [Test]
    public void PopScaling_UnderThreshold_ReturnsBaseMax()
    {
        Assert.That(ComplianceHunterSpawnRules.CalculateMaxConcurrent(14, 15, 1), Is.EqualTo(1));
    }

    [Test]
    public void PopScaling_AtThreshold_Increments()
    {
        Assert.That(ComplianceHunterSpawnRules.CalculateMaxConcurrent(15, 15, 1), Is.EqualTo(2));
    }

    [Test]
    public void PopScaling_DoubleThreshold_IncrementsTwice()
    {
        Assert.That(ComplianceHunterSpawnRules.CalculateMaxConcurrent(30, 15, 1), Is.EqualTo(3));
    }

    [Test]
    public void PopScaling_ZeroPlayersPerHunter_DefaultsToBaseMax()
    {
        Assert.That(ComplianceHunterSpawnRules.CalculateMaxConcurrent(100, 0, 1), Is.EqualTo(1));
    }

    [Test]
    public void PopScaling_NegativePopulation_ClampsToZero()
    {
        Assert.That(ComplianceHunterSpawnRules.CalculateMaxConcurrent(-10, 15, 2), Is.EqualTo(2));
    }
}
