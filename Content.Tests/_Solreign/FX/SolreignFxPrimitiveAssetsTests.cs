#nullable enable
using System;
using System.Linq;
using Content.Shared._Solreign.FX;
using NUnit.Framework;

namespace Content.Tests._Solreign.FX;

/// <summary>
///     <see cref="SolreignFxPrimitiveAssets"/>'s data-sanity contract: every category returns a
///     well-formed configuration (no null RSI path with a non-null state or vice versa, non-empty
///     palettes, bounds-safe palette lookups) — this is the pure-data half of "per-primitive client
///     render recipes composing EXISTING mechanisms" (mission text), directly testable without an
///     <c>IResourceCache</c>/live client.
/// </summary>
[TestFixture]
[TestOf(typeof(SolreignFxPrimitiveAssets))]
public sealed class SolreignFxPrimitiveAssetsTests
{
    private static readonly SolreignFxCategory[] AllCategories = Enum.GetValues<SolreignFxCategory>();

    [Test]
    public void GetAssets_EveryCategory_RsiPathAndStateAreBothPresentOrBothAbsent()
    {
        foreach (var category in AllCategories)
        {
            var assets = SolreignFxPrimitiveAssets.GetAssets(category);
            Assert.That(assets.RsiPath is null, Is.EqualTo(assets.SpriteState is null),
                $"{category}: RsiPath/SpriteState must be jointly null or jointly set, never mismatched");
        }
    }

    [TestCase(SolreignFxCategory.ImpactLight)]
    [TestCase(SolreignFxCategory.ImpactHeavy)]
    [TestCase(SolreignFxCategory.Electrical)]
    [TestCase(SolreignFxCategory.Dust)]
    [TestCase(SolreignFxCategory.Smoke)]
    [TestCase(SolreignFxCategory.Transformation)]
    [TestCase(SolreignFxCategory.StaminaBreak)]
    [TestCase(SolreignFxCategory.BodyShockGeneric)]
    public void GetAssets_EverySpriteHavingPrimitive_HasARealRsiAsset(SolreignFxCategory category)
    {
        // Per spec §3's budget table, every category except CastRing (overlay-only) and
        // Transformation-as-appearance-swap (its own extra burst sprite still needs one) carries a
        // pooled sprite — cast_ring alone is legitimately sprite-less (pure overlay + light).
        var assets = SolreignFxPrimitiveAssets.GetAssets(category);

        Assert.Multiple(() =>
        {
            Assert.That(assets.RsiPath, Is.Not.Null.And.Not.Empty);
            Assert.That(assets.SpriteState, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void GetAssets_CastRing_IsOverlayOnly_NoSpriteAsset()
    {
        var assets = SolreignFxPrimitiveAssets.GetAssets(SolreignFxCategory.CastRing);

        Assert.Multiple(() =>
        {
            Assert.That(assets.RsiPath, Is.Null);
            Assert.That(assets.SpriteState, Is.Null);
        });
    }

    [Test]
    public void GetAssets_EveryCategory_PaletteIsNonEmpty()
    {
        foreach (var category in AllCategories)
        {
            var assets = SolreignFxPrimitiveAssets.GetAssets(category);
            Assert.That(assets.Palette, Is.Not.Empty, $"{category}'s render-time palette must be non-empty");
        }
    }

    [TestCase(SolreignFxCategory.Dust)]
    [TestCase(SolreignFxCategory.Smoke)]
    [TestCase(SolreignFxCategory.CastRing)]
    public void GetSound_PureCosmeticOrCasterOwnedPrimitives_HaveNoDedicatedFxAudio(SolreignFxCategory category)
    {
        // spec §2's composing column names no dedicated audio for dust/smoke; cast_ring's audio, if
        // any, belongs to the caster's own ability (a non-FX co-channel per spec §4.0).
        Assert.That(SolreignFxPrimitiveAssets.GetSound(category), Is.Null);
    }

    [TestCase(SolreignFxCategory.ImpactLight)]
    [TestCase(SolreignFxCategory.ImpactHeavy)]
    [TestCase(SolreignFxCategory.Electrical)]
    [TestCase(SolreignFxCategory.Transformation)]
    [TestCase(SolreignFxCategory.StaminaBreak)]
    [TestCase(SolreignFxCategory.BodyShockGeneric)]
    public void GetSound_AudioCarryingPrimitives_HaveADedicatedSound(SolreignFxCategory category)
    {
        Assert.That(SolreignFxPrimitiveAssets.GetSound(category), Is.Not.Null);
    }

    [Test]
    public void GetPaletteColor_OutOfRangeIndex_ClampsToEntryZero_NeverThrows()
    {
        Assert.DoesNotThrow(() =>
        {
            var color = SolreignFxPrimitiveAssets.GetPaletteColor(SolreignFxCategory.ImpactLight, byte.MaxValue);
            var entryZero = SolreignFxPrimitiveAssets.GetAssets(SolreignFxCategory.ImpactLight).Palette.First();
            Assert.That(color, Is.EqualTo(entryZero));
        });
    }

    [Test]
    public void GetPaletteColor_InRangeIndex_ReturnsThatExactEntry()
    {
        var assets = SolreignFxPrimitiveAssets.GetAssets(SolreignFxCategory.CastRing);
        Assert.That(assets.Palette.Length, Is.GreaterThan(1), "test needs at least 2 palette entries to be meaningful");

        var color = SolreignFxPrimitiveAssets.GetPaletteColor(SolreignFxCategory.CastRing, 1);
        Assert.That(color, Is.EqualTo(assets.Palette[1]));
    }
}
