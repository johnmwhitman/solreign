#nullable enable
using System;
using Content.Shared._Solreign.FX;
using NUnit.Framework;

namespace Content.Tests._Solreign.FX;

/// <summary>
///     W3's "per-profile rendering-path tests" (mission text) made concrete against the pure
///     <see cref="SolreignFxRenderRecipe"/> table — every (category, profile) combination in spec
///     §4's accessibility matrix is exercised here without needing a client/server harness, per the
///     type's own "engine-free, directly unit-testable" design rationale.
/// </summary>
[TestFixture]
[TestOf(typeof(SolreignFxRenderRecipe))]
public sealed class SolreignFxRenderRecipeTests
{
    private static SolreignFxRenderPlan Build(SolreignFxCategory category, string effectId, SolreignFxProfile profile, bool noFlash = false, uint seed = 42)
    {
        var behavior = SolreignFxProfilePolicy.GetBehavior(category, profile);
        return SolreignFxRenderRecipe.BuildPlan(category, effectId, profile, behavior, noFlash, seed);
    }

    [Test]
    public void BuildPlan_FullyDroppedCategory_ReturnsNonePlan()
    {
        // Dust/Smoke are fully dropped under cosmetic_minimal per spec §4's own table.
        var plan = Build(SolreignFxCategory.Dust, "dust", SolreignFxProfile.CosmeticMinimal);

        Assert.That(plan, Is.EqualTo(SolreignFxRenderPlan.None));
    }

    [TestCase(SolreignFxProfile.Full)]
    [TestCase(SolreignFxProfile.ReducedMotion)]
    [TestCase(SolreignFxProfile.LowVfx)]
    [TestCase(SolreignFxProfile.CosmeticMinimal)]
    public void BuildPlan_EveryProfile_NeverExceedsTheWcagFlashCeiling(SolreignFxProfile profile)
    {
        foreach (SolreignFxCategory category in Enum.GetValues<SolreignFxCategory>())
        {
            var plan = Build(category, EffectIdFor(category), profile);

            Assert.That(plan.LightFlashRatePerSecond, Is.LessThanOrEqualTo(SolreignFxProfileGate.MaxFlashTransitionsPerSecond),
                $"{category}/{profile} exceeds the WCAG 2.3.1 flash-rate ceiling in EVERY profile (spec §4.0)");
        }
    }

    [TestCase(SolreignFxCategory.ImpactLight)]
    [TestCase(SolreignFxCategory.ImpactHeavy)]
    [TestCase(SolreignFxCategory.Electrical)]
    [TestCase(SolreignFxCategory.StaminaBreak)]
    public void BuildPlan_ReducedMotion_NeverRepeatingFlicker(SolreignFxCategory category)
    {
        // spec §4.0: "reduced_motion additionally converts flicker to a single non-repeating pulse."
        var plan = Build(category, EffectIdFor(category), SolreignFxProfile.ReducedMotion);

        Assert.That(plan.LightFlickers, Is.False);
    }

    [TestCase(SolreignFxCategory.ImpactLight)]
    [TestCase(SolreignFxCategory.ImpactHeavy)]
    [TestCase(SolreignFxCategory.Electrical)]
    [TestCase(SolreignFxCategory.StaminaBreak)]
    [TestCase(SolreignFxCategory.BodyShockGeneric)]
    public void BuildPlan_NoFlashToggle_ZeroesFlickerInEveryProfile(SolreignFxCategory category)
    {
        foreach (SolreignFxProfile profile in Enum.GetValues<SolreignFxProfile>())
        {
            var plan = Build(category, EffectIdFor(category), profile, noFlash: true);

            Assert.Multiple(() =>
            {
                Assert.That(plan.LightFlickers, Is.False, $"{category}/{profile} with no_flash must never flicker");
                Assert.That(plan.LightFlashRatePerSecond, Is.EqualTo(0f), $"{category}/{profile} with no_flash must report a zero flash rate");
            });
        }
    }

    [Test]
    public void BuildPlan_ImpactHeavy_Full_HasCameraImpulse()
    {
        var plan = Build(SolreignFxCategory.ImpactHeavy, "impact_heavy", SolreignFxProfile.Full);

        Assert.That(plan.CameraImpulseEnabled, Is.True);
    }

    [Test]
    public void BuildPlan_ImpactLight_Full_NeverHasCameraImpulse()
    {
        // grk W3 round-2 review finding M1: SolreignFxProfilePolicy.CameraImpulseAllowed is a
        // per-CATEGORY capability gate shared by ImpactLight/ImpactHeavy (true for both under
        // full) — but spec §2's primitive table names camera impulse under impact_heavy
        // specifically, never impact_light. The recipe must narrow this itself.
        var plan = Build(SolreignFxCategory.ImpactLight, "impact_light", SolreignFxProfile.Full);

        Assert.That(plan.CameraImpulseEnabled, Is.False,
            "impact_light must never trigger a camera impulse, in any profile including full — that's impact_heavy's signature, not impact_light's");
    }

