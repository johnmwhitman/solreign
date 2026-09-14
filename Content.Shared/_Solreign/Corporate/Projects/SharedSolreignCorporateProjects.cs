using System;
using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Solreign.Corporate.Projects;

/// <summary>UI key for the Corporate Projects bound user interface (SR-W-030).</summary>
[Serializable, NetSerializable]
public enum SolreignCorporateProjectUiKey : byte
{
    Key = 0,
}

/// <summary>
///     Rejection reasons for corporate project contribution attempts.
/// </summary>
[Serializable, NetSerializable]
public enum CorporateProjectContributionRejection : byte
{
    ProjectNotFound = 0,
    AlreadyCompleted = 1,
    InvalidAmount = 2,
    AccountCapReached = 3,
}

/// <summary>
///     Client-facing display record for one corporate project milestone.
/// </summary>
[Serializable, NetSerializable]
public sealed class SolreignCorporateProjectMilestoneState
{
    public string MilestoneId = string.Empty;
    public string Name = string.Empty;
    public float ThresholdFraction;
    public int StandingReward;
    public bool Unlocked;

    public SolreignCorporateProjectMilestoneState(
        string milestoneId,
        string name,
        float thresholdFraction,
        int standingReward,
        bool unlocked)
    {
        MilestoneId = milestoneId;
        Name = name;
        ThresholdFraction = thresholdFraction;
        StandingReward = standingReward;
        Unlocked = unlocked;
    }
}

/// <summary>
///     Client-facing display state for a single corporate project.
/// </summary>
[Serializable, NetSerializable]
public sealed class SolreignCorporateProjectState
{
    public string ProjectId = string.Empty;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public string Sponsor = string.Empty;
    public int TargetContribution;
    public int CurrentContribution;
    public int MaxContributionPerAction;
    public int AccountContribution;
    public int MaxAccountContribution;
    public bool Completed;
    public List<SolreignCorporateProjectMilestoneState> Milestones = new();

    public SolreignCorporateProjectState(
        string projectId,
        string name,
        string description,
        string sponsor,
        int targetContribution,
        int currentContribution,
        int maxContributionPerAction,
        int accountContribution,
        int maxAccountContribution,
        bool completed,
        List<SolreignCorporateProjectMilestoneState> milestones)
    {
        ProjectId = projectId;
        Name = name;
        Description = description;
        Sponsor = sponsor;
        TargetContribution = targetContribution;
        CurrentContribution = currentContribution;
        MaxContributionPerAction = maxContributionPerAction;
        AccountContribution = accountContribution;
        MaxAccountContribution = maxAccountContribution;
        Completed = completed;
        Milestones = milestones;
    }
}

/// <summary>
///     Full BUI snapshot pushed to the client corporate projects console.
/// </summary>
[Serializable, NetSerializable]
public sealed class SolreignCorporateProjectUiState : BoundUserInterfaceState
{
    public List<SolreignCorporateProjectState> Projects;
    public string? SelectedProjectId;
    public string? StatusMessage;
    public bool Offline;

    public SolreignCorporateProjectUiState(
        List<SolreignCorporateProjectState> projects,
        string? selectedProjectId = null,
        string? statusMessage = null,
        bool offline = false)
    {
        Projects = projects;
        SelectedProjectId = selectedProjectId;
        StatusMessage = statusMessage;
        Offline = offline;
    }
}

/// <summary>
///     Message sent from client BUI to contribute funding to a corporate project.
/// </summary>
[Serializable, NetSerializable]
public sealed class SolreignCorporateProjectContributeMessage : BoundUserInterfaceMessage
{
    public string ProjectId;
    public int Amount;

    public SolreignCorporateProjectContributeMessage(string projectId, int amount)
    {
        ProjectId = projectId;
        Amount = amount;
    }
}

/// <summary>
///     Message sent from client BUI when selecting a project tab/item.
/// </summary>
[Serializable, NetSerializable]
public sealed class SolreignCorporateProjectSelectMessage : BoundUserInterfaceMessage
{
    public string ProjectId;

    public SolreignCorporateProjectSelectMessage(string projectId)
    {
        ProjectId = projectId;
    }
}

/// <summary>
///     Component attached to Corporate Project Consoles in the world.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SolreignCorporateProjectConsoleComponent : Component
{
}
