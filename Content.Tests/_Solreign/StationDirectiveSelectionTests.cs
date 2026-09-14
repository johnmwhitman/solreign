using Content.Server._Solreign.StationDirective;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(StationDirectiveSelection))]
public sealed class StationDirectiveSelectionTests
{
    // --- SelectDirectiveIndex: round-robin rotation ---

    [TestCase(0, 3, ExpectedResult = 0)]
    [TestCase(1, 3, ExpectedResult = 1)]
    [TestCase(2, 3, ExpectedResult = 2)]
    [TestCase(3, 3, ExpectedResult = 0)]
    [TestCase(4, 3, ExpectedResult = 1)]
    [TestCase(9, 3, ExpectedResult = 0)]
    public int SelectDirectiveIndex_RoundRobinsThroughSetBeforeRepeating(int roundId, int directiveCount)
    {
        return StationDirectiveSelection.SelectDirectiveIndex(roundId, directiveCount);
    }

    [Test]
    public void SelectDirectiveIndex_SameRoundIdIsDeterministic()
    {
        var first = StationDirectiveSelection.SelectDirectiveIndex(42, 3);
        var second = StationDirectiveSelection.SelectDirectiveIndex(42, 3);

        Assert.That(second, Is.EqualTo(first));
    }

    [TestCase(-1, 3, ExpectedResult = 2)]
    [TestCase(-2, 3, ExpectedResult = 1)]
    [TestCase(-3, 3, ExpectedResult = 0)]
    public int SelectDirectiveIndex_NegativeRoundId_FoldsIntoValidRange(int roundId, int directiveCount)
    {
        return StationDirectiveSelection.SelectDirectiveIndex(roundId, directiveCount);
    }

    [TestCase(5, 0)]
    [TestCase(5, -1)]
    public void SelectDirectiveIndex_DegenerateDirectiveCount_ReturnsZero(int roundId, int directiveCount)
    {
        Assert.That(StationDirectiveSelection.SelectDirectiveIndex(roundId, directiveCount), Is.EqualTo(0));
    }

    // --- NextTickerIndex: cyclic advance, no immediate repeat ---

    [TestCase(0, 3, ExpectedResult = 1)]
    [TestCase(1, 3, ExpectedResult = 2)]
    [TestCase(2, 3, ExpectedResult = 0)]
    public int NextTickerIndex_AdvancesAndWrapsAroundTheSet(int currentIndex, int lineCount)
    {
        return StationDirectiveSelection.NextTickerIndex(currentIndex, lineCount);
    }

    [Test]
    public void NextTickerIndex_FullCycle_NeverRepeatsBackToBack()
    {
        var index = 0;
        var seen = new System.Collections.Generic.List<int> { index };

        for (var i = 0; i < 2; i++)
        {
            var next = StationDirectiveSelection.NextTickerIndex(index, lineCount: 3);
            Assert.That(next, Is.Not.EqualTo(index));
            seen.Add(next);
            index = next;
        }

        // Three distinct lines were visited before any repeat.
        Assert.That(seen, Is.EquivalentTo(new[] { 0, 1, 2 }));
    }

    [TestCase(0, 0)]
    [TestCase(0, -1)]
    public void NextTickerIndex_DegenerateLineCount_ReturnsZero(int currentIndex, int lineCount)
    {
        Assert.That(StationDirectiveSelection.NextTickerIndex(currentIndex, lineCount), Is.EqualTo(0));
    }

    // --- ShouldFireTicker: anti-spam gate ---

    [Test]
    public void ShouldFireTicker_BelowInterval_DoesNotFire()
    {
        Assert.That(StationDirectiveSelection.ShouldFireTicker(secondsSinceLastTicker: 100f, intervalSeconds: 300f), Is.False);
    }

    [Test]
    public void ShouldFireTicker_ExactlyAtInterval_Fires()
    {
        Assert.That(StationDirectiveSelection.ShouldFireTicker(secondsSinceLastTicker: 300f, intervalSeconds: 300f), Is.True);
    }

    [Test]
    public void ShouldFireTicker_PastInterval_Fires()
    {
        Assert.That(StationDirectiveSelection.ShouldFireTicker(secondsSinceLastTicker: 301f, intervalSeconds: 300f), Is.True);
    }

    [TestCase(1000f, 0f)]
    [TestCase(1000f, -1f)]
    public void ShouldFireTicker_NonPositiveInterval_NeverFires(float secondsSinceLastTicker, float intervalSeconds)
    {
        Assert.That(StationDirectiveSelection.ShouldFireTicker(secondsSinceLastTicker, intervalSeconds), Is.False);
    }

    // --- SelectDirectiveIndex against the real, SR-W-081-expanded catalog: every directive gets a turn
    //     before any repeats, same guarantee the original 3-entry catalog had, now proven for 12. ---

    [Test]
    public void SelectDirectiveIndex_FullCycleOverRealCatalog_VisitsEveryDirectiveBeforeAnyRepeat()
    {
        var count = StationDirectiveCatalog.Directives.Count;
        var seen = new System.Collections.Generic.HashSet<int>();

        for (var roundId = 0; roundId < count; roundId++)
        {
            var index = StationDirectiveSelection.SelectDirectiveIndex(roundId, count);
            Assert.That(seen.Add(index), Is.True, $"roundId {roundId} repeated an index ({index}) before the full cycle completed.");
        }

        Assert.That(seen, Has.Count.EqualTo(count));

        // One more round wraps back to the start of the cycle rather than continuing to a fresh index.
        Assert.That(StationDirectiveSelection.SelectDirectiveIndex(count, count), Is.EqualTo(StationDirectiveSelection.SelectDirectiveIndex(0, count)));
    }
}
