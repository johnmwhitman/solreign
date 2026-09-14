using System;
using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._Solreign.Contracts;

/// <summary>UI key for the Contracts Board bound user interface (client window lands in Milestone 2).</summary>
[Serializable, NetSerializable]
public enum SolreignContractsUiKey : byte
{
    Board = 0,
}

/// <summary>
///     One line of the Contracts Board listing — everything a client needs to render a contract row.
///     Copies the cargo bounty console state idiom (serializable snapshot, no live references).
/// </summary>
[Serializable, NetSerializable]
public sealed class SolreignContractListing
{
    /// <summary>The unique work-order id (e.g. "SOL-WO-042").</summary>
    public string Id = string.Empty;

    /// <summary>The contract prototype id.</summary>
    public string Prototype = string.Empty;

    public SolreignContractScope Scope;

    /// <summary>Display name of the claimant, if a personal contract has been claimed.</summary>
    public string? ClaimantName;

    /// <summary>Registered roster size (salvage raids).</summary>
    public int Participants;

    /// <summary>Whether a salvage raid has launched (roster locked, objectives scaled).</summary>
    public bool Launched;

    /// <summary>Per-entry deposited counts, parallel to <see cref="Required"/>.</summary>
    public List<int> Progress = new();

    /// <summary>Per-entry required counts (already group-scaled for launched raids).</summary>
    public List<int> Required = new();

    /// <summary>
    ///     Corporate Standing paid per claimant/participant. Snapshotted here because the client cannot
    ///     derive it from the prototype: launched salvage raids re-lock it to the roster size at launch.
    /// </summary>
    public int StandingPerHead;
}

/// <summary>Full board snapshot pushed to the Contracts Board BUI.</summary>
[Serializable, NetSerializable]
public sealed class SolreignContractsBoardState : BoundUserInterfaceState
{
    public List<SolreignContractListing> Contracts;
    public TimeSpan UntilNextSkip;

    public SolreignContractsBoardState(List<SolreignContractListing> contracts, TimeSpan untilNextSkip)
    {
        Contracts = contracts;
        UntilNextSkip = untilNextSkip;
    }
}

/// <summary>Claim a personal contract (bind it to the sender and print a work order).</summary>
[Serializable, NetSerializable]
public sealed class SolreignContractClaimMessage : BoundUserInterfaceMessage
{
    public string ContractId;

    public SolreignContractClaimMessage(string contractId)
    {
        ContractId = contractId;
    }
}

/// <summary>Skip (decline) a contract — access-gated and station-wide cooldown-gated (bounty idiom).</summary>
[Serializable, NetSerializable]
public sealed class SolreignContractSkipMessage : BoundUserInterfaceMessage
{
    public string ContractId;

    public SolreignContractSkipMessage(string contractId)
    {
        ContractId = contractId;
    }
}

/// <summary>Register the sender on a salvage raid's roster (recruiting phase).</summary>
[Serializable, NetSerializable]
public sealed class SolreignRaidJoinMessage : BoundUserInterfaceMessage
{
    public string ContractId;

    public SolreignRaidJoinMessage(string contractId)
    {
        ContractId = contractId;
    }
}

/// <summary>
///     Launch a salvage raid: locks the roster and scales objectives + per-head reward to the participant
///     count registered at this moment (accept-time scaling, see <c>ContractRules</c>).
/// </summary>
[Serializable, NetSerializable]
public sealed class SolreignRaidLaunchMessage : BoundUserInterfaceMessage
{
    public string ContractId;

    public SolreignRaidLaunchMessage(string contractId)
    {
        ContractId = contractId;
    }
}
