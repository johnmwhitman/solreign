using System;
using System.Collections.Generic;
using Content.Shared._Solreign.Contracts;

namespace Content.Server._Solreign.Contracts;

/// <summary>
///     Pure, unit-testable contract logic: completion scoring weights (spec §4.1), salvage-raid group
///     scaling, claim caps (anti-grief rule 7) and per-entry deposit progress. No ECS, no I/O — kept pure
///     so <c>ContractRulesTests</c> can exercise every branch without spinning up the game (the
///     <c>CorporateScoring</c> precedent).
///
///     Anti-grief invariants enforced here (spec §6):
///     — rule 7: hard caps on claimed contracts per player and active contracts per station;
///     — rule 9: every value produced is non-negative, so the ledger's never-demote invariant holds.
/// </summary>
public static class ContractRules
{
    /// <summary>Max personal contracts one player may hold claimed at once (anti-grief rule 7, verbatim).</summary>
    public const int MaxClaimedPerPlayer = 3;

    /// <summary>Weighted contract_score for a completed personal contract (spec §4.1).</summary>
    public const int PersonalCompletionScore = 2;

    /// <summary>Weighted contract_score for a department contribution (spec §4.1; Milestone 2 scope).</summary>
    public const int DepartmentContributionScore = 1;

    /// <summary>Weighted contract_score for completing the FINAL link of a chain (spec §4.1).</summary>
    public const int ChainFinalCompletionScore = 3;

    /// <summary>
    ///     Extra per-head Standing for every teammate beyond the first on a launched salvage raid. This is
    ///     the team-up incentive: per-head workload stays flat (objectives scale linearly with the roster)
    ///     while per-head pay goes UP, so recruiting a colleague is always the profitable move.
    /// </summary>
    public const int SalvageTeamBonusPerExtraParticipant = 1;

    /// <summary>
    ///     The weighted contract_score a completion is worth (spec §4.1: personal=2, department=1,
    ///     chain-final=3). Salvage-raid participants earn personal-grade credit — they did personal-sized
    ///     work each. Chain-final outranks everything else regardless of scope.
    /// </summary>
    public static int CompletionScore(SolreignContractScope scope, bool chainFinal)
    {
        if (chainFinal)
            return ChainFinalCompletionScore;

        return scope switch
        {
            SolreignContractScope.Department => DepartmentContributionScore,
            _ => PersonalCompletionScore,
        };
    }

    /// <summary>
    ///     Is this contract the final link of a chain? Final = some other contract chains INTO it
    ///     (<paramref name="isChainTarget"/>) and it chains into nothing itself. A standalone contract is
    ///     never chain-final, and a mid-chain link (has a next) is never final either.
    /// </summary>
    public static bool IsChainFinal(bool hasNextContract, bool isChainTarget)
    {
        return isChainTarget && !hasNextContract;
    }

    /// <summary>May a player who already holds <paramref name="currentClaims"/> personal contracts claim another?</summary>
    public static bool CanClaimAnother(int currentClaims)
    {
        return currentClaims < MaxClaimedPerPlayer;
    }

    /// <summary>
    ///     Rank-gated contract tiers (spec §3.1/§3.4, M3): does a player at <paramref name="rankIndex"/>
    ///     meet a contract's <c>minRankIndex</c>? A gate of 0 (the default) admits every rank, including a
    ///     fresh Probationary Asset. Shared by every surface that hands work to a player — board claim
    ///     (personal) and salvage-raid join alike — so a rank gate authored on any scope is honored
    ///     everywhere, not just on the surface it happened to be tested against first.
    /// </summary>
    public static bool MeetsRankGate(int rankIndex, int minRankIndex)
    {
        return rankIndex >= minRankIndex;
    }

    /// <summary>
    ///     Clamps a salvage-raid roster size into [1, max]. A degenerate max (&lt; 1) still yields 1 so the
    ///     scaling math below can never zero out an objective or a reward.
    /// </summary>
    public static int ClampParticipants(int participants, int maxParticipants)
    {
        return Math.Clamp(participants, 1, Math.Max(1, maxParticipants));
    }

    /// <summary>
    ///     Objective size for one entry of a salvage raid, locked at launch: linear in the roster size, so
    ///     per-head workload is constant no matter how many colleagues sign up.
    /// </summary>
    public static int SalvageScaledAmount(int baseAmount, int participants, int maxParticipants)
    {
        return Math.Max(1, baseAmount) * ClampParticipants(participants, maxParticipants);
    }

