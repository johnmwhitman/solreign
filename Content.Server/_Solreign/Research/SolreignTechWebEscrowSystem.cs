using System;
using System.Collections.Generic;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Server._Solreign.Research;

/// <summary>
///     Event raised when a technology node is unlocked via SR-W-063.
/// </summary>
public sealed class SolreignTechWebNodeUnlockedEvent : EntityEventArgs
{
    public SolreignTechWebEscrowComponent Component { get; }
    public string NodeId { get; }
    public bool IsExperimental { get; }
    public int Cost { get; }

    public SolreignTechWebNodeUnlockedEvent(SolreignTechWebEscrowComponent component, string nodeId, bool isExperimental, int cost)
    {
        Component = component;
        NodeId = nodeId;
        IsExperimental = isExperimental;
        Cost = cost;
    }
}

/// <summary>
///     Event raised when research points are generated into escrow under SR-W-063.
/// </summary>
public sealed class SolreignTechWebPointGeneratedEvent : EntityEventArgs
{
    public SolreignTechWebEscrowComponent Component { get; }
    public int PointsGenerated { get; }
    public int TotalEscrowBalance { get; }

    public SolreignTechWebPointGeneratedEvent(SolreignTechWebEscrowComponent component, int pointsGenerated, int totalEscrowBalance)
    {
        Component = component;
        PointsGenerated = pointsGenerated;
        TotalEscrowBalance = totalEscrowBalance;
    }
}

/// <summary>
///     Event raised when a department research milestone is claimed under SR-W-063.
/// </summary>
public sealed class SolreignTechWebMilestoneClaimedEvent : EntityEventArgs
{
    public SolreignTechWebEscrowComponent Component { get; }
    public int MilestoneTier { get; }
    public int RewardPoints { get; }

    public SolreignTechWebMilestoneClaimedEvent(SolreignTechWebEscrowComponent component, int milestoneTier, int rewardPoints)
    {
        Component = component;
        MilestoneTier = milestoneTier;
        RewardPoints = rewardPoints;
    }
}

/// <summary>
///     Pure, unit-testable evaluation and calculation logic for <see cref="SolreignTechWebEscrowSystem"/>.
/// </summary>
public static class SolreignTechWebEscrowEvaluator
{
    /// <summary>
    ///     Calculates research points generated over a duration given base rate and efficiency multiplier.
    /// </summary>
    public static int CalculatePointGeneration(int baseRate, float efficiencyMultiplier, float durationSeconds)
    {
        if (baseRate <= 0 || durationSeconds <= 0f)
            return 0;

        float effectiveMultiplier = Math.Max(0.0f, efficiencyMultiplier);
        double points = baseRate * effectiveMultiplier * durationSeconds;
        return Math.Max(0, (int)Math.Floor(points));
    }

