using System;
using System.Collections.Generic;
using Content.Shared.Roles;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Solreign.Contracts;

/// <summary>
///     Who a Solreign contract is for, and how completion credit is shared.
///     See the design spec (docs/specs/2026-07-11-contracts-progression-spec.md, §3.1).
/// </summary>
[Serializable, NetSerializable]
public enum SolreignContractScope : byte
{
    /// <summary>Claimed by one player at the board; reward goes to them alone.</summary>
    Personal = 0,

    /// <summary>Open to a department; anyone deposits, all depositors share credit. (Milestone 2 — schema only in M1.)</summary>
    Department = 1,

    /// <summary>
    ///     Group-scaled team-up: players register on a roster at the board, then a participant launches the
    ///     raid. At launch the objective sizes AND the per-head reward scale with the registered participant
    ///     count (see <c>ContractRules.SalvageScaledAmount</c> / <c>SalvageStanding</c>) — bigger crews take
    ///     on bigger hauls for a bigger per-head payout, so teaming up always beats soloing.
    /// </summary>
    SalvageRaid = 2,
}

/// <summary>
///     A Solreign Contract — a cheerfully menacing corporate work order (spec §3.1). Delivery-shaped:
///     completion means depositing <see cref="Entries"/> into a Fulfillment Dropbox. Mirrors the MIT
///     upstream <c>CargoBountyPrototype</c> shape (our primary template, spec §2.1) plus scope, standing,
///     spesos, chain, rank-gate, department and sabotage-spawn fields.
///
///     Anti-grief by construction (spec §6.1): there is deliberately NO data field that can name or target
///     a player, their gear, or their constructions — the schema cannot express a player target.
/// </summary>
[Prototype]
public sealed partial class SolreignContractPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Player-facing contract title (brand voice lives in the .ftl).</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>Flavor description for the board and the printed work order.</summary>
    [DataField]
    public LocId Description = string.Empty;

    /// <summary>Who this contract is for. Department contracts are dormant content until Milestone 2.</summary>
    [DataField]
    public SolreignContractScope Scope = SolreignContractScope.Personal;

    /// <summary>
    ///     Round-local Corporate Standing paid on completion — per claimant (personal) or per registered
    ///     participant (salvage raid; scaled up at launch, see <c>ContractRules.SalvageStanding</c>).
    /// </summary>
    [DataField(required: true)]
    public int Standing;

    /// <summary>
    ///     Optional spesos credit. Schema-complete but NOT paid out in Milestone 1 — payouts are Standing
    ///     only until the department-budget crediting API is confirmed (spec §10, open question 5).
    /// </summary>
    [DataField]
    public int Spesos;

    /// <summary>Prefix for the generated work-order id (bounty <c>idPrefix</c> idiom).</summary>
    [DataField]
    public string IdPrefix = "SOL-WO-";

    /// <summary>The deliverables that must be deposited for the contract to complete.</summary>
    [DataField(required: true)]
    public List<SolreignContractEntry> Entries = new();

    /// <summary>Owning department for <see cref="SolreignContractScope.Department"/> contracts (Milestone 2).</summary>
    [DataField]
    public ProtoId<DepartmentPrototype>? Department;

    /// <summary>
    ///     Optional chain link: completing this contract auto-issues the follow-up to the same claimant
    ///     (Milestone 2 wires the auto-issue; the field is schema-complete now so chains can be authored).
    /// </summary>
    [DataField]
    public ProtoId<SolreignContractPrototype>? NextContract;

    /// <summary>
    ///     Minimum career rank index (see <c>RankRules.CorporateRank</c>) required to claim. 0 = everyone.
    ///     Managers get management problems.
    /// </summary>
    [DataField]
    public int MinRankIndex;

    /// <summary>Roster cap for salvage raids (anti-hoarding, spec §6.7 spirit). Ignored for other scopes.</summary>
    [DataField]
    public int MaxParticipants = 6;

    /// <summary>
    ///     Props this contract spawns when issued and then asks players to remove (PG-"sabotage", spec §3.1).
    ///     Only ever contract-spawned props tagged <c>SolreignSabotageTarget</c> — never player property
    ///     (spec §6.2). Contracts with spawns are dormant content until Milestone 2 implements the spawner.
    /// </summary>
    [DataField]
    public List<SolreignSabotageSpawn> SabotageSpawns = new();

    /// <summary>
    ///     Marks this contract as always safely solo-completable: no department scope, no rank gate, no
    ///     sabotage-spawn dependency, and an item that's trivially obtainable without another crew
    ///     member's help (v14 low-pop extension, quest-board spec §2.2). The low-pop guaranteed-easy-
    ///     contract rule (<c>ContractRules.NeedsGuaranteedEasyContract</c>, wired in
    ///     <c>ContractsSystem.FillContracts</c>, gated by <c>CCVars.SolreignContractsQuestBoardEnabled</c>)
    ///     draws ONLY from prototypes tagged true here. Author-set, defaults false so every existing
    ///     prototype is unaffected until explicitly tagged.
    /// </summary>
    [DataField]
    public bool EasyTier;
}

/// <summary>
///     One deliverable line of a contract. Mirrors upstream <c>CargoBountyItemEntry</c>: an
///     <see cref="EntityWhitelist"/> (plus optional blacklist) is the entire "is this the right thing?"
///     verifier — zero new matching code (spec §2.1).
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public readonly partial record struct SolreignContractEntry()
{
    /// <summary>A whitelist for determining what items satisfy the entry.</summary>
    [DataField(required: true)]
    public EntityWhitelist Whitelist { get; init; } = default!;

    /// <summary>A blacklist that can be used to exclude items in the whitelist.</summary>
    [DataField]
    public EntityWhitelist? Blacklist { get; init; } = null;

    /// <summary>How many of the item must be deposited to satisfy the entry.</summary>
    [DataField]
    public int Amount { get; init; } = 1;

    /// <summary>A player-facing name for the item.</summary>
    [DataField(required: true)]
    public LocId Name { get; init; } = string.Empty;
}

/// <summary>A prop batch spawned by a PG-sabotage contract when it is issued (Milestone 2).</summary>
[DataDefinition, Serializable, NetSerializable]
public readonly partial record struct SolreignSabotageSpawn()
{
    /// <summary>The entity to spawn (an existing upstream prop, e.g. a rival poster).</summary>
    [DataField(required: true)]
    public EntProtoId Proto { get; init; } = default!;

    /// <summary>How many to spawn.</summary>
    [DataField]
    public int Amount { get; init; } = 1;
}
