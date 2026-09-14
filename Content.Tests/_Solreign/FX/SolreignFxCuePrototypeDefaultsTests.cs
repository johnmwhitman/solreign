#nullable enable
using Content.Shared._Solreign.FX;
using NUnit.Framework;

namespace Content.Tests._Solreign.FX;

/// <summary>
///     Coverage for the three <c>Default*</c> fields (<see cref="SolreignFxCuePrototype.DefaultIntensity"/>/
///     <see cref="SolreignFxCuePrototype.DefaultScale"/>/<see cref="SolreignFxCuePrototype.DefaultDuration"/>)
///     this worktree adds so <c>SolreignFxServerSystem.RaiseSecretRoleCue</c> has a well-defined,
///     load-time-validated value to scrub a redacted/generic variant's numeric fields to (spec §5.2
///     item 1: "Intensity/Scale/Duration at the generic prototype's fixed default values"). Same
///     rationale cdx #11 already established for palette/phase defaults — the fallback value itself
///     must never be capable of being out of the prototype's own declared bounds.
/// </summary>
[TestFixture]
[TestOf(typeof(SolreignFxCuePrototypeValidation))]
public sealed class SolreignFxCuePrototypeDefaultsTests
{
#pragma warning disable RA0039 // Pure logic-boundary test; no prototype manager is running here.
    private static SolreignFxCuePrototype MakeValid() => new()
    {
        MinIntensity = 0f,
        MaxIntensity = 1f,
        MinScale = 0.1f,
        MaxScale = 1f,
        MinDuration = 0.1f,
        MaxDuration = 1f,
        PaletteCount = 1,
        DefaultPaletteIndex = 0,
        PhaseCount = 1,
        DefaultPhaseIndex = 0,
        DefaultIntensity = 0.5f,
        DefaultScale = 0.5f,
        DefaultDuration = 0.5f,
    };
#pragma warning restore RA0039

    [Test]
    public void Validate_DefaultsWithinBounds_Passes()
    {
        Assert.That(SolreignFxCuePrototypeValidation.Validate(MakeValid(), out var reasons), Is.True);
        Assert.That(reasons, Is.Empty);
    }

    [Test]
    public void Validate_DefaultIntensityAboveMax_Fails()
    {
        var prototype = MakeValid();
        prototype.DefaultIntensity = 1.5f;

        Assert.That(SolreignFxCuePrototypeValidation.Validate(prototype, out var reasons), Is.False);
        Assert.That(reasons, Has.Some.Contains("DefaultIntensity"));
    }

    [Test]
    public void Validate_DefaultScaleBelowMin_Fails()
    {
        var prototype = MakeValid();
        prototype.DefaultScale = 0.01f; // below MinScale (0.1)

        Assert.That(SolreignFxCuePrototypeValidation.Validate(prototype, out var reasons), Is.False);
        Assert.That(reasons, Has.Some.Contains("DefaultScale"));
    }

    [Test]
    public void Validate_DefaultDurationNonFinite_Fails()
    {
        var prototype = MakeValid();
        prototype.DefaultDuration = float.NaN;

        Assert.That(SolreignFxCuePrototypeValidation.Validate(prototype, out var reasons), Is.False);
        Assert.That(reasons, Has.Some.Contains("DefaultDuration"));
    }

    [Test]
    public void Validate_DefaultDurationInfinite_Fails()
    {
        var prototype = MakeValid();
        prototype.DefaultDuration = float.PositiveInfinity;

        Assert.That(SolreignFxCuePrototypeValidation.Validate(prototype, out var reasons), Is.False);
    }

    [Test]
    public void Validate_AlreadyInvertedBounds_DoesNotAlsoCascadeIntoADefaultComplaint()
    {
        // When Min > Max is already reported, the default-within-bounds check should stay quiet
        // about the same field rather than piling on a second, misleading message about the
        // (already-known-bad) bounds.
        var prototype = MakeValid();
        prototype.MinIntensity = 0.9f;
        prototype.MaxIntensity = 0.1f;

        SolreignFxCuePrototypeValidation.Validate(prototype, out var reasons);

        Assert.That(reasons, Has.Some.Contains("inverted bounds"));
        Assert.That(reasons, Has.None.Contains("DefaultIntensity"));
    }
}
