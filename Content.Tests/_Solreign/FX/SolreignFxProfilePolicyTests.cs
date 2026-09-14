#nullable enable
using System;
using Content.Shared._Solreign.FX;
using NUnit.Framework;

namespace Content.Tests._Solreign.FX;

/// <summary>
///     Spec §4's per-primitive per-profile gating matrix, made mechanical
///     (<see cref="SolreignFxProfilePolicy"/>). Covers the mission's "per-profile gating matrix"
///     row, including the §4.0 structural rule that <c>cast_ring</c>/<c>transformation</c>/
///     <c>stamina_break</c>/<c>body_shock_generic</c> may NEVER be fully dropped in any profile.
/// </summary>
[TestFixture]
[TestOf(typeof(SolreignFxProfilePolicy))]
public sealed class SolreignFxProfilePolicyTests
{
    private static readonly SolreignFxCategory[] NeverDropCategories =
    {
        SolreignFxCategory.CastRing,
        SolreignFxCategory.Transformation,
        SolreignFxCategory.StaminaBreak,
        SolreignFxCategory.BodyShockGeneric,
    };

    private static readonly SolreignFxCategory[] PureCosmeticCategories =
    {
        SolreignFxCategory.ImpactLight,
        SolreignFxCategory.ImpactHeavy,
        SolreignFxCategory.Electrical,
        SolreignFxCategory.Dust,
        SolreignFxCategory.Smoke,
    };

    [Test]
    public void GetBehavior_NeverDropCategories_AreNeverFullyDroppedInAnyProfile(
        [ValueSource(nameof(NeverDropCategories))] SolreignFxCategory category,
        [Values] SolreignFxProfile profile)
    {
        var behavior = SolreignFxProfilePolicy.GetBehavior(category, profile);

        Assert.That(behavior.MustNeverFullyDrop, Is.True, $"{category} carries a gameplay-critical telegraph per spec §4.0 — it must be marked never-fully-drop in every profile, including {profile}");
        Assert.That(behavior.FullyDropped, Is.False, $"{category} must never actually compute as fully dropped under {profile}");
    }

    [Test]
    public void GetBehavior_CosmeticMinimal_PureCosmeticCategories_MayFullyDrop(
        [ValueSource(nameof(PureCosmeticCategories))] SolreignFxCategory category)
    {
        var behavior = SolreignFxProfilePolicy.GetBehavior(category, SolreignFxProfile.CosmeticMinimal);

        Assert.That(behavior.MustNeverFullyDrop, Is.False, $"{category} is pure cosmetic feedback per spec §4 — no gameplay state is encoded in it exclusively");
    }

    [Test]
    public void GetBehavior_DustAndSmoke_CosmeticMinimal_AreFullyDropped()
    {
        Assert.That(SolreignFxProfilePolicy.GetBehavior(SolreignFxCategory.Dust, SolreignFxProfile.CosmeticMinimal).FullyDropped, Is.True);
        Assert.That(SolreignFxProfilePolicy.GetBehavior(SolreignFxCategory.Smoke, SolreignFxProfile.CosmeticMinimal).FullyDropped, Is.True);
    }

    [Test]
    public void GetBehavior_ImpactCategories_ReducedMotion_ForcesNoCameraImpulse()
    {
        var heavy = SolreignFxProfilePolicy.GetBehavior(SolreignFxCategory.ImpactHeavy, SolreignFxProfile.ReducedMotion);
        var light = SolreignFxProfilePolicy.GetBehavior(SolreignFxCategory.ImpactLight, SolreignFxProfile.ReducedMotion);

        Assert.That(heavy.CameraImpulseAllowed, Is.False);
        Assert.That(light.CameraImpulseAllowed, Is.False);
        Assert.That(heavy.SpriteAllowed, Is.True, "the sprite itself is kept under reduced motion — only the camera impulse is forced off");
    }

    [Test]
    public void GetBehavior_Electrical_ReducedMotionAndLowVfx_DisableOverlayButKeepSpriteAndLight()
    {
        var reduced = SolreignFxProfilePolicy.GetBehavior(SolreignFxCategory.Electrical, SolreignFxProfile.ReducedMotion);
        var lowVfx = SolreignFxProfilePolicy.GetBehavior(SolreignFxCategory.Electrical, SolreignFxProfile.LowVfx);

        Assert.Multiple(() =>
        {
            Assert.That(reduced.OverlayAllowed, Is.False, "overlay distortion is motion-coded — disabled under reduced motion");
            Assert.That(reduced.SpriteAllowed, Is.True);
            Assert.That(reduced.LightAllowed, Is.True);
            Assert.That(lowVfx.OverlayAllowed, Is.False);
        });
    }

    [Test]
    public void GetBehavior_Electrical_CosmeticMinimal_DropsOverlayAndSpriteButKeepsLight()
    {
        var behavior = SolreignFxProfilePolicy.GetBehavior(SolreignFxCategory.Electrical, SolreignFxProfile.CosmeticMinimal);

        Assert.Multiple(() =>
        {
            Assert.That(behavior.OverlayAllowed, Is.False);
            Assert.That(behavior.SpriteAllowed, Is.False);
            Assert.That(behavior.LightAllowed, Is.True, "light-only pulse + audio carries the telegraph at cosmetic_minimal per spec §4");
        });
    }

    [Test]
    public void GetBehavior_LowVfx_ImpactAndDustSmoke_ScaleConcurrentCapDownNeverUp()
    {
        Assert.That(SolreignFxProfilePolicy.GetBehavior(SolreignFxCategory.ImpactLight, SolreignFxProfile.LowVfx).ConcurrentCapMultiplier, Is.EqualTo(0.5f));
        Assert.That(SolreignFxProfilePolicy.GetBehavior(SolreignFxCategory.Dust, SolreignFxProfile.LowVfx).ConcurrentCapMultiplier, Is.EqualTo(0.4f));
        Assert.That(SolreignFxProfilePolicy.GetBehavior(SolreignFxCategory.Dust, SolreignFxProfile.Full).ConcurrentCapMultiplier, Is.EqualTo(1f));
    }

    [Test]
    public void GetBehavior_EveryDefinedCategoryProfilePair_IsHandledExplicitly()
    {
        // A total function proof: no (category, profile) pair may silently fall through to the
        // catch-all "everything off" default — every combination the enum space defines must
        // resolve to a row this test can independently sanity-check (FullyDropped is only ever
        // legal for the pure-cosmetic categories at cosmetic_minimal).
        foreach (SolreignFxCategory category in Enum.GetValues<SolreignFxCategory>())
        {
            foreach (SolreignFxProfile profile in Enum.GetValues<SolreignFxProfile>())
            {
                var behavior = SolreignFxProfilePolicy.GetBehavior(category, profile);
                if (behavior.FullyDropped)
                {
                    Assert.That(Array.IndexOf(PureCosmeticCategories, category), Is.Not.EqualTo(-1),
                        $"({category}, {profile}) computed as fully dropped but isn't one of the categories spec §4 allows to degrade to nothing");
                    Assert.That(profile, Is.EqualTo(SolreignFxProfile.CosmeticMinimal),
                        $"({category}, {profile}) — only cosmetic_minimal may fully drop a pure-cosmetic category");
                }
            }
        }
    }
}
