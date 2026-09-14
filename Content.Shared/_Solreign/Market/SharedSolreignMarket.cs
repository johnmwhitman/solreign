using System;
using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._Solreign.Market;

/// <summary>UI key for the "Requisitions Anonymous" black-market console bound user interface.</summary>
[Serializable, NetSerializable]
public enum SolreignMarketUiKey : byte
{
    Key = 0,
}

/// <summary>
///     One black-market listing as sanitized for display — everything a client needs to render a
///     row. Mirrors <c>SolreignContractListing</c>'s "serializable snapshot, no live references"
///     idiom (Content.Shared._Solreign.Contracts). Every field here has already passed
///     <c>SolreignMarketSystem.SanitizeListings</c>'s length/range caps before this type is ever
///     constructed — the Director daemon's raw fields are UNTRUSTED display data (the public
///     inventory route's response is not signature-verified, by design; see
///     <c>SolreignMarketSystem</c>'s doc comment) and must never reach the client unsanitized.
/// </summary>
[Serializable, NetSerializable]
public sealed class SolreignMarketListing
{
    /// <summary>The daemon-assigned listing id, echoed back on <see cref="SolreignMarketBuyMessage"/>.</summary>
    public int Id;

    public string Name = string.Empty;

    public string Description = string.Empty;

    /// <summary>Corporate Standing cost. Authoritative price enforcement happens daemon-side.</summary>
    public int Price;

    public bool IsSold;
}

/// <summary>
///     Full market snapshot pushed to the console BUI. <see cref="StatusText"/> carries a
///     server-resolved flavor line for the offline/unreachable case (e.g. "the dealer is not
///     answering") rather than a second UI state — a successful/failed BUY's own result is
///     delivered as a popup, not rendered here, the same "reply arrives out of band" idiom the
///     Directive Terminal established for the Oracle's answers.
/// </summary>
[Serializable, NetSerializable]
public sealed class SolreignMarketUiState : BoundUserInterfaceState
{
    public List<SolreignMarketListing> Listings;
    public string StatusText;

    public SolreignMarketUiState(List<SolreignMarketListing> listings, string statusText)
    {
        Listings = listings;
        StatusText = statusText;
    }
}

/// <summary>
///     Buy a black-market listing by its daemon-assigned id. The buyer's identity is NEVER carried
///     on this message — the server resolves the sending session's player GUID itself
///     (<c>DeathAttribution.TryGetPlayerGuid</c>) from the BUI's own <c>Actor</c>, exactly like
///     every other Director-channel consumer that reports a player identity.
/// </summary>
[Serializable, NetSerializable]
public sealed class SolreignMarketBuyMessage : BoundUserInterfaceMessage
{
    public int ListingId;

    public SolreignMarketBuyMessage(int listingId)
    {
        ListingId = listingId;
    }
}
