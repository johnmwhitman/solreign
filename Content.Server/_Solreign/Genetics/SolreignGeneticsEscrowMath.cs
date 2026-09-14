using System;

namespace Content.Server._Solreign.Genetics;

/// <summary>
///     Sequencing state of a DNA sample.
/// </summary>
public enum SequencingState : byte
{
    Unsequenced = 0,
    Sequencing = 1,
    Sequenced = 2,
    Mutated = 3,
    Corrupted = 4,
}

/// <summary>
///     Mutation stability band classification.
/// </summary>
public enum StabilityBand : byte
{
    Critical = 0,    // < 0.2
    Unstable = 1,    // 0.2 <= s < 0.4
    Moderate = 2,    // 0.4 <= s < 0.8
    High = 3,        // >= 0.8
}

/// <summary>
///     Radiation hazard classification for DNA samples.
/// </summary>
public enum RadiationHazardLevel : byte
{
    None = 0,        // R <= RadThreshold
    Moderate = 1,    // RadThreshold < R <= RadThreshold * 2.0
    Severe = 2,      // RadThreshold * 2.0 < R <= RadThreshold * 4.0 or stability < 0.2
    Lethal = 3,      // R > RadThreshold * 4.0
}

/// <summary>
///     Outcome status of an escrow payout evaluation.
/// </summary>
public enum EscrowStatus : byte
{
    Rejected = 0,
    Forfeited = 1,
    SafetyRefund = 2,
    PartialPayout = 3,
    FullPayout = 4,
}

/// <summary>
///     Structured result of an escrow payout evaluation.
/// </summary>
public sealed record EscrowResult(
    int PayoutAmount,
    int RefundAmount,
    int TotalReturn,
    EscrowStatus Status,
    string Reason
);

/// <summary>
///     Pure, zero-side-effect mathematical engine for SR-W-068 DNA sample sequencing,
///     mutation stability calculations, radiation hazard thresholds, and escrow credit payout/refund logic.
/// </summary>
public static class SolreignGeneticsEscrowMath
{
    public const float DefaultInstabilityPerMutation = 0.15f;

    /// <summary>
    ///     Calculates normalized DNA sequencing progress in range [0.0, 1.0].
    ///     Degrades effective quality under radiation exposure exceeding <paramref name="radThreshold"/>.
    /// </summary>
    public static float CalculateSequencingProgress(
        int baseLength,
        int readsCompleted,
        float quality,
        float radExposure,
        float radThreshold)
    {
        if (baseLength <= 0 || readsCompleted <= 0)
            return 0.0f;

        float cleanQuality = float.IsNaN(quality) || float.IsInfinity(quality) ? 0.0f : Math.Clamp(quality, 0.0f, 1.0f);
        float cleanRad = float.IsNaN(radExposure) || float.IsInfinity(radExposure) ? 0.0f : Math.Max(0.0f, radExposure);
        float cleanThreshold = float.IsNaN(radThreshold) || float.IsInfinity(radThreshold) ? 50.0f : Math.Max(0.0f, radThreshold);

        float excessRad = Math.Max(0.0f, cleanRad - cleanThreshold);
        float qualityPenalty = excessRad * 0.005f;
        float effectiveQuality = Math.Max(0.0f, cleanQuality - qualityPenalty);

        float rawProgress = (readsCompleted * effectiveQuality) / baseLength;
        return float.IsNaN(rawProgress) || float.IsInfinity(rawProgress)
            ? 0.0f
            : Math.Clamp(rawProgress, 0.0f, 1.0f);
    }

    /// <summary>
    ///     Calculates mutation stability ratio S in range [0.0, 1.0].
    ///     Base stability starts at 1.0 and is reduced by mutation count and radiation penalties.
    /// </summary>
    public static float CalculateMutationStability(
        int mutationCount,
        float instabilityPerMutation,
        float radExposure,
        float radThreshold)
    {
        int cleanMutations = Math.Max(0, mutationCount);
        float cleanRate = float.IsNaN(instabilityPerMutation) || float.IsInfinity(instabilityPerMutation)
            ? DefaultInstabilityPerMutation
            : Math.Max(0.0f, instabilityPerMutation);
        float cleanRad = float.IsNaN(radExposure) || float.IsInfinity(radExposure) ? 0.0f : Math.Max(0.0f, radExposure);
        float cleanThreshold = float.IsNaN(radThreshold) || float.IsInfinity(radThreshold) ? 50.0f : Math.Max(0.0f, radThreshold);

        float mutationPenalty = cleanMutations * cleanRate;

        float excessRad = Math.Max(0.0f, cleanRad - cleanThreshold);
        float radPenalty = (excessRad / 100.0f) * 0.5f;

        float rawStability = 1.0f - mutationPenalty - radPenalty;
        return float.IsNaN(rawStability) || float.IsInfinity(rawStability)
            ? 0.0f
            : Math.Clamp(rawStability, 0.0f, 1.0f);
    }

