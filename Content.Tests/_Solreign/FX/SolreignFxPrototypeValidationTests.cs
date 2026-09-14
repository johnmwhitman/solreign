using Content.Shared._Solreign.FX;
using NUnit.Framework;

namespace Content.Tests._Solreign.FX;

/// <summary>
///     Unit coverage for <see cref="SolreignFxCuePrototypeValidation"/> (spec §1.5) — the
///     load-time rules that keep a bad prototype from ever reaching <c>TryCreate</c>/
///     <c>TryValidateReceived</c>: inverted/non-finite bounds disable the prototype, empty palette
///     lists are rejected, and hardcoded wire ceilings are enforced over any authored value
///     (cdx #7/#11). The "Unit — load-time prototype validation" row of §7's test plan.
/// </summary>
[TestFixture]
[TestOf(typeof(SolreignFxCuePrototypeValidation))]
public sealed class SolreignFxPrototypeValidationTests
{
    private static SolreignFxCuePrototype MakeValidPrototype()
    {
#pragma warning disable RA0039 // Pure logic-boundary test; no prototype manager is running here.
        return new SolreignFxCuePrototype
        {
            MinIntensity = 0f,
            MaxIntensity = 1f,
            MinScale = 0.1f,
            MaxScale = 1f,
            MinDuration = 0.1f,
            MaxDuration = 1f,
            PaletteCount = 3,
            DefaultPaletteIndex = 1,
            PhaseCount = 2,
            DefaultPhaseIndex = 0,
        };
#pragma warning restore RA0039
    }

    [Test]
    public void Validate_NullPrototype_ReturnsFalseWithoutThrowing()
    {
        bool ok = false;
        Assert.DoesNotThrow(() => ok = SolreignFxCuePrototypeValidation.Validate(null, out var reasons));
        Assert.That(ok, Is.False);
    }

    [Test]
    public void Validate_WellFormedPrototype_ReturnsTrueWithNoReasons()
    {
        var ok = SolreignFxCuePrototypeValidation.Validate(MakeValidPrototype(), out var reasons);

        Assert.That(ok, Is.True);
        Assert.That(reasons, Is.Empty);
    }

    // --- Inverted / non-finite bounds ---

    [Test]
    public void Validate_InvertedIntensityBounds_Fails()
    {
        var prototype = MakeValidPrototype();
        prototype.MinIntensity = 0.9f;
        prototype.MaxIntensity = 0.1f;

        var ok = SolreignFxCuePrototypeValidation.Validate(prototype, out var reasons);

        Assert.That(ok, Is.False);
        Assert.That(reasons, Has.Some.Contains("inverted"));
    }

    [Test]
    public void Validate_InvertedScaleBounds_Fails()
    {
        var prototype = MakeValidPrototype();
        prototype.MinScale = 5f;
        prototype.MaxScale = 1f;

        var ok = SolreignFxCuePrototypeValidation.Validate(prototype, out var reasons);

        Assert.That(ok, Is.False);
        Assert.That(reasons, Has.Some.Contains("Scale"));
    }

