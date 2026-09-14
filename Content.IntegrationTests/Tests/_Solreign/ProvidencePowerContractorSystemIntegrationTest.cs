#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.PowerContractor;
using Content.Server.Afk;
using Content.Server.GameTicking;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Power.Pow3r;
using Content.Server.Roles.Jobs;
using Content.Server.Station.Systems;
using Content.Shared.CCVar;
using Content.Shared.Coordinates;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Power.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Engine-level coverage for <see cref="ProvidencePowerContractorSystem"/'s deterministic core
///     (SPEC-ai-npc-phase1-v2.md §3-§9). The pure hysteresis/TTL/cooldown decision math is covered
///     separately and exhaustively in <c>Content.Tests/_Solreign/ProvidencePowerContractorGateTests.cs</c>
///     -- these tests exist to prove the REAL engine wiring around that math: the Pow3r solver
///     actually reads a live network's statistics, the contractor's PowerSupplierComponent actually
///     joins/leaves <c>network.Supplies</c> through standard node-group connectivity (not a manual
///     override), spawn validation actually calls SpawnIfUnobstructed, and destruction/round-restart
///     actually reach the safety-contract cleanup paths.
///
///     Monitor cadence discipline: every test sets
///     <see cref="CCVars.SolreignPowerContractorMonitorIntervalSeconds"/> to exactly
///     <see cref="MonitorIntervalSeconds"/> (1.0s) and steps time forward in
///     <see cref="TicksPerMonitorInterval"/>-tick blocks (31 ticks at this server's 30 TPS -- just
///     over one full interval, comfortably under two), so each <c>StepOneMonitorInterval</c> call
///     crosses EXACTLY one monitor sample boundary. This is deliberate: with a monitor interval
///     shorter than or comparable to the physics tick period, a single <c>RunTicks</c> call can cross
///     several sample boundaries at once, which silently defeats hysteresis-boundary assertions like
///     "must not dispatch on the very first below-floor sample."
///
///     The feature's master CVar is set to true INSIDE these tests only, scoped to each test's own
///     dirty, throwaway server instance (<c>PoolSettings.Dirty = true</c>) -- this never touches a
///     shared/production server and satisfies the build directive's "never flip the enable CVar on"
///     rule (that rule is about not enabling the feature on any real server, not about being unable
///     to test it at all).
///
///     Post-DO-NOT-LAND adversarial-review pass (7 activation-blocking defects): every new test below
///     is tagged with the review finding number it proves, and adds real station scaffolding
///     (<c>NpcContractorTestStation</c>) since discovery is now station-scoped (finding #3) -- every
///     pre-existing test's grid is now added to a station via <see cref="SetUpStation"/>/
///     <see cref="BuildFailingNetwork"/> for exactly that reason.
/// </summary>
[TestFixture]
public sealed class ProvidencePowerContractorSystemIntegrationTest : GameTest
{
    private const float MonitorIntervalSeconds = 1.0f;

