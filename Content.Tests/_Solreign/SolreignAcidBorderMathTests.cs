using Content.Shared._Solreign.FX;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(SolreignAcidBorderMath))]
public sealed class SolreignAcidBorderMathTests
{
    // --- ClampBorderSizePx ---

    [Test]
    public void ClampBorderSizePx_SmallRequest_LargeViewport_Unchanged()
    {
        // A modest ask on a normal desktop viewport is nowhere near the cap and should pass through.
        Assert.That(SolreignAcidBorderMath.ClampBorderSizePx(30f, 1920f, 1080f), Is.EqualTo(30f));
    }

    [Test]
    public void ClampBorderSizePx_LargeRequest_IsClampedDownToCap()
    {
        // A wildly oversized ask (e.g. a mistyped uniform, or a re-themed prototype) must never reach
        // the shader unclamped.
        var result = SolreignAcidBorderMath.ClampBorderSizePx(10_000f, 1920f, 1080f);

        Assert.That(result, Is.LessThan(10_000f));
        Assert.That(result, Is.EqualTo(1080f * 0.09f).Within(0.001f));
    }

    [Test]
    public void ClampBorderSizePx_SquareViewport_NeverExceedsCoveragePromise()
    {
        // Worst case for coverage is a square viewport (see the class doc-comment derivation).
        var clamped = SolreignAcidBorderMath.ClampBorderSizePx(10_000f, 1000f, 1000f);
        var coverage = SolreignAcidBorderMath.CoverageFraction(clamped, 1000f, 1000f);

        Assert.That(coverage, Is.LessThan(SolreignAcidBorderMath.MaxScreenCoverageFraction));
    }

    [Test]
    public void ClampBorderSizePx_TinyViewport_ScalesDown()
    {
        // A tiny/portrait/minimized viewport shrinks the cap proportionally rather than allowing a
        // fixed pixel count to swallow it whole.
        var result = SolreignAcidBorderMath.ClampBorderSizePx(30f, 100f, 100f);

        Assert.That(result, Is.EqualTo(9f).Within(0.001f));
    }

    [Test]
    public void ClampBorderSizePx_ZeroOrNegativeRequest_ReturnsZero()
    {
        Assert.That(SolreignAcidBorderMath.ClampBorderSizePx(0f, 1920f, 1080f), Is.EqualTo(0f));
        Assert.That(SolreignAcidBorderMath.ClampBorderSizePx(-5f, 1920f, 1080f), Is.EqualTo(0f));
    }

    [Test]
    public void ClampBorderSizePx_DegenerateViewport_ReturnsZeroWithoutDividing()
    {
        Assert.That(SolreignAcidBorderMath.ClampBorderSizePx(30f, 0f, 1080f), Is.EqualTo(0f));
        Assert.That(SolreignAcidBorderMath.ClampBorderSizePx(30f, 1920f, 0f), Is.EqualTo(0f));
        Assert.That(SolreignAcidBorderMath.ClampBorderSizePx(30f, -100f, 1080f), Is.EqualTo(0f));
    }

    // --- CoverageFraction ---

    [Test]
    public void CoverageFraction_ZeroBorder_IsZeroCoverage()
    {
        Assert.That(SolreignAcidBorderMath.CoverageFraction(0f, 1920f, 1080f), Is.EqualTo(0f));
    }

    [Test]
    public void CoverageFraction_FullHalfDimensionBorder_IsFullCoverage()
    {
        // A border thickness at or beyond half the smaller dimension swallows the whole screen —
        // CoverageFraction should saturate at 1, not overshoot or divide oddly.
        Assert.That(SolreignAcidBorderMath.CoverageFraction(10_000f, 1000f, 1000f), Is.EqualTo(1f).Within(0.001f));
    }

    [Test]
    public void CoverageFraction_TypicalClampedBorder_StaysUnderDesignCap()
    {
        // The actual design cap check: the clamp this class enforces must hold the promise in
        // MaxScreenCoverageFraction across a range of realistic viewport shapes, not just the square
        // worst case.
        foreach (var (w, h) in new[] { (1920f, 1080f), (1280f, 800f), (2560f, 1440f), (800f, 600f), (375f, 812f) })
        {
            var clamped = SolreignAcidBorderMath.ClampBorderSizePx(30f, w, h);
            var coverage = SolreignAcidBorderMath.CoverageFraction(clamped, w, h);

            Assert.That(coverage, Is.LessThan(SolreignAcidBorderMath.MaxScreenCoverageFraction),
                $"coverage exceeded cap for {w}x{h}");
        }
    }
}
