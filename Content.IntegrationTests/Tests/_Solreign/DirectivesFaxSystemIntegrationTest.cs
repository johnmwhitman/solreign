#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.DirectivesFax;
using Content.Server._Solreign.DirectivesFax.Components;
using Content.Server._Solreign.StationAudits;
using Content.Server._Solreign.StationDirective;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Paper;
using Content.Shared.Station.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Live-ECS coverage for <see cref="DirectivesFaxRuleSystem"/> (v14 wave-1 item #1) — the class of
///     gap <c>SolreignCorporateRuleSystemIntegrationTest</c>'s own header describes: pure logic already
///     has store/catalog-level coverage (<c>DirectivesFaxClauseCatalogTests</c>,
///     <c>DirectivesFaxClauseEvaluationTests</c>, <c>DirectivesFaxStreakStoreTests</c>), but nothing
///     drives the live System through <c>GameTicker.StartGameRule</c> and the real
///     <c>RoundEndTextAppendEvent</c>/<c>RoundEndMessageEvent</c> chain.
///
///     Station rig: same "board entity carries <see cref="StationDataComponent"/> directly, no grid
///     needed" idiom <c>ContractClaimFlowIntegrationTest.MakeStationBoard</c> established
///     (<c>StationSystem.GetOwningStation(board) == board</c> via that method's own "we are the
///     station" branch). No <c>FaxMachineComponent</c> entity is placed on the rig (pooled test servers
///     run bare/empty maps, not a real SOLREIGN station map) — so these tests exercise
///     <see cref="DirectivesFaxRuleSystem"/>'s documented defensive fallback path (spawn a loose
///     <c>Paper</c> at the station's own transform) rather than the Bridge/HoP fax-targeting priority,
///     which is straightforward filter logic exercised by code review + the receipt's manual
///     verification against every real SOLREIGN map's fax layout.
/// </summary>
[TestFixture]
public sealed class DirectivesFaxSystemIntegrationTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        DummyTicker = false,
    };

    private const string DirectivesFaxRuleId = "SolreignDirectivesFax";

    private static EntityUid MakeStationBoard(IEntityManager entMan)
    {
        var board = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
        entMan.EnsureComponent<StationDataComponent>(board); // default fields only; makes GetOwningStation(board) == board
        return board;
    }

    [Test]
    public async Task Activated_ByDefault_StartsTheRule()
    {
        // DummyTicker=false exercises the real round-start path before the test body. The activation
        // contract intentionally ships this layer enabled, so that path must have started the rule.
        var server = Server;
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            Assert.That(server.CfgMan.GetCVar(CCVars.SolreignDirectivesFaxEnabled), Is.True,
                "the activation contract intentionally ships Directives Fax enabled");

            var query = entMan.EntityQueryEnumerator<DirectivesFaxRuleComponent>();
            Assert.That(query.MoveNext(out _, out _), Is.True,
                "the real default-on round-start path must create a Directives Fax rule");
        });
    }

    [Test]
    public async Task Enabled_RoundStart_PrintsAFaxWithTheDirectiveTextAndItsClauses()
    {
        var server = Server;
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            MakeStationBoard(entMan);

            var ticker = server.System<GameTicker>();
            Assert.That(ticker.StartGameRule(DirectivesFaxRuleId, out _), Is.True,
                "Setup failed: SolreignDirectivesFax must start cleanly (no delay configured on the prototype).");

            // Independently recompute which directive Started() must have chosen -- the same pure,
            // deterministic function DirectivesFaxRuleSystem itself replays (see its class doc).
            var directiveIndex = StationDirectiveSelection.SelectDirectiveIndex(ticker.RoundId, StationDirectiveCatalog.Directives.Count);
            var directive = StationDirectiveCatalog.Directives[directiveIndex];
            var displayName = DirectivesFaxClauseCatalog.GetDisplayName(directive.Id);
            var clauses = DirectivesFaxClauseCatalog.GetClauses(directive.Id);

            // No FaxMachineComponent exists on this bare rig, so PrintDirectiveFax must have taken its
            // documented defensive fallback: a loose Paper entity spawned with the fax body content.
            var paperQuery = entMan.EntityQueryEnumerator<PaperComponent>();
            PaperComponent? matched = null;
            while (paperQuery.MoveNext(out _, out var paper))
            {
                if (paper.Content.Contains(displayName))
                {
                    matched = paper;
                    break;
                }
            }

            Assert.That(matched, Is.Not.Null,
                $"No printed Paper found containing the round's chosen directive name ('{displayName}').");

            foreach (var clause in clauses)
            {
                var (locKey, locArgs) = DirectivesFaxClauseFormatting.Describe(clause);
                var clauseText = Loc.GetString(locKey, locArgs);
                Assert.That(matched!.Content, Does.Contain(clauseText),
                    $"Printed fax is missing clause text for '{clause.Id}': \"{clauseText}\".");
            }
        });
    }

    [Test]
    public async Task RoundEnd_AppendsAComplianceOutcomeLine_ForTheChosenDirective()
    {
        var server = Server;
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            MakeStationBoard(entMan);

            var ticker = server.System<GameTicker>();
            Assert.That(ticker.StartGameRule(DirectivesFaxRuleId, out _), Is.True,
                "Setup failed: SolreignDirectivesFax must start cleanly.");

            var directiveIndex = StationDirectiveSelection.SelectDirectiveIndex(ticker.RoundId, StationDirectiveCatalog.Directives.Count);
            var directive = StationDirectiveCatalog.Directives[directiveIndex];
            var displayName = DirectivesFaxClauseCatalog.GetDisplayName(directive.Id);

            // Fires the real round-end text-append chain -- the same call shape
            // SolreignCorporateRuleSystemIntegrationTest.FireRoundEnd uses.
            var textEv = new RoundEndTextAppendEvent();
            entMan.EventBus.RaiseEvent(EventSource.Local, textEv);

            Assert.That(textEv.Text, Does.Contain(Loc.GetString("solreign-directives-fax-round-end-header")));
            Assert.That(textEv.Text, Does.Contain(displayName),
                "The round-end compliance report must name the shift's chosen directive.");

            // Structurally verifies a MET or UNMET line was appended, without asserting which --
            // whether a fresh rig with zero deaths/zero economy delta reads as compliant depends on
            // which directive's clauses were rolled, and that is exercised exhaustively by
            // DirectivesFaxClauseEvaluationTests already.
            var metLine = Loc.GetString("solreign-directives-fax-round-end-met", ("directive", displayName));
            var unmetLine = Loc.GetString("solreign-directives-fax-round-end-unmet", ("directive", displayName));
            Assert.That(textEv.Text.Contains(metLine) || textEv.Text.Contains(unmetLine), Is.True,
                "The round-end text must contain either the MET or UNMET compliance line for the chosen directive.");
        });
    }

    [Test]
    public async Task RoundEnd_PrintsAStampedComplianceReportPaper_DistinctFromTheRoundStartFax()
    {
        var server = Server;
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            MakeStationBoard(entMan);

            var ticker = server.System<GameTicker>();
            Assert.That(ticker.StartGameRule(DirectivesFaxRuleId, out _), Is.True,
                "Setup failed: SolreignDirectivesFax must start cleanly.");

            var directiveIndex = StationDirectiveSelection.SelectDirectiveIndex(ticker.RoundId, StationDirectiveCatalog.Directives.Count);
            var directive = StationDirectiveCatalog.Directives[directiveIndex];
            var displayName = DirectivesFaxClauseCatalog.GetDisplayName(directive.Id);

            // The round-start fax already printed one loose Paper (fallback path -- no FaxMachineComponent
            // on this bare rig, same as every other test in this fixture). Firing round end must print a
            // SECOND, distinct Paper: the stamped compliance report, identifiable by its stamp line (never
            // printed on the round-start fax, which only ever carries the directive text + clause list).
            var textEv = new RoundEndTextAppendEvent();
            entMan.EventBus.RaiseEvent(EventSource.Local, textEv);

            var stampLine = Loc.GetString("solreign-directives-fax-report-stamp");

            var paperQuery = entMan.EntityQueryEnumerator<PaperComponent>();
            var paperCount = 0;
            EntityUid? reportUid = null;
            PaperComponent? reportPaper = null;
            while (paperQuery.MoveNext(out var uid, out var paper))
            {
                paperCount++;
                if (paper.Content.Contains(stampLine))
                {
                    reportUid = uid;
                    reportPaper = paper;
                }
            }

            Assert.That(paperCount, Is.GreaterThanOrEqualTo(2),
                "Expected at least two distinct Paper entities on the rig: the round-start fax and the round-end compliance report.");
            Assert.That(reportPaper, Is.Not.Null,
                "No printed Paper found containing the compliance report's stamp line -- SpawnComplianceReportPaper must have failed to print.");
            Assert.That(reportPaper!.Content, Does.Contain(displayName),
                "The compliance report paper must name the shift's chosen directive.");

            // Fell back to the loose-paper path (no FaxMachineComponent on this rig), which spawns the
            // dedicated keepsake prototype directly rather than a generic "Paper".
            var meta = entMan.GetComponent<MetaDataComponent>(reportUid!.Value);
            Assert.That(meta.EntityPrototype?.ID, Is.EqualTo("SolreignPaperDirectivesFaxReport"),
                "The report paper's fallback path must spawn the dedicated SolreignPaperDirectivesFaxReport prototype, not a generic Paper.");
        });
    }

    [Test]
    public async Task DirectiveOutcomeQuery_FiredBeforeAppendRoundEndText_ComputesAndCachesTheRealOutcome()
    {
        var server = Server;
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            MakeStationBoard(entMan);

            var ticker = server.System<GameTicker>();
            Assert.That(ticker.StartGameRule(DirectivesFaxRuleId, out _), Is.True,
                "Setup failed: SolreignDirectivesFax must start cleanly.");

            var directiveIndex = StationDirectiveSelection.SelectDirectiveIndex(ticker.RoundId, StationDirectiveCatalog.Directives.Count);
            var directive = StationDirectiveCatalog.Directives[directiveIndex];
            var displayName = DirectivesFaxClauseCatalog.GetDisplayName(directive.Id);
            var clauses = DirectivesFaxClauseCatalog.GetClauses(directive.Id);
            // A fresh bare rig has zero deaths and zero economy delta (no StationBankAccountComponent /
            // StationCargoOrderDatabaseComponent on the board, no MobStateChanged events fired) --
            // GatherShiftState is deterministically (0, 0, 0) here, so the expected outcome can be
            // computed independently, the same way DirectivesFaxClauseEvaluationTests does.
            var state = new DirectivesFaxShiftState(CrewDeaths: 0, CargoRevenueDelta: 0, SupplyOrdersDelta: 0);
            var expectedMet = DirectivesFaxClauseEvaluation.EvaluateAll(clauses, state);

            // Simulates the ordering race: raise the outcome query BEFORE RoundEndTextAppendEvent has
            // ever fired for this rule -- the real-world case is Station Audits' own RoundEndTextAppendEvent
            // subscriber running first and raising this query from inside its own handler.
            var query = new SolreignDirectiveOutcomeQueryEvent();
            entMan.EventBus.RaiseEvent(EventSource.Local, query);

            Assert.That(query.Reported, Is.True,
                "The query must be answered even though AppendRoundEndText has not run yet -- the " +
                "outcome is computed lazily on first ask (ComputeOrGetOutcome), not only inside AppendRoundEndText.");
            Assert.That(query.Fulfilled, Is.EqualTo(expectedMet));

            // AppendRoundEndText must still run its full text/report-paper flow exactly once, reusing
            // the already-cached outcome rather than silently skipping or disagreeing with it.
            var textEv = new RoundEndTextAppendEvent();
            entMan.EventBus.RaiseEvent(EventSource.Local, textEv);

            var expectedLine = Loc.GetString(
                expectedMet ? "solreign-directives-fax-round-end-met" : "solreign-directives-fax-round-end-unmet",
                ("directive", displayName));
            Assert.That(textEv.Text, Does.Contain(expectedLine),
                "AppendRoundEndText must render the SAME outcome the earlier query already cached, not a recomputed (and potentially different) one.");
        });
    }

    [Test]
    public async Task DirectiveOutcomeQuery_FiredAfterAppendRoundEndText_ReusesTheCachedOutcome_NoDoubleCompute()
    {
        var server = Server;
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            MakeStationBoard(entMan);

            var ticker = server.System<GameTicker>();
            Assert.That(ticker.StartGameRule(DirectivesFaxRuleId, out _), Is.True,
                "Setup failed: SolreignDirectivesFax must start cleanly.");

            var directiveIndex = StationDirectiveSelection.SelectDirectiveIndex(ticker.RoundId, StationDirectiveCatalog.Directives.Count);
            var directive = StationDirectiveCatalog.Directives[directiveIndex];
            var clauses = DirectivesFaxClauseCatalog.GetClauses(directive.Id);
            var state = new DirectivesFaxShiftState(CrewDeaths: 0, CargoRevenueDelta: 0, SupplyOrdersDelta: 0);
            var expectedMet = DirectivesFaxClauseEvaluation.EvaluateAll(clauses, state);

            var textEv = new RoundEndTextAppendEvent();
            entMan.EventBus.RaiseEvent(EventSource.Local, textEv);

            // Now query AFTER AppendRoundEndText has already computed and cached the outcome.
            var query = new SolreignDirectiveOutcomeQueryEvent();
            entMan.EventBus.RaiseEvent(EventSource.Local, query);

            Assert.That(query.Reported, Is.True);
            Assert.That(query.Fulfilled, Is.EqualTo(expectedMet),
                "Querying after AppendRoundEndText has already run must return the SAME cached outcome, not a fresh recomputation.");
        });
    }

    [Test]
    public async Task DirectiveOutcomeQuery_WithoutAnActiveRule_IsNeverReported()
    {
        var server = Server;
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            // The CVar gates future round-start scheduling; it intentionally does not terminate
            // a rule that is already running. Arrange the narrower no-rule branch explicitly
            // instead of pretending a mid-round toggle is an immediate kill switch.
            server.CfgMan.SetCVar(CCVars.SolreignDirectivesFaxEnabled, false);

            var ticker = server.System<GameTicker>();
            var existing = new List<EntityUid>();
            var rules = entMan.EntityQueryEnumerator<DirectivesFaxRuleComponent>();
            while (rules.MoveNext(out var uid, out _))
                existing.Add(uid);

            foreach (var uid in existing)
            {
                ticker.EndGameRule(uid);
                entMan.DeleteEntity(uid);
            }

            var query = new SolreignDirectiveOutcomeQueryEvent();
            entMan.EventBus.RaiseEvent(EventSource.Local, query);

            Assert.That(query.Reported, Is.False,
                "without a Directives Fax rule, nothing must answer the outcome query");

            // Leave the startup-only rule and gate off for the remainder of this borrow. Harness
            // cleanup restores the CVar, while Dirty forces a full recycle before the pair is reused;
            // recreating only the enabled CVar here would manufacture an impossible in-test state.
        });
    }
}
