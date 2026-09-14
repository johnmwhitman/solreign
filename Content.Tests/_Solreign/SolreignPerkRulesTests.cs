#nullable enable
using Content.Server._Solreign.SeasonLedger;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(SolreignPerkRules))]
public sealed class SolreignPerkRulesTests
{
    [TestCase("Brand Ambassador", true, "Golden Brand Ambassador")]
    [TestCase("Golden Brand Ambassador", true, "Golden Brand Ambassador")]
    [TestCase("", true, "Golden Name")]
    [TestCase(null, true, "Golden Name")]
    [TestCase("Brand Ambassador", false, "Brand Ambassador")]
    [TestCase("", false, "Name")]
    public void ApplyGoldenName_IsIdempotentAndFailClosed(
        string? title,
        bool enabled,
        string expected)
    {
        Assert.That(SolreignPerkRules.ApplyGoldenName(title, enabled), Is.EqualTo(expected));
    }

    [Test]
    public void ApplyGoldenName_ReappliesAfterAsyncTitleLoadWithoutDoublePrefix()
    {
        var grantedBeforeTitleLoad = SolreignPerkRules.ApplyGoldenName(string.Empty, enabled: true);
        var reloadedTitle = SolreignPerkRules.ApplyGoldenName("Brand Ambassador", enabled: true);

        Assert.Multiple(() =>
        {
            Assert.That(grantedBeforeTitleLoad, Is.EqualTo("Golden Name"));
            Assert.That(reloadedTitle, Is.EqualTo("Golden Brand Ambassador"));
            Assert.That(
                SolreignPerkRules.ApplyGoldenName(reloadedTitle, enabled: true),
                Is.EqualTo(reloadedTitle));
        });
    }

    [Test]
    public void AsyncCompletionOrders_BothRefreshTheVisibleStanding()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Content.Server", "_Solreign", "SeasonLedger",
            "SeasonLedgerSystem.Perks.cs"));

        Assert.Multiple(() =>
        {
            // Perk first: the later title-load drain reapplies Golden and stamps the card.
            Assert.That(
                SolreignPerkRules.ApplyGoldenName("Brand Ambassador", enabled: true),
                Is.EqualTo("Golden Brand Ambassador"));

            // Title first: the perk drain itself must stamp the newly decorated title.
            Assert.That(source, Does.Contain("var previousTitle = comp.Title;"));
            Assert.That(
                source,
                Does.Contain("StampIdCardStanding(perk.Mob, comp, previousTitle);"));
        });
    }

    [Test]
    public void BuildPerksRequestBody_PutsUuidOnlyInSignedJsonBody()
    {
        var userId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var json = SolreignPerkRules.BuildRequestBody(userId);
        using var document = JsonDocument.Parse(json);

        Assert.Multiple(() =>
        {
            Assert.That(document.RootElement.EnumerateObject().Count(), Is.EqualTo(1));
            Assert.That(document.RootElement.GetProperty("player_id").GetGuid(), Is.EqualTo(userId));
            Assert.That(json, Does.Not.Contain("id="));
        });
    }

    [Test]
    public void RateLimitChannel_IsStablePerAccountAndDoesNotCollideAcrossJoinBurst()
    {
        var first = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var second = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        Assert.Multiple(() =>
        {
            Assert.That(
                SolreignPerkRules.RateLimitChannel(first),
                Is.EqualTo(SolreignPerkRules.RateLimitChannel(first)));
            Assert.That(
                SolreignPerkRules.RateLimitChannel(first),
                Is.Not.EqualTo(SolreignPerkRules.RateLimitChannel(second)));
        });
    }

    [Test]
    public void PendingGrant_IsBoundToTheAccountThatRequestedIt()
    {
        var expected = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var replacement = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        Assert.Multiple(() =>
        {
            Assert.That(
                SolreignPerkRules.IsStillOwnedByExpectedUser(expected, expected),
                Is.True);
            Assert.That(
                SolreignPerkRules.IsStillOwnedByExpectedUser(expected, replacement),
                Is.False);
        });
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "Content.Server",
                    "Content.Server.csproj")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the Game repository root.");
    }
}
