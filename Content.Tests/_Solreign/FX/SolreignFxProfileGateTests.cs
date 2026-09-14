using Content.Shared._Solreign.FX;
using NUnit.Framework;

namespace Content.Tests._Solreign.FX;

/// <summary>
///     Unit coverage for <see cref="SolreignFxProfileGate"/> (spec §4) — each accessibility
///     profile's computed-gate property under every CVar combination, mirroring
///     <c>MovementBobSystem.FeatureActive</c>'s test pattern. The "Unit — accessibility gate" row
///     of §7's test plan.
/// </summary>
[TestFixture]
[TestOf(typeof(SolreignFxProfileGate))]
public sealed class SolreignFxProfileGateTests
{
    // --- ParseProfile: never throws, unrecognized/null/empty falls back to Full ---

    [TestCase("full", SolreignFxProfile.Full)]
    [TestCase("reduced_motion", SolreignFxProfile.ReducedMotion)]
    [TestCase("low_vfx", SolreignFxProfile.LowVfx)]
    [TestCase("cosmetic_minimal", SolreignFxProfile.CosmeticMinimal)]
    public void ParseProfile_RecognizedValue_ParsesCorrectly(string raw, SolreignFxProfile expected)
    {
        Assert.That(SolreignFxProfileGate.ParseProfile(raw), Is.EqualTo(expected));
    }

    [Test]
    public void ParseProfile_Null_FallsBackToFull()
    {
        Assert.That(SolreignFxProfileGate.ParseProfile(null), Is.EqualTo(SolreignFxProfile.Full));
    }

    [Test]
    public void ParseProfile_Empty_FallsBackToFull()
    {
        Assert.That(SolreignFxProfileGate.ParseProfile(string.Empty), Is.EqualTo(SolreignFxProfile.Full));
    }

    [Test]
    public void ParseProfile_UnrecognizedTypo_FallsBackToFullWithoutThrowing()
    {
        SolreignFxProfile result = default;
        Assert.DoesNotThrow(() => result = SolreignFxProfileGate.ParseProfile("full_typo_corrupted"));
        Assert.That(result, Is.EqualTo(SolreignFxProfile.Full));
    }

    // --- EffectiveProfile: engine reduced-motion can only strengthen an explicit Full selection, never weaken a stricter one ---

    [Test]
    public void EffectiveProfile_ExplicitFull_EngineReducedMotionOff_StaysFull()
    {
        var result = SolreignFxProfileGate.EffectiveProfile(SolreignFxProfile.Full, engineReducedMotion: false);
        Assert.That(result, Is.EqualTo(SolreignFxProfile.Full));
    }

    [Test]
    public void EffectiveProfile_ExplicitFull_EngineReducedMotionOn_StrengthensToReducedMotion()
    {
        var result = SolreignFxProfileGate.EffectiveProfile(SolreignFxProfile.Full, engineReducedMotion: true);
        Assert.That(result, Is.EqualTo(SolreignFxProfile.ReducedMotion));
    }

    [TestCase(SolreignFxProfile.ReducedMotion)]
    [TestCase(SolreignFxProfile.LowVfx)]
    [TestCase(SolreignFxProfile.CosmeticMinimal)]
    public void EffectiveProfile_AlreadyStricterThanFull_EngineReducedMotionNeverWeakensIt(SolreignFxProfile explicitProfile)
    {
        var withEngineOff = SolreignFxProfileGate.EffectiveProfile(explicitProfile, engineReducedMotion: false);
        var withEngineOn = SolreignFxProfileGate.EffectiveProfile(explicitProfile, engineReducedMotion: true);

        Assert.That(withEngineOff, Is.EqualTo(explicitProfile));
        Assert.That(withEngineOn, Is.EqualTo(explicitProfile));
    }

    // --- CueSystemActive: master kill-switch gate ---

    [Test]
    public void CueSystemActive_MirrorsMasterCVarExactly()
    {
        Assert.That(SolreignFxProfileGate.CueSystemActive(true), Is.True);
        Assert.That(SolreignFxProfileGate.CueSystemActive(false), Is.False);
    }

    // --- Photosensitivity: WCAG 2.3.1 flash-rate cap, applies in EVERY profile (spec §4.0, grk #4C) ---

    [Test]
    public void MaxFlashTransitionsPerSecond_IsTheWcagGeneralFlashThreshold()
    {
        Assert.That(SolreignFxProfileGate.MaxFlashTransitionsPerSecond, Is.EqualTo(3f));
    }

    [Test]
    public void WithinFlashSafeRate_AtExactlyTheCap_IsSafe()
    {
        Assert.That(SolreignFxProfileGate.WithinFlashSafeRate(3f), Is.True);
    }

    [Test]
    public void WithinFlashSafeRate_AboveTheCap_IsUnsafe()
    {
        Assert.That(SolreignFxProfileGate.WithinFlashSafeRate(3.0001f), Is.False);
    }

    [Test]
    public void WithinFlashSafeRate_Zero_IsSafe()
    {
        Assert.That(SolreignFxProfileGate.WithinFlashSafeRate(0f), Is.True);
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void WithinFlashSafeRate_NonFinite_IsNeverSafe(float poison)
    {
        bool result = true;
        Assert.DoesNotThrow(() => result = SolreignFxProfileGate.WithinFlashSafeRate(poison));
        Assert.That(result, Is.False);
    }

    // --- FlickerAllowed: the dedicated no_flash toggle, independent of profile ---

    [Test]
    public void FlickerAllowed_NoFlashEnabled_IsFalse()
    {
        Assert.That(SolreignFxProfileGate.FlickerAllowed(noFlashEnabled: true), Is.False);
    }

    [Test]
    public void FlickerAllowed_NoFlashDisabled_IsTrue()
    {
        Assert.That(SolreignFxProfileGate.FlickerAllowed(noFlashEnabled: false), Is.True);
    }
}
