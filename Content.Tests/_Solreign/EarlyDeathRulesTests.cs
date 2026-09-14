using System;
using Content.Server._Solreign.SeasonLedger;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Unit tests for the pure early-death timing decision (<see cref="SeasonLedgerSystem.IsEarlyDeath"/>).
///     No ECS, no I/O — just the 300s window logic that unlocks the "Amortized Asset" title.
/// </summary>
[TestFixture]
[TestOf(typeof(SeasonLedgerSystem))]
public sealed class EarlyDeathRulesTests
{
    private static readonly TimeSpan Start = TimeSpan.FromMinutes(10); // arbitrary non-zero round start

    [Test]
    public void DeathAtRoundStart_IsEarly()
    {
        Assert.That(SeasonLedgerSystem.IsEarlyDeath(Start, Start), Is.True);
    }

    [Test]
    public void DeathWithinWindow_IsEarly()
    {
        var death = Start + TimeSpan.FromSeconds(120);
        Assert.That(SeasonLedgerSystem.IsEarlyDeath(Start, death), Is.True);
    }

    [Test]
    public void DeathExactlyAtWindowEdge_IsEarly()
    {
        // Boundary: exactly 300s counts (inclusive).
        var death = Start + TimeSpan.FromSeconds(SeasonLedgerSystem.EarlyDeathWindowSeconds);
        Assert.That(SeasonLedgerSystem.IsEarlyDeath(Start, death), Is.True);
    }

    [Test]
    public void DeathJustPastWindow_IsNotEarly()
    {
        var death = Start + TimeSpan.FromSeconds(SeasonLedgerSystem.EarlyDeathWindowSeconds + 1);
        Assert.That(SeasonLedgerSystem.IsEarlyDeath(Start, death), Is.False);
    }

    [Test]
    public void LateDeath_IsNotEarly()
    {
        var death = Start + TimeSpan.FromMinutes(30);
        Assert.That(SeasonLedgerSystem.IsEarlyDeath(Start, death), Is.False);
    }

    [Test]
    public void DeathBeforeRoundStart_IsNotEarly()
    {
        // Defensive: a timestamp earlier than round start must not count as an early death.
        var death = Start - TimeSpan.FromSeconds(5);
        Assert.That(SeasonLedgerSystem.IsEarlyDeath(Start, death), Is.False);
    }
}
