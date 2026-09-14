#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.SeasonLedger;
using Content.Server._Solreign.StationAudits;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Shared.CCVar;
using Content.Shared.Paper;
using NUnit.Framework;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Utility;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Station Audits (v14 wave-1 #3) ECS wiring: enabled=false must be zero-behavior (nothing
///     appended to the round-end screen, no paper, no ledger row), enabled=true must produce all
///     three on a real round-end handoff. Same "raise the real <c>RoundEndTextAppendEvent</c> on the
///     real event bus" idiom as <c>SolreignCorporateRuleSystemIntegrationTest</c> /
///     <c>SeasonLedgerSystemIntegrationTest</c> — this fork's established meaning of "a real round
///     end" in a test (real handlers, real event bus, no multi-minute shuttle wait).
///
///     Pure assembly/composition (every section present/absent, empty-shift honesty, Item of Concern
///     determinism) is exhaustively unit-tested without a server in
///     <c>Content.Tests/_Solreign/StationAuditComposerTests.cs</c> — this file only covers the ECS
///     wiring those tests cannot reach: the CVar gate, the paper spawn, and the ledger append.
/// </summary>
[TestFixture]
public sealed class StationAuditSystemIntegrationTest : GameTest
{
    // Dirty: raises a synthetic RoundEndTextAppendEvent on the real bus, spawns a comms console +
    // audit paper outside GameTest's entity-cleanup tracking, and writes directly to the on-disk
    // ledger DB — same "Dirty=true" precedent as SeasonLedgerSystemIntegrationTest /
    // SolreignCorporateRuleSystemIntegrationTest for exactly this class of side effect.
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        DummyTicker = false,
    };

    private string ResolveLedgerDbPath()
    {
        var server = Server;
        return SeasonLedgerDbPath.Resolve(
            server.ResolveDependency<IConfigurationManager>(),
            server.ResolveDependency<IResourceManager>());
    }

    private static async Task PollUntilAsync(
        RobustIntegrationTest.ServerIntegrationInstance server,
        Func<Task<bool>> predicate,
        string failureMessage,
        int maxAttempts = 150)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (await predicate())
                return;

            await server.WaitRunTicks(1);
        }

        Assert.Fail(failureMessage);
    }

    private static bool TryFindAuditPaper(IEntityManager entMan, out string content)
    {
        var query = entMan.EntityQueryEnumerator<PaperComponent, MetaDataComponent>();
        while (query.MoveNext(out _, out var paper, out var meta))
        {
            if (meta.EntityPrototype?.ID == "SolreignPaperStationAudit")
            {
                content = paper.Content;
                return true;
            }
        }

        content = string.Empty;
        return false;
    }

    [Test]
    public async Task Disabled_ProducesZeroBehavior()
    {
        var server = Server;
        var entMan = server.EntMan;
        var config = server.ResolveDependency<IConfigurationManager>();
        var dbPath = ResolveLedgerDbPath();

        await server.WaitPost(() => _ = server.System<SeasonLedgerSystem>());
        await server.WaitPost(() => _ = server.System<StationAuditSystem>());

        var textEv = new RoundEndTextAppendEvent();
        var roundId = 0;
        await server.WaitPost(() =>
        {
            config.SetCVar(CCVars.SolreignStationAuditEnabled, false);
            entMan.SpawnEntity("ComputerComms", MapCoordinates.Nullspace);
            roundId = server.System<GameTicker>().RoundId;
            entMan.EventBus.RaiseEvent(EventSource.Local, textEv);
        });

        // Give any (incorrectly-fired) async persistence a few ticks to land before asserting absence.
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            // RoundEndTextAppendEvent is a shared broadcast — other always-on Solreign rules
            // (Corporate Ladder, Station Directive) also append their own lines to the same Text,
            // so "disabled" is scoped to THIS system never contributing its own section, not the
            // whole event being empty.
            Assert.That(textEv.Text, Does.Not.Contain("PROVIDENCE STATION AUDIT"),
                "disabled must append nothing to the round-end screen");
            Assert.That(TryFindAuditPaper(entMan, out _), Is.False, "disabled must never print the keepsake");
        });

        var store = new SeasonLedgerStore(dbPath);
        var rows = await store.GetRecentStationAuditsAsync();
        Assert.That(rows.Any(row => row.RoundId == roundId), Is.False,
            "disabled must never append a ledger row for the current round");
    }

    [Test]
    public async Task Enabled_RealRoundEnd_AppendsTextPrintsPaperAndPersistsLedgerRow()
    {
        var server = Server;
        var entMan = server.EntMan;
        var config = server.ResolveDependency<IConfigurationManager>();
        var dbPath = ResolveLedgerDbPath();

        await server.WaitPost(() => _ = server.System<SeasonLedgerSystem>());
        await server.WaitPost(() => _ = server.System<StationAuditSystem>());

        var textEv = new RoundEndTextAppendEvent();
        var roundId = 0;
        await server.WaitPost(() =>
        {
            config.SetCVar(CCVars.SolreignStationAuditEnabled, true);
            entMan.SpawnEntity("ComputerComms", MapCoordinates.Nullspace);

            roundId = server.System<GameTicker>().RoundId;
            entMan.EventBus.RaiseEvent(EventSource.Local, textEv);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(textEv.Text, Does.Contain("PROVIDENCE STATION AUDIT"),
                "enabled must append the audit to the round-end screen");
            Assert.DoesNotThrow(() => FormattedMessage.FromMarkupOrThrow(textEv.Text),
                "every Station Audit line appended to the rich-text round-end screen must be valid markup");

            Assert.That(TryFindAuditPaper(entMan, out var content), Is.True,
                "enabled must print the keepsake paper at the comms console");
            Assert.That(content, Does.Contain("PROVIDENCE STATION AUDIT"));
        });

        var store = new SeasonLedgerStore(dbPath);
        await PollUntilAsync(
            server,
            async () => (await store.GetRecentStationAuditsAsync()).Any(r => r.RoundId == roundId),
            "Station Audit ledger row for this round never landed.");
    }

    // --- Inspection layer / mandatory PROVIDENCE consequence (v14 gap-closure pass) -----------------
    //
    // Pure decision logic (grading, selection, the pass/fail/NA -> consequence-kind reduction) is
    // exhaustively covered without a server in Content.Tests/_Solreign/StationAuditCriterionGradingTests
    // and StationAuditInspectionSelectionTests. This section only covers the ECS wiring those tests
    // cannot reach: the CVar gate, round-start assignment + checkpoint scheduling, the checkpoint firing
    // path, the round-end fallback, and the real mechanical side effect of a Commendation (HR Points
    // ledger delta). StationAuditSystem's internal test seams (ForceCheckpointFireForTests,
    // FireConsequenceForTests, etc.) let these tests drive specific consequence kinds deterministically
    // without depending on which criteria a real round-id-driven assignment happens to pick.

    [Test]
    public async Task InspectionDisabled_RoundStart_SchedulesNoCheckpoint()
    {
        var server = Server;
        var config = server.ResolveDependency<IConfigurationManager>();

        await server.WaitPost(() =>
        {
            config.SetCVar(CCVars.SolreignStationAuditInspectionEnabled, false);
            var system = server.System<StationAuditSystem>();
            system.SimulateRoundStartForTests();

            Assert.That(system.CheckpointScheduledForTests, Is.False,
                "disabled inspection layer must never schedule a checkpoint");
            Assert.That(system.AssignedCriteriaCountForTests, Is.EqualTo(0),
                "disabled inspection layer must never assign criteria");
        });
    }

    [Test]
    public async Task InspectionEnabled_RoundStart_AssignsCriteriaAndSchedulesCheckpoint()
    {
        var server = Server;
        var config = server.ResolveDependency<IConfigurationManager>();

        await server.WaitPost(() =>
        {
            config.SetCVar(CCVars.SolreignStationAuditInspectionEnabled, true);
            config.SetCVar(CCVars.SolreignStationAuditInspectionCount, 2);
            var system = server.System<StationAuditSystem>();
            system.SimulateRoundStartForTests();

            Assert.That(system.CheckpointScheduledForTests, Is.True,
                "enabled inspection layer must schedule a checkpoint at round start");
            Assert.That(system.AssignedCriteriaCountForTests, Is.InRange(1, 6),
                "assignment must pick at least one, at most the whole catalog's worth of criteria");
            Assert.That(system.CheckpointFiredForTests, Is.False,
                "the checkpoint must not have fired yet — only scheduled");
        });
    }

    [Test]
    public async Task Checkpoint_ForcedFire_FiresExactlyOnce_EvenIfForcedTwice()
    {
        var server = Server;
        var config = server.ResolveDependency<IConfigurationManager>();

        await server.WaitPost(() =>
        {
            config.SetCVar(CCVars.SolreignStationAuditInspectionEnabled, true);
            var system = server.System<StationAuditSystem>();
            system.SimulateRoundStartForTests();

            system.ForceCheckpointFireForTests();
            Assert.That(system.CheckpointFiredForTests, Is.True);
            var kindAfterFirstFire = system.CheckpointConsequenceKindForTests;

            // Idempotent guard: a second force-fire (mirrors Update() racing the round-end fallback in
            // the same tick) must not re-fire or change the recorded kind.
            system.ForceCheckpointFireForTests();
            Assert.That(system.CheckpointConsequenceKindForTests, Is.EqualTo(kindAfterFirstFire));
        });
    }

    [Test]
    public async Task RoundEndFallback_FiresTheCheckpoint_WhenTheRoundEndsBeforeTheTimerElapses()
    {
        var server = Server;
        var entMan = server.EntMan;
        var config = server.ResolveDependency<IConfigurationManager>();

        await server.WaitPost(() =>
        {
            config.SetCVar(CCVars.SolreignStationAuditEnabled, true);
            config.SetCVar(CCVars.SolreignStationAuditInspectionEnabled, true);
            // A checkpoint mark far beyond any real test's runtime — the round-end fallback, not the
            // Update() timer, must be what fires it.
            config.SetCVar(CCVars.SolreignStationAuditCheckpointMinutes, 999);
            entMan.SpawnEntity("ComputerComms", MapCoordinates.Nullspace);

            var system = server.System<StationAuditSystem>();
            system.SimulateRoundStartForTests();

            Assert.That(system.CheckpointFiredForTests, Is.False, "must not have fired yet — the mark is far in the future");

            entMan.EventBus.RaiseEvent(EventSource.Local, new RoundEndTextAppendEvent());

            Assert.That(system.CheckpointFiredForTests, Is.True,
                "the round-end fallback must fire the checkpoint inline when the round ends first");
            Assert.That(system.CheckpointFiredViaRoundEndFallbackForTests, Is.True);
        });
    }

    [Test]
    public async Task RoundEndFallback_FiresEvenWhenTheBaseReportIsDisabled()
    {
        // The mandatory consequence must not silently depend on whether the base Station Audit
        // report itself is enabled — SolreignStationAuditInspectionEnabled is documented as
        // independent of SolreignStationAuditEnabled.
        var server = Server;
        var entMan = server.EntMan;
        var config = server.ResolveDependency<IConfigurationManager>();

        await server.WaitPost(() =>
        {
            config.SetCVar(CCVars.SolreignStationAuditEnabled, false);
            config.SetCVar(CCVars.SolreignStationAuditInspectionEnabled, true);
            config.SetCVar(CCVars.SolreignStationAuditCheckpointMinutes, 999);

            var system = server.System<StationAuditSystem>();
            system.SimulateRoundStartForTests();

            var textEv = new RoundEndTextAppendEvent();
            entMan.EventBus.RaiseEvent(EventSource.Local, textEv);

            Assert.That(system.CheckpointFiredForTests, Is.True,
                "the consequence layer must fire regardless of the base report's own CVar");
            Assert.That(textEv.Text, Does.Not.Contain("PROVIDENCE STATION AUDIT"),
                "the base report itself must still render nothing while its own CVar is off");
        });
    }

    [Test]
    public async Task RenderedReport_IncludesInspectionSection_WhenACheckpointHasFired()
    {
        var server = Server;
        var entMan = server.EntMan;
        var config = server.ResolveDependency<IConfigurationManager>();

        await server.WaitPost(() =>
        {
            config.SetCVar(CCVars.SolreignStationAuditEnabled, true);
            config.SetCVar(CCVars.SolreignStationAuditInspectionEnabled, true);
            entMan.SpawnEntity("ComputerComms", MapCoordinates.Nullspace);

            var system = server.System<StationAuditSystem>();
            system.SimulateRoundStartForTests();
            system.ForceCheckpointFireForTests();

            var textEv = new RoundEndTextAppendEvent();
            entMan.EventBus.RaiseEvent(EventSource.Local, textEv);

            Assert.That(textEv.Text, Does.Contain("[PROVIDENCE INSPECTION"),
                "a fired checkpoint must render the inspection section on the round-end report");
            Assert.DoesNotThrow(() => FormattedMessage.FromMarkupOrThrow(textEv.Text),
                "the explicitly rendered inspection section must remain valid round-end markup");
        });
    }

    [Test]
    public async Task Commendation_PaysRealHrPoints_ViaTheAlreadyShippedLedgerMethod()
    {
        var server = Server;
        var dbPath = ResolveLedgerDbPath();
        var user = Guid.NewGuid();

        await server.WaitPost(() => _ = server.System<SeasonLedgerSystem>());

        var configuredPoints = 0;
        await server.WaitPost(() =>
        {
            var config = server.ResolveDependency<IConfigurationManager>();
            config.SetCVar(CCVars.SolreignStationAuditInspectionEnabled, true);
            configuredPoints = config.GetCVar(CCVars.SolreignStationAuditCommendationHrPoints);

            var system = server.System<StationAuditSystem>();
            system.SetCommendationGuidForTests(user);
            system.FireConsequenceForTests(StationAuditConsequenceKind.Commendation);
        });

        Assert.That(configuredPoints, Is.GreaterThan(0), "test assumes the default commendation payout is positive");

        var store = new SeasonLedgerStore(dbPath);
        await PollUntilAsync(
            server,
            async () => (await store.GetCareerStatsAsync(user)).HrPoints >= configuredPoints,
            "Commendation consequence never paid the expected HR Points bonus.");
    }

    [Test]
    public async Task Commendation_NoResolvedAccount_PaysNothing_ButStillFiresWithoutThrowing()
    {
        // Spec-documented degrade rule: with no commendation account resolved yet (e.g. a checkpoint
        // firing very early in the shift), the tone still fires but no HR Points target exists — this
        // must never throw or invent a payout target.
        var server = Server;

        await server.WaitPost(() =>
        {
            var config = server.ResolveDependency<IConfigurationManager>();
            config.SetCVar(CCVars.SolreignStationAuditInspectionEnabled, true);

            var system = server.System<StationAuditSystem>();
            system.SetCommendationGuidForTests(null);

            Assert.DoesNotThrow(() => system.FireConsequenceForTests(StationAuditConsequenceKind.Commendation));
        });
    }

    [Test]
    public async Task Escalation_FiresWithoutThrowing()
    {
        // Escalation's mechanical side effect (SolreignScreenFxEvent) is the same already-shipped,
        // engine-clamped primitive StationDirectiveRuleSystem/SolreignSolarFlareRule already raise —
        // this test proves the wiring doesn't throw; the primitive itself is covered by its own
        // existing tests (SolreignScreenFxTimingTests).
        var server = Server;

        await server.WaitPost(() =>
        {
            var config = server.ResolveDependency<IConfigurationManager>();
            config.SetCVar(CCVars.SolreignStationAuditInspectionEnabled, true);

            var system = server.System<StationAuditSystem>();
            Assert.DoesNotThrow(() => system.FireConsequenceForTests(StationAuditConsequenceKind.Escalation));
        });
    }

    [Test]
    public async Task QuietlyCorrectedError_FiresWithoutThrowing_AndAwardsNoHrPoints()
    {
        var server = Server;
        var dbPath = ResolveLedgerDbPath();
        var user = Guid.NewGuid();

        await server.WaitPost(() => _ = server.System<SeasonLedgerSystem>());

        await server.WaitPost(() =>
        {
            var config = server.ResolveDependency<IConfigurationManager>();
            config.SetCVar(CCVars.SolreignStationAuditInspectionEnabled, true);

            var system = server.System<StationAuditSystem>();
            system.SetCommendationGuidForTests(user);

            Assert.DoesNotThrow(() => system.FireConsequenceForTests(StationAuditConsequenceKind.QuietlyCorrectedError));
        });

        // Give any (incorrectly-fired) async payout a few ticks to land before asserting absence.
        await server.WaitRunTicks(5);

        var store = new SeasonLedgerStore(dbPath);
        var stats = await store.GetCareerStatsAsync(user);
        Assert.That(stats.HrPoints, Is.EqualTo(0), "QuietlyCorrectedError must be PA/ledger-only, never a mechanical payout");
    }

    [Test]
    public async Task LedgerRow_PersistsInspectionColumns_WhenACheckpointHasFired()
    {
        var server = Server;
        var entMan = server.EntMan;
        var dbPath = ResolveLedgerDbPath();

        await server.WaitPost(() => _ = server.System<SeasonLedgerSystem>());
        await server.WaitPost(() => _ = server.System<StationAuditSystem>());

        var roundId = 0;
        await server.WaitPost(() =>
        {
            var config = server.ResolveDependency<IConfigurationManager>();
            config.SetCVar(CCVars.SolreignStationAuditEnabled, true);
            config.SetCVar(CCVars.SolreignStationAuditInspectionEnabled, true);
            entMan.SpawnEntity("ComputerComms", MapCoordinates.Nullspace);

            var system = server.System<StationAuditSystem>();
            system.SimulateRoundStartForTests();
            system.ForceCheckpointFireForTests();

            roundId = server.System<GameTicker>().RoundId;
            entMan.EventBus.RaiseEvent(EventSource.Local, new RoundEndTextAppendEvent());
        });

        var store = new SeasonLedgerStore(dbPath);
        await PollUntilAsync(
            server,
            async () =>
            {
                var rows = await store.GetRecentStationAuditsAsync();
                var row = rows.FirstOrDefault(r => r.RoundId == roundId);
                return row is not null && (row.CheckpointConsequenceKind != "none" || row.CriteriaJson != "[]");
            },
            "Station Audit ledger row never persisted the inspection layer's columns.");
    }
}
