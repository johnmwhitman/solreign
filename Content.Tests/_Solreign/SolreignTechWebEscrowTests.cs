using System.Collections.Generic;
using Content.Server._Solreign.Research;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(SolreignTechWebEscrowSystem))]
public sealed class SolreignTechWebEscrowTests
{
    [Test]
    public void TechWebEscrow_DefaultComponentState_InitializedCorrectly()
    {
        var comp = new SolreignTechWebEscrowComponent();

        Assert.Multiple(() =>
        {
            Assert.That(comp.EscrowPointBalance, Is.EqualTo(0));
            Assert.That(comp.BasePointGenerationRate, Is.EqualTo(10));
            Assert.That(comp.GenerationEfficiencyMultiplier, Is.EqualTo(1.0f));
            Assert.That(comp.TotalPointsGenerated, Is.EqualTo(0));
            Assert.That(comp.UnlockedNodes, Is.Empty);
            Assert.That(comp.ExperimentalNodesUnlocked, Is.Empty);
            Assert.That(comp.ClaimedMilestoneTiers, Is.Empty);
            Assert.That(comp.DepartmentMilestoneMultiplier, Is.EqualTo(1.0f));
            Assert.That(comp.TotalMilestonePointsAwarded, Is.EqualTo(0));
        });
    }

