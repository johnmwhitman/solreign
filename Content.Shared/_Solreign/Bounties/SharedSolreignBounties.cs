using System;
using System.Collections.Generic;
using Robust.Shared.Serialization;

namespace Content.Shared._Solreign.Bounties;

/// <summary>UI key for the Liability Board bound user interface.</summary>
[Serializable, NetSerializable]
public enum SolreignBountyUiKey : byte
{
    Key = 0,
}

/// <summary>
///     One line of the Liability Board listing — untrusted daemon-supplied display data
///     (<c>GET /api/public/bounties</c> is unsigned; see <c>SolreignBountySystem</c>'s doc
///     comment). Every field is already length-capped by the server before it reaches the client.
/// </summary>
[Serializable, NetSerializable]
public sealed class SolreignBountyListing
{
    public int Id;

    public string Title = string.Empty;

    public string Description = string.Empty;
}

/// <summary>
///     Full board snapshot pushed to the Liability Board BUI: the current bounty pool plus an
///     optional status line (e.g. the last claim verdict) shown above the claim input.
/// </summary>
[Serializable, NetSerializable]
public sealed class SolreignBountyUiState : BoundUserInterfaceState
{
    public List<SolreignBountyListing> Bounties;

    public string? Status;

    /// <summary>
    ///     ALIVENESS P0 #2: true when the last listing fetch FAILED (daemon down/unreachable/
    ///     malformed), so the client can render a distinct in-fiction offline line instead of the
    ///     true-empty copy — a dead daemon must never be indistinguishable from a genuinely empty
    ///     bounty pool. Additive with a default so every pre-existing constructor call keeps its
    ///     meaning (a successful fetch).
    /// </summary>
    public bool Offline;

    public SolreignBountyUiState(List<SolreignBountyListing> bounties, string? status = null, bool offline = false)
    {
        Bounties = bounties;
        Status = status;
        Offline = offline;
    }
}

/// <summary>
///     Submit a claim against the whole board — the daemon's LLM matches the claim text against
///     every active bounty itself (the public claim API takes no bounty id), so there is one
///     claim input for the entire window, not a per-row claim button.
/// </summary>
[Serializable, NetSerializable]
public sealed class SolreignBountyClaimMessage : BoundUserInterfaceMessage
{
    public string ClaimText;

    public SolreignBountyClaimMessage(string claimText)
    {
        ClaimText = claimText;
    }
}
