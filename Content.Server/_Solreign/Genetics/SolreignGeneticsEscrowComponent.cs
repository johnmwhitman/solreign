using Robust.Shared.GameObjects;

namespace Content.Server._Solreign.Genetics;

/// <summary>
///     Component attached to DNA sample escrow terminals or genetics analyzers
///     tracking DNA sequencing progress, mutation counts, radiation hazards, and credit escrow balances.
/// </summary>
[RegisterComponent]
public sealed partial class SolreignGeneticsEscrowComponent : Component
{
    /// <summary>
    ///     Base sequence length required for complete DNA sequencing.
    /// </summary>
    [DataField]
    public int BaseSequenceLength = 1000;

    /// <summary>
    ///     Number of sequencing reads completed.
    /// </summary>
    [DataField]
    public int ReadsCompleted = 0;

    /// <summary>
    ///     Raw sequencing quality score [0.0, 1.0].
    /// </summary>
    [DataField]
    public float SequencingQuality = 1.0f;

    /// <summary>
    ///     Number of genetic mutations present in the sample.
    /// </summary>
    [DataField]
    public int MutationCount = 0;

    /// <summary>
    ///     Instability penalty per mutation.
    /// </summary>
    [DataField]
    public float InstabilityPerMutation = SolreignGeneticsEscrowMath.DefaultInstabilityPerMutation;

    /// <summary>
    ///     Current radiation exposure level (rads).
    /// </summary>
    [DataField]
    public float RadiationExposure = 0.0f;

    /// <summary>
    ///     Active credit deposit held in escrow for the current sample.
    /// </summary>
    [DataField]
    public int PendingDeposit = 0;

    /// <summary>
    ///     Accumulated genetic credits in the terminal account balance.
    /// </summary>
    [DataField]
    public int TotalAccountCredits = 0;

    /// <summary>
    ///     Current sequencing state of the sample.
    /// </summary>
    [DataField]
    public SequencingState State = SequencingState.Unsequenced;

    /// <summary>
    ///     Calculated sequencing progress [0.0, 1.0].
    /// </summary>
    public float SequenceProgress => SolreignGeneticsEscrowMath.CalculateSequencingProgress(
        BaseSequenceLength, ReadsCompleted, SequencingQuality, RadiationExposure, 50.0f);

    /// <summary>
    ///     Calculated mutation stability ratio [0.0, 1.0].
    /// </summary>
    public float MutationStability => SolreignGeneticsEscrowMath.CalculateMutationStability(
        MutationCount, InstabilityPerMutation, RadiationExposure, 50.0f);
}
