using System;
using Content.Server._Solreign.Genetics;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(SolreignGeneticsEscrowMath))]
[TestOf(typeof(SolreignGeneticsEscrowSystem))]
public sealed class SolreignGeneticsEscrowTests
{
    private const float RadThreshold = 50.0f;
    private const float MinStability = 0.4f;

    // ---------------------------------------------------------------- DNA Sequencing Math

    [Test]
    public void CalculateSequencingProgress_NormalInputs_ReturnsExpectedProgress()
    {
        float progress = SolreignGeneticsEscrowMath.CalculateSequencingProgress(
            baseLength: 1000,
            readsCompleted: 500,
            quality: 1.0f,
            radExposure: 0.0f,
            radThreshold: RadThreshold);

        Assert.That(progress, Is.EqualTo(0.5f).Within(1e-5));
    }

    [Test]
    public void CalculateSequencingProgress_FullReads_CapsAtOne()
    {
        float progress = SolreignGeneticsEscrowMath.CalculateSequencingProgress(
            baseLength: 1000,
            readsCompleted: 1500,
            quality: 1.0f,
            radExposure: 0.0f,
            radThreshold: RadThreshold);

        Assert.That(progress, Is.EqualTo(1.0f));
    }

    [Test]
    public void CalculateSequencingProgress_RadiationExposureExceedsThreshold_DegradesQuality()
    {
        float cleanProgress = SolreignGeneticsEscrowMath.CalculateSequencingProgress(1000, 1000, 1.0f, 0.0f, RadThreshold);
        float radProgress = SolreignGeneticsEscrowMath.CalculateSequencingProgress(1000, 1000, 1.0f, 100.0f, RadThreshold);

        Assert.Multiple(() =>
        {
            Assert.That(cleanProgress, Is.EqualTo(1.0f));
            Assert.That(radProgress, Is.LessThan(1.0f));
            // 50 excess rads * 0.005 = 0.25 quality penalty -> 0.75 effective quality -> 0.75 progress
            Assert.That(radProgress, Is.EqualTo(0.75f).Within(1e-5));
        });
    }

