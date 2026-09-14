#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.StationDirective;
using Content.Server.GameTicking;
using NUnit.Framework;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     SR-W-081 ("random round modifiers"): live coverage for the expanded Station Directive catalog
///     (3 -&gt; 12 entries). <c>StationDirectiveCatalogTests</c> (Content.Tests) already covers the pure
///     data shape (unique ids, ticker counts, screen-FX clamp sanity); this file is the analogue of
///     <c>ProvidenceVoiceSystemIntegrationTest</c> for this system — it exercises the real
///     <see cref="Robust.Shared.Localization.ILocalizationManager"/> lookup for every directive's
///     announcement and ticker lines (the exact "built but never instantiated" defect class
///     <c>SolreignPrototypeIdIntegrityTest</c>'s doc comment describes, just for Fluent keys instead of
///     entity prototypes), plus a live round-start smoke test that starts the actual
///     <c>SolreignStationDirective</c> game rule and asserts it announces without throwing — covering
///     whichever directive the round-robin selects for that server's live round id, including the new
///     screen-FX code path when it lands on one of the two directives that use it.
/// </summary>
[TestFixture]
public sealed class StationDirectiveIntegrationTest : GameTest
{
    // Dirty: StartGameRule_LiveRound_AnnouncesWithoutThrowing starts a real, persistent game-rule entity
    // on the server (the ProvidenceVoiceSystemIntegrationTest idiom for any test that mutates shared
    // server state) — that server must never be handed back to the pool for reuse by another test.
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
    };

    // Named const (not an inline literal) so RA0033 (ForbidLiteral on StartGameRule's ruleId
    // parameter) doesn't flag it — same workaround SolreignCorporateRuleSystemIntegrationTest
    // already uses for its own StartGameRule call.
    private const string DirectiveRuleId = "SolreignStationDirective";

    private static IEnumerable<StationDirectiveDefinition> AllDirectives() => StationDirectiveCatalog.Directives;

    [TestCaseSource(nameof(AllDirectives))]
    public async Task AnnouncementLocKey_ForEveryDirective_ResolvesToRealText(StationDirectiveDefinition directive)
    {
        var server = Server;

        await server.WaitAssertion(() =>
        {
            string resolved = default!;
            Assert.DoesNotThrow(() => resolved = Loc.GetString(directive.AnnouncementLocKey));
            Assert.That(resolved, Is.Not.EqualTo(directive.AnnouncementLocKey),
                $"{directive.Id}'s announcement loc key '{directive.AnnouncementLocKey}' has no matching " +
                "entry in station_directive.ftl — Loc.GetString silently echoes the raw key back on a miss " +
                "(it does not throw), so this is the only signal that would catch it.");
        });
    }

    [TestCaseSource(nameof(AllDirectives))]
    public async Task TickerLocKeys_ForEveryDirective_AllResolveToRealText(StationDirectiveDefinition directive)
    {
        var server = Server;

        await server.WaitAssertion(() =>
        {
            foreach (var tickerKey in directive.TickerLocKeys)
            {
                string resolved = default!;
                Assert.DoesNotThrow(() => resolved = Loc.GetString(tickerKey));
                Assert.That(resolved, Is.Not.EqualTo(tickerKey),
                    $"{directive.Id}'s ticker loc key '{tickerKey}' has no matching entry in station_directive.ftl.");
            }
        });
    }

    [Test]
    public async Task HrSenderLocKey_Resolves()
    {
        var server = Server;

        await server.WaitAssertion(() =>
        {
            var resolved = Loc.GetString("solreign-station-directive-hr-sender");
            Assert.That(resolved, Is.Not.EqualTo("solreign-station-directive-hr-sender"));
        });
    }

    [Test]
    public async Task StartGameRule_LiveRound_AnnouncesWithoutThrowing()
    {
        var server = Server;
        var ticker = server.System<GameTicker>();

        await server.WaitAssertion(() =>
        {
            Assert.DoesNotThrow(() => ticker.StartGameRule(DirectiveRuleId),
                "Starting the Station Directive game rule (StationDirectiveRuleSystem.Started) must never " +
                "throw for whichever directive the round-robin selects for this round id — including the " +
                "screen-FX RaiseNetworkEvent path for Safety Inspection / Surveillance Sweep.");
        });

        // Let the rule's Started callback and any queued events actually run.
        await server.WaitRunTicks(2);
    }
}
