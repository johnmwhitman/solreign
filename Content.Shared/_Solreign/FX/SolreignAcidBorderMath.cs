namespace Content.Shared._Solreign.FX;

/// <summary>
///     Pure, engine-free coverage math for the Solreign acid-green screen-border overlay (see
///     <see cref="SolreignScreenFxEvent"/> / Content.Client._Solreign.FX.SolreignAcidBorderOverlay).
///     Kept separate from <see cref="SolreignScreenFxTiming"/> because that file governs HOW LONG the
///     sting stays up; this one governs HOW MUCH OF THE SCREEN it is allowed to cover.
///
///     FLASH-FIX-2026-07-16: the live "whole screen turns green" bug (Corporate Ladder's quarterly
///     earnings call) traced to an uninitialized-COLOR bug in the shader itself (see
///     Resources/Textures/Shaders/_Solreign/acid_screen_border.swsl), not this math -- but the design
///     brief also asks for a hard backstop so no future caller (a re-themed shader prototype, a typo'd
///     uniform, a copy-paste into a new event) can accidentally paint the whole screen again. This
///     class is that backstop: it clamps the requested border thickness so the rendered frame can
///     never exceed <see cref="MaxScreenCoverageFraction"/> of total screen area, regardless of what
///     the shader prototype's YAML asks for.
/// </summary>
public static class SolreignAcidBorderMath
{
    /// <summary>
    ///     Design cap (FX-LANGUAGE intent, applied directionally): the sting is a screen-border/vignette
    ///     accent, never a tint that obscures gameplay. It must never cover more than this fraction of
    ///     total screen area.
    /// </summary>
    public const float MaxScreenCoverageFraction = 0.35f;

    /// <summary>
    ///     Fraction of the smaller screen dimension the border may occupy inward from each edge. Chosen
    ///     so that even a square (worst-case) viewport never exceeds <see cref="MaxScreenCoverageFraction"/>
    ///     total coverage: for a WxW screen, coverage(b) = 1 - (1 - 2b/W)^2, and 1 - (1 - 2*0.09)^2 ≈
    ///     0.328, comfortably under the 0.35 cap. Verified directly (not just by this derivation) in
    ///     Content.Tests/_Solreign/SolreignAcidBorderMathTests.cs.
    /// </summary>
    private const float MaxBorderFractionOfMinDimension = 0.09f;

    /// <summary>
    ///     Clamps a requested border thickness (in screen pixels) against the current viewport size so
    ///     it can never balloon into a near-full-screen tint. Returns 0 for a degenerate (zero or
    ///     negative) viewport dimension rather than dividing by it.
    /// </summary>
    public static float ClampBorderSizePx(float requestedPx, float screenWidthPx, float screenHeightPx)
    {
        if (requestedPx <= 0f || screenWidthPx <= 0f || screenHeightPx <= 0f)
            return 0f;

        var minDimension = System.MathF.Min(screenWidthPx, screenHeightPx);
        var cap = minDimension * MaxBorderFractionOfMinDimension;

        return System.MathF.Min(requestedPx, cap);
    }

    /// <summary>
    ///     Exact fraction of total screen area a border of the given thickness covers (the "picture
    ///     frame" area: total minus the untouched interior rectangle). Used by tests to verify the
    ///     clamp above actually holds the coverage promise, not just approximates it.
    /// </summary>
    public static float CoverageFraction(float borderPx, float screenWidthPx, float screenHeightPx)
    {
        if (borderPx <= 0f || screenWidthPx <= 0f || screenHeightPx <= 0f)
            return 0f;

        var clampedBorder = System.MathF.Min(borderPx, System.MathF.Min(screenWidthPx, screenHeightPx) / 2f);

        var innerWidth = System.MathF.Max(screenWidthPx - 2f * clampedBorder, 0f);
        var innerHeight = System.MathF.Max(screenHeightPx - 2f * clampedBorder, 0f);

        var totalArea = screenWidthPx * screenHeightPx;
        var innerArea = innerWidth * innerHeight;

        return 1f - innerArea / totalArea;
    }
}