    /// <summary>
    ///     Per-head Standing payout for a salvage raid, locked at launch: the base reward plus
    ///     <see cref="SalvageTeamBonusPerExtraParticipant"/> for every teammate beyond the first.
    ///     Solo raiders get exactly the base — teams always beat them per head. Never negative (rule 9).
    /// </summary>
    public static int SalvageStanding(int baseStanding, int participants, int maxParticipants)
    {
        var crew = ClampParticipants(participants, maxParticipants);
        return Math.Max(0, baseStanding) + SalvageTeamBonusPerExtraParticipant * (crew - 1);
    }

    /// <summary>
    ///     Applies a deposit of <paramref name="available"/> matched items against entry
    ///     <paramref name="entryIndex"/>, mutating <paramref name="progress"/>. Returns how many items were
    ///     actually consumed (0 for an invalid index, a non-positive deposit, or an already-full entry) —
    ///     never more than the entry still needs, so over-depositing can't eat a whole stack.
    /// </summary>
    public static int ApplyDeposit(IList<int> progress, IReadOnlyList<int> required, int entryIndex, int available)
    {
        if (entryIndex < 0 || entryIndex >= progress.Count || entryIndex >= required.Count || available <= 0)
            return 0;

        var needed = required[entryIndex] - progress[entryIndex];
        if (needed <= 0)
            return 0;

        var consumed = Math.Min(needed, available);
        progress[entryIndex] += consumed;
        return consumed;
    }

    /// <summary>True when every entry's progress has met its requirement.</summary>
    public static bool IsComplete(IReadOnlyList<int> progress, IReadOnlyList<int> required)
    {
        if (progress.Count != required.Count)
            return false;

        for (var i = 0; i < required.Count; i++)
        {
            if (progress[i] < required[i])
                return false;
        }

        return true;
    }

    // ---------------------------------------------------------------- v14 low-pop extension (quest-board spec §2)

    /// <summary>
    ///     Default player-count threshold for the low-pop guaranteed-easy-contract rule, mirrored by
    ///     <c>CCVars.SolreignContractsLowPopThreshold</c> at the call site. Kept here too so this rule
    ///     stays directly unit-testable without spinning up IoC/config.
    /// </summary>
    public const int DefaultLowPopThreshold = 4;

    /// <summary>
    ///     INVARIANT (enforced here, sole gate): total ACTIVE Personal contracts (open + claimed) is at
    ///     most <paramref name="maxPersonal"/> + 2. The first bounded slot lets the low-pop guarantee
    ///     issue at a normally full board; the second lets it re-arm once after that easy is claimed.
    ///     Claimants still occupy capacity, so a second claim reaches the hard bound and cannot cause a
    ///     third issue until an active contract resolves.
    ///
    ///     At or below <paramref name="lowPopThreshold"/> connected players, the bounded re-arm keeps
    ///     one <c>EasyTier</c> contract open while capacity remains below the hard cap.
    ///     <paramref name="poolHasEasyTierOpen"/> is true when an unclaimed EasyTier contract is
    ///     already on the board (nothing to do); false means the extension may force-issue one.
    ///     <paramref name="personalCount"/> is ACTIVE Personal
    ///     (claimed + open, every tier). Force-issue is allowed only while
    ///     <c>personalCount &lt;= maxPersonal + 1</c>, so after issue the pool is at most
    ///     MaxPersonal+2. At the hard bound, a temporarily missing open easy is intentional until an
    ///     active contract resolves. Above the threshold this is always false.
    /// </summary>
    public static bool NeedsGuaranteedEasyContract(
        int playerCount,
        int lowPopThreshold,
        bool poolHasEasyTierOpen,
        int personalCount,
        int maxPersonal)
    {
        if (playerCount > lowPopThreshold)
            return false;
        if (poolHasEasyTierOpen)
            return false;
        // ACTIVE (open+claimed) bound: one claim may re-arm the guarantee, but the second bounded
        // slot prevents repeated claims from growing the board without limit.
        return personalCount <= maxPersonal + 1;
    }

    /// <summary>
    ///     A missing issuable EasyTier pool is an authoring/configuration defect, but Ensure may run
    ///     repeatedly. Report the gap once per station component instead of flooding the server log.
    /// </summary>
    public static bool ShouldLogMissingEasyContent(bool alreadyLogged)
    {
        return !alreadyLogged;
    }
}