    [TestCase(SolreignFxProfile.ReducedMotion)]
    [TestCase(SolreignFxProfile.LowVfx)]
    [TestCase(SolreignFxProfile.CosmeticMinimal)]
    public void BuildPlan_ImpactHeavy_NeverCameraImpulse_OutsideFull(SolreignFxProfile profile)
    {
        var plan = Build(SolreignFxCategory.ImpactHeavy, "impact_heavy", profile);

        Assert.That(plan.CameraImpulseEnabled, Is.False,
            "spec §4: 'sprite kept (no camera impulse ever fires — camera-intensity forced to 0)' outside full profile");
    }

    [TestCase(SolreignFxProfile.Full)]
    [TestCase(SolreignFxProfile.ReducedMotion)]
    [TestCase(SolreignFxProfile.LowVfx)]
    public void BuildPlan_ImpactPrimitives_CosmeticMinimalIsTheOnlyIconFlashProfile(SolreignFxProfile profile)
    {
        var plan = Build(SolreignFxCategory.ImpactLight, "impact_light", profile);
        Assert.That(plan.SpriteIsIconFlash, Is.False);
    }

    [Test]
    public void BuildPlan_ImpactLight_CosmeticMinimal_IsIconFlashButLightSurvives()
    {
        var plan = Build(SolreignFxCategory.ImpactLight, "impact_light", SolreignFxProfile.CosmeticMinimal);

        Assert.Multiple(() =>
        {
            Assert.That(plan.SpriteEnabled, Is.True, "spec: sprite replaced by icon flash, not dropped");
            Assert.That(plan.SpriteIsIconFlash, Is.True);
            Assert.That(plan.LightEnabled, Is.True, "spec: light kept (color-independent, single non-flashing pulse)");
        });
    }

    [Test]
    public void BuildPlan_Electrical_Full_HasOverlayAndAnimatedDistortion()
    {
        var plan = Build(SolreignFxCategory.Electrical, "electrical", SolreignFxProfile.Full);

        Assert.Multiple(() =>
        {
            Assert.That(plan.OverlayEnabled, Is.True);
            Assert.That(plan.OverlayAnimated, Is.True);
        });
    }

    [TestCase(SolreignFxProfile.ReducedMotion)]
    [TestCase(SolreignFxProfile.LowVfx)]
    public void BuildPlan_Electrical_OverlayDisabled_ReducedMotionAndLowVfx(SolreignFxProfile profile)
    {
        var plan = Build(SolreignFxCategory.Electrical, "electrical", profile);

        Assert.That(plan.OverlayEnabled, Is.False, "spec §4: overlay distortion disabled (motion-coded) outside full");
    }

    [Test]
    public void BuildPlan_Electrical_CosmeticMinimal_SpriteAndOverlayDropped_LightAndAudioSurvive()
    {
        var plan = Build(SolreignFxCategory.Electrical, "electrical", SolreignFxProfile.CosmeticMinimal);

        Assert.Multiple(() =>
        {
            Assert.That(plan.SpriteEnabled, Is.False);
            Assert.That(plan.OverlayEnabled, Is.False);
            Assert.That(plan.LightEnabled, Is.True, "spec: 'light-only pulse + audio cue carries the telegraph'");
            Assert.That(plan.AudioEnabled, Is.True);
        });
    }

    [TestCase(SolreignFxProfile.Full)]
    [TestCase(SolreignFxProfile.ReducedMotion)]
    [TestCase(SolreignFxProfile.LowVfx)]
    public void BuildPlan_CastRing_NeverDroppedOutsideCosmeticMinimal(SolreignFxProfile profile)
    {
        var plan = Build(SolreignFxCategory.CastRing, "cast_ring", profile);

        Assert.That(plan.OverlayEnabled, Is.True, "cast_ring is gameplay-critical (spec §4.0) — never fully dropped");
    }

    [Test]
    public void BuildPlan_CastRing_ReducedMotion_OverlayFrozenNotAnimated()
    {
        var plan = Build(SolreignFxCategory.CastRing, "cast_ring", SolreignFxProfile.ReducedMotion);

        Assert.That(plan.OverlayAnimated, Is.False, "spec: 'ring animation frozen to a slow linear fill (no pulsing/rotation)'");
    }

    [Test]
    public void BuildPlan_CastRing_Full_OverlayAnimated()
    {
        var plan = Build(SolreignFxCategory.CastRing, "cast_ring", SolreignFxProfile.Full);

        Assert.That(plan.OverlayAnimated, Is.True);
    }

    [Test]
    public void BuildPlan_CastRing_CosmeticMinimal_ReplacedByCountdownText_NeverOverlay()
    {
        var plan = Build(SolreignFxCategory.CastRing, "cast_ring", SolreignFxProfile.CosmeticMinimal);

        Assert.Multiple(() =>
        {
            Assert.That(plan.OverlayEnabled, Is.False);
            Assert.That(plan.CountdownTextEnabled, Is.True, "spec: 'ring replaced by a static icon + countdown text above the caster'");
        });
    }

