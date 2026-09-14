using Content.Server._Solreign.Season1;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(SolreignSeason1BeatSequencer))]
public sealed class SolreignSeason1BeatSequencerTests
{
    // --- Basic cadence progression ---

    [Test]
    public void LineIndexForElapsed_AtStart_ReturnsFirstLine()
    {
        Assert.That(SolreignSeason1BeatSequencer.LineIndexForElapsed(0f, 20f, 3), Is.EqualTo(0));
    }

    [Test]
    public void LineIndexForElapsed_JustBeforeCadence_StaysOnFirstLine()
    {
        Assert.That(SolreignSeason1BeatSequencer.LineIndexForElapsed(19.9f, 20f, 3), Is.EqualTo(0));
    }

    [Test]
    public void LineIndexForElapsed_AtCadence_AdvancesToSecondLine()
    {
        Assert.That(SolreignSeason1BeatSequencer.LineIndexForElapsed(20f, 20f, 3), Is.EqualTo(1));
    }

    [Test]
    public void LineIndexForElapsed_AtDoubleCadence_AdvancesToThirdLine()
    {
        Assert.That(SolreignSeason1BeatSequencer.LineIndexForElapsed(40f, 20f, 3), Is.EqualTo(2));
    }

    // --- Clamping past the last line ---

    [Test]
    public void LineIndexForElapsed_FarPastLastLine_ClampsAtLastLine()
    {
        Assert.That(SolreignSeason1BeatSequencer.LineIndexForElapsed(9999f, 20f, 3), Is.EqualTo(2));
    }

    [Test]
    public void LineIndexForElapsed_NeverRegressesAsElapsedGrows()
    {
        var earlier = SolreignSeason1BeatSequencer.LineIndexForElapsed(20f, 20f, 3);
        var later = SolreignSeason1BeatSequencer.LineIndexForElapsed(200f, 20f, 3);

        Assert.That(later, Is.GreaterThanOrEqualTo(earlier));
        Assert.That(later, Is.EqualTo(2));
    }

    // --- Single-line beats ---

    [Test]
    public void LineIndexForElapsed_SingleLine_AlwaysZero()
    {
        Assert.That(SolreignSeason1BeatSequencer.LineIndexForElapsed(500f, 20f, 1), Is.EqualTo(0));
    }

    [Test]
    public void LineIndexForElapsed_NonPositiveLineCount_TreatedAsOne()
    {
        Assert.That(SolreignSeason1BeatSequencer.LineIndexForElapsed(500f, 20f, 0), Is.EqualTo(0));
        Assert.That(SolreignSeason1BeatSequencer.LineIndexForElapsed(500f, 20f, -3), Is.EqualTo(0));
    }

    // --- Degenerate cadence / elapsed guards ---

    [Test]
    public void LineIndexForElapsed_ZeroOrNegativeCadence_DoesNotThrowAndClamps()
    {
        Assert.That(SolreignSeason1BeatSequencer.LineIndexForElapsed(10f, 0f, 3), Is.EqualTo(2));
        Assert.That(SolreignSeason1BeatSequencer.LineIndexForElapsed(10f, -5f, 3), Is.EqualTo(2));
    }

    [Test]
    public void LineIndexForElapsed_NegativeElapsed_ReturnsFirstLine()
    {
        Assert.That(SolreignSeason1BeatSequencer.LineIndexForElapsed(-5f, 20f, 3), Is.EqualTo(0));
    }
}
