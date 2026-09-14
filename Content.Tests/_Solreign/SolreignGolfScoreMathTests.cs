using Content.Server._Solreign.Recreation;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(SolreignGolfScoreMath))]
public sealed class SolreignGolfScoreMathTests
{
    // --- RecordStroke: stroke counting ---

    [Test]
    public void RecordStroke_FromZero_IsOne()
    {
        Assert.That(SolreignGolfScoreMath.RecordStroke(0), Is.EqualTo(1));
    }

    [Test]
    public void RecordStroke_Increments()
    {
        Assert.That(SolreignGolfScoreMath.RecordStroke(5), Is.EqualTo(6));
    }

    [Test]
    public void RecordStroke_NegativeStartingCount_ClampsToZeroThenIncrements()
    {
        // Defends against corrupt/hand-edited state instead of compounding a negative count.
        Assert.That(SolreignGolfScoreMath.RecordStroke(-3), Is.EqualTo(1));
    }

    [Test]
    public void RecordStroke_RepeatedSwings_CountsEachOne()
    {
        var strokes = 0;
        for (var i = 0; i < 4; i++)
            strokes = SolreignGolfScoreMath.RecordStroke(strokes);

        Assert.That(strokes, Is.EqualTo(4));
    }
}
