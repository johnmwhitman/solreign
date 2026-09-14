using System.Collections.Generic;
using Content.Server._Solreign.Corporate;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(CorporateScoring))]
public sealed class CorporateScoringTests
{
    private static CorporateStanding S(string name, int score) => new(name, score);

    // --- TopN ordering ---

    [Test]
    public void TopN_OrdersByScoreDescending()
    {
        var input = new List<CorporateStanding> { S("Alice", 3), S("Bob", 9), S("Carol", 5) };

        var top = CorporateScoring.TopN(input, 3);

        Assert.That(top.Count, Is.EqualTo(3));
        Assert.That(top[0], Is.EqualTo(S("Bob", 9)));
        Assert.That(top[1], Is.EqualTo(S("Carol", 5)));
        Assert.That(top[2], Is.EqualTo(S("Alice", 3)));
    }

    [Test]
    public void TopN_TruncatesToRequestedCount()
    {
        var input = new List<CorporateStanding> { S("Alice", 1), S("Bob", 2), S("Carol", 3), S("Dave", 4) };

        var top = CorporateScoring.TopN(input, 2);

        Assert.That(top.Count, Is.EqualTo(2));
        Assert.That(top[0], Is.EqualTo(S("Dave", 4)));
        Assert.That(top[1], Is.EqualTo(S("Carol", 3)));
    }

    [Test]
    public void TopN_FewerEntriesThanCount_ReturnsAll()
    {
        var input = new List<CorporateStanding> { S("Alice", 5), S("Bob", 1) };

        var top = CorporateScoring.TopN(input, 10);

        Assert.That(top.Count, Is.EqualTo(2));
        Assert.That(top[0], Is.EqualTo(S("Alice", 5)));
    }

    // --- Ties break deterministically by name (ordinal) ---

    [Test]
    public void TopN_TiesBreakByNameOrdinal()
    {
        var input = new List<CorporateStanding> { S("Charlie", 5), S("Alice", 5), S("Bob", 5) };

        var top = CorporateScoring.TopN(input, 3);

        Assert.That(top[0], Is.EqualTo(S("Alice", 5)));
        Assert.That(top[1], Is.EqualTo(S("Bob", 5)));
        Assert.That(top[2], Is.EqualTo(S("Charlie", 5)));
    }

    [Test]
    public void TopN_IsDeterministic_RegardlessOfInputOrder()
    {
        var a = new List<CorporateStanding> { S("Zoe", 2), S("Amy", 2), S("Max", 7) };
        var b = new List<CorporateStanding> { S("Max", 7), S("Amy", 2), S("Zoe", 2) };

        Assert.That(CorporateScoring.TopN(a, 3), Is.EqualTo(CorporateScoring.TopN(b, 3)));
    }

    // --- Empty / non-positive count edge cases ---

    [Test]
    public void TopN_EmptyInput_ReturnsEmpty()
    {
        Assert.That(CorporateScoring.TopN(new List<CorporateStanding>(), 3), Is.Empty);
    }

    [Test]
    public void TopN_NonPositiveCount_ReturnsEmpty()
    {
        var input = new List<CorporateStanding> { S("Alice", 5) };

        Assert.That(CorporateScoring.TopN(input, 0), Is.Empty);
        Assert.That(CorporateScoring.TopN(input, -1), Is.Empty);
    }

    // --- FormatBoard rendering ---

    [Test]
    public void FormatBoard_RendersRankedLines()
    {
        var ranked = new List<CorporateStanding> { S("Bob", 9), S("Alice", 3) };

        var lines = CorporateScoring.FormatBoard(ranked);

        Assert.That(lines.Count, Is.EqualTo(2));
        Assert.That(lines[0], Is.EqualTo("#1 Bob — 9 pts"));
        Assert.That(lines[1], Is.EqualTo("#2 Alice — 3 pts"));
    }

    [Test]
    public void FormatBoard_Empty_ReturnsEmpty()
    {
        Assert.That(CorporateScoring.FormatBoard(new List<CorporateStanding>()), Is.Empty);
    }
}