    /// <summary>Ticks per <see cref="StepOneMonitorInterval"/> step. This server runs at 30 TPS
    /// (33.33ms/tick); 31 ticks = ~1.033s, safely past one 1.0s interval and safely short of two.</summary>
    private const int TicksPerMonitorInterval = 31;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: NpcContractorConsumerDummy
  components:
  - type: Transform
    anchored: true
  - type: NodeContainer
    nodes:
      input:
        !type:CableDeviceNode
        nodeGroupID: HVPower
  - type: PowerConsumer

- type: entity
  id: NpcContractorTestStation
  parent: BaseStation
";

    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        // DummyTicker=false so the connected session gets a real attached body+mind (same precedent
        // as SeasonLedgerSystemIntegrationTest) -- required for the human-coverage half of the
        // acceptance test.
        DummyTicker = false,
    };

    private static int CountLiveContractors(IEntityManager entMan)
    {
        var query = entMan.EntityQueryEnumerator<ProvidencePowerContractorComponent>();
        var count = 0;

        while (query.MoveNext(out _))
            count++;

        return count;
    }

    private static List<EntityUid> GetLiveContractorUids(IEntityManager entMan)
    {
        var query = entMan.EntityQueryEnumerator<ProvidencePowerContractorComponent>();
        var list = new List<EntityUid>();

        while (query.MoveNext(out var uid, out _))
            list.Add(uid);

        return list;
    }

    /// <summary>Review finding #3: discovery is now station-scoped, so every test grid needs a real
    /// station. Spawns a bare <c>NpcContractorTestStation</c> (parented to the engine's own
    /// <c>BaseStation</c>) and adds <paramref name="grid"/> to it. Must be called from inside a
    /// WaitPost/WaitAssertion block.</summary>
    private static EntityUid SetUpStation(IEntityManager entMan, StationSystem stationSystem, EntityUid grid, string? name = null)
    {
        var station = entMan.SpawnEntity("NpcContractorTestStation", MapCoordinates.Nullspace);
        stationSystem.AddGridToStation(station, grid, null, null, name);
        return station;
    }

    private readonly struct FailingNetwork
    {
        public EntityUid Grid { get; init; }
        public EntityUid Station { get; init; }
        public EntityUid SmesEnt { get; init; }
        public BatteryComponent SmesBattery { get; init; }
        public EntityUid ConsumerEnt { get; init; }
    }

    /// <summary>
    ///     Shared scaffolding for "one station, one failing HV network" tests: a
    ///     <paramref name="lineHalfLength"/>*2+1 tile cable line, an SMES anchor at the middle tile, a
    ///     dummy consumer at the far end, and a real station owning the grid (review finding #3). By
    ///     default the SMES starts already reserve-EXHAUSTED (0 charge, review finding #1) rather than
    ///     the old "30% then drain it" two-step dance most tests don't actually need -- callers that
    ///     specifically want a declining-not-yet-exhausted reserve (the original acceptance walkthrough)
    ///     pass <paramref name="smesInitialCharge"/> explicitly and drain it themselves afterward.
    /// </summary>
    private static FailingNetwork BuildFailingNetwork(
        IEntityManager entMan,
        SharedMapSystem mapSys,
        BatterySystem batterySys,
        StationSystem stationSystem,
        float smesMaxSupply = 500f,
        float smesRampRate = 100000f,
        float consumerDrawRate = 2000f,
        float smesMaxCharge = 100000f,
        float smesInitialCharge = 0f,
        int lineHalfLength = 3,
        string? stationName = null)
    {
        var map = mapSys.CreateMap(out var mapId);
        var grid = mapSys.CreateGridEntity(mapId);

        for (var i = -lineHalfLength; i <= lineHalfLength; i++)
        {
            mapSys.SetTile(grid, new Vector2i(0, i), new Tile(1));
            entMan.SpawnEntity("CableHV", grid.Owner.ToCoordinates(0, i));
        }

        var station = SetUpStation(entMan, stationSystem, grid.Owner, stationName);

        var smesEnt = entMan.SpawnEntity("SMESBasic", grid.Owner.ToCoordinates(0, 0));
        var consumerEnt = entMan.SpawnEntity("NpcContractorConsumerDummy", grid.Owner.ToCoordinates(0, lineHalfLength));

        var smesBattery = entMan.GetComponent<BatteryComponent>(smesEnt);
        var smesNetBattery = entMan.GetComponent<PowerNetworkBatteryComponent>(smesEnt);
        var consumer = entMan.GetComponent<PowerConsumerComponent>(consumerEnt);

        smesNetBattery.MaxSupply = smesMaxSupply;
        smesNetBattery.SupplyRampRate = smesRampRate;
        smesNetBattery.SupplyRampTolerance = smesRampRate;
        batterySys.SetMaxCharge((smesEnt, smesBattery), smesMaxCharge);
        batterySys.SetCharge((smesEnt, smesBattery), smesInitialCharge);
        consumer.DrawRate = consumerDrawRate;

        return new FailingNetwork
        {
            Grid = grid.Owner,
            Station = station,
            SmesEnt = smesEnt,
            SmesBattery = smesBattery,
            ConsumerEnt = consumerEnt,
        };
    }

    /// <summary>Enables the feature with the given tuning and lets exactly the FIRST monitor sample
    /// fire (the one that fires almost immediately, since the system's internal next-sample-time
    /// defaults to zero on a fresh instance).</summary>
    private static async Task EnableAndFireFirstSample(
        RobustIntegrationTest.ServerIntegrationInstance server,
        int hysteresisTicks,
        float leaseWatts = 4000f,
        float leaseTtlSeconds = 300f,
        float redispatchCooldownSeconds = 60f,
        int concurrencyCap = 1,
        int spawnRingRadius = 4,
        float deficitFraction = 0.15f,
        float reserveFloorFraction = 0.20f,
        float minUnmetWatts = 500f)
    {
        var cfg = server.CfgMan;

        await server.WaitPost(() =>
        {
            cfg.SetCVar(CCVars.SolreignPowerContractorEnabled, true);
            cfg.SetCVar(CCVars.SolreignPowerContractorMonitorIntervalSeconds, MonitorIntervalSeconds);
            cfg.SetCVar(CCVars.SolreignPowerContractorHysteresisTicks, hysteresisTicks);
            cfg.SetCVar(CCVars.SolreignPowerContractorDeficitFraction, deficitFraction);
            cfg.SetCVar(CCVars.SolreignPowerContractorReserveFloorFraction, reserveFloorFraction);
            cfg.SetCVar(CCVars.SolreignPowerContractorLeaseWatts, leaseWatts);
            cfg.SetCVar(CCVars.SolreignPowerContractorLeaseTtlSeconds, leaseTtlSeconds);
            cfg.SetCVar(CCVars.SolreignPowerContractorRedispatchCooldownSeconds, redispatchCooldownSeconds);
            cfg.SetCVar(CCVars.SolreignPowerContractorConcurrencyCap, concurrencyCap);
            cfg.SetCVar(CCVars.SolreignPowerContractorSpawnRingRadius, spawnRingRadius);
            cfg.SetCVar(CCVars.SolreignPowerContractorMinUnmetWatts, minUnmetWatts);
        });

        server.RunTicks(1); // fires the first sample (next-sample-time defaults to zero)
    }

    private static void StepOneMonitorInterval(RobustIntegrationTest.ServerIntegrationInstance server)
    {
        server.RunTicks(TicksPerMonitorInterval);
    }

    /// <summary>
    ///     Raising a synthetic <see cref="RoundEndMessageEvent"/> in a throwaway/dirty test server (no
    ///     properly-resolved season-ledger DB path, exactly the landmine
    ///     <c>SeasonLedgerSystemIntegrationTest</c>'s own doc comment documents fighting) makes
    ///     <c>SeasonLedgerSystem.OnRoundEnd</c> -- an entirely unrelated system reacting to the same
    ///     broadcast event this test needs for its OWN purposes -- log an async, swallowed
    ///     <c>StorageFailure</c> ERROR. Robust's pooled test harness fails a test on ANY unexpected
    ///     ERROR-level server log at teardown, regardless of which system logged it, so this whitelists
    ///     that one specific, pre-existing, unrelated sawmill rather than silencing errors broadly.
    /// </summary>
    private void SuppressSeasonLedgerRoundEndNoise()
    {
        Pair.ServerLogHandler.JudgeLog += (sawmill, _) => sawmill == "system.season_ledger";
    }

    /// <summary>
    ///     Polls for at least <paramref name="expectedAtLeast"/> live contractors, checking BEFORE
    ///     stepping and returning the instant the count is reached (never stepping past it). This is
    ///     the safe way to confirm "dispatch happened" with <c>hysteresisTicks: 1</c>: dispatch can
    ///     legitimately already have occurred during <see cref="EnableAndFireFirstSample"/>'s own
    ///     internal single-tick sample (hysteresisTicks=1 needs only one gap-held sample), and once a
    ///     contractor is actually live and supplying, its OWN success typically clears the deficit --
    ///     with hysteresisTicks=1 that single clearing sample is ALSO immediately actionable and
    ///     recalls it again. An unconditional extra <see cref="StepOneMonitorInterval"/> call after
    ///     dispatch already happened would therefore race straight into that recall instead of
    ///     observing the dispatch. Checking first, then stepping only if still short, avoids the race
    ///     entirely regardless of exactly which sample dispatch actually lands on.
    /// </summary>
    private static async Task<int> WaitForLiveContractorCount(
        RobustIntegrationTest.ServerIntegrationInstance server, IEntityManager entMan, int expectedAtLeast, int maxIntervals = 5)
    {
        var count = 0;

        for (var i = 0; i < maxIntervals; i++)
        {
            await server.WaitAssertion(() => count = CountLiveContractors(entMan));

            if (count >= expectedAtLeast)
                return count;

            StepOneMonitorInterval(server);
        }

        await server.WaitAssertion(() => count = CountLiveContractors(entMan));
        return count;
    }

    /// <summary>
    ///     §9 test 1 -- the defining acceptance test, exactly §8's walkthrough: a network in real
    ///     deficit with a genuinely declining SMES reserve and no qualified human dispatches a
    ///     contractor whose PowerSupplierComponent actually joins the network; a session that then
    ///     satisfies every §4.4 coverage check triggers ClearNet() (asserted BEFORE the entity is
    ///     gone) and the contractor is deleted.
    /// </summary>
    [Test]
    public async Task AcceptanceWalkthrough_DispatchesOnGap_RecallsOnHumanTakeover()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var xformSys = entMan.System<SharedTransformSystem>();
        var batterySys = entMan.System<BatterySystem>();
        var stationSys = entMan.System<StationSystem>();
        var jobSys = entMan.System<JobSystem>();
        var mindSys = entMan.System<SharedMindSystem>();
        var afk = server.ResolveDependency<IAfkManager>();

        EntityUid gridUid = default;
        EntityUid smesEnt = default;
        BatteryComponent smesBattery = default!;

        await server.WaitAssertion(() =>
        {
            var net = BuildFailingNetwork(entMan, mapSys, batterySys, stationSys,
                smesMaxSupply: 500f,
                consumerDrawRate: 2000f,
                // Generous capacity relative to the 500W discharge rate cap -- the reserve must be
                // able to visibly decline across several monitor samples without bottoming out at 0
                // (a flatlined-at-zero reserve is covered separately by
                // FlatZeroReserve_DispatchesAfterHysteresis_EvenWithoutADecliningSample).
                smesMaxCharge: 100000f,
                smesInitialCharge: 30000f); // 30% -- above the 20% floor initially.

            gridUid = net.Grid;
            smesEnt = net.SmesEnt;
            smesBattery = net.SmesBattery;
        });

        // Let the solver settle at the initial (above-floor) reserve level before the feature is even
        // enabled, so the first real monitor sample sees a stable, known reserve ratio.
        server.RunTicks(10);

        await EnableAndFireFirstSample(server, hysteresisTicks: 2);

        // First sample: reserve at/near 30% (above floor), no previous sample to compare against
        // either way -- must NOT dispatch.
        await server.WaitAssertion(() =>
        {
            Assert.That(CountLiveContractors(entMan), Is.EqualTo(0),
                "must not dispatch on the very first sample (no previous reading) or while above the floor");
        });

        // Force the reserve below the floor AND below the previous sample's reading.
        await server.WaitPost(() => batterySys.SetCharge((smesEnt, smesBattery), 10000)); // 10%

        StepOneMonitorInterval(server); // first below-floor sample -- hysteresis streak = 1

        await server.WaitAssertion(() =>
        {
            Assert.That(CountLiveContractors(entMan), Is.EqualTo(0),
                "hysteresis requires a SECOND consecutive gap-held sample -- must not dispatch on the very first below-floor sample");
        });

        StepOneMonitorInterval(server); // second below-floor sample -- hysteresis satisfied, dispatch expected

        EntityUid contractorUid = default;

        await server.WaitAssertion(() =>
        {
            var contractors = GetLiveContractorUids(entMan);
            Assert.That(contractors, Has.Count.EqualTo(1),
                "exactly one contractor must be dispatched once the hysteresis window is satisfied");
            contractorUid = contractors[0];

            Assert.That(entMan.HasComponent<PowerSupplierComponent>(contractorUid), Is.True);
            var supplier = entMan.GetComponent<PowerSupplierComponent>(contractorUid);
            Assert.That(supplier.MaxSupply, Is.EqualTo(4000f).Within(0.1),
                "the leased wattage must come from the CVar, not the prototype's placeholder value");
        });

        server.RunTicks(5); // let real node-group connectivity actually place the supplier in the network

        await server.WaitAssertion(() =>
        {
            var supplier = entMan.GetComponent<PowerSupplierComponent>(contractorUid);
            Assert.That(supplier.Net, Is.Not.Null,
                "the contractor's PowerSupplierComponent must actually join the network's Supplies via " +
                "real engine node-group connectivity -- not a manual/simulated override");
        });

        // Human takes over: move the connected session's real mob onto this grid, give it a
        // qualified job, and make sure it isn't flagged AFK.
        var session = ServerSession!;

        await server.WaitPost(() =>
        {
            var mob = session.AttachedEntity!.Value;
            xformSys.SetCoordinates(mob, gridUid.ToCoordinates(0, -3));

            var gotMind = mindSys.TryGetMind(mob, out var mindId, out _);
            Assert.That(gotMind, Is.True, "connected session's mob has no mind -- DummyTicker must be false");

            jobSys.MindAddJob(mindId, "StationEngineer");
            afk.PlayerDidAction(session);
        });

        StepOneMonitorInterval(server); // first coverage-confirmed sample -- clear streak = 1

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(contractorUid), Is.True,
                "must not recall on the very first coverage-confirmed sample -- clearing needs hysteresis too");
        });

        StepOneMonitorInterval(server); // second coverage-confirmed sample -- recall expected

        await server.WaitAssertion(() =>
        {
            // Ordering assertion (§9 test 1): ClearNet() must have already fired by the time recall
            // is observable, whether or not the entity itself has finished being deleted yet.
            if (entMan.TryGetComponent<PowerSupplierComponent>(contractorUid, out var supplier))
            {
                Assert.That(supplier.Net, Is.Null,
                    "ClearNet() must fire before/at recall -- the network must drop the leased supply");
            }
        });

        server.RunTicks(5); // let the queued deletion actually process

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(contractorUid), Is.False,
                "the contractor entity must actually be deleted after a clean recall");
            Assert.That(CountLiveContractors(entMan), Is.EqualTo(0));

            var system = entMan.System<ProvidencePowerContractorSystem>();
            Assert.That(system.LiveContractorCount, Is.EqualTo(0),
                "the concurrency-cap slot must be released on clean recall");
        });
    }

    /// <summary>
    ///     §9 test 4a: spawn validation must refuse cleanly when no candidate DeployTile exists (here:
    ///     no HV cable anywhere within the ring radius except the anchor's own -- necessarily
    ///     obstructed -- tile), stay Dormant, and retry on the next monitor tick rather than throwing
    ///     or leaving orphaned state.
    ///
    ///     Review-flagged (adversarial pass): the ORIGINAL version of this test was VACUOUS -- it never
    ///     spawned a consumer, so <c>Consumption == 0</c> made <c>EvaluateDeficitAndReserve</c>'s
    ///     deficit condition permanently false and <c>DispatchZone</c> (and therefore spawn validation
    ///     itself) was NEVER actually invoked; the test was "passing" for the wrong reason (nothing
    ///     ever tried to dispatch, not "dispatch was correctly refused"). Fixed by giving the network a
    ///     real consumer, exactly like every other test in this file.
    /// </summary>
    [Test]
    public async Task SpawnValidation_NoCandidateDeployTile_FailsCleanAndStaysDormant()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var batterySys = entMan.System<BatterySystem>();
        var stationSys = entMan.System<StationSystem>();

        EntityUid smesEnt = default;
        BatteryComponent smesBattery = default!;

        await server.WaitAssertion(() =>
        {
            var map = mapSys.CreateMap(out var mapId);
            var grid = mapSys.CreateGridEntity(mapId);

            // Only the anchor's OWN tile has HV cable -- ring search (radius >= 1, excluding the
            // anchor's own tile) can never find a candidate.
            mapSys.SetTile(grid, new Vector2i(0, 0), new Tile(1));
            entMan.SpawnEntity("CableHV", grid.Owner.ToCoordinates(0, 0));

            SetUpStation(entMan, stationSys, grid.Owner);

            smesEnt = entMan.SpawnEntity("SMESBasic", grid.Owner.ToCoordinates(0, 0));
            // Fixed (was vacuous): a real consumer on the SAME tile so Consumption > 0 and the
            // deficit condition can actually hold -- production requires Consumption > 0 for
            // DispatchZone to ever be invoked at all, so a test with none never exercised spawn
            // validation in the first place.
            var consumerEnt = entMan.SpawnEntity("NpcContractorConsumerDummy", grid.Owner.ToCoordinates(0, 0));

            smesBattery = entMan.GetComponent<BatteryComponent>(smesEnt);
            var smesNetBattery = entMan.GetComponent<PowerNetworkBatteryComponent>(smesEnt);
            var consumer = entMan.GetComponent<PowerConsumerComponent>(consumerEnt);

            smesNetBattery.MaxSupply = 100; // deficit is trivially true once Consumption > 0
            smesNetBattery.SupplyRampRate = 100000;
            smesNetBattery.SupplyRampTolerance = 100000;
            batterySys.SetMaxCharge((smesEnt, smesBattery), 100000);
            batterySys.SetCharge((smesEnt, smesBattery), 30000); // 30%, above the floor initially
            consumer.DrawRate = 2000;
        });

        server.RunTicks(10);

        await EnableAndFireFirstSample(server, hysteresisTicks: 1, spawnRingRadius: 3);

        await server.WaitPost(() => batterySys.SetCharge((smesEnt, smesBattery), 10000)); // 10%, below floor
        StepOneMonitorInterval(server); // gap now held -- hysteresisTicks=1, dispatch attempt expected this sample

        await server.WaitAssertion(() =>
        {
            Assert.That(CountLiveContractors(entMan), Is.EqualTo(0),
                "spawn validation must refuse cleanly -- no valid DeployTile exists");

            var system = entMan.System<ProvidencePowerContractorSystem>();
            Assert.That(system.ZoneCount, Is.EqualTo(1),
                "the zone itself must still be tracked (Dormant), not dropped, after a failed spawn attempt");
            Assert.That(system.LiveContractorCount, Is.EqualTo(0));
        });

        // Retries cleanly on subsequent ticks too -- no crash, no orphaned state, no duplicate zones.
        StepOneMonitorInterval(server);

        await server.WaitAssertion(() =>
        {
            Assert.That(CountLiveContractors(entMan), Is.EqualTo(0));
            var system = entMan.System<ProvidencePowerContractorSystem>();
            Assert.That(system.ZoneCount, Is.EqualTo(1));
        });
    }

    /// <summary>
    ///     §9 test 4b: a contractor destroyed mid-lease by something other than this system's own
    ///     RecallZone call (killed, gibbed, exploded -- proxied here by a direct entity deletion,
    ///     which is the terminal common denominator of every one of those in-game paths) must be
    ///     treated as an implicit recall: the concurrency-cap slot is released, exactly as a clean
    ///     recall would.
    /// </summary>
    [Test]
    public async Task Destruction_MidLease_TreatedAsImplicitRecall()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var batterySys = entMan.System<BatterySystem>();
        var stationSys = entMan.System<StationSystem>();

        EntityUid smesEnt = default;
        BatteryComponent smesBattery = default!;

        await server.WaitAssertion(() =>
        {
            var net = BuildFailingNetwork(entMan, mapSys, batterySys, stationSys);
            smesEnt = net.SmesEnt;
            smesBattery = net.SmesBattery;
        });

        server.RunTicks(10);

        await EnableAndFireFirstSample(server, hysteresisTicks: 1, redispatchCooldownSeconds: 30f);

        await WaitForLiveContractorCount(server, entMan, 1); // reserve already exhausted -- dispatch expected

        EntityUid contractorUid = default;

        await server.WaitAssertion(() =>
        {
            var contractors = GetLiveContractorUids(entMan);
            Assert.That(contractors, Has.Count.EqualTo(1));
            contractorUid = contractors[0];

            var system = entMan.System<ProvidencePowerContractorSystem>();
            Assert.That(system.LiveContractorCount, Is.EqualTo(1));
        });

        // Simulate destruction (weapon damage -> Destructible -> DoActsBehavior:Destruction, an
        // explosion, or gibbing all terminate in entity deletion -- proxied directly here).
        await server.WaitPost(() => entMan.DeleteEntity(contractorUid));

        server.RunTicks(3); // give the EntityTerminatingEvent handler / reconciler a chance to run

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(contractorUid), Is.False);

            var system = entMan.System<ProvidencePowerContractorSystem>();
            Assert.That(system.LiveContractorCount, Is.EqualTo(0),
                "destruction outside RecallZone must still release the concurrency-cap slot");
        });
    }

    /// <summary>
    ///     §9's "round-restart zero-surviving-state test": a live lease's tracked state (the zone
    ///     dictionary, the concurrency-cap count) must be fully cleared across a REAL round restart,
    ///     driven the same way <c>Content.IntegrationTests/Pair/TestPair.Recycle.cs</c> hands a
    ///     server back to the pool between tests (<c>gameTicker.RestartRound()</c> -- same precedent
    ///     as <c>SolreignHtnPlanQueueRoundRestartTest</c>), not a hand-raised
    ///     <see cref="RoundRestartCleanupEvent"/> in isolation (which skips the rest of the real
    ///     restart sequence -- FlushEntities, mind wipe, new station setup -- and leaves the server in
    ///     a state no other system expects). <c>RestartRound()</c> flushes every entity, including the
    ///     test's own map/SMES/contractor, so the "zero-state proof" half rebuilds a fresh failing
    ///     network afterward rather than reusing anything from before the restart.
    /// </summary>
    [Test]
    public async Task RoundRestartCleanup_LeavesZeroSurvivingState()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var batterySys = entMan.System<BatterySystem>();
        var stationSys = entMan.System<StationSystem>();
        var gameTicker = entMan.System<GameTicker>();

        EntityUid smesEnt = default;

        await server.WaitAssertion(() =>
        {
            var net = BuildFailingNetwork(entMan, mapSys, batterySys, stationSys);
            smesEnt = net.SmesEnt;
        });

        server.RunTicks(10);

        // Long cooldown -- if round-restart cleanup did NOT reset state, this cooldown would block
        // the post-restart re-dispatch this test checks for.
        await EnableAndFireFirstSample(server, hysteresisTicks: 1, redispatchCooldownSeconds: 999f);

        await WaitForLiveContractorCount(server, entMan, 1); // reserve already exhausted -- dispatch expected

        await server.WaitAssertion(() =>
        {
            Assert.That(CountLiveContractors(entMan), Is.EqualTo(1),
                "setup failed: a lease must be live before testing the round-restart reset");
        });

        // Real round restart -- raises RoundRestartCleanupEvent (this system's HardResetAllZones)
        // THEN flushes every entity, exactly as the live server does between rounds.
        await server.WaitPost(() => gameTicker.RestartRound());

        server.RunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(CountLiveContractors(entMan), Is.EqualTo(0),
                "the live contractor must be gone after a real round restart");
            Assert.That(entMan.EntityExists(smesEnt), Is.False,
                "RestartRound()'s FlushEntities should have deleted the old map/SMES along with everything else");

            var system = entMan.System<ProvidencePowerContractorSystem>();
            Assert.That(system.ZoneCount, Is.EqualTo(0),
                "zone tracking must be fully cleared, not just the live entity");
            Assert.That(system.LiveContractorCount, Is.EqualTo(0),
                "the concurrency-cap count must be reset to zero, not decremented-but-stale");
        });

        // Zero-state proof: build a fresh failing network for the "new round" and confirm a dispatch
        // succeeds cleanly -- a stale cap count or a stale zone entry (with its old cooldown still
        // attached) would block or corrupt this.
        await server.WaitAssertion(() => BuildFailingNetwork(entMan, mapSys, batterySys, stationSys));

        server.RunTicks(10);
        await WaitForLiveContractorCount(server, entMan, 1); // fresh zone -- dispatch expected (already exhausted)

        await server.WaitAssertion(() =>
        {
            Assert.That(CountLiveContractors(entMan), Is.EqualTo(1),
                "a fresh dispatch must succeed after round-restart cleanup -- proves no stale " +
                "cap/cooldown/zone state survived the restart");
        });
    }

    /// <summary>
    ///     §2 Finding 6 / §9 test 3 (scoped): confirms the structural precondition
    ///     <c>SeasonLedgerSystem.OnRoundEnd</c> relies on (§2's table: "a mindless contractor never
    ///     has an account-backed PlayerGuid, so it structurally cannot appear in
    ///     ev.AllPlayersEndInfo") -- a spawned contractor has no Mind at all, so no code path can ever
    ///     resolve it to a NetUserId. This test deliberately does not reconstruct
    ///     <c>GameTicker</c>'s round-end roster assembly (a large, unrelated surface); it proves the
    ///     one fact that assembly depends on for this entity to be excluded.
    /// </summary>
    [Test]
    public async Task Contractor_HasNoMind_StructurallyCannotAppearInRoundEndRoster()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var mindSys = entMan.System<SharedMindSystem>();

        EntityUid contractor = default;

        await server.WaitAssertion(() =>
        {
            var map = mapSys.CreateMap(out var mapId);
            var grid = mapSys.CreateGridEntity(mapId);
            mapSys.SetTile(grid, new Vector2i(0, 0), new Tile(1));
            entMan.SpawnEntity("CableHV", grid.Owner.ToCoordinates(0, 0));

            contractor = entMan.SpawnEntity(ProvidencePowerContractorSystem.ContractorPrototypeId, grid.Owner.ToCoordinates(0, 0));
        });

        await server.WaitAssertion(() =>
        {
            var hasMind = mindSys.TryGetMind(contractor, out _, out _);
            Assert.That(hasMind, Is.False,
                "a dispatch-spawned contractor must never have a Mind -- this is the exact structural " +
                "precondition SeasonLedgerSystem.OnRoundEnd's PlayerGuid filter relies on to exclude it");
        });
    }

    /// <summary>
    ///     Review finding #1 (DEADLY): reserve exhausted to EXACTLY 0 and held flat there (never
    ///     "declining" sample-to-sample, since 0 can never be lower than a previous 0) must still be a
    ///     dispatch-eligible failure. Pre-fix, <c>reserveRatio &lt; previous</c> with both sides at 0
    ///     is permanently false, so the WORST blackout (a totally drained battery) could never
    ///     dispatch. This test starts the SMES already at 0 charge (not draining down to it across
    ///     samples like the acceptance test) specifically so the very first monitor sample already
    ///     sees a flat, non-declining 0% reserve -- proving hysteresis alone (not a fabricated
    ///     "decline") gates the dispatch.
    /// </summary>
    [Test]
    public async Task FlatZeroReserve_DispatchesAfterHysteresis_EvenWithoutADecliningSample()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var batterySys = entMan.System<BatterySystem>();
        var stationSys = entMan.System<StationSystem>();

        await server.WaitAssertion(() =>
        {
            // smesInitialCharge defaults to 0 in BuildFailingNetwork -- flat-zero from sample 1.
            BuildFailingNetwork(entMan, mapSys, batterySys, stationSys, smesMaxSupply: 500f, consumerDrawRate: 2000f);
        });

        server.RunTicks(10);

        await EnableAndFireFirstSample(server, hysteresisTicks: 2);

        await server.WaitAssertion(() =>
        {
            Assert.That(CountLiveContractors(entMan), Is.EqualTo(0),
                "hysteresis streak = 1 after the first sample -- must not dispatch yet");
        });

        StepOneMonitorInterval(server); // second flat-zero sample -- hysteresis satisfied

        await server.WaitAssertion(() =>
        {
            Assert.That(CountLiveContractors(entMan), Is.EqualTo(1),
                "a reserve flatlined at exactly 0% across two consecutive samples (never numerically " +
                "'declining' sample-to-sample) must still be dispatch-eligible once hysteresis is " +
                "satisfied -- pre-fix, 'reserveRatio < previous' with both at 0 is permanently false " +
                "and this NEVER dispatches");
        });
    }

    /// <summary>
    ///     Review finding #2: the deficit check must read the network's ACTUAL delivered supply
    ///     (<c>PowerState.Network.LastCombinedSupply</c>), not its nameplate maximum
    ///     (<c>LastCombinedMaxSupply</c>, which is what the buggy <c>NetworkPowerStatistics
    ///     .SupplyCurrent</c> field actually contains). The SMES here has a generous 2000W nameplate
    ///     rate cap (comfortably above the 1000W consumer -- pre-fix code reading nameplate would see
    ///     ample headroom and NEVER detect a deficit) but a crippled 5 W/s ramp rate, so its ACTUAL
    ///     delivered supply stays near zero for the whole test -- a genuinely under-delivering/
    ///     ramp-limited supplier that pre-fix code would blind itself to.
    /// </summary>
    [Test]
    public async Task UnderDeliveringSupply_ActualNotNameplate_TriggersDispatch()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var batterySys = entMan.System<BatterySystem>();
        var stationSys = entMan.System<StationSystem>();

        await server.WaitAssertion(() =>
        {
            BuildFailingNetwork(entMan, mapSys, batterySys, stationSys,
                smesMaxSupply: 2000f,      // generous NAMEPLATE -- exceeds the 1000W consumer
                smesRampRate: 5f,          // but crippled ACTUAL ramp -- stays near 0 delivered for seconds
                consumerDrawRate: 1000f);
        });

        server.RunTicks(10);

        await EnableAndFireFirstSample(server, hysteresisTicks: 2);

        await server.WaitAssertion(() =>
        {
            Assert.That(CountLiveContractors(entMan), Is.EqualTo(0),
                "hysteresis streak = 1 after the first sample -- must not dispatch yet");
        });

        StepOneMonitorInterval(server);

        await server.WaitAssertion(() =>
        {
            Assert.That(CountLiveContractors(entMan), Is.EqualTo(1),
                "a nameplate-high but ramp-crippled (genuinely under-delivering) supplier must be " +
                "detected as failing via ACTUAL delivered supply -- reading nameplate capacity instead " +
                "would see 2000W >= 1000W consumption and never dispatch");
        });
    }

    /// <summary>Review finding #7: an ABSOLUTE watt floor on top of the fractional deficit check --
    /// a trivial 10W load against zero delivered supply reads as a 100% fractional deficit (comfortably
    /// over the default 15% threshold) but must NOT summon a full contractor, because the absolute
    /// unmet wattage (10W) never crosses <c>solreign.power_contractor.min_unmet_watts</c> (default
    /// 500W). Reserve is deliberately already exhausted (0% via BuildFailingNetwork's default) so this
    /// test isolates the ABSOLUTE deficit floor specifically -- if that floor were missing, this would
    /// dispatch on the very first hysteresis-satisfied sample exactly like FlatZeroReserve's test
    /// does.</summary>
    [Test]
    public async Task MinAbsoluteUnmetWatts_TrivialLoad_NeverDispatches()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var batterySys = entMan.System<BatterySystem>();
        var stationSys = entMan.System<StationSystem>();

        await server.WaitAssertion(() =>
        {
            BuildFailingNetwork(entMan, mapSys, batterySys, stationSys,
                smesMaxSupply: 0f,       // the SMES cannot deliver ANY power -- delivered supply ~= 0
                consumerDrawRate: 10f);  // trivial load: 10W unmet, 100% fractional, but < 500W absolute floor
        });

        server.RunTicks(10);

        await EnableAndFireFirstSample(server, hysteresisTicks: 1, minUnmetWatts: 500f);

        // Several samples, well past any plausible hysteresis window -- must never dispatch.
        for (var i = 0; i < 4; i++)
            StepOneMonitorInterval(server);

        await server.WaitAssertion(() =>
        {
            Assert.That(CountLiveContractors(entMan), Is.EqualTo(0),
                "a trivial 10W unmet load must never dispatch a contractor despite reading as a 100% " +
                "fractional deficit -- the absolute watt floor exists specifically to block this");
        });
    }

    /// <summary>Review finding #3: an anchor whose grid was never added to ANY station (an
    /// undocked/enemy/ruin/derelict/admin-spawned grid) must never become a tracked zone at all, even
    /// while genuinely failing -- it must not be able to consume the global concurrency cap ahead of a
    /// real station. Docking it (adding the SAME grid to a real station mid-round, exactly like a
    /// shuttle docking) must make it eligible on the very next monitor tick.</summary>
    [Test]
    public async Task OffStationGrid_NeverDispatches_UntilDockedToAStation()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var batterySys = entMan.System<BatterySystem>();
        var stationSys = entMan.System<StationSystem>();

        EntityUid gridUid = default;

        await server.WaitAssertion(() =>
        {
            var map = mapSys.CreateMap(out var mapId);
            var grid = mapSys.CreateGridEntity(mapId);
            gridUid = grid.Owner;

            for (var i = -3; i <= 3; i++)
            {
                mapSys.SetTile(grid, new Vector2i(0, i), new Tile(1));
                entMan.SpawnEntity("CableHV", grid.Owner.ToCoordinates(0, i));
            }

            // Deliberately NOT added to any station -- an undocked/derelict/admin-spawned grid.
            var smesEnt = entMan.SpawnEntity("SMESBasic", grid.Owner.ToCoordinates(0, 0));
            var consumerEnt = entMan.SpawnEntity("NpcContractorConsumerDummy", grid.Owner.ToCoordinates(0, 3));

            var smesBattery = entMan.GetComponent<BatteryComponent>(smesEnt);
            var smesNetBattery = entMan.GetComponent<PowerNetworkBatteryComponent>(smesEnt);
            var consumer = entMan.GetComponent<PowerConsumerComponent>(consumerEnt);

            smesNetBattery.MaxSupply = 500;
            smesNetBattery.SupplyRampRate = 100000;
            smesNetBattery.SupplyRampTolerance = 100000;
            batterySys.SetMaxCharge((smesEnt, smesBattery), 100000);
            batterySys.SetCharge((smesEnt, smesBattery), 0); // exhausted from the start
            consumer.DrawRate = 2000;
        });

        server.RunTicks(10);

        await EnableAndFireFirstSample(server, hysteresisTicks: 1);

        for (var i = 0; i < 3; i++)
            StepOneMonitorInterval(server);

        await server.WaitAssertion(() =>
        {
            Assert.That(CountLiveContractors(entMan), Is.EqualTo(0),
                "an off-station grid must never dispatch a contractor no matter how badly it's failing");

            var system = entMan.System<ProvidencePowerContractorSystem>();
            Assert.That(system.ZoneCount, Is.EqualTo(0),
                "an off-station anchor must never even become a tracked zone");
        });

        // Dock it -- add the SAME grid to a real station mid-round.
        await server.WaitPost(() => SetUpStation(entMan, stationSys, gridUid));

        // Discovery + dispatch happen together in the SAME monitor tick (hysteresisTicks=1) --
        // WaitForLiveContractorCount stops the instant it sees the dispatch, never overshooting into
        // a later sample where the now-live contractor's own success would clear the deficit and
        // (with hysteresisTicks=1) recall itself again.
        await WaitForLiveContractorCount(server, entMan, 1);

        await server.WaitAssertion(() =>
        {
            Assert.That(CountLiveContractors(entMan), Is.EqualTo(1),
                "the SAME grid must become dispatch-eligible immediately once it's docked to a real station");
        });
    }

    /// <summary>Review finding #3: zero SMES anywhere (a station with power infrastructure that just
    /// never happens to include a monitorable battery-discharger) must be a clean no-op -- no zones,
    /// no exceptions, every monitor tick a cheap pass over an empty query.</summary>
    [Test]
    public async Task ZeroSmes_NoZonesTracked_NoCrash()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var stationSys = entMan.System<StationSystem>();

        await server.WaitAssertion(() =>
        {
            var map = mapSys.CreateMap(out var mapId);
            var grid = mapSys.CreateGridEntity(mapId);
            mapSys.SetTile(grid, new Vector2i(0, 0), new Tile(1));
            SetUpStation(entMan, stationSys, grid.Owner);
            // No SMES, no cable even -- just a station with an empty grid.
        });

        await EnableAndFireFirstSample(server, hysteresisTicks: 1);

        for (var i = 0; i < 3; i++)
            StepOneMonitorInterval(server);

        await server.WaitAssertion(() =>
        {
            var system = entMan.System<ProvidencePowerContractorSystem>();
            Assert.That(system.ZoneCount, Is.EqualTo(0));
            Assert.That(system.LiveContractorCount, Is.EqualTo(0));
            Assert.That(CountLiveContractors(entMan), Is.EqualTo(0));
        });
    }

    /// <summary>
    ///     Review finding #3 (cap-priority half): when the global concurrency cap is scarce and TWO
    ///     different stations' zones both become dispatch-eligible on the SAME monitor tick, the
    ///     PUBLIC (larger) station must win the scarce slot -- never a smaller secondary station. The
    ///     smaller station's SMES/grid/zone is deliberately created FIRST (so it would be the first
    ///     entry discovered/iterated under plain insertion order) specifically so this test would FAIL
    ///     without the cap-priority sort: without it, the secondary station's zone would win the race
    ///     purely by discovery order, not by which station is actually the real one.
    /// </summary>
    [Test]
    public async Task MultipleStations_PublicStationWinsScarceCapSlot()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var batterySys = entMan.System<BatterySystem>();
        var stationSys = entMan.System<StationSystem>();

        EntityUid publicSmes = default;

        await server.WaitAssertion(() =>
        {
            // Secondary station FIRST (smaller grid: 3 tiles) -- deliberately discovered before the
            // public station so a plain insertion-order iteration would let IT win.
            BuildFailingNetwork(entMan, mapSys, batterySys, stationSys,
                lineHalfLength: 1, stationName: "Secondary Outpost");

            // Public station SECOND (larger grid: 7 tiles) -- must still win the cap slot.
            var publicNet = BuildFailingNetwork(entMan, mapSys, batterySys, stationSys,
                lineHalfLength: 3, stationName: "Public Station");
            publicSmes = publicNet.SmesEnt;
        });

        server.RunTicks(10);

        await EnableAndFireFirstSample(server, hysteresisTicks: 1, concurrencyCap: 1);

        // Both zones' hysteresis is satisfied on the SAME tick (possibly already during
        // EnableAndFireFirstSample's own first sample) -- check immediately rather than
        // unconditionally stepping again, which could race into the winner's own success clearing
        // its deficit and freeing the cap slot back up for the loser.
        await WaitForLiveContractorCount(server, entMan, 1);

        await server.WaitAssertion(() =>
        {
            var contractors = GetLiveContractorUids(entMan);
            Assert.That(contractors, Has.Count.EqualTo(1),
                "the scarce cap (1) must allow exactly one dispatch this tick");

            var contractorComp = entMan.GetComponent<ProvidencePowerContractorComponent>(contractors[0]);
            Assert.That(contractorComp.AnchorUid, Is.EqualTo(publicSmes),
                "the PUBLIC (larger) station's zone must win the scarce cap slot, not the secondary " +
                "station's zone that happened to be discovered first");
        });
    }

    /// <summary>
    ///     Review finding #4 (merge half): a cable bridging two previously-independent, INDEPENDENTLY
    ///     DISPATCHED zones into one combined network must never leave two contractors racing to
    ///     supply the same network. Both zones dispatch their own lease first (cap raised to 2 so
    ///     both can go live simultaneously); a bridge cable is then spawned between them, forcing a
    ///     real engine topology remake; the very next monitor tick must reconcile down to exactly one
    ///     live zone/contractor.
    /// </summary>
    [Test]
    public async Task NetworkMerge_RetiresDuplicateZone_KeepsExactlyOneLive()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var batterySys = entMan.System<BatterySystem>();
        var stationSys = entMan.System<StationSystem>();

        EntityUid gridUid = default;

        await server.WaitAssertion(() =>
        {
            var map = mapSys.CreateMap(out var mapId);
            var grid = mapSys.CreateGridEntity(mapId);
            gridUid = grid.Owner;

            // Segment A: y=-3..-1 (SMES-A at -3, consumer-A at -1).
            for (var i = -3; i <= -1; i++)
            {
                mapSys.SetTile(grid, new Vector2i(0, i), new Tile(1));
                entMan.SpawnEntity("CableHV", grid.Owner.ToCoordinates(0, i));
            }

            // Gap: y=0 -- tile exists (so a bridge can be added later) but no cable yet.
            mapSys.SetTile(grid, new Vector2i(0, 0), new Tile(1));

            // Segment B: y=1..3 (SMES-B at 1, consumer-B at 3).
            for (var i = 1; i <= 3; i++)
            {
                mapSys.SetTile(grid, new Vector2i(0, i), new Tile(1));
                entMan.SpawnEntity("CableHV", grid.Owner.ToCoordinates(0, i));
            }

            SetUpStation(entMan, stationSys, gridUid);

            void SetUpSmes(EntityUid smes, EntityUid consumerEnt)
            {
                var smesBattery = entMan.GetComponent<BatteryComponent>(smes);
                var smesNetBattery = entMan.GetComponent<PowerNetworkBatteryComponent>(smes);
                var consumer = entMan.GetComponent<PowerConsumerComponent>(consumerEnt);

                smesNetBattery.MaxSupply = 500;
                smesNetBattery.SupplyRampRate = 100000;
                smesNetBattery.SupplyRampTolerance = 100000;
                batterySys.SetMaxCharge((smes, smesBattery), 100000);
                batterySys.SetCharge((smes, smesBattery), 0); // exhausted from the start
                // 5000W each (10000W combined post-merge) -- deliberately more than even BOTH
                // contractors' leases summed (8000W) could cover. This is a deliberate safety margin
                // over "more than one lease can cover": PowerState.Network.LastCombinedSupply is a
                // last-SOLVER-tick cache (Pow3r's own documented "as of last tick" semantics, review
                // finding #2's target field), so on the exact tick the merge is reconciled, the
                // surviving zone's own gap check can transiently still see the JUST-RETIRED loser's
                // supply counted (ClearNet() already ran synchronously, but the solver hasn't re-run
                // yet to reflect it). Demand this large stays a genuine deficit either way, so the
                // test doesn't flake on that one-tick solver-cache staleness.
                consumer.DrawRate = 5000;
            }

            var smesA = entMan.SpawnEntity("SMESBasic", grid.Owner.ToCoordinates(0, -3));
            var consumerA = entMan.SpawnEntity("NpcContractorConsumerDummy", grid.Owner.ToCoordinates(0, -1));
            SetUpSmes(smesA, consumerA);

            var smesB = entMan.SpawnEntity("SMESBasic", grid.Owner.ToCoordinates(0, 1));
            var consumerB = entMan.SpawnEntity("NpcContractorConsumerDummy", grid.Owner.ToCoordinates(0, 3));
            SetUpSmes(smesB, consumerB);
        });

        server.RunTicks(10);

        await EnableAndFireFirstSample(server, hysteresisTicks: 1, concurrencyCap: 2, spawnRingRadius: 2);

        // Both independent zones dispatch (still disconnected networks) -- possibly already during
        // EnableAndFireFirstSample's own first sample.
        await WaitForLiveContractorCount(server, entMan, 2);

        await server.WaitAssertion(() =>
        {
            Assert.That(CountLiveContractors(entMan), Is.EqualTo(2),
                "setup failed: both independent networks must dispatch their own lease before the merge");
            var system = entMan.System<ProvidencePowerContractorSystem>();
            Assert.That(system.ZoneCount, Is.EqualTo(2));
            Assert.That(system.LiveContractorCount, Is.EqualTo(2));
        });

        // Bridge the gap -- a real cable connecting the two previously-separate networks, forcing a
        // genuine engine topology remake into ONE combined network.
        await server.WaitPost(() => entMan.SpawnEntity("CableHV", gridUid.ToCoordinates(0, 0)));

        server.RunTicks(5); // let the topology remake actually happen
        StepOneMonitorInterval(server); // ReconcileZoneTopology's next pass sees the merge

        await server.WaitAssertion(() =>
        {
            Assert.That(CountLiveContractors(entMan), Is.EqualTo(1),
                "a network merge must retire the duplicate zone's contractor -- never leave two live " +
                "contractors racing to supply the same now-combined network");

            var system = entMan.System<ProvidencePowerContractorSystem>();
            Assert.That(system.ZoneCount, Is.EqualTo(1),
                "exactly one zone must survive tracking the merged network");
            Assert.That(system.LiveContractorCount, Is.EqualTo(1),
                "the retired zone's concurrency-cap slot must be released, not leaked");
        });
    }

    /// <summary>
    ///     Review finding #4 (split half): if the zone's monitored network and the live contractor's
    ///     ACTUAL supplied network ever diverge (the structural signature of a topology split moving
    ///     them apart), the zone must recall rather than silently continue "monitoring" one network
    ///     while a lease it thinks it owns is actually feeding a completely different one. Produced
    ///     here via the same engine-native relocation sequence a player physically moving an anchored
    ///     machine would trigger (Unanchor -&gt; SetCoordinates -&gt; AnchorEntity onto a second,
    ///     wholly independent HV network) -- the resulting mismatch (zone's network via the anchor's
    ///     discharger vs. the still-in-place contractor's PowerSupplierComponent.Net) is exactly the
    ///     structural invariant a real cable-topology split would also produce, since both leave the
    ///     anchor and its already-dispatched contractor on two different, freshly-made
    ///     <c>PowerState.Network</c> objects.
    /// </summary>
    [Test]
    public async Task TopologySplit_AnchorAndContractorOnDifferentNetworks_ForcesRecall()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var xformSys = entMan.System<SharedTransformSystem>();
        var batterySys = entMan.System<BatterySystem>();
        var stationSys = entMan.System<StationSystem>();

        EntityUid smesEnt = default;
        EntityUid gridB = default;

        await server.WaitAssertion(() =>
        {
            var net = BuildFailingNetwork(entMan, mapSys, batterySys, stationSys,
                smesMaxSupply: 500f, consumerDrawRate: 2000f, lineHalfLength: 3);
            smesEnt = net.SmesEnt;

            // A second, wholly independent HV network the anchor will be relocated onto -- same
            // station (station-scoping is orthogonal to this test), no consumer/battery needed, just
            // enough cable to form its own valid PowerState.Network.
            var mapB = mapSys.CreateMap(out var mapIdB);
            var gridBEnt = mapSys.CreateGridEntity(mapIdB);
            gridB = gridBEnt.Owner;
            mapSys.SetTile(gridBEnt, new Vector2i(0, 0), new Tile(1));
            mapSys.SetTile(gridBEnt, new Vector2i(0, 1), new Tile(1));
            entMan.SpawnEntity("CableHV", gridB.ToCoordinates(0, 0));
            entMan.SpawnEntity("CableHV", gridB.ToCoordinates(0, 1));
            stationSys.AddGridToStation(net.Station, gridB);
        });

        server.RunTicks(10);

        await EnableAndFireFirstSample(server, hysteresisTicks: 1, spawnRingRadius: 2);

        await WaitForLiveContractorCount(server, entMan, 1); // reserve already exhausted -- dispatch expected

        EntityUid contractorUid = default;

        await server.WaitAssertion(() =>
        {
            var contractors = GetLiveContractorUids(entMan);
            Assert.That(contractors, Has.Count.EqualTo(1), "setup failed: dispatch must succeed before the split");
            contractorUid = contractors[0];
        });

        server.RunTicks(5); // let real node-group connectivity actually place the supplier in the network

        await server.WaitAssertion(() =>
        {
            var supplier = entMan.GetComponent<PowerSupplierComponent>(contractorUid);
            Assert.That(supplier.Net, Is.Not.Null, "setup failed: the contractor must have joined the network before the split");
        });

        // Relocate the anchor onto the second, independent network -- the contractor stays exactly
        // where it is, on the ORIGINAL network. This produces the same structural mismatch a real
        // topology split would: the zone's anchor now resolves to a DIFFERENT network than the one
        // its live contractor actually supplies.
        await server.WaitPost(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(smesEnt);
            xformSys.Unanchor(smesEnt, xform);
            xformSys.SetCoordinates(smesEnt, gridB.ToCoordinates(0, 0));
            xformSys.AnchorEntity((smesEnt, xform));
        });

        server.RunTicks(5); // let node-group reconnection actually happen
        StepOneMonitorInterval(server); // AdvanceZones' split-mismatch check runs

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(contractorUid), Is.False,
                "a zone/contractor network mismatch must force a recall -- never keep 'monitoring' a " +
                "network the live lease isn't actually feeding");

            var system = entMan.System<ProvidencePowerContractorSystem>();
            Assert.That(system.LiveContractorCount, Is.EqualTo(0),
                "the split-forced recall must release the concurrency-cap slot exactly like a normal recall");
        });
    }

    /// <summary>
    ///     Review finding #4 (cooldown-continuity half) + finding #6 (UID-driven cleanup) + external
    ///     deletion coverage: an anchor deleted WHILE its zone is Dormant and mid-cooldown (a lease just
    ///     ended, redispatch backoff still running) must not silently forget that cooldown. A brand-new
    ///     SMES placed on the SAME physical grid moments later must inherit the remaining cooldown
    ///     rather than being immediately dispatch-eligible.
    /// </summary>
    [Test]
    public async Task AnchorDeleted_WhileCoolingDown_TransplantsCooldownOntoReplacementOnSameGrid()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var xformSys = entMan.System<SharedTransformSystem>();
        var batterySys = entMan.System<BatterySystem>();
        var stationSys = entMan.System<StationSystem>();
        var jobSys = entMan.System<JobSystem>();
        var mindSys = entMan.System<SharedMindSystem>();
        var afk = server.ResolveDependency<IAfkManager>();

        EntityUid gridUid = default;
        EntityUid smesEnt = default;

        await server.WaitAssertion(() =>
        {
            var net = BuildFailingNetwork(entMan, mapSys, batterySys, stationSys,
                smesMaxSupply: 500f, consumerDrawRate: 2000f);
            gridUid = net.Grid;
            smesEnt = net.SmesEnt;
        });

        server.RunTicks(10);

        // Long cooldown so it's unmistakably still running when we check.
        await EnableAndFireFirstSample(server, hysteresisTicks: 1, redispatchCooldownSeconds: 500f);

        await WaitForLiveContractorCount(server, entMan, 1); // dispatch expected (already exhausted)

        await server.WaitAssertion(() =>
        {
            Assert.That(CountLiveContractors(entMan), Is.EqualTo(1), "setup failed: a lease must be live first");
        });

        // Force a clean recall via human takeover (the production-primary recall path -- same idiom
        // as the acceptance test) rather than an admin/round-end proxy, so this test never touches
        // RoundEndMessageEvent at all.
        var session = ServerSession!;

        await server.WaitPost(() =>
        {
            var mob = session.AttachedEntity!.Value;
            xformSys.SetCoordinates(mob, gridUid.ToCoordinates(0, -3));

            var gotMind = mindSys.TryGetMind(mob, out var mindId, out _);
            Assert.That(gotMind, Is.True);

            jobSys.MindAddJob(mindId, "StationEngineer");
            afk.PlayerDidAction(session);
        });

        StepOneMonitorInterval(server); // hysteresisTicks=1 -- recall expected this sample

        await server.WaitAssertion(() =>
        {
            Assert.That(CountLiveContractors(entMan), Is.EqualTo(0), "the forced recall must have finished");
        });

        // Now delete the anchor itself WHILE its zone is Dormant and mid-cooldown.
        await server.WaitPost(() => entMan.DeleteEntity(smesEnt));

        StepOneMonitorInterval(server); // the zone notices its anchor is gone and orphans -- cooldown must transplant

        // A brand-new SMES on the SAME grid, moments later.
        EntityUid replacementSmes = default;
        BatteryComponent replacementBattery = default!;

        await server.WaitAssertion(() =>
        {
            replacementSmes = entMan.SpawnEntity("SMESBasic", gridUid.ToCoordinates(0, 0));
            var netBattery = entMan.GetComponent<PowerNetworkBatteryComponent>(replacementSmes);
            replacementBattery = entMan.GetComponent<BatteryComponent>(replacementSmes);

            netBattery.MaxSupply = 500;
            netBattery.SupplyRampRate = 100000;
            netBattery.SupplyRampTolerance = 100000;
            batterySys.SetMaxCharge((replacementSmes, replacementBattery), 100000);
            batterySys.SetCharge((replacementSmes, replacementBattery), 0); // exhausted immediately
        });

        server.RunTicks(2);
        StepOneMonitorInterval(server); // discovery picks up the replacement anchor

        await server.WaitAssertion(() =>
        {
            Assert.That(CountLiveContractors(entMan), Is.EqualTo(0),
                "the replacement anchor on the SAME grid must inherit the still-running cooldown -- it " +
                "must NOT be immediately dispatch-eligible even though its own gate has never sampled before");
        });
    }

    /// <summary>Review finding #5: a non-finite (NaN) or negative <c>lease_watts</c> CVar must never
    /// reach the Pow3r solver -- it must fall back to the CVar's own declared default (6000W) instead
    /// of corrupting <c>PowerSupplierComponent.MaxSupply</c> with a NaN/negative value.</summary>
    [Test]
    public async Task CVarValidation_NonFiniteLeaseWatts_FallsBackToDeclaredDefault()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var batterySys = entMan.System<BatterySystem>();
        var stationSys = entMan.System<StationSystem>();

        await server.WaitAssertion(() =>
        {
            BuildFailingNetwork(entMan, mapSys, batterySys, stationSys, smesMaxSupply: 500f, consumerDrawRate: 2000f);
        });

        server.RunTicks(10);

        // NaN from the very start (before the first sample fires) -- the validator must clamp it at
        // CVar-set time, well before any dispatch ever reads it. Separately re-slamming an invalid
        // value on top of an ALREADY-valid, already-subscribed CVar (proving Subs.CVar's callback
        // re-validates on every SetCVar, not just at subscribe-time) is covered by the fact that
        // EnableAndFireFirstSample itself calls SetCVar for every CVar on every test run -- this is
        // exactly that same re-validation path, just exercised with an invalid value from the start.
        await EnableAndFireFirstSample(server, hysteresisTicks: 1, leaseWatts: float.NaN);

        await WaitForLiveContractorCount(server, entMan, 1); // dispatch expected (already exhausted)

        await server.WaitAssertion(() =>
        {
            var contractors = GetLiveContractorUids(entMan);
            Assert.That(contractors, Has.Count.EqualTo(1));

            var supplier = entMan.GetComponent<PowerSupplierComponent>(contractors[0]);
            Assert.That(float.IsFinite(supplier.MaxSupply), Is.True,
                "a NaN lease_watts CVar must never reach the Pow3r solver's MaxSupply field");
            Assert.That(supplier.MaxSupply, Is.EqualTo(CCVars.SolreignPowerContractorLeaseWatts.DefaultValue).Within(0.1),
                "a non-finite CVar must fall back to the CVar's own declared default, not a stale prior value");
        });
    }

    /// <summary>Review finding #5: an absurdly oversized (but finite, non-negative) <c>lease_watts</c>
    /// CVar must be clamped to a sane upper bound, not passed through raw.</summary>
    [Test]
    public async Task CVarValidation_OversizedLeaseWatts_ClampedToSaneMax()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var batterySys = entMan.System<BatterySystem>();
        var stationSys = entMan.System<StationSystem>();

        await server.WaitAssertion(() =>
        {
            BuildFailingNetwork(entMan, mapSys, batterySys, stationSys, smesMaxSupply: 500f, consumerDrawRate: 2000f);
        });

        server.RunTicks(10);

        await EnableAndFireFirstSample(server, hysteresisTicks: 1, leaseWatts: 50_000_000f);

        await WaitForLiveContractorCount(server, entMan, 1); // dispatch expected (already exhausted)

        await server.WaitAssertion(() =>
        {
            var contractors = GetLiveContractorUids(entMan);
            Assert.That(contractors, Has.Count.EqualTo(1));

            var supplier = entMan.GetComponent<PowerSupplierComponent>(contractors[0]);
            Assert.That(supplier.MaxSupply, Is.LessThanOrEqualTo(1_000_000f),
                "an absurdly oversized lease_watts CVar must be clamped to a sane upper bound before " +
                "reaching the Pow3r solver");
        });
    }

    /// <summary>
    ///     Review finding #7 (coverage-scope half): a qualified engineer on a DIFFERENT grid of the
    ///     SAME station must suppress dispatch station-wide -- not just on the exact grid the failing
    ///     SMES sits on. An engineer on a SIBLING station's grid must NOT suppress it. Exercises both
    ///     directions of the documented rule with a real multi-grid, multi-station layout.
    /// </summary>
    [Test]
    public async Task HumanCoverage_StationWide_SiblingGridCounts_SiblingStationDoesNot()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var xformSys = entMan.System<SharedTransformSystem>();
        var batterySys = entMan.System<BatterySystem>();
        var stationSys = entMan.System<StationSystem>();
        var jobSys = entMan.System<JobSystem>();
        var mindSys = entMan.System<SharedMindSystem>();
        var afk = server.ResolveDependency<IAfkManager>();

        EntityUid siblingGridOfSameStation = default;
        EntityUid siblingStationGrid = default;

        await server.WaitAssertion(() =>
        {
            var net = BuildFailingNetwork(entMan, mapSys, batterySys, stationSys, smesMaxSupply: 500f, consumerDrawRate: 2000f);

            // A second grid on the SAME station (e.g. a docked cargo shuttle) -- just a bare tile, no
            // power equipment needed.
            var mapB = mapSys.CreateMap(out var mapIdB);
            var gridB = mapSys.CreateGridEntity(mapIdB);
            siblingGridOfSameStation = gridB.Owner;
            mapSys.SetTile(gridB, new Vector2i(0, 0), new Tile(1));
            stationSys.AddGridToStation(net.Station, siblingGridOfSameStation);

            // A wholly separate, sibling station with its own grid.
            var mapC = mapSys.CreateMap(out var mapIdC);
            var gridC = mapSys.CreateGridEntity(mapIdC);
            siblingStationGrid = gridC.Owner;
            mapSys.SetTile(gridC, new Vector2i(0, 0), new Tile(1));
            SetUpStation(entMan, stationSys, siblingStationGrid, "Sibling Station");
        });

        var session = ServerSession!;

        // Phase 1 setup happens BEFORE the feature is even enabled: the qualified engineer is already
        // on the SIBLING GRID of the SAME station by the time the very first monitor sample fires, so
        // this zone never dispatches in the first place (clean, unambiguous proof of suppression --
        // rather than "dispatch then immediately get recalled by coverage", which would also leave
        // CountLiveContractors at 0 but for the wrong reason and would burn a redispatch cooldown that
        // Phase 2 doesn't want).
        await server.WaitPost(() =>
        {
            var mob = session.AttachedEntity!.Value;
            xformSys.SetCoordinates(mob, siblingGridOfSameStation.ToCoordinates(0, 0));

            var gotMind = mindSys.TryGetMind(mob, out var mindId, out _);
            Assert.That(gotMind, Is.True);

            jobSys.MindAddJob(mindId, "StationEngineer");
            afk.PlayerDidAction(session);
        });

        server.RunTicks(10);

        await EnableAndFireFirstSample(server, hysteresisTicks: 1);

        // A few more samples for good measure -- must stay covered/Dormant throughout.
        for (var i = 0; i < 2; i++)
            StepOneMonitorInterval(server);

        await server.WaitAssertion(() =>
        {
            Assert.That(CountLiveContractors(entMan), Is.EqualTo(0),
                "an engineer on a DIFFERENT grid of the SAME station must suppress dispatch station-wide");

            var system = entMan.System<ProvidencePowerContractorSystem>();
            Assert.That(system.LiveContractorCount, Is.EqualTo(0));
        });

        // Phase 2: move the SAME engineer to a SIBLING STATION's grid -- must NOT suppress dispatch.
        // The zone is still Dormant (never dispatched in Phase 1, so no redispatch cooldown is
        // running) -- a single fresh sample is enough.
        await server.WaitPost(() =>
        {
            var mob = session.AttachedEntity!.Value;
            xformSys.SetCoordinates(mob, siblingStationGrid.ToCoordinates(0, 0));
            afk.PlayerDidAction(session);
        });

        await WaitForLiveContractorCount(server, entMan, 1);

        await server.WaitAssertion(() =>
        {
            Assert.That(CountLiveContractors(entMan), Is.EqualTo(1),
                "an engineer physically present on a DIFFERENT (sibling) station must NOT suppress " +
                "dispatch for a station it isn't on");
        });
    }

    /// <summary>Exact solver-membership assertion + <c>PowerNetSystem.Validate()</c>: the contractor's
    /// leased supply must actually enter <c>PowerState.Network.Supplies</c> while live, and leave it
    /// again after recall -- not merely "supplier.Net is non-null" (the acceptance test's own check)
    /// but the network's own membership list containing/not-containing the exact NodeId. The whole
    /// Pow3r state must validate cleanly at every step.</summary>
    [Test]
    public async Task SolverMembership_ContractorSupplyEntersAndLeavesNetworkSupplies_ValidatesCleanly()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var xformSys = entMan.System<SharedTransformSystem>();
        var batterySys = entMan.System<BatterySystem>();
        var stationSys = entMan.System<StationSystem>();
        var powerNet = entMan.System<PowerNetSystem>();
        var jobSys = entMan.System<JobSystem>();
        var mindSys = entMan.System<SharedMindSystem>();
        var afk = server.ResolveDependency<IAfkManager>();

        EntityUid smesEnt = default;
        EntityUid gridUid = default;

        await server.WaitAssertion(() =>
        {
            var net = BuildFailingNetwork(entMan, mapSys, batterySys, stationSys, smesMaxSupply: 500f, consumerDrawRate: 2000f);
            smesEnt = net.SmesEnt;
            gridUid = net.Grid;
        });

        server.RunTicks(10);

        await EnableAndFireFirstSample(server, hysteresisTicks: 1);

        await WaitForLiveContractorCount(server, entMan, 1); // dispatch expected (already exhausted)

        EntityUid contractorUid = default;

        await server.WaitAssertion(() =>
        {
            var contractors = GetLiveContractorUids(entMan);
            Assert.That(contractors, Has.Count.EqualTo(1));
            contractorUid = contractors[0];
        });

        server.RunTicks(5); // let real node-group connectivity actually place the supplier in the network

        await server.WaitAssertion(() =>
        {
            var discharger = entMan.GetComponent<BatteryDischargerComponent>(smesEnt);
            Assert.That(discharger.Net, Is.Not.Null, "setup failed: the anchor must still resolve a network");
            var network = discharger.Net!.NetworkNode;

            var supplier = entMan.GetComponent<PowerSupplierComponent>(contractorUid);
            Assert.That(supplier.Net, Is.Not.Null);

            Assert.That(network.Supplies, Does.Contain(supplier.NetworkSupply.Id),
                "the contractor's supply must actually be a member of the network's own Supplies list " +
                "-- exact solver membership, not just a non-null Net reference");

            Assert.DoesNotThrow(() => powerNet.Validate(), "the whole Pow3r power state must validate cleanly while the lease is live");
        });

        // Force recall via human takeover -- the production-primary recall path, same idiom as the
        // acceptance test -- rather than an admin/round-end proxy.
        var session = ServerSession!;

        await server.WaitPost(() =>
        {
            var mob = session.AttachedEntity!.Value;
            xformSys.SetCoordinates(mob, gridUid.ToCoordinates(0, -3));

            var gotMind = mindSys.TryGetMind(mob, out var mindId, out _);
            Assert.That(gotMind, Is.True);

            jobSys.MindAddJob(mindId, "StationEngineer");
            afk.PlayerDidAction(session);
        });

        StepOneMonitorInterval(server); // hysteresisTicks=1 -- recall expected this sample

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(contractorUid), Is.False);

            var discharger = entMan.GetComponent<BatteryDischargerComponent>(smesEnt);
            Assert.That(discharger.Net, Is.Not.Null);
            var network = discharger.Net!.NetworkNode;

            Assert.That(network.Supplies, Has.Count.EqualTo(0),
                "the recalled contractor's supply must be gone from the network's Supplies list, not " +
                "just soft-deleted");

            Assert.DoesNotThrow(() => powerNet.Validate(), "the power state must still validate cleanly after recall");
        });
    }

    /// <summary>
    ///     RoundEnd cleanup, distinct from the round-RESTART test above: raising
    ///     <see cref="RoundEndMessageEvent"/> alone (without a full <c>RestartRound()</c>) must force a
    ///     live lease's recall (ClearNet + delete + cap release) but must NOT clear the zone dictionary
    ///     itself -- the zone survives (Dormant, cooling down) exactly as it would mid-round after any
    ///     other recall, ready for the round-restart's own HardResetAllZones to do the FULL reset later.
    /// </summary>
    [Test]
    public async Task RoundEnd_ForcesRecallOfLiveLease_ButKeepsZoneTrackedWithCooldown()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var batterySys = entMan.System<BatterySystem>();
        var stationSys = entMan.System<StationSystem>();

        await server.WaitAssertion(() =>
        {
            BuildFailingNetwork(entMan, mapSys, batterySys, stationSys, smesMaxSupply: 500f, consumerDrawRate: 2000f);
        });

        server.RunTicks(10);

        await EnableAndFireFirstSample(server, hysteresisTicks: 1, redispatchCooldownSeconds: 300f);

        await WaitForLiveContractorCount(server, entMan, 1); // dispatch expected (already exhausted)

        EntityUid contractorUid = default;

        await server.WaitAssertion(() =>
        {
            var contractors = GetLiveContractorUids(entMan);
            Assert.That(contractors, Has.Count.EqualTo(1));
            contractorUid = contractors[0];

            var system = entMan.System<ProvidencePowerContractorSystem>();
            Assert.That(system.ZoneCount, Is.EqualTo(1));
        });

        SuppressSeasonLedgerRoundEndNoise();
        await server.WaitPost(() =>
        {
            var ev = new RoundEndMessageEvent(
                gamemodeTitle: "Test",
                roundEndText: "round-end-only cleanup test",
                roundDuration: TimeSpan.Zero,
                roundId: 0,
                playerCount: 0,
                allPlayersEndInfo: Array.Empty<RoundEndMessageEvent.RoundEndPlayerInfo>(),
                restartSound: null);
            entMan.EventBus.RaiseEvent(EventSource.Local, ev);
        });

        server.RunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(contractorUid), Is.False,
                "RoundEndMessageEvent alone must still force-recall the live lease");
            Assert.That(CountLiveContractors(entMan), Is.EqualTo(0));

            var system = entMan.System<ProvidencePowerContractorSystem>();
            Assert.That(system.LiveContractorCount, Is.EqualTo(0));
            Assert.That(system.ZoneCount, Is.EqualTo(1),
                "RoundEndMessageEvent (unlike RoundRestartCleanupEvent) must NOT clear the zone itself " +
                "-- it stays tracked, Dormant, cooling down, ready for the round-restart's full reset");
        });
    }

    /// <summary>Disabling the master CVar mid-lease must immediately hard-reset: ClearNet the live
    /// supplier, delete the contractor, release the cap slot, and drop zone tracking entirely -- the
    /// same "leave zero footprint" guarantee <c>Update()</c> already documents for "never flip the
    /// enable CVar on," now proven for the "flip it back off" direction too.</summary>
    [Test]
    public async Task Disable_MidLease_HardResetsImmediately()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var batterySys = entMan.System<BatterySystem>();
        var stationSys = entMan.System<StationSystem>();

        await server.WaitAssertion(() =>
        {
            BuildFailingNetwork(entMan, mapSys, batterySys, stationSys, smesMaxSupply: 500f, consumerDrawRate: 2000f);
        });

        server.RunTicks(10);

        await EnableAndFireFirstSample(server, hysteresisTicks: 1);

        await WaitForLiveContractorCount(server, entMan, 1); // dispatch expected (already exhausted)

        EntityUid contractorUid = default;

        await server.WaitAssertion(() =>
        {
            var contractors = GetLiveContractorUids(entMan);
            Assert.That(contractors, Has.Count.EqualTo(1));
            contractorUid = contractors[0];
        });

        await server.WaitPost(() => server.CfgMan.SetCVar(CCVars.SolreignPowerContractorEnabled, false));

        server.RunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(contractorUid), Is.False,
                "disabling the master CVar mid-lease must delete the live contractor");
            Assert.That(CountLiveContractors(entMan), Is.EqualTo(0));

            var system = entMan.System<ProvidencePowerContractorSystem>();
            Assert.That(system.LiveContractorCount, Is.EqualTo(0));
            Assert.That(system.ZoneCount, Is.EqualTo(0),
                "disabling must drop zone tracking entirely -- zero footprint while off");
        });
    }

    /// <summary>Steady-state OFF: the feature's master CVar is NEVER flipped on for this test. A
    /// genuinely, badly failing network must produce zero footprint -- zero zones, zero contractors --
    /// no matter how many monitor-interval-equivalent ticks pass, proving <c>Update()</c>'s early-out
    /// really does perform zero network discovery while disabled (the default, production posture for
    /// this whole feature today).</summary>
    [Test]
    public async Task SteadyState_FeatureNeverEnabled_ZeroFootprintDespiteFailingNetwork()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var batterySys = entMan.System<BatterySystem>();
        var stationSys = entMan.System<StationSystem>();

        await server.WaitAssertion(() =>
        {
            BuildFailingNetwork(entMan, mapSys, batterySys, stationSys, smesMaxSupply: 500f, consumerDrawRate: 2000f);
        });

        // No EnableAndFireFirstSample call at all -- the CVar stays at its declared default (false).
        for (var i = 0; i < 5; i++)
            server.RunTicks(TicksPerMonitorInterval);

        await server.WaitAssertion(() =>
        {
            var system = entMan.System<ProvidencePowerContractorSystem>();
            Assert.That(system.ZoneCount, Is.EqualTo(0),
                "the feature must perform zero network discovery while its master CVar is off (the default)");
            Assert.That(system.LiveContractorCount, Is.EqualTo(0));
            Assert.That(CountLiveContractors(entMan), Is.EqualTo(0));
        });
    }

    /// <summary>
    ///     G6 production regression (box log 2026-08-02, 24 occurrences): AdvanceZones' public-station
    ///     priority sort calls GetOwningStation on every tracked anchor, and GetOwningStation throws
    ///     "Tried to use an abstract entity" once an anchor has been deleted (its transform is gone).
    ///     The loop below the sort already re-validates and cleans stale anchors; the sort ran first
    ///     and died every monitor tick. Contract: a deleted anchor among the zones must flow into the
    ///     loop's implicit-recall cleanup, never crash the sort.
    /// </summary>
    [Test]
    public async Task AdvanceZones_DeletedAnchorAmongZones_CleansUpInsteadOfThrowing()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var batterySys = entMan.System<BatterySystem>();
        var stationSys = entMan.System<StationSystem>();

        FailingNetwork netA = default!;

        await server.WaitAssertion(() =>
        {
            // Two stations, two failing networks -> two tracked zones, so the priority sort
            // actually invokes its comparer (a single-element sort never compares anything).
            netA = BuildFailingNetwork(entMan, mapSys, batterySys, stationSys, stationName: "g6-a");
            BuildFailingNetwork(entMan, mapSys, batterySys, stationSys, stationName: "g6-b");
        });

        // High hysteresis: zones get DISCOVERED by sampling but never reach dispatch,
        // keeping this test about the monitor loop itself.
        await EnableAndFireFirstSample(server, hysteresisTicks: 1000);

        // Discovery may take a few samples to see both failing networks; poll like
        // WaitForLiveContractorCount does rather than assuming the first sample caught them.
        for (var i = 0; i < 10; i++)
        {
            var count = 0;
            await server.WaitAssertion(() => count = entMan.System<ProvidencePowerContractorSystem>().ZoneCount);
            if (count >= 2)
                break;
            StepOneMonitorInterval(server);
        }

        await server.WaitAssertion(() =>
        {
            var system = entMan.System<ProvidencePowerContractorSystem>();
            Assert.That(system.ZoneCount, Is.EqualTo(2),
                "precondition: both anchors must be tracked before the deletion");

            entMan.DeleteEntity(netA.SmesEnt);
        });

        server.RunTicks(1); // let the deletion fully process; the anchor's transform is gone

        // The next monitor sample runs AdvanceZones over { deletedAnchor, liveAnchor } with a
        // resolvable public station. Unguarded, the priority sort throws ArgumentException inside
        // the comparer (the entsys ERROR the box logged every tick); the pooled harness fails this
        // test on that ERROR log. Guarded, the deleted anchor takes the implicit-recall cleanup path.
        StepOneMonitorInterval(server);

        await server.WaitAssertion(() =>
        {
            var system = entMan.System<ProvidencePowerContractorSystem>();
            Assert.That(system.ZoneCount, Is.EqualTo(1),
                "the deleted anchor's zone must be cleaned up by the advance loop, not crash it");
        });
    }
}
