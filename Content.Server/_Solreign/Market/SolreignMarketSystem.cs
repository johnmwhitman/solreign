using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Content.Server._Solreign.Director;
using Content.Shared._Solreign.Market;
using Content.Shared.CCVar;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;

namespace Content.Server._Solreign.Market;

/// <summary>
///     "Requisitions Anonymous" — the black-market console (v11 front door #3): click a wallmount
///     terminal, browse the Director daemon's curated black-market inventory
///     (<c>GET /api/public/market</c>), and spend Corporate Standing on a listing
///     (<c>POST /api/market/buy</c>). The physical item is NOT spawned here — a successful buy
///     makes the daemon queue a <c>spawn_entity</c> Director event, which arrives later through the
///     EXISTING poll pipe (<c>SolreignOracleSystem.Update</c>'s Director-event drain handles it,
///     per the v11 backbone rule "the poll is the ONE delivery channel for ALL daemon-&gt;game
///     events"). This system never touches Standing itself — the daemon deducts it atomically
///     server-side on a successful buy (see <c>orchestrator/market.py</c>'s conditional UPDATE).
///
///     Threading idiom copied from <see cref="Content.Server.Administration.Systems.SolreignOracleSystem"/>:
///     every game-thread fact a <c>Task.Run</c> block needs (console/buyer <see cref="EntityUid"/>,
///     buyer GUID, listing id) is captured on the GAME THREAD before the async block starts; the
///     async block does HTTP only, never touches entities/components/UI; results marshal back via
///     <see cref="ConcurrentQueue{T}"/>, drained in <see cref="Update"/>, with the feature gate
///     rechecked at every drain step (Codex v2 check-then-drain race pattern, same as Oracle).
///
///     DAEMON API CONTRACT (fixed, do not invent fields): the public inventory route's response is
///     intentionally NOT <see cref="DirectorChannel.VerifyResponse"/>-checked — it is treated as
///     UNTRUSTED display data and run through <see cref="SanitizeListings"/> (bounded list size,
///     bounded name/description length, bounded price; entries that fail are dropped, not
///     truncated) before ever reaching a <see cref="SolreignMarketUiState"/>. The buy route IS
///     signed both directions and its ack IS verified before any popup or refresh.
/// </summary>
public sealed partial class SolreignMarketSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private UserInterfaceSystem _uiSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;

    private static readonly HttpClient WebhookClient = new HttpClient();

    private const string MarketChannel = "market";

    // Sanitize caps for the untrusted public inventory response (DAEMON API contract).
    private const int MaxListings = 50;
    private const int MaxNameLength = 60;
    private const int MaxDescriptionLength = 200;
    private const int MinPrice = 0;
    private const int MaxPrice = 100_000;

    private const int MaxPopupMessageLength = 200;

    /// <summary>One console's BoundUIOpenedEvent inventory fetch, awaiting its SetUiState.</summary>
    private readonly ConcurrentQueue<(EntityUid Console, List<SolreignMarketListing> Listings)> _pendingInventory = new();

    /// <summary>
    ///     A refreshed inventory snapshot to push to EVERY open console (post-buy refresh — the
    ///     Contracts Board <c>UpdateBoards</c> idiom. The market has no per-station scope the way
    ///     contracts do, since it's a single daemon-side feed, so this is a global broadcast).
    /// </summary>
    private readonly ConcurrentQueue<List<SolreignMarketListing>> _pendingRefreshBroadcast = new();

    /// <summary>A buy result awaiting its buyer-facing popup (and, on success, a refresh trigger).
    /// <c>PriceAtClickTime</c> is snapshotted synchronously in <see cref="OnBuyMessage"/> at the
    /// moment of the click (see its doc comment) — never re-queried from the (possibly since-
    /// repriced) cache at drain time.</summary>
    private readonly ConcurrentQueue<(EntityUid Buyer, int ListingId, bool Success, string Message, int? PriceAtClickTime)> _pendingBuyResults = new();

    /// <summary>
    ///     UX-SIMPLE FIX 4: last-seen price per listing id, refreshed every time a console fetches
    ///     or is broadcast a sanitized inventory. Lets a successful buy's confirmation popup show
    ///     "-N Standing" alongside the item name — the daemon's buy ack itself never echoes back a
    ///     price (see <see cref="MarketBuyResponseDto"/>), so this is the only game-side source for
    ///     it. Best-effort only: if the listing has never been seen by this process (e.g. a stale
    ///     server restart) the popup simply omits the Standing line rather than showing a wrong
    ///     number — see the drain in <see cref="Update"/>.
    /// </summary>
    private readonly Dictionary<int, int> _lastKnownPriceByListingId = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SolreignMarketConsoleComponent, BoundUIOpenedEvent>(OnMarketUiOpened);
        SubscribeLocalEvent<SolreignMarketConsoleComponent, SolreignMarketBuyMessage>(OnBuyMessage);
    }

    private void OnMarketUiOpened(EntityUid uid, SolreignMarketConsoleComponent component, BoundUIOpenedEvent args)
    {
        if (!DirectorChannel.TryGetReadyToken(_config, CCVars.SolreignMarketEnabled, out var token))
        {
            // Simplest honest UX: an explicit "not answering" status line rather than silence, so
            // a disabled/misconfigured console doesn't just look broken.
            _uiSystem.SetUiState(uid, SolreignMarketUiKey.Key,
                new SolreignMarketUiState(new List<SolreignMarketListing>(), Loc.GetString("solreign-market-status-offline")));
            return;
        }

        if (!DirectorChannel.TryEnterRateLimit(MarketChannel))
            return;

        FetchInventory(uid, token);
    }

    private void FetchInventory(EntityUid console, string token)
    {
        Task.Run(async () =>
        {
            try
            {
                var (request, _) = DirectorChannel.BuildSignedRequest(HttpMethod.Get,
                    DirectorChannel.GetBaseUrl(_config) + "/api/public/market", token, string.Empty);
                using (request)
                {
                    using var response = await WebhookClient.SendAsync(request);
                    if (!response.IsSuccessStatusCode)
                        return;

                    var body = await response.Content.ReadAsStringAsync();

                    // DAEMON API contract: the public route's response is NOT signed (untrusted
                    // display data by design) — no VerifyResponse call here, SanitizeListings is
                    // the trust boundary instead.
                    var raw = JsonSerializer.Deserialize<MarketListingDto[]>(body);
                    if (raw == null)
                        return;

                    _pendingInventory.Enqueue((console, SanitizeListings(raw)));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Market inventory fetch failed: {ex.Message}");
            }
        });
    }

    private void OnBuyMessage(EntityUid uid, SolreignMarketConsoleComponent component, SolreignMarketBuyMessage args)
    {
        if (!DirectorChannel.TryGetReadyToken(_config, CCVars.SolreignMarketEnabled, out var token))
        {
            // P3.2 OTHER SILENT DROPS (audit fix 2 of 4) — daemon-down branch. OnUiOpened already
            // tells the player the dealer is not answering; the press path didn't. A player who
            // walks up to a half-down console and mashes Buy now sees the same "not answering" line
            // the open path already showed, instead of five identical silent drops. Mirrors the
            // bounties-claim-failure-popup trio voice: dead air removed, in-fiction denial surfaced.
            if (args.Actor is { Valid: true } offlineActor)
                _popupSystem.PopupEntity(Loc.GetString("solreign-market-buy-offline-popup"), offlineActor, offlineActor, PopupType.MediumCaution);
            return;
        }

        if (args.Actor is not { Valid: true } actor)
            return;

        // Server-side GUID resolution ONLY — the buyer identity is never taken from the client
        // message (DAEMON API contract). Reuses the exact helper Crypt/Rivalry use for the same
        // "daemon keys everything by player GUID, never round-local EntityUid" reason.
        if (!DeathAttribution.TryGetPlayerGuid(EntityManager, actor, out var buyerGuid))
            return;

        if (!DirectorChannel.TryEnterRateLimit(MarketChannel))
        {
            // P3.2 OTHER SILENT DROPS (audit fix 2 of 4) — rate-limited branch. The claim never
            // left the station. Distinct "busy desk" copy (matches bounties-claim-busy-popup
            // separation) so a busy desk never reads as an outage. Popup directly on the game
            // thread — no queue needed, same idiom as the bounties rate-limit fix.
            _popupSystem.PopupEntity(Loc.GetString("solreign-market-buy-busy-popup"), actor, actor, PopupType.MediumCaution);
            return;
        }

        // Everything the async block needs is captured here, on the game thread, before Task.Run.
        var listingId = args.ListingId;

        // UX-SIMPLE FIX 4 (grk review finding, Standing-amount race): snapshot the price THIS
        // PLAYER was just shown, synchronously, at the moment of the click — not re-queried from
        // _lastKnownPriceByListingId later at drain time, when the daemon could have repriced (or
        // another console could have refreshed) the same listing id in between. This does not
        // eliminate every possible race with the daemon's own authoritative charge, but it collapses
        // the window from "since this console's last inventory fetch" down to "since this exact
        // click," which is the best a game-side-only fix can do without the daemon echoing back
        // the amount it actually charged.
        var priceAtClickTime = _lastKnownPriceByListingId.TryGetValue(listingId, out var cachedPrice)
            ? cachedPrice
            : (int?) null;

        Task.Run(async () => await FireBuyRequest(token, listingId, buyerGuid, actor, priceAtClickTime));
    }

    private async Task FireBuyRequest(string token, int listingId, string buyerGuid, EntityUid actor, int? priceAtClickTime)
    {
        try
        {
            var payload = new { item_id = listingId, buyer_guid = buyerGuid };
            var json = JsonSerializer.Serialize(payload);
            var (request, ctx) = DirectorChannel.BuildSignedRequest(HttpMethod.Post,
                DirectorChannel.GetBaseUrl(_config) + "/api/market/buy", token, json);
            using (request)
            {
                using var response = await WebhookClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    _pendingBuyResults.Enqueue((actor, listingId, false, "The dealer's line went dead.", priceAtClickTime));
                    return;
                }

                var body = await response.Content.ReadAsStringAsync();
                response.Headers.TryGetValues(DirectorChannel.SignatureHeader, out var sigVals);
                response.Headers.TryGetValues(DirectorChannel.TimestampHeader, out var tsVals);
                response.Headers.TryGetValues(DirectorChannel.NonceHeader, out var nonceVals);
                if (!DirectorChannel.VerifyResponse(token, ctx, body,
                        sigVals?.FirstOrDefault(), tsVals?.FirstOrDefault(), nonceVals?.FirstOrDefault()))
                {
                    Log.Warning("Market buy response unsigned/unverified/unbound to request; discarding.");
                    _pendingBuyResults.Enqueue((actor, listingId, false, "The dealer's signature didn't check out.", priceAtClickTime));
                    return;
                }

                var result = JsonSerializer.Deserialize<MarketBuyResponseDto>(body);
                if (result == null)
                {
                    _pendingBuyResults.Enqueue((actor, listingId, false, "The dealer said nothing intelligible.", priceAtClickTime));
                    return;
                }

                _pendingBuyResults.Enqueue(result.success
                    ? (actor, listingId, true, result.item ?? string.Empty, priceAtClickTime)
                    : (actor, listingId, false, result.error ?? string.Empty, priceAtClickTime));
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Market buy failed: {ex.Message}");
            _pendingBuyResults.Enqueue((actor, listingId, false, "The line dropped.", priceAtClickTime));
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Codex v2 check-then-drain race pattern (Oracle idiom): a kill switch flipped mid-drain
        // must stop queued effects immediately, not just future enqueues.
        if (!DirectorChannel.IsReady(_config, CCVars.SolreignMarketEnabled))
        {
            _pendingInventory.Clear();
            _pendingRefreshBroadcast.Clear();
            _pendingBuyResults.Clear();
            return;
        }

        while (_pendingInventory.TryDequeue(out var item))
        {
            if (!DirectorChannel.IsReady(_config, CCVars.SolreignMarketEnabled))
            {
                _pendingInventory.Clear();
                break;
            }

            if (!Exists(item.Console) || !TryComp<UserInterfaceComponent>(item.Console, out var ui))
                continue;

            _uiSystem.SetUiState((item.Console, ui), SolreignMarketUiKey.Key,
                new SolreignMarketUiState(item.Listings, string.Empty));

            // UX-SIMPLE FIX 4: cache each listing's price so a later successful buy can show how
            // much Standing it cost — the daemon's buy ack never echoes the price back (see
            // MarketBuyResponseDto), so this snapshot is the only game-side source for it.
            CachePrices(item.Listings);
        }

        while (_pendingBuyResults.TryDequeue(out var result))
        {
            if (!DirectorChannel.IsReady(_config, CCVars.SolreignMarketEnabled))
            {
                _pendingBuyResults.Clear();
                _pendingRefreshBroadcast.Clear();
                break;
            }

            if (!Exists(result.Buyer))
                continue;

            var capped = Cap(result.Message, MaxPopupMessageLength);
            if (result.Success && result.PriceAtClickTime is { } price)
            {
                // UX-SIMPLE FIX 4: known price (snapshotted at click time, see OnBuyMessage) -> the
                // shared "-N Standing — [reason]" confirmation, consistent with every other Standing
                // confirmation across the Solreign feature set. Falls through to the plain item-name
                // popup below when no price was cached yet at click time (e.g. a fresh process that
                // never fetched this console's inventory).
                Content.Server._Solreign.Notifications.SolreignAwardPopup.Show(_popupSystem, result.Buyer, -price,
                    Loc.GetString("solreign-market-award-reason-bought", ("item", capped)));
            }
            else
            {
                var text = result.Success
                    ? Loc.GetString("solreign-market-popup-bought", ("item", capped))
                    : Loc.GetString("solreign-market-popup-failed", ("error", capped));
                _popupSystem.PopupEntity(text, result.Buyer, result.Buyer, PopupType.Medium);
            }

            // A successful buy means the daemon's stock just changed — refresh every open console
            // so nobody is staring at a listing that's actually already sold.
            if (result.Success && DirectorChannel.TryGetReadyToken(_config, CCVars.SolreignMarketEnabled, out var refreshToken))
                FetchInventoryBroadcast(refreshToken);
        }

        while (_pendingRefreshBroadcast.TryDequeue(out var listings))
        {
            if (!DirectorChannel.IsReady(_config, CCVars.SolreignMarketEnabled))
            {
                _pendingRefreshBroadcast.Clear();
                break;
            }

            var query = EntityQueryEnumerator<SolreignMarketConsoleComponent, UserInterfaceComponent>();
            while (query.MoveNext(out var consoleUid, out _, out var ui))
            {
                _uiSystem.SetUiState((consoleUid, ui), SolreignMarketUiKey.Key,
                    new SolreignMarketUiState(listings, string.Empty));
            }

            CachePrices(listings);
        }
    }

    private void FetchInventoryBroadcast(string token)
    {
        Task.Run(async () =>
        {
            try
            {
                var (request, _) = DirectorChannel.BuildSignedRequest(HttpMethod.Get,
                    DirectorChannel.GetBaseUrl(_config) + "/api/public/market", token, string.Empty);
                using (request)
                {
                    using var response = await WebhookClient.SendAsync(request);
                    if (!response.IsSuccessStatusCode)
                        return;

                    var body = await response.Content.ReadAsStringAsync();
                    var raw = JsonSerializer.Deserialize<MarketListingDto[]>(body);
                    if (raw == null)
                        return;

                    _pendingRefreshBroadcast.Enqueue(SanitizeListings(raw));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Market refresh fetch failed: {ex.Message}");
            }
        });
    }

    /// <summary>
    ///     Sanitizes the untrusted public-inventory response into a bounded, safe-to-render
    ///     snapshot: caps the list to <see cref="MaxListings"/> entries and DROPS (never truncates)
    ///     any entry whose name/description/price falls outside the documented DAEMON API caps.
    ///     Internal + unit-tested directly (Content.Tests, via the same
    ///     <c>[InternalsVisibleTo("Content.Tests")]</c> on Content.Server that
    ///     <see cref="DirectorChannel"/>'s canonicalization helpers use).
    /// </summary>
    internal static List<SolreignMarketListing> SanitizeListings(MarketListingDto[] raw)
    {
        var result = new List<SolreignMarketListing>();
        foreach (var dto in raw)
        {
            if (result.Count >= MaxListings)
                break;

            var name = dto.name ?? string.Empty;
            var description = dto.description ?? string.Empty;

            if (name.Length == 0 || name.Length > MaxNameLength)
                continue;

            if (description.Length > MaxDescriptionLength)
                continue;

            if (dto.price < MinPrice || dto.price > MaxPrice)
                continue;

            result.Add(new SolreignMarketListing
            {
                Id = dto.id,
                Name = name,
                Description = description,
                Price = dto.price,
                IsSold = dto.is_sold,
            });
        }

        return result;
    }

    private static string Cap(string text, int max)
    {
        return text.Length > max ? text[..max] : text;
    }

    /// <summary>UX-SIMPLE FIX 4: refreshes the price cache from a just-fetched sanitized inventory
    /// snapshot. Overwrites rather than merges — a listing's price can legitimately change between
    /// fetches, and the cache should always reflect the MOST RECENT thing this console (or the
    /// broadcast) actually showed a player, never a stale earlier price.</summary>
    private void CachePrices(List<SolreignMarketListing> listings)
    {
        foreach (var listing in listings)
            _lastKnownPriceByListingId[listing.Id] = listing.Price;
    }

    /// <summary>UX-SIMPLE FIX 4 test seam: exercises the real price-cache update path without
    /// needing IoC/Initialize() — mirrors the SanitizeListings pure-testing idiom in this file.</summary>
    internal void CachePricesForTests(List<SolreignMarketListing> listings) => CachePrices(listings);

    /// <summary>UX-SIMPLE FIX 4 test seam: reads back a cached price, or null if this listing id has
    /// never been seen by this process.</summary>
    internal int? GetCachedPriceForTests(int listingId) =>
        _lastKnownPriceByListingId.TryGetValue(listingId, out var price) ? price : null;
}

/// <summary>
///     Wire DTO for one <c>GET /api/public/market</c> row. Field names/casing match the daemon's
///     JSON exactly (house idiom, see <c>SolreignOracleSystem.OracleResponse</c>/<c>DirectorEvent</c>)
///     so no <c>JsonSerializerOptions</c> is needed. UNTRUSTED — never used outside
///     <see cref="SolreignMarketSystem.SanitizeListings"/>.
/// </summary>
public sealed class MarketListingDto
{
    public int id { get; set; }
    public string? name { get; set; }
    public string? description { get; set; }
    public int price { get; set; }
    public bool is_sold { get; set; }
}

/// <summary>Wire DTO for the signed-and-verified <c>POST /api/market/buy</c> ack.</summary>
public sealed class MarketBuyResponseDto
{
    public bool success { get; set; }
    public string? item { get; set; }
    public string? error { get; set; }
}
