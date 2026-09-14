using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._Solreign.Contracts;

namespace Content.Server._Solreign.Contracts;

/// <summary>
///     Pure, unit-testable mapping from live server contract state to the wire-format board snapshot
///     (<see cref="SolreignContractListing"/> / <see cref="SolreignContractsBoardState"/>). No ECS, no I/O
///     — the <see cref="ContractRules"/> precedent, so <c>ContractBoardViewTests</c> can pin the snapshot
///     semantics (copies, not references; clamped cooldowns) without spinning up the game.
/// </summary>
public static class ContractBoardView
{
    /// <summary>
    ///     Snapshots one live contract into a board listing. Progress/Required are COPIED (the client
    ///     state must never alias server arrays), and the roster is reduced to a head-count — account
    ///     GUIDs never leave the server (spec §6 spirit: the board shows names and numbers, not identities).
    /// </summary>
    public static SolreignContractListing BuildListing(SolreignActiveContract contract)
    {
        return new SolreignContractListing
        {
            Id = contract.Id,
            Prototype = contract.Prototype,
            Scope = contract.Scope,
            ClaimantName = contract.ClaimantName,
            Participants = contract.Participants.Count,
            Launched = contract.Launched,
            Progress = new List<int>(contract.Progress),
            Required = new List<int>(contract.Required),
            StandingPerHead = contract.StandingPerHead,
        };
    }

    /// <summary>Snapshots a whole pool, preserving board order.</summary>
    public static List<SolreignContractListing> BuildListings(IEnumerable<SolreignActiveContract> contracts)
    {
        return contracts.Select(BuildListing).ToList();
    }

    /// <summary>
    ///     Time remaining on the station-wide skip cooldown, clamped to zero — the client renders a
    ///     countdown and must never see a negative span (bounty console idiom, minus its underflow).
    /// </summary>
    public static TimeSpan UntilNextSkip(TimeSpan nextSkipTime, TimeSpan curTime)
    {
        var remaining = nextSkipTime - curTime;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
