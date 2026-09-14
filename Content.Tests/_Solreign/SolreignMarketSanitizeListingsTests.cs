#nullable enable
using System.Linq;
using Content.Server._Solreign.Market;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Pure unit tests for <see cref="SolreignMarketSystem.SanitizeListings"/> — the trust boundary
///     between the Director daemon's UNSIGNED <c>GET /api/public/market</c> response and anything
///     that ever reaches a <c>SolreignMarketUiState</c>. Per the DAEMON API contract this response
///     is intentionally never <see cref="Content.Server._Solreign.Director.DirectorChannel.VerifyResponse"/>-checked,
///     so this sanitizer is the ONLY thing standing between arbitrary daemon output and the
///     client. Uses <c>InternalsVisibleTo("Content.Tests")</c> on Content.Server.
/// </summary>
[TestFixture]
[TestOf(typeof(SolreignMarketSystem))]
public sealed class SolreignMarketSanitizeListingsTests
{
    private static MarketListingDto Dto(int id, string? name, string? description, int price, bool isSold = false)
    {
        return new MarketListingDto { id = id, name = name, description = description, price = price, is_sold = isSold };
    }

    [Test]
    public void SanitizeListings_ValidEntry_PassesThrough()
    {
        var result = SolreignMarketSystem.SanitizeListings(new[] { Dto(1, "Top Hat", "Dignified.", 20) });

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Id, Is.EqualTo(1));
        Assert.That(result[0].Name, Is.EqualTo("Top Hat"));
        Assert.That(result[0].Description, Is.EqualTo("Dignified."));
        Assert.That(result[0].Price, Is.EqualTo(20));
        Assert.That(result[0].IsSold, Is.False);
    }

    [Test]
    public void SanitizeListings_CapsListAtFifty()
    {
        var raw = Enumerable.Range(1, 75).Select(i => Dto(i, $"Item {i}", "desc", 10)).ToArray();

        var result = SolreignMarketSystem.SanitizeListings(raw);

        Assert.That(result, Has.Count.EqualTo(50));
    }

    [Test]
    public void SanitizeListings_DropsEmptyName()
    {
        var result = SolreignMarketSystem.SanitizeListings(new[] { Dto(1, "", "desc", 10), Dto(2, null, "desc", 10) });

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void SanitizeListings_DropsOversizedName()
    {
        var result = SolreignMarketSystem.SanitizeListings(new[] { Dto(1, new string('a', 61), "desc", 10) });

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void SanitizeListings_DropsOversizedDescription()
    {
        var result = SolreignMarketSystem.SanitizeListings(new[] { Dto(1, "name", new string('a', 201), 10) });

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void SanitizeListings_AllowsNullDescription()
    {
        var result = SolreignMarketSystem.SanitizeListings(new[] { Dto(1, "name", null, 10) });

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Description, Is.EqualTo(string.Empty));
    }

    [Test]
    public void SanitizeListings_DropsNegativePrice()
    {
        var result = SolreignMarketSystem.SanitizeListings(new[] { Dto(1, "name", "desc", -1) });

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void SanitizeListings_DropsPriceOverMax()
    {
        var result = SolreignMarketSystem.SanitizeListings(new[] { Dto(1, "name", "desc", 100_001) });

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void SanitizeListings_AllowsPriceAtBoundaries()
    {
        var result = SolreignMarketSystem.SanitizeListings(new[] { Dto(1, "name", "desc", 0), Dto(2, "name2", "desc", 100_000) });

        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public void SanitizeListings_PreservesIsSoldFlag()
    {
        var result = SolreignMarketSystem.SanitizeListings(new[] { Dto(1, "name", "desc", 10, isSold: true) });

        Assert.That(result[0].IsSold, Is.True);
    }

    // -----------------------------------------------------------------------------------------
    // UX-SIMPLE FIX 4: the price cache that lets a successful buy's confirmation popup show
    // "-N Standing" alongside the item name (the daemon's buy ack never echoes a price back).
    // -----------------------------------------------------------------------------------------

    [Test]
    public void CachePrices_UnseenListing_HasNoCachedPrice()
    {
        var system = new SolreignMarketSystem();

        Assert.That(system.GetCachedPriceForTests(1), Is.Null);
    }

    [Test]
    public void CachePrices_SeenListing_IsRetrievableByIdAfterward()
    {
        var system = new SolreignMarketSystem();
        var listings = SolreignMarketSystem.SanitizeListings(new[] { Dto(1, "Top Hat", "Dignified.", 42) });

        system.CachePricesForTests(listings);

        Assert.That(system.GetCachedPriceForTests(1), Is.EqualTo(42));
    }

    [Test]
    public void CachePrices_RepeatedFetchWithAChangedPrice_OverwritesRatherThanKeepsTheStaleValue()
    {
        // A listing's price can legitimately change between fetches (daemon-side repricing); the
        // cache must always reflect the MOST RECENT thing a player was actually shown, never an
        // earlier price that could now overstate or understate what they paid.
        var system = new SolreignMarketSystem();
        system.CachePricesForTests(SolreignMarketSystem.SanitizeListings(new[] { Dto(1, "Top Hat", "Dignified.", 42) }));
        system.CachePricesForTests(SolreignMarketSystem.SanitizeListings(new[] { Dto(1, "Top Hat", "Dignified.", 99) }));

        Assert.That(system.GetCachedPriceForTests(1), Is.EqualTo(99));
    }

    [Test]
    public void CachePrices_MultipleDistinctListings_AreCachedIndependently()
    {
        var system = new SolreignMarketSystem();
        system.CachePricesForTests(SolreignMarketSystem.SanitizeListings(new[]
        {
            Dto(1, "Top Hat", "Dignified.", 20),
            Dto(2, "Bike Horn", "Honk.", 5),
        }));

        Assert.Multiple(() =>
        {
            Assert.That(system.GetCachedPriceForTests(1), Is.EqualTo(20));
            Assert.That(system.GetCachedPriceForTests(2), Is.EqualTo(5));
        });
    }
}
