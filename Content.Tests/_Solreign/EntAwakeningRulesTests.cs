using Content.Server._Solreign.Ents;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(EntAwakeningRules))]
public sealed class EntAwakeningRulesTests
{
    // --- Core roll-vs-chance comparison ---

    [Test]
    public void RollBelowChance_Awakens()
    {
        Assert.That(EntAwakeningRules.ShouldAwaken(0.01, 0.05f), Is.True);
    }

    [Test]
    public void RollAboveChance_StaysDormant()
    {
        Assert.That(EntAwakeningRules.ShouldAwaken(0.5, 0.05f), Is.False);
    }

    [Test]
    public void RollExactlyAtChance_StaysDormant()
    {
        // roll < chance strictly; an exact tie does not awaken.
        Assert.That(EntAwakeningRules.ShouldAwaken(0.05, 0.05f), Is.False);
    }

    // --- Degenerate chances ---

    [Test]
    public void ZeroChance_NeverAwakens()
    {
        for (var i = 0; i <= 10; i++)
            Assert.That(EntAwakeningRules.ShouldAwaken(i / 10.0, 0f), Is.False);
    }

    [Test]
    public void FullChance_AlwaysAwakens()
    {
        for (var i = 0; i < 10; i++) // roll sampled from [0, 1) never reaches 1.0
            Assert.That(EntAwakeningRules.ShouldAwaken(i / 10.0, 1f), Is.True);
    }

    // --- Chance sanitization (YAML misconfiguration) ---

    [Test]
    public void NegativeChance_ClampsToZero_NeverAwakens()
    {
        Assert.That(EntAwakeningRules.ShouldAwaken(0.0, -0.5f), Is.False);
    }

    [Test]
    public void ChanceAboveOne_ClampsToOne_AlwaysAwakens()
    {
        Assert.That(EntAwakeningRules.ShouldAwaken(0.99, 2f), Is.True);
    }
}
