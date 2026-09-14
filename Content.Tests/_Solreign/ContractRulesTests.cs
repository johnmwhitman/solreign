using System.Collections.Generic;
using Content.Server._Solreign.Contracts;
using Content.Shared._Solreign.Contracts;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(ContractRules))]
public sealed class ContractRulesTests
{
    // --- Completion scoring weights (spec §4.1: personal=2, department=1, chain-final=3) ---

    [Test]
    public void CompletionScore_Personal_IsTwo()
    {
        Assert.That(ContractRules.CompletionScore(SolreignContractScope.Personal, chainFinal: false),
            Is.EqualTo(2));
    }

    [Test]
    public void CompletionScore_DepartmentContribution_IsOne()
    {
        Assert.That(ContractRules.CompletionScore(SolreignContractScope.Department, chainFinal: false),
            Is.EqualTo(1));
    }

    [Test]
    public void CompletionScore_ChainFinal_IsThree_RegardlessOfScope()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ContractRules.CompletionScore(SolreignContractScope.Personal, chainFinal: true),
                Is.EqualTo(3));
            Assert.That(ContractRules.CompletionScore(SolreignContractScope.Department, chainFinal: true),
                Is.EqualTo(3));
        });
    }

    [Test]
    public void CompletionScore_SalvageRaid_IsPersonalGrade()
    {
        // Salvage raiders each did personal-sized work (objectives scale linearly with the roster).
        Assert.That(ContractRules.CompletionScore(SolreignContractScope.SalvageRaid, chainFinal: false),
            Is.EqualTo(2));
    }

    [Test]
    public void CompletionScore_IsAlwaysNonNegative()
    {
        // Anti-grief rule 9: contract stats are non-negative accumulators — never-demote holds.
        foreach (var scope in new[]
                 {
                     SolreignContractScope.Personal,
                     SolreignContractScope.Department,
                     SolreignContractScope.SalvageRaid,
                 })
        {
            Assert.That(ContractRules.CompletionScore(scope, false), Is.GreaterThanOrEqualTo(0));
            Assert.That(ContractRules.CompletionScore(scope, true), Is.GreaterThanOrEqualTo(0));
        }
    }

    // --- Chain-final detection ---

    [Test]
    public void IsChainFinal_TargetWithNoNext_IsFinal()
    {
        Assert.That(ContractRules.IsChainFinal(hasNextContract: false, isChainTarget: true), Is.True);
    }

    [Test]
    public void IsChainFinal_MidChainLink_IsNotFinal()
    {
        Assert.That(ContractRules.IsChainFinal(hasNextContract: true, isChainTarget: true), Is.False);
    }

    [Test]
    public void IsChainFinal_StandaloneContract_IsNotFinal()
    {
        Assert.That(ContractRules.IsChainFinal(hasNextContract: false, isChainTarget: false), Is.False);
    }

    [Test]
    public void IsChainFinal_ChainStarter_IsNotFinal()
    {
        Assert.That(ContractRules.IsChainFinal(hasNextContract: true, isChainTarget: false), Is.False);
    }

    // --- Claim cap (anti-grief rule 7: max 3 claimed personal contracts per player) ---

    [Test]
    public void CanClaimAnother_BelowCap_Allows()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ContractRules.CanClaimAnother(0), Is.True);
            Assert.That(ContractRules.CanClaimAnother(2), Is.True);
        });
    }

    [Test]
    public void CanClaimAnother_AtCap_Denies()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ContractRules.CanClaimAnother(ContractRules.MaxClaimedPerPlayer), Is.False);
            Assert.That(ContractRules.CanClaimAnother(ContractRules.MaxClaimedPerPlayer + 1), Is.False);
        });
    }

    [Test]
    public void MaxClaimedPerPlayer_IsThree_PerAntiGriefRuleSeven()
    {
        Assert.That(ContractRules.MaxClaimedPerPlayer, Is.EqualTo(3));
    }

    // --- Rank-gated contract tiers (M3, spec §3.1/§3.4): minRankIndex enforcement on claim/join ---

    [Test]
    public void MeetsRankGate_ZeroGate_AdmitsEveryRank()
    {
        // minRankIndex 0 (the default) is "everyone" — even a fresh Probationary Asset (index 0).
        Assert.Multiple(() =>
        {
            Assert.That(ContractRules.MeetsRankGate(0, 0), Is.True);
            Assert.That(ContractRules.MeetsRankGate(6, 0), Is.True);
        });
    }

    [Test]
    public void MeetsRankGate_ExactlyAtThreshold_Admits()
    {
        // Associate+ (minRankIndex 1): an Associate (index 1) is admitted, not just Senior Associate+.
        Assert.That(ContractRules.MeetsRankGate(1, 1), Is.True);
    }

    [Test]
    public void MeetsRankGate_BelowThreshold_Denies()
    {
        // Manager+ (minRankIndex 3, the Executive Lunch gate): a Senior Associate (index 2) is denied.
        Assert.That(ContractRules.MeetsRankGate(2, 3), Is.False);
    }

    [Test]
    public void MeetsRankGate_AboveThreshold_Admits()
    {
        // Manager+ gate: a Director (index 4) and above are all fine.
        Assert.Multiple(() =>
        {
            Assert.That(ContractRules.MeetsRankGate(4, 3), Is.True);
            Assert.That(ContractRules.MeetsRankGate(6, 3), Is.True);
        });
    }

    [Test]
    public void MeetsRankGate_ProbationaryAsset_DeniedAnyRealGate()
    {
        // A brand-new Probationary Asset (index 0) never qualifies for a gated tier.
        for (var gate = 1; gate <= 6; gate++)
            Assert.That(ContractRules.MeetsRankGate(0, gate), Is.False, $"gate {gate} wrongly admitted index 0");
    }

    // --- Salvage-raid group scaling (accept-time: locked to the roster registered at launch) ---

    [Test]
    public void ClampParticipants_ClampsIntoOneToMax()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ContractRules.ClampParticipants(0, 6), Is.EqualTo(1), "empty roster still scales as solo");
            Assert.That(ContractRules.ClampParticipants(3, 6), Is.EqualTo(3));
            Assert.That(ContractRules.ClampParticipants(9, 6), Is.EqualTo(6), "roster cap holds");
            Assert.That(ContractRules.ClampParticipants(2, 0), Is.EqualTo(1), "degenerate max never zeroes the math");
        });
    }

    [Test]
    public void SalvageScaledAmount_ScalesLinearlyWithCrew()
    {
        // Per-head workload stays flat: base 5 -> 5 solo, 15 for a crew of three.
        Assert.Multiple(() =>
        {
            Assert.That(ContractRules.SalvageScaledAmount(5, 1, 6), Is.EqualTo(5));
            Assert.That(ContractRules.SalvageScaledAmount(5, 3, 6), Is.EqualTo(15));
            Assert.That(ContractRules.SalvageScaledAmount(5, 6, 6), Is.EqualTo(30));
        });
    }

    [Test]
    public void SalvageScaledAmount_NeverBelowOnePerHead()
    {
        Assert.That(ContractRules.SalvageScaledAmount(0, 2, 6), Is.EqualTo(2), "degenerate base amount floors at 1");
    }

    [Test]
    public void SalvageStanding_TeamAlwaysBeatsSolo_PerHead()
    {
        var solo = ContractRules.SalvageStanding(3, 1, 6);
        var duo = ContractRules.SalvageStanding(3, 2, 6);
        var fullCrew = ContractRules.SalvageStanding(3, 6, 6);

        Assert.Multiple(() =>
        {
            Assert.That(solo, Is.EqualTo(3), "solo gets exactly the base");
            Assert.That(duo, Is.EqualTo(4), "+1 per teammate beyond the first");
            Assert.That(fullCrew, Is.EqualTo(8));
            Assert.That(duo, Is.GreaterThan(solo), "teaming up is always the profitable move");
        });
    }

    [Test]
    public void SalvageStanding_IsNeverNegative()
    {
        // Anti-grief rule 9: a hostile prototype (negative base) still can't write a negative reward.
        Assert.That(ContractRules.SalvageStanding(-5, 1, 6), Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void SalvageStanding_RespectsRosterCap()
    {
        // 99 claimed participants scale as the cap (6): base + 5.
        Assert.That(ContractRules.SalvageStanding(3, 99, 6), Is.EqualTo(8));
    }

    // --- Deposit progress ---

    [Test]
    public void ApplyDeposit_TicksProgressAndReportsConsumption()
    {
        var progress = new List<int> { 0 };
        var required = new List<int> { 5 };

        var consumed = ContractRules.ApplyDeposit(progress, required, 0, 2);

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.EqualTo(2));
            Assert.That(progress[0], Is.EqualTo(2));
        });
    }

    [Test]
    public void ApplyDeposit_NeverOverconsumesAStack()
    {
        // Entry needs 5, has 3; a stack of 10 must only lose the 2 still needed.
        var progress = new List<int> { 3 };
        var required = new List<int> { 5 };

        var consumed = ContractRules.ApplyDeposit(progress, required, 0, 10);

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.EqualTo(2));
            Assert.That(progress[0], Is.EqualTo(5), "entry is exactly full, never over");
        });
    }

    [Test]
    public void ApplyDeposit_FullEntry_ConsumesNothing()
    {
        var progress = new List<int> { 5 };
        var required = new List<int> { 5 };

        Assert.That(ContractRules.ApplyDeposit(progress, required, 0, 3), Is.EqualTo(0));
        Assert.That(progress[0], Is.EqualTo(5));
    }

    [Test]
    public void ApplyDeposit_InvalidIndexOrNonPositiveAmount_ConsumesNothing()
    {
        var progress = new List<int> { 0 };
        var required = new List<int> { 5 };

        Assert.Multiple(() =>
        {
            Assert.That(ContractRules.ApplyDeposit(progress, required, -1, 1), Is.EqualTo(0));
            Assert.That(ContractRules.ApplyDeposit(progress, required, 1, 1), Is.EqualTo(0));
            Assert.That(ContractRules.ApplyDeposit(progress, required, 0, 0), Is.EqualTo(0));
            Assert.That(progress[0], Is.EqualTo(0));
        });
    }

    [Test]
    public void IsComplete_AllEntriesMet_IsTrue()
    {
        Assert.That(ContractRules.IsComplete(new[] { 5, 2 }, new[] { 5, 2 }), Is.True);
    }

    [Test]
    public void IsComplete_AnyEntryShort_IsFalse()
    {
        Assert.That(ContractRules.IsComplete(new[] { 5, 1 }, new[] { 5, 2 }), Is.False);
    }

    [Test]
    public void IsComplete_MismatchedShapes_IsFalse()
    {
        Assert.That(ContractRules.IsComplete(new[] { 5 }, new[] { 5, 2 }), Is.False);
    }

    // --- M1 exit test, pure-logic half (spec §8 M1): claim "Beverage Compliance Audit", deposit
    // --- 5 colas one at a time, contract completes and is worth +3 Standing / score 2.

    [Test]
    public void ExitTest_ColaAudit_FiveSingleDeposits_Complete()
    {
        // 5x cola, one entry — the SolContractColaAudit shape.
        var progress = new List<int> { 0 };
        var required = new List<int> { 5 };

        for (var i = 0; i < 5; i++)
        {
            Assert.That(ContractRules.IsComplete(progress, required), Is.False, $"not complete after {i} deposits");
            Assert.That(ContractRules.ApplyDeposit(progress, required, 0, 1), Is.EqualTo(1));
        }

        Assert.Multiple(() =>
        {
            Assert.That(ContractRules.IsComplete(progress, required), Is.True, "the chime moment");
            Assert.That(ContractRules.CompletionScore(SolreignContractScope.Personal, false), Is.EqualTo(2),
                "one personal completion is worth contract_score 2 in the ledger");
        });
    }

    // --- v14 low-pop extension (quest-board spec §2): NeedsGuaranteedEasyContract ---

    [Test]
    public void NeedsGuaranteedEasyContract_AtOrUnderThreshold_NoEasyTierOpen_IsTrue()
    {
        Assert.That(ContractRules.NeedsGuaranteedEasyContract(playerCount: 2, lowPopThreshold: 4, poolHasEasyTierOpen: false, personalCount: 0, maxPersonal: 6),
            Is.True);
    }

    [Test]
    public void NeedsGuaranteedEasyContract_ExactlyAtThreshold_NoEasyTierOpen_IsTrue()
    {
        // Boundary: "at or below" the threshold — the threshold count itself must still guarantee.
        Assert.That(ContractRules.NeedsGuaranteedEasyContract(playerCount: 4, lowPopThreshold: 4, poolHasEasyTierOpen: false, personalCount: 6, maxPersonal: 6),
            Is.True);
    }

    [Test]
    public void NeedsGuaranteedEasyContract_AboveThreshold_IsFalse_RegardlessOfPool()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ContractRules.NeedsGuaranteedEasyContract(playerCount: 5, lowPopThreshold: 4, poolHasEasyTierOpen: false, personalCount: 0, maxPersonal: 6),
                Is.False, "above threshold, an empty easy-tier pool still does not trigger the guarantee");
            Assert.That(ContractRules.NeedsGuaranteedEasyContract(playerCount: 20, lowPopThreshold: 4, poolHasEasyTierOpen: false, personalCount: 0, maxPersonal: 6),
                Is.False);
        });
    }

    [Test]
    public void NeedsGuaranteedEasyContract_AtOrUnderThreshold_EasyTierAlreadyOpen_IsFalse()
    {
        // Low pop, but the pool already satisfies the guarantee — nothing to force-issue.
        Assert.That(ContractRules.NeedsGuaranteedEasyContract(playerCount: 1, lowPopThreshold: 4, poolHasEasyTierOpen: true, personalCount: 1, maxPersonal: 6),
            Is.False);
    }

    [Test]
    public void NeedsGuaranteedEasyContract_ZeroPlayers_NoEasyTierOpen_IsTrue()
    {
        // Degenerate case (empty server, e.g. round-restart transition) must not throw or misbehave.
        Assert.That(ContractRules.NeedsGuaranteedEasyContract(playerCount: 0, lowPopThreshold: 4, poolHasEasyTierOpen: false, personalCount: 0, maxPersonal: 0),
            Is.True);
    }

    [Test]
    public void NeedsGuaranteedEasyContract_ActiveAtMaxPlusOne_NoOpenEasy_IsTrue()
    {
        // Claiming the sole open easy leaves active at MaxPersonal+1. The low-pop guarantee must
        // re-arm once more, using the bounded MaxPersonal+2 hard ceiling.
        Assert.That(ContractRules.NeedsGuaranteedEasyContract(
                playerCount: 1, lowPopThreshold: 4, poolHasEasyTierOpen: false, personalCount: 7, maxPersonal: 6),
            Is.True);
    }

    [Test]
    public void NeedsGuaranteedEasyContract_PersonalAtMax_NoOpenEasy_IsTrue()
    {
        // At MaxPersonal with no open easy: the bounded +1 exception still fires once.
        Assert.That(ContractRules.NeedsGuaranteedEasyContract(
                playerCount: 1, lowPopThreshold: 4, poolHasEasyTierOpen: false, personalCount: 6, maxPersonal: 6),
            Is.True);
    }

    [Test]
    public void NeedsGuaranteedEasyContract_ClaimedEasyAtMaxPlusOne_RearmsGuarantee()
    {
        // Claimed easies remain active, but the single replacement open easy is the deliberate
        // MaxPersonal+2 exception that keeps the board usable for another low-pop player.
        Assert.That(ContractRules.NeedsGuaranteedEasyContract(
                playerCount: 1, lowPopThreshold: 4, poolHasEasyTierOpen: false, personalCount: 2, maxPersonal: 1),
            Is.True);
    }

    [Test]
    public void NeedsGuaranteedEasyContract_ActiveAtMaxPlusTwo_BlocksThirdIssue()
    {
        // The second claimed easy consumes the hard-cap slot. No third easy may be issued until an
        // active contract resolves, even though the board temporarily has no open easy.
        Assert.That(ContractRules.NeedsGuaranteedEasyContract(
                playerCount: 1, lowPopThreshold: 4, poolHasEasyTierOpen: false, personalCount: 8, maxPersonal: 6),
            Is.False);
    }

    [Test]
    public void NeedsGuaranteedEasyContract_ResolvedClaimedEasy_ActiveBackAtMax_RearmsPlusOne()
    {
        // After the claimed easy completes/expires, active returns to MaxPersonal with no open easy
        // → the +1 exception re-arms exactly once.
        Assert.That(ContractRules.NeedsGuaranteedEasyContract(
                playerCount: 1, lowPopThreshold: 4, poolHasEasyTierOpen: false, personalCount: 6, maxPersonal: 6),
            Is.True);
    }

    [Test]
    public void NeedsGuaranteedEasyContract_RepeatedClaimScenario_ActiveNeverExceedsMaxPlusTwo()
    {
        // First claim re-arms once at MaxPersonal+1. A second claim leaves active at
        // MaxPersonal+2 and must not re-arm again.
        const int maxPersonal = 6;
        const int activeAfterForceAndClaim = maxPersonal + 1; // 6 non-easy + 1 claimed easy
        Assert.That(ContractRules.NeedsGuaranteedEasyContract(
                playerCount: 1, lowPopThreshold: 4, poolHasEasyTierOpen: false,
                personalCount: activeAfterForceAndClaim, maxPersonal: maxPersonal),
            Is.True, "the sole open easy guarantee may consume the bounded second slot");

        const int activeAfterSecondClaim = maxPersonal + 2;
        Assert.That(ContractRules.NeedsGuaranteedEasyContract(
                playerCount: 1, lowPopThreshold: 4, poolHasEasyTierOpen: false,
                personalCount: activeAfterSecondClaim, maxPersonal: maxPersonal),
            Is.False, "the hard cap blocks a third issue until an active contract resolves");
    }

    [Test]
    public void ShouldLogMissingEasyContent_OnlyFirstGapIsReportable()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ContractRules.ShouldLogMissingEasyContent(alreadyLogged: false), Is.True);
            Assert.That(ContractRules.ShouldLogMissingEasyContent(alreadyLogged: true), Is.False);
        });
    }
}