    [Test]
    public void BuildPlan_Transformation_ScreenSting_OnlyFullProfileAndOnlyRealDetailId()
    {
        var fullDetail = Build(SolreignFxCategory.Transformation, "transformation", SolreignFxProfile.Full);
        var fullGeneric = Build(SolreignFxCategory.Transformation, "transformation_generic", SolreignFxProfile.Full);
        var reducedDetail = Build(SolreignFxCategory.Transformation, "transformation", SolreignFxProfile.ReducedMotion);

        Assert.Multiple(() =>
        {
            Assert.That(fullDetail.ScreenStingEnabled, Is.True, "the actor's own real cue, full profile, should sting");
            Assert.That(fullGeneric.ScreenStingEnabled, Is.False, "a bystander's redacted cue must NEVER sting their screen (spec §5.2)");
            Assert.That(reducedDetail.ScreenStingEnabled, Is.False, "spec §4: 'screen sting disabled for the actor' under reduced_motion");
        });
    }

    [Test]
    public void BuildPlan_Transformation_CosmeticMinimal_BurstDroppedAppearanceSurvivesOutsideThisTable()
    {
        var plan = Build(SolreignFxCategory.Transformation, "transformation", SolreignFxProfile.CosmeticMinimal);

        Assert.Multiple(() =>
        {
            Assert.That(plan.SpriteEnabled, Is.False, "spec: 'burst/sting dropped' at cosmetic_minimal");
            Assert.That(plan.ScreenStingEnabled, Is.False);
        });

        // The FullyDropped short-circuit must never fire for Transformation — its appearance-layer
        // swap (outside this table entirely) must always survive per spec §4.0's structural rule.
        var behavior = SolreignFxProfilePolicy.GetBehavior(SolreignFxCategory.Transformation, SolreignFxProfile.CosmeticMinimal);
        Assert.That(behavior.FullyDropped, Is.False);
    }

    [TestCase(SolreignFxCategory.StaminaBreak)]
    [TestCase(SolreignFxCategory.BodyShockGeneric)]
    public void BuildPlan_StaminaBreakFamily_AudioAlwaysMandatory_EveryProfile(SolreignFxCategory category)
    {
        foreach (SolreignFxProfile profile in Enum.GetValues<SolreignFxProfile>())
        {
            var plan = Build(category, EffectIdFor(category), profile);
            Assert.That(plan.AudioEnabled, Is.True, $"{category}/{profile}: audio must be mandatory per spec §4's stamina_break row, in EVERY profile including cosmetic_minimal");
        }
    }

    [TestCase(SolreignFxCategory.StaminaBreak)]
    [TestCase(SolreignFxCategory.BodyShockGeneric)]
    public void BuildPlan_StaminaBreakFamily_CosmeticMinimal_SpriteDroppedLightSurvives(SolreignFxCategory category)
    {
        var plan = Build(category, EffectIdFor(category), SolreignFxProfile.CosmeticMinimal);

        Assert.Multiple(() =>
        {
            Assert.That(plan.SpriteEnabled, Is.False);
            Assert.That(plan.LightEnabled, Is.True);
        });
    }

    [Test]
    public void BuildPlan_DeterministicSeed_SameSeedSameCategory_SameVariationIndex()
    {
        var a = Build(SolreignFxCategory.ImpactLight, "impact_light", SolreignFxProfile.Full, seed: 777);
        var b = Build(SolreignFxCategory.ImpactLight, "impact_light", SolreignFxProfile.Full, seed: 777);

        Assert.That(a.VariationIndex, Is.EqualTo(b.VariationIndex));
    }

    [Test]
    public void BuildPlan_VariationIndex_NeverUsesSignedModuloOrAbs()
    {
        // Regression guard for cdx #18 (spec §1.2): a seed with the sign bit set must still produce
        // an in-range variation index (Math.Abs(int.MinValue) would throw/overflow if this were ever
        // implemented via signed modulo instead of SolreignFxSeedMixing.Index).
        var plan = Build(SolreignFxCategory.ImpactLight, "impact_light", SolreignFxProfile.Full, seed: 0x8000_0000u);

        Assert.That(plan.VariationIndex, Is.LessThan(SolreignFxRenderRecipe.VariationCount));
    }

    private static string EffectIdFor(SolreignFxCategory category) => category switch
    {
        SolreignFxCategory.ImpactLight => "impact_light",
        SolreignFxCategory.ImpactHeavy => "impact_heavy",
        SolreignFxCategory.Electrical => "electrical",
        SolreignFxCategory.Dust => "dust",
        SolreignFxCategory.Smoke => "smoke",
        SolreignFxCategory.CastRing => "cast_ring",
        SolreignFxCategory.Transformation => "transformation",
        SolreignFxCategory.StaminaBreak => "stamina_break",
        SolreignFxCategory.BodyShockGeneric => "body_shock_generic",
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };
}
