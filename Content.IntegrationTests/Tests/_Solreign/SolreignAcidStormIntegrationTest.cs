#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.StationIdentity;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using NUnit.Framework;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Wow-wiring wave (docs/receipts/wow-wiring/WOW-WIRING-2026-07-16.md), Feature 2: "Acid Storm"
///     station event. Same shape as <c>StationDirectiveIntegrationTest</c>'s live-round smoke test —
///     starts the real game rule and asserts it announces/scatters/screen-fx's without throwing, plus a
///     dedicated CVar-off test proving the rule ends itself and never becomes active. Grok's review of
///     this feature caught that a naive CVar check only inside <c>Started</c> is NOT a full kill
///     switch: <c>StationEventSystem{T}.Added</c>/<c>Ended</c> dispatch the start/end announcement text
///     unconditionally, outside <c>Started</c>'s control -- fixed by <c>SolreignAcidStormRule</c> also
///     overriding <c>Added</c>/<c>Ended</c> to skip <c>base</c> entirely while disabled. This file
///     verifies the rule-activity side of that fix (<c>IsGameRuleActive</c> false); the
///     announcement-suppression side is verified by code review of the paired
///     Added/Started/Ended CVar checks rather than a chat-capture test, which this codebase has no
///     existing harness for.
///
///     Pure prototype-id typo safety (<c>SolreignAcidMote</c>, referenced from
///     <c>SolreignAcidStormRuleComponent.MoteEffectPrototype</c>) is already covered by the existing
///     <c>SolreignPrototypeIdIntegrityTest</c> — no new pure test needed for that.
/// </summary>
[TestFixture]
public sealed class SolreignAcidStormIntegrationTest : GameTest
{
    // Dirty: starts a real, persistent game-rule entity on the server (the ProvidenceVoiceSystem /
    // StationDirective idiom for any test that mutates shared server state) — this server must never
    // be handed back to the pool for reuse by another test.
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
    };

    [Test]
    public async Task StartGameRule_LiveRound_AnnouncesScattersAndScreenFxesWithoutThrowing()
    {
        var server = Server;
        var ticker = server.System<GameTicker>();

        server.CfgMan.SetCVar(CCVars.SolreignAcidStormEnabled, true);
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.DoesNotThrow(() => ticker.StartGameRule(SolreignAcidStormRule.EventPrototypeId),
                "Starting the Acid Storm game rule (SolreignAcidStormRule.Started) must never throw -- " +
                "covering the announcement dispatch, the Providence EventAcidStorm voice line, the " +
                "broadcast screen-fx sting, and the acid-mote scatter across whichever station is chosen.");
        });

        // Let the rule's Started callback and any queued events/spawns actually run.
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.IsGameRuleActive<SolreignAcidStormRuleComponent>(), Is.True,
                "With the CVar on, the rule must actually be running (not immediately force-ended) -- " +
                "this is the positive-path counterpart to the CVar-off test below.");
        });
    }

    [Test]
    public async Task StartGameRule_CvarDisabled_ForceEndsImmediately_NeverRunsInert()
    {
        var server = Server;
        var ticker = server.System<GameTicker>();

        server.CfgMan.SetCVar(CCVars.SolreignAcidStormEnabled, false);
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.DoesNotThrow(() => ticker.StartGameRule(SolreignAcidStormRule.EventPrototypeId),
                "Starting the rule while the CVar is off must never throw -- ForceEndSelf is expected " +
                "to gracefully end it, not crash.");
        });

        // Let Started() run (and ForceEndSelf take effect) before checking rule state.
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.IsGameRuleActive<SolreignAcidStormRuleComponent>(), Is.False,
                "solreign.events.acid_storm=false must be a full kill switch: an admin-started Acid " +
                "Storm must immediately force-end itself rather than silently running inert (no " +
                "announcement effects, no scatter) for its full 60-120s duration -- ForceEndSelf inside " +
                "Started() is the mechanism under test here.");
        });

        // Restore for pool hygiene even though this fixture is Dirty (defensive, cheap).
        server.CfgMan.SetCVar(CCVars.SolreignAcidStormEnabled, true);
    }
}