    [Test]
    public void CalculateSequencingProgress_InvalidAndBoundaryInputs_NoExceptions()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SolreignGeneticsEscrowMath.CalculateSequencingProgress(0, 500, 1.0f, 0f, RadThreshold), Is.EqualTo(0.0f));
            Assert.That(SolreignGeneticsEscrowMath.CalculateSequencingProgress(-100, 500, 1.0f, 0f, RadThreshold), Is.EqualTo(0.0f));
            Assert.That(SolreignGeneticsEscrowMath.CalculateSequencingProgress(1000, -50, 1.0f, 0f, RadThreshold), Is.EqualTo(0.0f));
            Assert.That(SolreignGeneticsEscrowMath.CalculateSequencingProgress(1000, 500, float.NaN, 0f, RadThreshold), Is.EqualTo(0.0f));
            Assert.That(SolreignGeneticsEscrowMath.CalculateSequencingProgress(1000, 500, 1.0f, float.PositiveInfinity, RadThreshold), Is.GreaterThanOrEqualTo(0.0f));
        });
    }

    // ---------------------------------------------------------------- Mutation Stability Calculations

    [Test]
    public void CalculateMutationStability_ZeroMutations_ReturnsFullStability()
    {
        float stability = SolreignGeneticsEscrowMath.CalculateMutationStability(
            mutationCount: 0,
            instabilityPerMutation: 0.15f,
            radExposure: 0.0f,
            radThreshold: RadThreshold);

        Assert.That(stability, Is.EqualTo(1.0f));
    }

    [Test]
    public void CalculateMutationStability_MultipleMutations_DegradesStability()
    {
        // 2 mutations * 0.15 = 0.30 instability penalty -> 0.70 stability
        float stability = SolreignGeneticsEscrowMath.CalculateMutationStability(
            mutationCount: 2,
            instabilityPerMutation: 0.15f,
            radExposure: 0.0f,
            radThreshold: RadThreshold);

        Assert.That(stability, Is.EqualTo(0.70f).Within(1e-5));
    }

    [Test]
    public void CalculateMutationStability_RadiationSurge_DegradesStabilityFurther()
    {
        // 1 mutation * 0.15 = 0.15 penalty
        // 100 rads (50 excess) -> (50/100)*0.5 = 0.25 rad penalty
        // Total stability = 1.0 - 0.15 - 0.25 = 0.60
        float stability = SolreignGeneticsEscrowMath.CalculateMutationStability(
            mutationCount: 1,
            instabilityPerMutation: 0.15f,
            radExposure: 100.0f,
            radThreshold: RadThreshold);

        Assert.That(stability, Is.EqualTo(0.60f).Within(1e-5));
    }

    [Test]
    public void GetStabilityBand_ClassifiesThresholdsCorrectly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SolreignGeneticsEscrowMath.GetStabilityBand(1.0f), Is.EqualTo(StabilityBand.High));
            Assert.That(SolreignGeneticsEscrowMath.GetStabilityBand(0.8f), Is.EqualTo(StabilityBand.High));
            Assert.That(SolreignGeneticsEscrowMath.GetStabilityBand(0.79f), Is.EqualTo(StabilityBand.Moderate));
            Assert.That(SolreignGeneticsEscrowMath.GetStabilityBand(0.4f), Is.EqualTo(StabilityBand.Moderate));
            Assert.That(SolreignGeneticsEscrowMath.GetStabilityBand(0.39f), Is.EqualTo(StabilityBand.Unstable));
            Assert.That(SolreignGeneticsEscrowMath.GetStabilityBand(0.2f), Is.EqualTo(StabilityBand.Unstable));
            Assert.That(SolreignGeneticsEscrowMath.GetStabilityBand(0.19f), Is.EqualTo(StabilityBand.Critical));
            Assert.That(SolreignGeneticsEscrowMath.GetStabilityBand(0.0f), Is.EqualTo(StabilityBand.Critical));
        });
    }

    // ---------------------------------------------------------------- Radiation Hazard Thresholds

    [Test]
    public void EvaluateRadiationHazard_ClassifiesLevelsCorrectly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SolreignGeneticsEscrowMath.EvaluateRadiationHazard(0f, RadThreshold, 1.0f), Is.EqualTo(RadiationHazardLevel.None));
            Assert.That(SolreignGeneticsEscrowMath.EvaluateRadiationHazard(50.0f, RadThreshold, 1.0f), Is.EqualTo(RadiationHazardLevel.None));
            Assert.That(SolreignGeneticsEscrowMath.EvaluateRadiationHazard(75.0f, RadThreshold, 1.0f), Is.EqualTo(RadiationHazardLevel.Moderate));
            Assert.That(SolreignGeneticsEscrowMath.EvaluateRadiationHazard(120.0f, RadThreshold, 1.0f), Is.EqualTo(RadiationHazardLevel.Severe));
            Assert.That(SolreignGeneticsEscrowMath.EvaluateRadiationHazard(250.0f, RadThreshold, 1.0f), Is.EqualTo(RadiationHazardLevel.Lethal));
            Assert.That(SolreignGeneticsEscrowMath.EvaluateRadiationHazard(0f, RadThreshold, 0.1f), Is.EqualTo(RadiationHazardLevel.Severe));
        });
    }

    // ---------------------------------------------------------------- Escrow Payout & Refund Logic

    [Test]
    public void EvaluateEscrowPayout_FullPayout_HighStabilityCleanSample()
    {
        var result = SolreignGeneticsEscrowMath.EvaluateEscrowPayout(
            deposit: 100,
            stability: 0.9f,
            radExposure: 0.0f,
            radThreshold: RadThreshold,
            state: SequencingState.Sequenced,
            minStabilityThreshold: MinStability);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(EscrowStatus.FullPayout));
            Assert.That(result.RefundAmount, Is.EqualTo(100));
            Assert.That(result.PayoutAmount, Is.EqualTo(150));
            Assert.That(result.TotalReturn, Is.EqualTo(250));
        });
    }

    [Test]
    public void EvaluateEscrowPayout_PartialPayout_ModerateStabilitySample()
    {
        // 0.6 stability -> payout = 100 * 0.6 = 60
        var result = SolreignGeneticsEscrowMath.EvaluateEscrowPayout(
            deposit: 100,
            stability: 0.6f,
            radExposure: 0.0f,
            radThreshold: RadThreshold,
            state: SequencingState.Sequenced,
            minStabilityThreshold: MinStability);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(EscrowStatus.PartialPayout));
            Assert.That(result.RefundAmount, Is.EqualTo(100));
            Assert.That(result.PayoutAmount, Is.EqualTo(60));
            Assert.That(result.TotalReturn, Is.EqualTo(160));
        });
    }

    [Test]
    public void EvaluateEscrowPayout_Forfeited_UnstableSampleBelowMinStability()
    {
        var result = SolreignGeneticsEscrowMath.EvaluateEscrowPayout(
            deposit: 100,
            stability: 0.3f,
            radExposure: 0.0f,
            radThreshold: RadThreshold,
            state: SequencingState.Sequenced,
            minStabilityThreshold: MinStability);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(EscrowStatus.Forfeited));
            Assert.That(result.RefundAmount, Is.EqualTo(0));
            Assert.That(result.PayoutAmount, Is.EqualTo(0));
            Assert.That(result.TotalReturn, Is.EqualTo(0));
        });
    }

    [Test]
    public void EvaluateEscrowPayout_SafetyRefund_RadiationSurgeHazard()
    {
        // Radiation exposure 150 > threshold 50 * 2.0 (100) -> Severe hazard refund (50% refund)
        var result = SolreignGeneticsEscrowMath.EvaluateEscrowPayout(
            deposit: 100,
            stability: 0.5f,
            radExposure: 150.0f,
            radThreshold: RadThreshold,
            state: SequencingState.Corrupted,
            minStabilityThreshold: MinStability);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(EscrowStatus.SafetyRefund));
            Assert.That(result.RefundAmount, Is.EqualTo(50));
            Assert.That(result.PayoutAmount, Is.EqualTo(0));
            Assert.That(result.TotalReturn, Is.EqualTo(50));
        });
    }

    [Test]
    public void EvaluateEscrowPayout_Rejected_IncompleteSequencingOrZeroDeposit()
    {
        var resultUnsequenced = SolreignGeneticsEscrowMath.EvaluateEscrowPayout(
            deposit: 100,
            stability: 1.0f,
            radExposure: 0.0f,
            radThreshold: RadThreshold,
            state: SequencingState.Unsequenced,
            minStabilityThreshold: MinStability);

        var resultZeroDeposit = SolreignGeneticsEscrowMath.EvaluateEscrowPayout(
            deposit: 0,
            stability: 1.0f,
            radExposure: 0.0f,
            radThreshold: RadThreshold,
            state: SequencingState.Sequenced,
            minStabilityThreshold: MinStability);

        Assert.Multiple(() =>
        {
            Assert.That(resultUnsequenced.Status, Is.EqualTo(EscrowStatus.Rejected));
            Assert.That(resultUnsequenced.RefundAmount, Is.EqualTo(100));

            Assert.That(resultZeroDeposit.Status, Is.EqualTo(EscrowStatus.Rejected));
            Assert.That(resultZeroDeposit.TotalReturn, Is.EqualTo(0));
        });
    }

    // ---------------------------------------------------------------- Component State & System Logic Tests

    [Test]
    public void Component_InitialValues_ComputePropertiesCorrectly()
    {
        var comp = new SolreignGeneticsEscrowComponent
        {
            BaseSequenceLength = 1000,
            ReadsCompleted = 1000,
            SequencingQuality = 1.0f,
            MutationCount = 1,
            RadiationExposure = 0.0f,
        };

        Assert.Multiple(() =>
        {
            Assert.That(comp.SequenceProgress, Is.EqualTo(1.0f));
            Assert.That(comp.MutationStability, Is.EqualTo(0.85f).Within(1e-5));
        });
    }

    // ---------------------------------------------------------------- Zero-Exception Fuzzing

    [Test]
    public void ZeroExceptionFuzzing_ExtremeAndMalformedInputs_NeverThrows()
    {
        int[] deposits = { int.MinValue, -100, 0, 100, int.MaxValue };
        float[] floats = { float.MinValue, -999.0f, -0.0f, 0.0f, 0.5f, 1.0f, 100.0f, float.MaxValue, float.NaN, float.PositiveInfinity, float.NegativeInfinity };
        int[] counts = { int.MinValue, -1, 0, 1, 10, 100, int.MaxValue };

        Assert.DoesNotThrow(() =>
        {
            foreach (var dep in deposits)
            {
                foreach (var s in floats)
                {
                    foreach (var rad in floats)
                    {
                        foreach (SequencingState st in Enum.GetValues(typeof(SequencingState)))
                        {
                            SolreignGeneticsEscrowMath.EvaluateEscrowPayout(dep, s, rad, RadThreshold, st, MinStability);
                            SolreignGeneticsEscrowMath.GetStabilityBand(s);
                            SolreignGeneticsEscrowMath.EvaluateRadiationHazard(rad, RadThreshold, s);
                        }
                    }
                }
            }

            foreach (var len in counts)
            {
                foreach (var reads in counts)
                {
                    foreach (var q in floats)
                    {
                        SolreignGeneticsEscrowMath.CalculateSequencingProgress(len, reads, q, 0f, RadThreshold);
                    }
                }
            }
        });
    }
}
