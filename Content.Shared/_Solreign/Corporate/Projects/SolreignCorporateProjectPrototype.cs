using System.Collections.Generic;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared._Solreign.Corporate.Projects;

/// <summary>
///     Single milestone definition within a corporate project prototype (SR-W-030).
/// </summary>
[DataDefinition]
public sealed partial class SolreignCorporateProjectMilestone
{
    [DataField("milestoneId", required: true)]
    public string MilestoneId = string.Empty;

    /// <summary>
    ///     Target completion fraction required to trigger this milestone (e.g. 0.25f, 0.50f, 0.75f, 1.00f).
    /// </summary>
    [DataField("thresholdFraction")]
    public float ThresholdFraction = 0.25f;

    /// <summary>
    ///     Localization key or display name for the milestone unlock.
    /// </summary>
    [DataField("name")]
    public string Name = string.Empty;

    /// <summary>
    ///     Corporate Standing points awarded to contributors when this milestone is unlocked.
    /// </summary>
    [DataField("standingReward")]
    public int StandingReward = 5;

    /// <summary>
    ///     Identifier for systemic feature or prototype unlocked by this milestone.
    /// </summary>
    [DataField("unlockId")]
    public string UnlockId = string.Empty;
}

/// <summary>
///     Prototype definition for a player-run corporate project (SR-W-030).
/// </summary>
[Prototype]
public sealed partial class SolreignCorporateProjectPrototype : IPrototype
{
    [ViewVariables]
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("name")]
    public string Name { get; private set; } = string.Empty;

    [DataField("description")]
    public string Description { get; private set; } = string.Empty;

    [DataField("sponsor")]
    public string Sponsor { get; private set; } = string.Empty;

    /// <summary>
    ///     Total contribution points required for 100% project completion.
    /// </summary>
    [DataField("targetContribution")]
    public int TargetContribution { get; private set; } = 1000;

    /// <summary>
    ///     Bounded contribution limit: max points accepted in a single contribution action.
    /// </summary>
    [DataField("maxContributionPerAction")]
    public int MaxContributionPerAction { get; private set; } = 50;

    /// <summary>
    ///     Anti-monopoly limit: maximum fraction of total project target any single account can contribute (e.g. 0.35 = 35%).
    /// </summary>
    [DataField("maxAccountShareFraction")]
    public float MaxAccountShareFraction { get; private set; } = 0.35f;

    /// <summary>
    ///     Milestone thresholds and unlock definitions for this project.
    /// </summary>
    [DataField("milestones")]
    public List<SolreignCorporateProjectMilestone> Milestones { get; private set; } = new();

    /// <summary>
    ///     Calculates the maximum contribution points any single account can provide to this project.
    /// </summary>
    public int GetMaxAccountContribution()
    {
        return (int) System.Math.Floor(TargetContribution * MaxAccountShareFraction);
    }
}