    [Test]
    public void Validate_InvertedDurationBounds_Fails()
    {
        var prototype = MakeValidPrototype();
        prototype.MinDuration = 5f;
        prototype.MaxDuration = 1f;

        var ok = SolreignFxCuePrototypeValidation.Validate(prototype, out var reasons);

        Assert.That(ok, Is.False);
        Assert.That(reasons, Has.Some.Contains("Duration"));
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void Validate_NonFiniteMinIntensity_Fails(float poison)
    {
        var prototype = MakeValidPrototype();
        prototype.MinIntensity = poison;

        bool ok = false;
        Assert.DoesNotThrow(() => ok = SolreignFxCuePrototypeValidation.Validate(prototype, out var reasons));
        Assert.That(ok, Is.False);
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void Validate_NonFiniteMaxDuration_Fails(float poison)
    {
        var prototype = MakeValidPrototype();
        prototype.MaxDuration = poison;

        bool ok = false;
        Assert.DoesNotThrow(() => ok = SolreignFxCuePrototypeValidation.Validate(prototype, out var reasons));
        Assert.That(ok, Is.False);
    }

    // --- MinDuration one-tick floor (spec §1.5) ---

    [Test]
    public void Validate_MinDurationBelowOneTickFloor_Fails()
    {
        var prototype = MakeValidPrototype();
        prototype.MinDuration = 0.01f; // below MinDurationFloor (0.05s)
        prototype.MaxDuration = 1f;

        var ok = SolreignFxCuePrototypeValidation.Validate(prototype, out var reasons);

        Assert.That(ok, Is.False);
        Assert.That(reasons, Has.Some.Contains("one-tick floor"));
    }

    [Test]
    public void Validate_MinDurationAtExactlyOneTickFloor_Passes()
    {
        var prototype = MakeValidPrototype();
        prototype.MinDuration = SolreignFxCuePrototypeValidation.MinDurationFloor;
        prototype.MaxDuration = 1f;

        var ok = SolreignFxCuePrototypeValidation.Validate(prototype, out var reasons);

        Assert.That(ok, Is.True);
    }

    // --- MinScale positivity floor (spec §1.5's (0, 8] open interval, grk review finding M4) ---

    [Test]
    public void Validate_MinScaleBelowFloor_Fails()
    {
        var prototype = MakeValidPrototype();
        prototype.MinScale = 0.001f; // below MinScaleFloor (0.01)
        prototype.MaxScale = 1f;

        var ok = SolreignFxCuePrototypeValidation.Validate(prototype, out var reasons);

        Assert.That(ok, Is.False);
        Assert.That(reasons, Has.Some.Contains("minimum scale floor"));
    }

    [Test]
    public void Validate_MinScaleAtExactlyFloor_Passes()
    {
        var prototype = MakeValidPrototype();
        prototype.MinScale = SolreignFxCuePrototypeValidation.MinScaleFloor;
        prototype.MaxScale = 1f;

        var ok = SolreignFxCuePrototypeValidation.Validate(prototype, out var reasons);

        Assert.That(ok, Is.True);
    }

    // --- Hardcoded, non-configurable wire ceilings (spec §1.5) — no prototype/CVar may exceed these ---

    [Test]
    public void Validate_MaxIntensityAboveWireCeiling_Fails()
    {
        var prototype = MakeValidPrototype();
        prototype.MaxIntensity = SolreignFxCuePrototypeValidation.WireIntensityMax + 0.5f;

        var ok = SolreignFxCuePrototypeValidation.Validate(prototype, out var reasons);

        Assert.That(ok, Is.False);
        Assert.That(reasons, Has.Some.Contains("wire ceiling"));
    }

    [Test]
    public void Validate_MinIntensityBelowWireFloor_Fails()
    {
        var prototype = MakeValidPrototype();
        prototype.MinIntensity = SolreignFxCuePrototypeValidation.WireIntensityMin - 1f;
        prototype.MaxIntensity = 1f;

        var ok = SolreignFxCuePrototypeValidation.Validate(prototype, out var reasons);

        Assert.That(ok, Is.False);
        Assert.That(reasons, Has.Some.Contains("wire floor"));
    }

    [Test]
    public void Validate_MaxScaleAboveWireCeiling_Fails()
    {
        var prototype = MakeValidPrototype();
        prototype.MaxScale = SolreignFxCuePrototypeValidation.WireScaleMax + 1f; // ceiling is (0, 8]

        var ok = SolreignFxCuePrototypeValidation.Validate(prototype, out var reasons);

        Assert.That(ok, Is.False);
    }

    [Test]
    public void Validate_MaxScaleAtExactlyWireCeiling_Passes()
    {
        var prototype = MakeValidPrototype();
        prototype.MinScale = 0.1f;
        prototype.MaxScale = SolreignFxCuePrototypeValidation.WireScaleMax;

        var ok = SolreignFxCuePrototypeValidation.Validate(prototype, out var reasons);

        Assert.That(ok, Is.True);
    }

    [Test]
    public void Validate_MaxDurationAboveWireCeiling_Fails()
    {
        var prototype = MakeValidPrototype();
        prototype.MaxDuration = SolreignFxCuePrototypeValidation.WireDurationMax + 1f; // ceiling is (0, 30s]

        var ok = SolreignFxCuePrototypeValidation.Validate(prototype, out var reasons);

        Assert.That(ok, Is.False);
    }

    // --- Palette/phase: non-empty, actual==declared (N/A pre-W3, see class remarks), <=256, default in range (spec §1.5) ---

    [Test]
    public void Validate_ZeroPaletteCount_Fails()
    {
        var prototype = MakeValidPrototype();
        prototype.PaletteCount = 0;

        var ok = SolreignFxCuePrototypeValidation.Validate(prototype, out var reasons);

        Assert.That(ok, Is.False);
        Assert.That(reasons, Has.Some.Contains("non-empty"));
    }

    [Test]
    public void Validate_NegativePaletteCount_Fails()
    {
        var prototype = MakeValidPrototype();
        prototype.PaletteCount = -1;

        var ok = SolreignFxCuePrototypeValidation.Validate(prototype, out var reasons);

        Assert.That(ok, Is.False);
    }

    [Test]
    public void Validate_PaletteCountAboveByteAddressableCeiling_Fails()
    {
        var prototype = MakeValidPrototype();
        prototype.PaletteCount = SolreignFxCuePrototypeValidation.MaxPaletteOrPhaseCount + 1;
        prototype.DefaultPaletteIndex = 0;

        var ok = SolreignFxCuePrototypeValidation.Validate(prototype, out var reasons);

        Assert.That(ok, Is.False);
        Assert.That(reasons, Has.Some.Contains("byte-addressable"));
    }

    [Test]
    public void Validate_PaletteCountAtExactlyByteAddressableCeiling_Passes()
    {
        var prototype = MakeValidPrototype();
        prototype.PaletteCount = SolreignFxCuePrototypeValidation.MaxPaletteOrPhaseCount;
        prototype.DefaultPaletteIndex = 0;

        var ok = SolreignFxCuePrototypeValidation.Validate(prototype, out var reasons);

        Assert.That(ok, Is.True);
    }

    [Test]
    public void Validate_DefaultPaletteIndexNotLessThanCount_Fails()
    {
        var prototype = MakeValidPrototype();
        prototype.PaletteCount = 3;
        prototype.DefaultPaletteIndex = 3; // must be < 3

        var ok = SolreignFxCuePrototypeValidation.Validate(prototype, out var reasons);

        Assert.That(ok, Is.False);
        Assert.That(reasons, Has.Some.Contains("DefaultPaletteIndex"));
    }

    [Test]
    public void Validate_ZeroPhaseCount_Fails()
    {
        var prototype = MakeValidPrototype();
        prototype.PhaseCount = 0;

        var ok = SolreignFxCuePrototypeValidation.Validate(prototype, out var reasons);

        Assert.That(ok, Is.False);
    }

    [Test]
    public void Validate_DefaultPhaseIndexNotLessThanCount_Fails()
    {
        var prototype = MakeValidPrototype();
        prototype.PhaseCount = 2;
        prototype.DefaultPhaseIndex = 2; // must be < 2

        var ok = SolreignFxCuePrototypeValidation.Validate(prototype, out var reasons);

        Assert.That(ok, Is.False);
        Assert.That(reasons, Has.Some.Contains("DefaultPhaseIndex"));
    }

    // --- Every violated rule is reported, not just the first (so an error log names everything at once) ---

    [Test]
    public void Validate_MultipleSimultaneousViolations_ReportsAllOfThem()
    {
        var prototype = MakeValidPrototype();
        prototype.MinIntensity = 0.9f;
        prototype.MaxIntensity = 0.1f; // inverted
        prototype.PaletteCount = 0; // empty
        prototype.PhaseCount = -5; // empty, negative

        var ok = SolreignFxCuePrototypeValidation.Validate(prototype, out var reasons);

        Assert.That(ok, Is.False);
        Assert.That(reasons.Count, Is.GreaterThanOrEqualTo(3));
    }
}
