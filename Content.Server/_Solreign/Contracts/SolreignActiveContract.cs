using System;
using System.Collections.Generic;
using Content.Shared._Solreign.Contracts;
using Robust.Shared.Prototypes;

namespace Content.Server._Solreign.Contracts;

/// <summary>
///     One live contract on a station's board. Round-scoped and never persisted (spec §3.3) — only
///     COMPLETIONS reach the Season Ledger SQLite, which keeps the DB append-only-ish and sidesteps
///     cross-round contract-state migration entirely. Accounts are keyed by raw <see cref="Guid"/>
///     (the ledger's key), captured from the claimant's mind at claim time.
/// </summary>
public sealed class SolreignActiveContract
{
    /// <summary>Unique work-order id, e.g. "SOL-WO-042" (bounty idPrefix idiom).</summary>
    public string Id = string.Empty;

    /// <summary>The contract prototype this work order was issued from.</summary>
    public ProtoId<SolreignContractPrototype> Prototype;

    public SolreignContractScope Scope;

    /// <summary>Claimant account (personal contracts). Null until claimed.</summary>
    public Guid? Claimant;

    /// <summary>Claimant display name, captured at claim time for the board and the scoreboard.</summary>
    public string? ClaimantName;

    /// <summary>Registered roster for salvage raids: account -> display name. Insertion-ordered.</summary>
    public readonly Dictionary<Guid, string> Participants = new();

    /// <summary>Salvage raids: true once launched — roster locked, objectives and reward scaled.</summary>
    public bool Launched;

    /// <summary>
    ///     Per-entry required amounts, parallel to the prototype's entries. Base amounts at issue; for
    ///     salvage raids these are re-scaled to the roster size at launch (accept-time scaling).
    /// </summary>
    public int[] Required = Array.Empty<int>();

    /// <summary>Per-entry deposited counts, parallel to <see cref="Required"/>.</summary>
    public int[] Progress = Array.Empty<int>();

    /// <summary>
    ///     Corporate Standing paid per claimant/participant on completion. The prototype's base at issue;
    ///     for salvage raids re-locked at launch via <see cref="ContractRules.SalvageStanding"/>.
    /// </summary>
    public int StandingPerHead;
}