    /// <summary>
    ///     Classifies mutation stability ratio into a discrete StabilityBand.
    /// </summary>
    public static StabilityBand GetStabilityBand(float stability)
    {
        float s = float.IsNaN(stability) || float.IsInfinity(stability) ? 0.0f : Math.Clamp(stability, 0.0f, 1.0f);

        if (s >= 0.8f)
            return StabilityBand.High;
        if (s >= 0.4f)
            return StabilityBand.Moderate;
        if (s >= 0.2f)
            return StabilityBand.Unstable;

        return StabilityBand.Critical;
    }

    /// <summary>
    ///     Evaluates radiation hazard level based on radiation exposure vs threshold and sample stability.
    /// </summary>
    public static RadiationHazardLevel EvaluateRadiationHazard(float radExposure, float radThreshold, float stability)
    {
        float rad = float.IsNaN(radExposure) || float.IsInfinity(radExposure) ? 0.0f : Math.Max(0.0f, radExposure);
        float thresh = float.IsNaN(radThreshold) || float.IsInfinity(radThreshold) ? 50.0f : Math.Max(0.0f, radThreshold);
        float s = float.IsNaN(stability) || float.IsInfinity(stability) ? 0.0f : Math.Clamp(stability, 0.0f, 1.0f);

        if (rad > thresh * 4.0f)
            return RadiationHazardLevel.Lethal;

        if (rad > thresh * 2.0f || s < 0.2f)
            return RadiationHazardLevel.Severe;

        if (rad > thresh)
            return RadiationHazardLevel.Moderate;

        return RadiationHazardLevel.None;
    }

    /// <summary>
    ///     Evaluates escrow deposit refund and reward payout based on sample sequencing state,
    ///     stability, radiation hazard, and system parameters.
    /// </summary>
    public static EscrowResult EvaluateEscrowPayout(
        int deposit,
        float stability,
        float radExposure,
        float radThreshold,
        SequencingState state,
        float minStabilityThreshold = 0.4f)
    {
        int cleanDeposit = Math.Max(0, deposit);
        float cleanStability = float.IsNaN(stability) || float.IsInfinity(stability) ? 0.0f : Math.Clamp(stability, 0.0f, 1.0f);
        float cleanMinStability = float.IsNaN(minStabilityThreshold) || float.IsInfinity(minStabilityThreshold) ? 0.4f : Math.Clamp(minStabilityThreshold, 0.0f, 1.0f);

        if (cleanDeposit == 0)
        {
            return new EscrowResult(0, 0, 0, EscrowStatus.Rejected, "Zero deposit provided");
        }

        if (state == SequencingState.Unsequenced || state == SequencingState.Sequencing)
        {
            return new EscrowResult(0, cleanDeposit, cleanDeposit, EscrowStatus.Rejected, "Sequencing incomplete");
        }

        var hazard = EvaluateRadiationHazard(radExposure, radThreshold, cleanStability);

        // Environmental hazard refund path (radiation surge beyond operator control)
        if (state == SequencingState.Corrupted || hazard >= RadiationHazardLevel.Severe)
        {
            float cleanRad = float.IsNaN(radExposure) ? 0.0f : radExposure;
            float cleanThresh = float.IsNaN(radThreshold) ? 50.0f : radThreshold;

            if (cleanRad > cleanThresh * 2.0f)
            {
                int refund = cleanDeposit / 2;
                return new EscrowResult(0, refund, refund, EscrowStatus.SafetyRefund, "Radiation hazard safety refund issued");
            }

            return new EscrowResult(0, 0, 0, EscrowStatus.Forfeited, "Sample corrupted, escrow deposit forfeited");
        }

        // Sequenced or Mutated state evaluation
        var band = GetStabilityBand(cleanStability);

        if (band == StabilityBand.High && hazard == RadiationHazardLevel.None)
        {
            int payout = (int)(cleanDeposit * 1.5f);
            int refund = cleanDeposit;
            return new EscrowResult(payout, refund, refund + payout, EscrowStatus.FullPayout, "Full escrow payout awarded for high stability DNA sample");
        }

        if (cleanStability >= cleanMinStability)
        {
            int payout = (int)(cleanDeposit * cleanStability);
            int refund = cleanDeposit;
            return new EscrowResult(payout, refund, refund + payout, EscrowStatus.PartialPayout, "Partial escrow payout awarded for degraded DNA sample");
        }

        return new EscrowResult(0, 0, 0, EscrowStatus.Forfeited, "Sample stability below minimum threshold, escrow deposit forfeited");
    }
}