    [Test]
    public void GenerateResearchPoints_ValidRateAndDuration_IncreasesEscrowBalance()
    {
        var sys = new SolreignTechWebEscrowSystem();
        var comp = new SolreignTechWebEscrowComponent
        {
            BasePointGenerationRate = 10,
            GenerationEfficiencyMultiplier = 1.5f
        };

        bool success = sys.GenerateResearchPoints(comp, 10f, out int generated);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(generated, Is.EqualTo(150)); // 10 * 1.5 * 10 = 150
            Assert.That(comp.EscrowPointBalance, Is.EqualTo(150));
            Assert.That(comp.TotalPointsGenerated, Is.EqualTo(150));
        });
    }

    [Test]
    public void GenerateResearchPoints_ZeroOrNegativeDuration_ReturnsFalse()
    {
        var sys = new SolreignTechWebEscrowSystem();
        var comp = new SolreignTechWebEscrowComponent();

        Assert.Multiple(() =>
        {
            Assert.That(sys.GenerateResearchPoints(comp, 0f, out int gen0), Is.False);
            Assert.That(gen0, Is.EqualTo(0));
            Assert.That(sys.GenerateResearchPoints(comp, -5f, out int genNeg), Is.False);
            Assert.That(genNeg, Is.EqualTo(0));
            Assert.That(comp.EscrowPointBalance, Is.EqualTo(0));
        });
    }

    [Test]
    public void UnlockNode_SufficientPoints_UnlocksNodeAndDeductsPoints()
    {
        var sys = new SolreignTechWebEscrowSystem();
        var comp = new SolreignTechWebEscrowComponent { EscrowPointBalance = 1000 };

        bool success = sys.UnlockNode(comp, "tech_laser_v1", 300, isExperimental: false);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(comp.EscrowPointBalance, Is.EqualTo(700));
            Assert.That(comp.UnlockedNodes, Does.Contain("tech_laser_v1"));
            Assert.That(comp.ExperimentalNodesUnlocked, Does.Not.Contain("tech_laser_v1"));
        });
    }

    [Test]
    public void UnlockNode_ExperimentalNode_TracksInExperimentalSet()
    {
        var sys = new SolreignTechWebEscrowSystem();
        var comp = new SolreignTechWebEscrowComponent { EscrowPointBalance = 1000 };

        bool success = sys.UnlockNode(comp, "tech_exp_singularity_v2", 500, isExperimental: true);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(comp.EscrowPointBalance, Is.EqualTo(500));
            Assert.That(comp.UnlockedNodes, Does.Contain("tech_exp_singularity_v2"));
            Assert.That(comp.ExperimentalNodesUnlocked, Does.Contain("tech_exp_singularity_v2"));
        });
    }

    [Test]
    public void UnlockNode_PrerequisitesMet_Succeeds()
    {
        var sys = new SolreignTechWebEscrowSystem();
        var comp = new SolreignTechWebEscrowComponent { EscrowPointBalance = 2000 };

        // Unlock prereq first
        sys.UnlockNode(comp, "tech_base_power", 200);

        // Unlock dependent node with prereq specified
        var prereqs = new List<string> { "tech_base_power" };
        bool success = sys.UnlockNode(comp, "tech_adv_power", 500, isExperimental: false, prerequisiteNodes: prereqs);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(comp.UnlockedNodes, Does.Contain("tech_adv_power"));
            Assert.That(comp.EscrowPointBalance, Is.EqualTo(1300));
        });
    }

    [Test]
    public void UnlockNode_PrerequisitesMissing_Fails()
    {
        var sys = new SolreignTechWebEscrowSystem();
        var comp = new SolreignTechWebEscrowComponent { EscrowPointBalance = 2000 };

        var prereqs = new List<string> { "tech_missing_base" };
        bool success = sys.UnlockNode(comp, "tech_adv_power", 500, isExperimental: false, prerequisiteNodes: prereqs);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.False);
            Assert.That(comp.UnlockedNodes, Does.Not.Contain("tech_adv_power"));
            Assert.That(comp.EscrowPointBalance, Is.EqualTo(2000));
        });
    }

    [Test]
    public void UnlockNode_InsufficientPointsOrAlreadyUnlocked_Fails()
    {
        var sys = new SolreignTechWebEscrowSystem();
        var comp = new SolreignTechWebEscrowComponent { EscrowPointBalance = 200 };

        Assert.Multiple(() =>
        {
            // Insufficient points
            Assert.That(sys.UnlockNode(comp, "tech_expensive", 500), Is.False);
            Assert.That(comp.EscrowPointBalance, Is.EqualTo(200));
        });

        // Unlock once
        sys.UnlockNode(comp, "tech_cheap", 100);
        Assert.That(comp.EscrowPointBalance, Is.EqualTo(100));

        // Unlock duplicate
        Assert.That(sys.UnlockNode(comp, "tech_cheap", 100), Is.False);
        Assert.That(comp.EscrowPointBalance, Is.EqualTo(100));
    }

    [Test]
    public void ClaimDepartmentMilestone_ValidTierAndNodes_AwardsPoints()
    {
        var sys = new SolreignTechWebEscrowSystem();
        var comp = new SolreignTechWebEscrowComponent
        {
            EscrowPointBalance = 100,
            DepartmentMilestoneMultiplier = 1.2f
        };

        // Simulate 5 unlocked nodes
        for (int i = 1; i <= 5; i++)
        {
            comp.UnlockedNodes.Add($"tech_node_{i}");
        }

        bool success = sys.ClaimDepartmentMilestone(comp, milestoneTier: 1, requiredUnlockedCount: 5, baseTierReward: 1000, out int awarded);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(awarded, Is.EqualTo(1200)); // 1000 * 1.2 = 1200
            Assert.That(comp.EscrowPointBalance, Is.EqualTo(1300)); // 100 + 1200
            Assert.That(comp.TotalMilestonePointsAwarded, Is.EqualTo(1200));
            Assert.That(comp.ClaimedMilestoneTiers, Does.Contain(1));
        });
    }

    [Test]
    public void ClaimDepartmentMilestone_InsufficientNodesOrDuplicateClaim_Fails()
    {
        var sys = new SolreignTechWebEscrowSystem();
        var comp = new SolreignTechWebEscrowComponent();

        comp.UnlockedNodes.Add("tech_node_1");

        // Insufficient nodes (has 1, requires 5)
        Assert.Multiple(() =>
        {
            Assert.That(sys.ClaimDepartmentMilestone(comp, 1, 5, 1000, out int awarded), Is.False);
            Assert.That(awarded, Is.EqualTo(0));
        });

        // Add 4 more nodes to reach 5
        for (int i = 2; i <= 5; i++)
        {
            comp.UnlockedNodes.Add($"tech_node_{i}");
        }

        // Claim tier 1
        Assert.That(sys.ClaimDepartmentMilestone(comp, 1, 5, 1000, out _), Is.True);

        // Attempt duplicate claim on tier 1
        Assert.That(sys.ClaimDepartmentMilestone(comp, 1, 5, 1000, out _), Is.False);
    }

    [Test]
    public void CVarThreshold_DisabledPosture_BlocksOperations()
    {
        var sys = new SolreignTechWebEscrowSystem();
        var comp = new SolreignTechWebEscrowComponent { EscrowPointBalance = 1000 };

        // Disable via CVar posture switch
        sys.SetTechWebEnabled(false);

        Assert.Multiple(() =>
        {
            Assert.That(sys.IsTechWebEnabled, Is.False);
            Assert.That(sys.GenerateResearchPoints(comp, 10f, out _), Is.False);
            Assert.That(sys.UnlockNode(comp, "tech_node", 100), Is.False);
            Assert.That(sys.ClaimDepartmentMilestone(comp, 1, 0, 500, out _), Is.False);
        });

        // Re-enable CVar posture switch
        sys.SetTechWebEnabled(true);
        Assert.Multiple(() =>
        {
            Assert.That(sys.IsTechWebEnabled, Is.True);
            Assert.That(sys.UnlockNode(comp, "tech_node", 100), Is.True);
            Assert.That(comp.EscrowPointBalance, Is.EqualTo(900));
        });
    }

    [Test]
    public void Evaluator_CalculatePointGeneration_EdgeCasesAndZeroExceptions()
    {
        Assert.Multiple(() =>
        {
            // Zero base rate
            Assert.That(SolreignTechWebEscrowEvaluator.CalculatePointGeneration(0, 1.5f, 10f), Is.EqualTo(0));

            // Negative efficiency multiplier
            Assert.That(SolreignTechWebEscrowEvaluator.CalculatePointGeneration(10, -0.5f, 10f), Is.EqualTo(0));

            // Null node ID unlock check
            Assert.That(SolreignTechWebEscrowEvaluator.CanUnlockNode(true, null!, 100, 500, new HashSet<string>()), Is.False);

            // Empty whitespace node ID unlock check
            Assert.That(SolreignTechWebEscrowEvaluator.CanUnlockNode(true, "   ", 100, 500, new HashSet<string>()), Is.False);

            // Zero or negative tier reward
            Assert.That(SolreignTechWebEscrowEvaluator.CalculateMilestoneReward(1, 0, 1.0f), Is.EqualTo(0));
            Assert.That(SolreignTechWebEscrowEvaluator.CalculateMilestoneReward(0, 500, 1.0f), Is.EqualTo(0));
        });
    }

    [Test]
    public void ResetEscrowState_ResetsAllFields()
    {
        var sys = new SolreignTechWebEscrowSystem();
        var comp = new SolreignTechWebEscrowComponent
        {
            EscrowPointBalance = 5000,
            TotalPointsGenerated = 10000,
            TotalMilestonePointsAwarded = 2500
        };

        comp.UnlockedNodes.Add("node1");
        comp.ExperimentalNodesUnlocked.Add("node1");
        comp.ClaimedMilestoneTiers.Add(1);

        sys.ResetEscrowState(comp);

        Assert.Multiple(() =>
        {
            Assert.That(comp.EscrowPointBalance, Is.EqualTo(0));
            Assert.That(comp.TotalPointsGenerated, Is.EqualTo(0));
            Assert.That(comp.UnlockedNodes, Is.Empty);
            Assert.That(comp.ExperimentalNodesUnlocked, Is.Empty);
            Assert.That(comp.ClaimedMilestoneTiers, Is.Empty);
            Assert.That(comp.TotalMilestonePointsAwarded, Is.EqualTo(0));
        });
    }
}