    /// <summary>
    ///     Evaluates whether a tech web node can be unlocked based on prerequisites, cost, and CVar posture.
    /// </summary>
    public static bool CanUnlockNode(
        bool cvarEnabled,
        string nodeId,
        int costPoints,
        int availablePoints,
        HashSet<string> unlockedNodes,
        List<string>? prerequisiteNodes = null)
    {
        if (!cvarEnabled || string.IsNullOrWhiteSpace(nodeId))
            return false;

        if (unlockedNodes.Contains(nodeId))
            return false;

        if (availablePoints < costPoints || costPoints < 0)
            return false;

        if (prerequisiteNodes != null)
        {
            foreach (var prereq in prerequisiteNodes)
            {
                if (!unlockedNodes.Contains(prereq))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Calculates department milestone point reward for a tier based on tier base value and department multiplier.
    /// </summary>
    public static int CalculateMilestoneReward(int milestoneTier, int baseTierReward, float departmentMultiplier)
    {
        if (milestoneTier <= 0 || baseTierReward <= 0)
            return 0;

        float mult = Math.Max(0.0f, departmentMultiplier);
        return Math.Max(0, (int)Math.Floor(baseTierReward * mult));
    }

    /// <summary>
    ///     Evaluates whether a department milestone tier can be claimed.
    /// </summary>
    public static bool CanClaimMilestone(
        bool cvarEnabled,
        int milestoneTier,
        int requiredUnlockedCount,
        int currentUnlockedCount,
        HashSet<int> claimedTiers)
    {
        if (!cvarEnabled || milestoneTier <= 0)
            return false;

        if (claimedTiers.Contains(milestoneTier))
            return false;

        return currentUnlockedCount >= requiredUnlockedCount;
    }
}

/// <summary>
///     SR-W-063: SS14 Solreign Research Tech Web & Department Point Escrow System.
///     Tracks experimental node unlocking, research point generation rates, department milestone rewards,
///     and CVar thresholds (<c>solreign.tech_web_enabled</c>).
/// </summary>
public sealed partial class SolreignTechWebEscrowSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;

    private bool _cvarEnabled = true;

    /// <summary>
    ///     Whether research tech web escrow is enabled via CVar (<c>solreign.tech_web_enabled</c>).
    /// </summary>
    public bool IsTechWebEnabled => _cfg != null ? _cfg.GetCVar(CCVars.SolreignTechWebEnabled) : _cvarEnabled;

    public override void Initialize()
    {
        base.Initialize();

        if (_cfg != null)
        {
            _cfg.OnValueChanged(CCVars.SolreignTechWebEnabled, SetTechWebEnabled, true);
        }
    }

    /// <summary>
    ///     Updates the CVar override state (used for testing or explicit configuration changes).
    /// </summary>
    public void SetTechWebEnabled(bool enabled)
    {
        _cvarEnabled = enabled;
    }

    /// <summary>
    ///     Generates research points into component escrow balance over duration.
    /// </summary>
    public bool GenerateResearchPoints(SolreignTechWebEscrowComponent comp, float durationSeconds, out int generatedPoints)
    {
        generatedPoints = 0;

        if (!IsTechWebEnabled)
            return false;

        generatedPoints = SolreignTechWebEscrowEvaluator.CalculatePointGeneration(
            comp.BasePointGenerationRate,
            comp.GenerationEfficiencyMultiplier,
            durationSeconds);

        if (generatedPoints <= 0)
            return false;

        comp.EscrowPointBalance += generatedPoints;
        comp.TotalPointsGenerated += generatedPoints;

        if (EntityManager != null)
        {
            RaiseLocalEvent(new SolreignTechWebPointGeneratedEvent(comp, generatedPoints, comp.EscrowPointBalance));
        }

        return true;
    }

    /// <summary>
    ///     Attempts to unlock a standard or experimental technology node.
    /// </summary>
    public bool UnlockNode(
        SolreignTechWebEscrowComponent comp,
        string nodeId,
        int costPoints,
        bool isExperimental = false,
        List<string>? prerequisiteNodes = null)
    {
        if (!IsTechWebEnabled)
            return false;

        if (!SolreignTechWebEscrowEvaluator.CanUnlockNode(
                IsTechWebEnabled,
                nodeId,
                costPoints,
                comp.EscrowPointBalance,
                comp.UnlockedNodes,
                prerequisiteNodes))
        {
            return false;
        }

        comp.EscrowPointBalance -= costPoints;
        comp.UnlockedNodes.Add(nodeId);

        if (isExperimental)
        {
            comp.ExperimentalNodesUnlocked.Add(nodeId);
        }

        if (EntityManager != null)
        {
            RaiseLocalEvent(new SolreignTechWebNodeUnlockedEvent(comp, nodeId, isExperimental, costPoints));
        }

        return true;
    }

    /// <summary>
    ///     Claims a department milestone tier if criteria are met and awards reward points into escrow.
    /// </summary>
    public bool ClaimDepartmentMilestone(
        SolreignTechWebEscrowComponent comp,
        int milestoneTier,
        int requiredUnlockedCount,
        int baseTierReward,
        out int awardedPoints)
    {
        awardedPoints = 0;

        if (!IsTechWebEnabled)
            return false;

        int totalUnlocked = comp.UnlockedNodes.Count;
        if (!SolreignTechWebEscrowEvaluator.CanClaimMilestone(
                IsTechWebEnabled,
                milestoneTier,
                requiredUnlockedCount,
                totalUnlocked,
                comp.ClaimedMilestoneTiers))
        {
            return false;
        }

        awardedPoints = SolreignTechWebEscrowEvaluator.CalculateMilestoneReward(
            milestoneTier,
            baseTierReward,
            comp.DepartmentMilestoneMultiplier);

        comp.ClaimedMilestoneTiers.Add(milestoneTier);
        comp.EscrowPointBalance += awardedPoints;
        comp.TotalMilestonePointsAwarded += awardedPoints;

        if (EntityManager != null)
        {
            RaiseLocalEvent(new SolreignTechWebMilestoneClaimedEvent(comp, milestoneTier, awardedPoints));
        }

        return true;
    }

    /// <summary>
    ///     Resets escrow balance, unlocked nodes, and milestone tiers.
    /// </summary>
    public void ResetEscrowState(SolreignTechWebEscrowComponent comp)
    {
        comp.EscrowPointBalance = 0;
        comp.TotalPointsGenerated = 0;
        comp.UnlockedNodes.Clear();
        comp.ExperimentalNodesUnlocked.Clear();
        comp.ClaimedMilestoneTiers.Clear();
        comp.TotalMilestonePointsAwarded = 0;
    }
}
