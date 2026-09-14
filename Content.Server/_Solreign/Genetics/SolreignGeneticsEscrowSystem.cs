using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Server._Solreign.Genetics;

/// <summary>
///     SR-W-068: Solreign Genetics Mutation &amp; DNA Sample Escrow System.
///     Manages DNA sample sequencing workflows, mutation stability ratios, genetic credit escrow deposits,
///     radiation hazard thresholds, and CVar-gated escrow payout evaluations.
/// </summary>
public sealed partial class SolreignGeneticsEscrowSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;

    private bool _enabled = true;
    private int _baseDeposit = 100;
    private float _radThreshold = 50.0f;
    private float _minStabilityThreshold = 0.4f;

    public bool IsEnabled => _enabled;
    public int BaseDeposit => _baseDeposit;
    public float RadThreshold => _radThreshold;
    public float MinStabilityThreshold => _minStabilityThreshold;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_config, CCVars.SolreignGeneticsEscrowEnabled, v => _enabled = v, invokeImmediately: true);
        Subs.CVar(_config, CCVars.SolreignGeneticsEscrowBaseDeposit, v => _baseDeposit = v, invokeImmediately: true);
        Subs.CVar(_config, CCVars.SolreignGeneticsEscrowRadThreshold, v => _radThreshold = v, invokeImmediately: true);
        Subs.CVar(_config, CCVars.SolreignGeneticsEscrowMinStability, v => _minStabilityThreshold = v, invokeImmediately: true);
    }

    /// <summary>
    ///     Locks a credit deposit into escrow for the target genetics component.
    /// </summary>
    public bool DepositEscrow(Entity<SolreignGeneticsEscrowComponent> ent, int depositAmount)
    {
        if (!_enabled || depositAmount <= 0)
            return false;

        ent.Comp.PendingDeposit += depositAmount;
        return true;
    }

    /// <summary>
    ///     Processes sequencing reads for a DNA sample and updates sequencing state.
    /// </summary>
    public float ProcessSequencingRead(Entity<SolreignGeneticsEscrowComponent> ent, int additionalReads, float quality)
    {
        if (!_enabled || additionalReads <= 0)
            return ent.Comp.SequenceProgress;

        ent.Comp.ReadsCompleted += additionalReads;
        ent.Comp.SequencingQuality = quality;

        float progress = SolreignGeneticsEscrowMath.CalculateSequencingProgress(
            ent.Comp.BaseSequenceLength,
            ent.Comp.ReadsCompleted,
            ent.Comp.SequencingQuality,
            ent.Comp.RadiationExposure,
            _radThreshold);

        if (progress >= 1.0f && (ent.Comp.State == SequencingState.Unsequenced || ent.Comp.State == SequencingState.Sequencing))
        {
            ent.Comp.State = ent.Comp.MutationCount > 0 ? SequencingState.Mutated : SequencingState.Sequenced;
        }
        else if (progress > 0.0f && ent.Comp.State == SequencingState.Unsequenced)
        {
            ent.Comp.State = SequencingState.Sequencing;
        }

        return progress;
    }

    /// <summary>
    ///     Adds genetic mutations to the sample and updates state if corrupted.
    /// </summary>
    public void AddMutation(Entity<SolreignGeneticsEscrowComponent> ent, int count = 1)
    {
        if (!_enabled || count <= 0)
            return;

        ent.Comp.MutationCount += count;
        if (ent.Comp.State == SequencingState.Sequenced)
        {
            ent.Comp.State = SequencingState.Mutated;
        }

        float stability = GetMutationStability(ent);
        if (stability < 0.2f && ent.Comp.State != SequencingState.Unsequenced)
        {
            ent.Comp.State = SequencingState.Corrupted;
        }
    }

    /// <summary>
    ///     Updates radiation exposure levels and checks radiation hazard thresholds.
    /// </summary>
    public RadiationHazardLevel UpdateRadiationExposure(Entity<SolreignGeneticsEscrowComponent> ent, float radExposure)
    {
        if (!_enabled)
            return RadiationHazardLevel.None;

        ent.Comp.RadiationExposure = radExposure;
        float stability = GetMutationStability(ent);
        var hazard = SolreignGeneticsEscrowMath.EvaluateRadiationHazard(radExposure, _radThreshold, stability);

        if (hazard >= RadiationHazardLevel.Severe && ent.Comp.State != SequencingState.Unsequenced)
        {
            ent.Comp.State = SequencingState.Corrupted;
        }

        return hazard;
    }

    /// <summary>
    ///     Calculates effective mutation stability ratio for the given component.
    /// </summary>
    public float GetMutationStability(Entity<SolreignGeneticsEscrowComponent> ent)
    {
        return SolreignGeneticsEscrowMath.CalculateMutationStability(
            ent.Comp.MutationCount,
            ent.Comp.InstabilityPerMutation,
            ent.Comp.RadiationExposure,
            _radThreshold);
    }

    /// <summary>
    ///     Evaluates the pending escrow deposit for payout or refund, updates account balances, and clears pending deposit.
    /// </summary>
    public EscrowResult EvaluateEscrow(Entity<SolreignGeneticsEscrowComponent> ent)
    {
        if (!_enabled)
        {
            int pending = ent.Comp.PendingDeposit;
            ent.Comp.PendingDeposit = 0;
            return new EscrowResult(0, pending, pending, EscrowStatus.Rejected, "Genetics escrow system disabled");
        }

        float stability = GetMutationStability(ent);
        var result = SolreignGeneticsEscrowMath.EvaluateEscrowPayout(
            ent.Comp.PendingDeposit,
            stability,
            ent.Comp.RadiationExposure,
            _radThreshold,
            ent.Comp.State,
            _minStabilityThreshold);

        ent.Comp.TotalAccountCredits += result.TotalReturn;
        ent.Comp.PendingDeposit = 0;
        return result;
    }

    /// <summary>
    ///     Refunds the pending escrow deposit in full without completing evaluation.
    /// </summary>
    public int RefundEscrow(Entity<SolreignGeneticsEscrowComponent> ent)
    {
        int refund = ent.Comp.PendingDeposit;
        ent.Comp.PendingDeposit = 0;
        ent.Comp.TotalAccountCredits += refund;
        return refund;
    }
}
