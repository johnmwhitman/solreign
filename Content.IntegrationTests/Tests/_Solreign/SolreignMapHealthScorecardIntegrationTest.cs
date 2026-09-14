using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Atmos.EntitySystems;
using Content.Server.GameTicking;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Spawners.Components;
using Content.Server.Station.Components;
using Content.Shared.Atmos;
using Content.Shared.CCVar;
using Content.Shared.Maps;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
/// SR-W-012 Phase A: the map-health scorecard. Reuses SR-W-011's reviewed map catalog
/// (<see cref="SolreignMapTestCatalog"/>) and the same load/dock/spawn mechanisms already proven by
/// <c>Content.IntegrationTests.Tests.PostMapInitTest.GameMapsLoadableTest</c> and
/// <c>Content.IntegrationTests.Tests.Power.StationPowerTests.TestApcLoad</c>, but scopes every query to
/// the loaded station's own member grids (<see cref="StationDataComponent.Grids"/>) and turns the two
/// gaps the audit flagged -- silently-passing total shuttle absence, and unscoped global spawn-point
/// queries -- into hard <see cref="MapHealthStatus.Fail"/> results. Each map is loaded exactly once,
/// and one bounded <c>MapHealthResultV1</c> JSON row is ALWAYS emitted before any assertion runs, so
/// the worst failures (unresolvable prototype, map-load exception, no station grid) still produce a
/// scorecard row. See docs/research/SR-W-012-MAP-HEALTH-GAP-AUDIT-2026-07-15.md.
/// </summary>
[TestFixture]
public sealed class SolreignMapHealthScorecardIntegrationTest : GameTest
{
    private const int SchemaVersion = 1;

    /// <summary>
    /// Matches the settle window <c>StationPowerTests.TestApcLoad</c> already uses: long enough for
    /// power to ramp up, short enough that nothing has tripped yet.
    /// </summary>
    private const float ApcSettleSeconds = 2f;

    /// <summary>
    /// Bounded readiness budget for check 6. Grid tile atmosphere is populated through
    /// <c>AtmosphereSystem</c>'s per-tick-budgeted invalidation queue (<c>atmos.max_process_time</c>,
    /// see <c>AtmosphereSystem.Processing.cs</c>/<c>ProcessRevalidate</c>), not synchronously at map
    /// load. Instead of a fixed sleep, the test polls (every
    /// <see cref="AtmosphereReadinessPollSeconds"/>) until every station-grid spawn tile has a
    /// resolvable mixture or this budget expires. A tile whose mixture never resolves inside the
    /// budget is evaluated anyway and fails the check -- expiry is a failure path, never a skip.
    /// </summary>
    private const float AtmosphereReadinessBudgetSeconds = 8f;

    /// <summary>Poll interval for the check-6 readiness condition.</summary>
    private const float AtmosphereReadinessPollSeconds = 0.5f;

    /// <summary>
    /// Reviewed, test-local breathable-oxygen partial-pressure floor in kPa. <c>AtmosphereSystem.
    /// IsMixtureProbablySafe</c> deliberately does not check gas composition (see its own remarks: "Note
    /// that oxygen mix isn't checked, but survival boxes make that not necessary."); Phase A adds this
    /// check because a map-health scorecard cannot assume a survival box is present. 16 kPa is the
    /// conventional real-world hypoxia-onset partial pressure (sea-level pO2 is ~21.3 kPa); this is a
    /// scorecard-local threshold, not a change to any core atmospherics constant.
    /// </summary>
    private const float MinimumSafeOxygenPartialPressureKpa = 16f;

    public override PoolSettings PoolSettings => new()
    {
        // Loading a real game map and letting its power network settle mutates enough global engine
        // state (map entities, power networks, atmosphere) that this pair must never be recycled --
        // same reasoning as StationPowerTests and SolreignPerfBaselineTest.
        Dirty = true,
    };

    [Test]
    [TestCaseSource(typeof(SolreignMapTestCatalog), nameof(SolreignMapTestCatalog.MapIds))]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.GridFill), false)]
    public async Task ProductionMapPassesPhaseAHardChecks(string mapProtoId)
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var ticker = entMan.System<GameTicker>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var mapLoader = entMan.System<MapLoaderSystem>();
        var shuttleSystem = entMan.System<ShuttleSystem>();
        var atmosSystem = entMan.System<AtmosphereSystem>();
        var turfSystem = entMan.System<TurfSystem>();
        var xformSystem = entMan.System<TransformSystem>();

        var failureCodes = new List<string>();
        var stationGridStatus = MapHealthStatus.Fail;
        var latejoinStatus = MapHealthStatus.NotEvaluated;
        var jobSpawnStatus = MapHealthStatus.NotEvaluated;
        var shuttleStatus = MapHealthStatus.NotEvaluated;
        var apcStatus = MapHealthStatus.NotEvaluated;
        var atmosStatus = MapHealthStatus.NotEvaluated;
        var stationGridCount = 0;
        var configuredJobCount = 0;
        var missingJobSpawnCount = 0;
        var latejoinSpawnCount = 0;
        var apcCount = 0;
        var overloadedApcCount = 0;
        var evaluatedSpawnTileCount = 0;
        var unsafeSpawnTileCount = 0;
        int minPlayers = 0, maxPlayers = -1;
        var stationGrids = new HashSet<EntityUid>();
        EntityUid? station = null;
        EntityUid? targetGrid = null;
        var loadedMapId = MapId.Nullspace;

        // Stage 1: prototype resolution, load, and checks 1-3. Deliberately NO assertions in here:
        // every failure is recorded as a bounded failure code so the scorecard row below is ALWAYS
        // emitted -- assertions run only after emission (a prototype/load abort must not mean "the
        // worst failures produce no row").
        await server.WaitPost(() =>
        {
            if (!protoMan.TryIndex<GameMapPrototype>(mapProtoId, out var mapProto))
            {
                AddFailureCodeOnce(failureCodes, MapHealthFailureCode.MapPrototypeUnresolved);
                return;
            }

            minPlayers = (int) mapProto.MinPlayers;
            maxPlayers = mapProto.MaxPlayers == uint.MaxValue ? -1 : (int) mapProto.MaxPlayers;

            try
            {
                var opts = DeserializationOptions.Default with { InitializeMaps = true };
                ticker.LoadGameMap(mapProto, out loadedMapId, opts);
            }
            catch
            {
                AddFailureCodeOnce(failureCodes, MapHealthFailureCode.MapLoadFailed);
                return;
            }

            // Same "largest station-member grid" resolution GameMapsLoadableTest already uses.
            var memberQuery = entMan.GetEntityQuery<StationMemberComponent>();
            var grids = mapSystem.GetAllGrids(loadedMapId).ToList();
            if (grids.Count == 0)
            {
                AddFailureCodeOnce(failureCodes, MapHealthFailureCode.NoStationGrid);
                return;
            }

            targetGrid = grids[0].Owner;
            var largest = 0f;
            foreach (var grid in grids)
            {
                if (!memberQuery.HasComponent(grid.Owner))
                    continue;

                var area = grid.Comp.LocalAABB.Width * grid.Comp.LocalAABB.Height;
                if (area > largest)
                {
                    largest = area;
                    targetGrid = grid.Owner;
                }
            }

            if (targetGrid is { } tg && entMan.TryGetComponent<StationMemberComponent>(tg, out var memberComp))
                station = memberComp.Station;

            if (station is { } st && entMan.TryGetComponent<StationDataComponent>(st, out var stationData))
                stationGrids.UnionWith(stationData.Grids);

            stationGridCount = stationGrids.Count;
            stationGridStatus = stationGridCount > 0 ? MapHealthStatus.Pass : MapHealthStatus.Fail;
            if (stationGridStatus == MapHealthStatus.Fail)
                AddFailureCodeOnce(failureCodes, MapHealthFailureCode.NoStationGrid);

            if (station is not { } stationUid)
                return;

            // Check 2: latejoin/container spawn presence, scoped to this station's own grids.
            latejoinSpawnCount = CountLatejoinSpawns(entMan, stationGrids);
            latejoinStatus = latejoinSpawnCount > 0 ? MapHealthStatus.Pass : MapHealthStatus.Fail;
            if (latejoinStatus == MapHealthStatus.Fail)
                AddFailureCodeOnce(failureCodes, MapHealthFailureCode.NoLatejoinSpawn);

            // Check 3: every configured round-start job has a spawn on one of THIS station's grids
            // (never the unscoped global entity query PostMapInitTest.GameMapsLoadableTest uses).
            // A missing StationJobsComponent -- or one with an empty job configuration -- is a hard
            // FAIL, not not_evaluated: a rotation map that configures no jobs at all cannot pass a
            // health check whose whole point is job-spawn integrity.
            if (entMan.TryGetComponent<StationJobsComponent>(stationUid, out var jobsComp)
                && jobsComp.SetupAvailableJobs is { Count: > 0 })
            {
                configuredJobCount = jobsComp.SetupAvailableJobs.Count;
                missingJobSpawnCount = CountMissingJobSpawns(entMan, stationGrids, jobsComp);
                jobSpawnStatus = missingJobSpawnCount == 0 ? MapHealthStatus.Pass : MapHealthStatus.Fail;
                if (jobSpawnStatus == MapHealthStatus.Fail)
                    AddFailureCodeOnce(failureCodes, MapHealthFailureCode.MissingJobSpawn);
            }
            else
            {
                jobSpawnStatus = MapHealthStatus.Fail;
                AddFailureCodeOnce(failureCodes, MapHealthFailureCode.NoJobConfiguration);
            }
        });

        // Check 5 needs a real settle window, same as StationPowerTests.TestApcLoad. This runs BEFORE
        // the emergency-shuttle dock so the power network is observed in pristine round-start state.
        await pair.RunSeconds(ApcSettleSeconds);

        await server.WaitPost(() =>
        {
            if (station is null)
                return;

            (apcStatus, apcCount, overloadedApcCount) = EvaluateApcLoad(entMan, stationGrids);
            if (apcStatus == MapHealthStatus.Fail)
                AddFailureCodeOnce(failureCodes, MapHealthFailureCode.ApcOverloaded);
        });

        // Check 6 readiness: poll (bounded) until every station-grid spawn tile's mixture resolves.
        // Budget expiry is not a skip -- evaluation below still runs and a never-resolving tile fails.
        if (station is not null)
        {
            var waited = 0f;
            while (waited < AtmosphereReadinessBudgetSeconds)
            {
                var ready = false;
                await server.WaitPost(() =>
                    ready = AllStationSpawnTileMixturesResolved(entMan, protoMan, atmosSystem, stationGrids));

                if (ready)
                    break;

                await pair.RunSeconds(AtmosphereReadinessPollSeconds);
                waited += AtmosphereReadinessPollSeconds;
            }
        }

        await server.WaitPost(() =>
        {
            if (station is null)
                return;

            (atmosStatus, evaluatedSpawnTileCount, unsafeSpawnTileCount) = EvaluateSpawnAtmosphere(
                entMan, protoMan, atmosSystem, turfSystem, xformSystem, stationGrids, failureCodes);
        });

        // Check 4 runs LAST: TryFTLDock physically re-parents and docks the shuttle onto the station
        // map on success, so running it earlier would let the docked shuttle contaminate the APC and
        // atmosphere observations above. The shuttle grid is explicitly deleted afterwards.
        await server.WaitPost(() =>
        {
            if (station is { } stationUid && targetGrid is { } dockTarget)
            {
                if (entMan.TryGetComponent<StationEmergencyShuttleComponent>(stationUid, out var evac))
                {
                    mapSystem.CreateMap(out var shuttleMap);
                    var loaded = mapLoader.TryLoadGrid(shuttleMap, evac.EmergencyShuttlePath, out var shuttle);
                    if (!loaded || shuttle is null)
                    {
                        shuttleStatus = MapHealthStatus.Fail;
                        AddFailureCodeOnce(failureCodes, MapHealthFailureCode.EmergencyShuttleLoadFailed);
                    }
                    else
                    {
                        var docked = shuttleSystem.TryFTLDock(shuttle.Value.Owner,
                            entMan.GetComponent<ShuttleComponent>(shuttle.Value.Owner),
                            dockTarget);
                        shuttleStatus = docked ? MapHealthStatus.Pass : MapHealthStatus.Fail;
                        if (!docked)
                            AddFailureCodeOnce(failureCodes, MapHealthFailureCode.EmergencyShuttleDockFailed);

                        // On success the shuttle now lives on the station's map, not shuttleMap --
                        // delete the grid entity itself, not just the scratch map.
                        entMan.DeleteEntity(shuttle.Value.Owner);
                    }

                    mapSystem.DeleteMap(shuttleMap);
                }
                else
                {
                    shuttleStatus = MapHealthStatus.Fail;
                    AddFailureCodeOnce(failureCodes, MapHealthFailureCode.NoEmergencyShuttleComponent);
                }
            }

            if (loadedMapId != MapId.Nullspace)
                mapSystem.DeleteMap(loadedMapId);
        });

        var result = new MapHealthResultV1(
            SchemaVersion,
            mapProtoId,
            minPlayers,
            maxPlayers,
            stationGridStatus,
            latejoinStatus,
            jobSpawnStatus,
            shuttleStatus,
            apcStatus,
            atmosStatus,
            MapHealthStatus.NotEvaluated, // stored-power runway: Phase A leaves this not_evaluated.
            stationGridCount,
            configuredJobCount,
            missingJobSpawnCount,
            latejoinSpawnCount,
            apcCount,
            overloadedApcCount,
            evaluatedSpawnTileCount,
            unsafeSpawnTileCount,
            failureCodes);

        // The row is emitted BEFORE any assertion so an expected health failure still produces its
        // bounded scorecard evidence.
        TestContext.Out.WriteLine(MapHealthResultV1Jsonl.ToLine(result));

        Assert.Multiple(() =>
        {
            Assert.That(result.StationGrid, Is.EqualTo(MapHealthStatus.Pass),
                $"{mapProtoId}: must resolve one station-owned grid.");
            Assert.That(result.LatejoinSpawn, Is.EqualTo(MapHealthStatus.Pass),
                $"{mapProtoId}: must have a latejoin/container spawn on a station grid.");
            Assert.That(result.JobSpawns, Is.EqualTo(MapHealthStatus.Pass),
                $"{mapProtoId}: must configure jobs and give every configured job a spawn on a station grid ({result.MissingJobSpawnCount} missing of {result.ConfiguredJobCount} configured).");
            Assert.That(result.EmergencyShuttle, Is.EqualTo(MapHealthStatus.Pass),
                $"{mapProtoId}: must have a working emergency shuttle.");
            Assert.That(result.ApcLoad, Is.EqualTo(MapHealthStatus.Pass),
                $"{mapProtoId}: no APC may start overloaded ({result.OverloadedApcCount} of {result.ApcCount}).");
            Assert.That(result.SpawnAtmosphere, Is.EqualTo(MapHealthStatus.Pass),
                $"{mapProtoId}: every job/latejoin spawn tile must be safe ({result.UnsafeSpawnTileCount} of {result.EvaluatedSpawnTileCount} unsafe).");
            Assert.That(result.StoredPowerRunway, Is.EqualTo(MapHealthStatus.NotEvaluated));
            Assert.That(result.FailureCodes, Is.Empty,
                $"{mapProtoId}: expected a clean scorecard row.");
        });
    }

    /// <summary>
    /// Injected-failure fixture (never in the rotation pool -- reuses <see cref="PoolManager.TestMap"/>,
    /// the "Empty" debug map already excluded from <c>GameMapsLoadableTest</c>'s own case list). Proves
    /// the APC-load detector and the pressure/temperature branch of the spawn-atmosphere detector fail
    /// for the intended reason, by driving the exact same engine state the detectors read
    /// (<see cref="ApcComponent.MaxLoad"/> / <see cref="PowerNetworkBatteryComponent.CurrentSupply"/>,
    /// and the spawn tile's real <c>GasMixture</c>) to a known-bad value immediately before evaluation
    /// -- so no engine tick runs between injection and the read that could "fix" it back to a passing
    /// state. The oxygen-composition branch has its own dedicated RED case
    /// (<see cref="InjectedOxygenOnlyFailureProvesOxygenBranch"/>) because a near-vacuum mixture fails
    /// on pressure before the oxygen calculation is ever reached.
    /// </summary>
    [Test]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.GridFill), false)]
    public async Task InjectedFailureFixtureProvesRedDetection()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var mapSystem = entMan.System<SharedMapSystem>();
        var atmosSystem = entMan.System<AtmosphereSystem>();
        var turfSystem = entMan.System<TurfSystem>();
        var xformSystem = entMan.System<TransformSystem>();

        var (loadedMapId, stationGrids, spawnUid) = await LoadRedFixtureMap();

        await pair.RunSeconds(ApcSettleSeconds);

        var apcStatus = MapHealthStatus.NotEvaluated;
        var apcCount = 0;
        var overloadedApcCount = 0;
        var atmosStatus = MapHealthStatus.NotEvaluated;
        var evaluatedSpawnTileCount = 0;
        var unsafeSpawnTileCount = 0;
        var failureCodes = new List<string>();

        await server.WaitAssertion(() =>
        {
            // Injected failure #1: an overloaded APC. A freshly spawned, unwired APC would otherwise
            // settle at ~0 current supply, so this drives both sides of the same comparison
            // EvaluateApcLoad reads (apc.MaxLoad >= battery.CurrentSupply) to a deterministic FAIL.
            var spawnCoords = entMan.GetComponent<TransformComponent>(spawnUid).Coordinates;
            var apcUid = entMan.SpawnEntity("APCBasic", spawnCoords);
            var apc = entMan.GetComponent<ApcComponent>(apcUid);
            var battery = entMan.EnsureComponent<PowerNetworkBatteryComponent>(apcUid);
            apc.MaxLoad = 1f;
            battery.CurrentSupply = 500f;

            // Injected failure #2: a near-vacuum spawn tile (trace inert nitrogen). This exercises the
            // pressure branch of IsTileMixtureProbablySafe -- the oxygen branch is proven separately.
            var spawnXform = entMan.GetComponent<TransformComponent>(spawnUid);
            var mixture = atmosSystem.GetTileMixture((spawnUid, spawnXform), excite: false);
            Assert.That(mixture, Is.Not.Null,
                $"Fixture spawn tile on '{PoolManager.TestMap}' must have a real gas mixture to corrupt.");
            Assert.That(mixture!.Immutable, Is.False,
                "Fixture spawn tile mixture must be mutable, or the injection would silently no-op.");
            mixture.Clear();
            mixture.AdjustMoles(Gas.Nitrogen, 2f);
            mixture.Temperature = Atmospherics.T20C;

            // Evaluate immediately -- no ticks run between injection and evaluation, and everything
            // stays on the server thread (same idiom as the production-map check).
            (apcStatus, apcCount, overloadedApcCount) = EvaluateApcLoad(entMan, stationGrids);
            (atmosStatus, evaluatedSpawnTileCount, unsafeSpawnTileCount) = EvaluateSpawnAtmosphere(
                entMan, protoMan, atmosSystem, turfSystem, xformSystem, stationGrids, failureCodes);

            if (loadedMapId != MapId.Nullspace)
                mapSystem.DeleteMap(loadedMapId);
        });

        Assert.Multiple(() =>
        {
            Assert.That(apcCount, Is.GreaterThan(0), "Fixture must have evaluated at least one APC.");
            Assert.That(apcStatus, Is.EqualTo(MapHealthStatus.Fail),
                "Injected APC overload must be detected as FAIL -- proves the APC-load detector works.");
            Assert.That(overloadedApcCount, Is.GreaterThanOrEqualTo(1));

            Assert.That(evaluatedSpawnTileCount, Is.GreaterThan(0),
                "Fixture must have evaluated at least one spawn tile.");
            Assert.That(atmosStatus, Is.EqualTo(MapHealthStatus.Fail),
                "Injected near-vacuum spawn tile must be detected as FAIL -- proves the pressure branch works.");
            Assert.That(unsafeSpawnTileCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(failureCodes, Does.Contain(MapHealthFailureCode.UnsafeSpawnTileAtmosphere),
                "The near-vacuum injection must fail specifically on the pressure/temperature branch.");
        });
    }

    /// <summary>
    /// Dedicated RED case for the oxygen-composition branch: a normobaric, safe-temperature,
    /// oxygen-free mixture passes <c>IsTileMixtureProbablySafe</c> (which only checks pressure and
    /// temperature) and can therefore only be caught by the oxygen partial-pressure check. This
    /// asserts <see cref="MapHealthFailureCode.UnsafeSpawnTileOxygen"/> specifically, so deleting the
    /// oxygen branch would turn this test red -- unlike a combined vacuum injection, which fails on
    /// pressure before the oxygen calculation runs.
    /// </summary>
    [Test]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.GridFill), false)]
    public async Task InjectedOxygenOnlyFailureProvesOxygenBranch()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var mapSystem = entMan.System<SharedMapSystem>();
        var atmosSystem = entMan.System<AtmosphereSystem>();
        var turfSystem = entMan.System<TurfSystem>();
        var xformSystem = entMan.System<TransformSystem>();

        var (loadedMapId, stationGrids, spawnUid) = await LoadRedFixtureMap();

        await pair.RunSeconds(ApcSettleSeconds);

        var atmosStatus = MapHealthStatus.NotEvaluated;
        var evaluatedSpawnTileCount = 0;
        var unsafeSpawnTileCount = 0;
        var failureCodes = new List<string>();

        await server.WaitAssertion(() =>
        {
            // One standard cell's worth of pure nitrogen at 20C: ~101 kPa (normobaric), 293.15 K
            // (safe temperature), zero oxygen. Passes the pressure and temperature gates; only the
            // oxygen partial-pressure check can catch it.
            var spawnXform = entMan.GetComponent<TransformComponent>(spawnUid);
            var mixture = atmosSystem.GetTileMixture((spawnUid, spawnXform), excite: false);
            Assert.That(mixture, Is.Not.Null,
                $"Fixture spawn tile on '{PoolManager.TestMap}' must have a real gas mixture to corrupt.");
            Assert.That(mixture!.Immutable, Is.False,
                "Fixture spawn tile mixture must be mutable, or the injection would silently no-op.");
            mixture.Clear();
            mixture.AdjustMoles(Gas.Nitrogen, Atmospherics.MolesCellStandard);
            mixture.Temperature = Atmospherics.T20C;

            (atmosStatus, evaluatedSpawnTileCount, unsafeSpawnTileCount) = EvaluateSpawnAtmosphere(
                entMan, protoMan, atmosSystem, turfSystem, xformSystem, stationGrids, failureCodes);

            if (loadedMapId != MapId.Nullspace)
                mapSystem.DeleteMap(loadedMapId);
        });

        Assert.Multiple(() =>
        {
            Assert.That(evaluatedSpawnTileCount, Is.GreaterThan(0),
                "Fixture must have evaluated at least one spawn tile.");
            Assert.That(atmosStatus, Is.EqualTo(MapHealthStatus.Fail),
                "Normobaric oxygen-free spawn tile must be detected as FAIL.");
            Assert.That(unsafeSpawnTileCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(failureCodes, Does.Contain(MapHealthFailureCode.UnsafeSpawnTileOxygen),
                "The oxygen-free injection must fail specifically on the oxygen branch.");
            Assert.That(failureCodes, Does.Not.Contain(MapHealthFailureCode.UnsafeSpawnTileAtmosphere),
                "A normobaric safe-temperature mixture must NOT trip the pressure/temperature branch.");
            Assert.That(failureCodes, Does.Not.Contain(MapHealthFailureCode.UnsafeSpawnTileSpace));
        });
    }

    /// <summary>
    /// Targeted regression for distinct-tile counting: "Empty" already ships two spawn markers
    /// (SpawnPointAnyJob + SpawnPointLatejoin) on the SAME tile, and this test adds a third. If the
    /// per-(grid, tile) deduplication were reverted to per-entity counting, the evaluated count would
    /// read 3 instead of 1.
    /// </summary>
    [Test]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.GridFill), false)]
    public async Task DuplicateSpawnMarkersOnOneTileCountOnce()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var mapSystem = entMan.System<SharedMapSystem>();
        var atmosSystem = entMan.System<AtmosphereSystem>();
        var turfSystem = entMan.System<TurfSystem>();
        var xformSystem = entMan.System<TransformSystem>();

        var (loadedMapId, stationGrids, spawnUid) = await LoadRedFixtureMap();

        await pair.RunSeconds(ApcSettleSeconds);

        var evaluatedSpawnTileCount = 0;

        await server.WaitAssertion(() =>
        {
            var spawnCoords = entMan.GetComponent<TransformComponent>(spawnUid).Coordinates;
            entMan.SpawnEntity("SpawnPointLatejoin", spawnCoords); // third marker, same tile

            (_, evaluatedSpawnTileCount, _) = EvaluateSpawnAtmosphere(
                entMan, protoMan, atmosSystem, turfSystem, xformSystem, stationGrids, new List<string>());

            if (loadedMapId != MapId.Nullspace)
                mapSystem.DeleteMap(loadedMapId);
        });

        Assert.That(evaluatedSpawnTileCount, Is.EqualTo(1),
            "Three spawn markers on one tile must count as ONE evaluated tile, not three.");
    }

    /// <summary>
    /// Targeted regression for the atmospheric exemption and its OR-merge: an exempt borg-type spawn
    /// on a vacuum tile must NOT trip the atmosphere check, but the moment a non-exempt spawn shares
    /// that same tile, the tile regains the full check and fails. Reverting either the exemption or
    /// the OR-merge turns this red.
    /// </summary>
    [Test]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.GridFill), false)]
    public async Task ExemptSpawnSkipsAtmosphereButSharedTileKeepsFullCheck()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var mapSystem = entMan.System<SharedMapSystem>();
        var atmosSystem = entMan.System<AtmosphereSystem>();
        var turfSystem = entMan.System<TurfSystem>();
        var xformSystem = entMan.System<TransformSystem>();
        var tileDefMan = server.ResolveDependency<Robust.Shared.Map.ITileDefinitionManager>();

        var (loadedMapId, stationGrids, spawnUid) = await LoadRedFixtureMap();

        await pair.RunSeconds(ApcSettleSeconds);

        EntityUid gridUid = default;
        var vacuumTile = Vector2i.Zero;

        await server.WaitAssertion(() =>
        {
            // Lay a brand-new floor tile next door: real turf (non-space) whose atmosphere will
            // materialize over the settle below. Gas contents are forced later, tick-free.
            var spawnXform = entMan.GetComponent<TransformComponent>(spawnUid);
            gridUid = spawnXform.GridUid!.Value;
            var stockTile = xformSystem.GetGridTilePositionOrDefault((spawnUid, spawnXform));
            vacuumTile = stockTile + new Vector2i(1, 0);
            var grid = entMan.GetComponent<Robust.Shared.Map.Components.MapGridComponent>(gridUid);
            mapSystem.SetTile((gridUid, grid), vacuumTile,
                new Robust.Shared.Map.Tile(tileDefMan["FloorSteel"].TileId));
        });

        // Let atmos process the new tile's invalidation so its mixture materializes as mutable.
        await pair.RunSeconds(1f);

        var exemptOnlyStatus = MapHealthStatus.NotEvaluated;
        var exemptOnlyEvaluated = 0;
        var exemptOnlyUnsafe = 0;
        var mergedUnsafe = 0;
        var mergedCodes = new List<string>();

        await server.WaitAssertion(() =>
        {
            // Force BOTH tiles' gas state now, in the same tick-free block as the evaluations, so
            // atmos equalization between the adjacent tiles cannot smear the setup: the stock tile
            // gets fully breathable air (21/79 at ~1 atm; oxygen pp ~21 kPa clears the 16 kPa floor),
            // the new tile is cleared to hard vacuum.
            var spawnXform = entMan.GetComponent<TransformComponent>(spawnUid);
            var stockMixture = atmosSystem.GetTileMixture((spawnUid, spawnXform), excite: false);
            Assert.That(stockMixture, Is.Not.Null);
            Assert.That(stockMixture!.Immutable, Is.False);
            stockMixture.Clear();
            stockMixture.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            stockMixture.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            stockMixture.Temperature = Atmospherics.T20C;

            var vacuumMixture = atmosSystem.GetTileMixture(gridUid, spawnXform.MapUid!.Value, vacuumTile, excite: false);
            Assert.That(vacuumMixture, Is.Not.Null, "The new floor tile's mixture must have materialized.");
            Assert.That(vacuumMixture!.Immutable, Is.False,
                "The new floor tile's mixture must be mutable, or the vacuum setup would silently no-op.");
            vacuumMixture.Clear();

            var vacuumCoords = new EntityCoordinates(gridUid,
                new System.Numerics.Vector2(vacuumTile.X + 0.5f, vacuumTile.Y + 0.5f));

            // Phase 1: ONLY an exempt borg-type job spawn sits on the vacuum tile.
            entMan.SpawnEntity("SpawnPointBorg", vacuumCoords);

            (exemptOnlyStatus, exemptOnlyEvaluated, exemptOnlyUnsafe) = EvaluateSpawnAtmosphere(
                entMan, protoMan, atmosSystem, turfSystem, xformSystem, stationGrids, new List<string>());

            // Phase 2: a non-exempt latejoin marker joins the SAME vacuum tile -- the OR-merge must
            // restore the full check for that tile.
            entMan.SpawnEntity("SpawnPointLatejoin", vacuumCoords);

            (_, _, mergedUnsafe) = EvaluateSpawnAtmosphere(
                entMan, protoMan, atmosSystem, turfSystem, xformSystem, stationGrids, mergedCodes);

            if (loadedMapId != MapId.Nullspace)
                mapSystem.DeleteMap(loadedMapId);
        });

        Assert.Multiple(() =>
        {
            Assert.That(exemptOnlyEvaluated, Is.EqualTo(2),
                "Stock tile + vacuum tile must both be counted as evaluated tiles.");
            Assert.That(exemptOnlyUnsafe, Is.Zero,
                "An exempt borg-type spawn on a vacuum (non-space) tile must NOT trip the atmosphere check.");
            Assert.That(exemptOnlyStatus, Is.EqualTo(MapHealthStatus.Pass));

            Assert.That(mergedUnsafe, Is.EqualTo(1),
                "A non-exempt spawn sharing the vacuum tile must restore the full check and fail it.");
            Assert.That(mergedCodes, Does.Contain(MapHealthFailureCode.UnsafeSpawnTileAtmosphere));
        });
    }

    /// <summary>
    /// Loads the RED-fixture map (<see cref="PoolManager.TestMap"/>) and picks a spawn point that is
    /// actually ON one of the loaded map's grids. The dirty test pool may already contain other maps
    /// (including another "Empty" instance) with their own spawn points, so a global
    /// first-spawn-point query could select an entity outside <c>stationGrids</c> and the injections
    /// would be silently ignored by the grid-scoped evaluators. The "Empty" grid also ships without a
    /// <c>GridAtmosphereComponent</c>, in which case <c>GetTileMixture</c> falls back to a fresh
    /// IMMUTABLE <c>GasMixture.SpaceGas</c> instance per call -- injections into that are silent
    /// no-ops -- so the component is ensured here (its <c>ComponentStartup</c> invalidates every tile,
    /// and the callers' settle window lets processing populate real, mutable per-tile mixtures).
    /// </summary>
    private async Task<(MapId LoadedMapId, HashSet<EntityUid> StationGrids, EntityUid SpawnUid)> LoadRedFixtureMap()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var ticker = entMan.System<GameTicker>();
        var mapSystem = entMan.System<SharedMapSystem>();

        var loadedMapId = MapId.Nullspace;
        var stationGrids = new HashSet<EntityUid>();
        EntityUid spawnUid = default;

        await server.WaitAssertion(() =>
        {
            Assert.That(protoMan.TryIndex<GameMapPrototype>(PoolManager.TestMap, out var mapProto),
                $"Fixture map '{PoolManager.TestMap}' must resolve to one GameMapPrototype.");

            var opts = DeserializationOptions.Default with { InitializeMaps = true };
            ticker.LoadGameMap(mapProto!, out loadedMapId, opts);

            foreach (var grid in mapSystem.GetAllGrids(loadedMapId))
            {
                stationGrids.Add(grid.Owner);
                entMan.EnsureComponent<Content.Shared.Atmos.Components.GridAtmosphereComponent>(grid.Owner);
            }

            // "Empty" ships exactly one SpawnPointAnyJob + one SpawnPointLatejoin at the same tile --
            // pick one that is actually on OUR loaded grids, never just the first global result.
            var found = false;
            var spawnQuery = entMan.AllEntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
            while (spawnQuery.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid is { } gridUid && stationGrids.Contains(gridUid))
                {
                    spawnUid = uid;
                    found = true;
                    break;
                }
            }

            Assert.That(found, Is.True,
                $"Fixture map '{PoolManager.TestMap}' must have a SpawnPointComponent on its own grids to corrupt.");
        });

        return (loadedMapId, stationGrids, spawnUid);
    }

    private static void AddFailureCodeOnce(List<string> codes, string code)
    {
        if (!codes.Contains(code))
            codes.Add(code);
    }

    private static int CountLatejoinSpawns(IEntityManager entMan, HashSet<EntityUid> stationGrids)
    {
        var count = 0;

        var spawnPoints = entMan.AllEntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        while (spawnPoints.MoveNext(out _, out var spawn, out var xform))
        {
            if (spawn.SpawnType == SpawnPointType.LateJoin
                && xform.GridUid is { } gridUid
                && stationGrids.Contains(gridUid))
            {
                count++;
            }
        }

        var containerSpawnPoints = entMan.AllEntityQueryEnumerator<ContainerSpawnPointComponent, TransformComponent>();
        while (containerSpawnPoints.MoveNext(out _, out var spawn, out var xform))
        {
            if (spawn.SpawnType == SpawnPointType.LateJoin
                && xform.GridUid is { } gridUid
                && stationGrids.Contains(gridUid))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountMissingJobSpawns(
        IEntityManager entMan,
        HashSet<EntityUid> stationGrids,
        StationJobsComponent jobsComp)
    {
        var jobs = new HashSet<ProtoId<JobPrototype>>(jobsComp.SetupAvailableJobs.Keys);

        var spawnPoints = entMan.AllEntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        while (spawnPoints.MoveNext(out _, out var spawn, out var xform))
        {
            if (spawn.SpawnType == SpawnPointType.Job
                && spawn.Job is { } job
                && xform.GridUid is { } gridUid
                && stationGrids.Contains(gridUid))
            {
                jobs.Remove(job);
            }
        }

        var containerSpawnPoints = entMan.AllEntityQueryEnumerator<ContainerSpawnPointComponent, TransformComponent>();
        while (containerSpawnPoints.MoveNext(out _, out var spawn, out var xform))
        {
            if (spawn.SpawnType is SpawnPointType.Job or SpawnPointType.Unset
                && spawn.Job is { } job
                && xform.GridUid is { } gridUid
                && stationGrids.Contains(gridUid))
            {
                jobs.Remove(job);
            }
        }

        return jobs.Count;
    }

    private static (MapHealthStatus Status, int ApcCount, int OverloadedApcCount) EvaluateApcLoad(
        IEntityManager entMan,
        HashSet<EntityUid> stationGrids)
    {
        var apcCount = 0;
        var overloaded = 0;

        var query = entMan.EntityQueryEnumerator<ApcComponent, PowerNetworkBatteryComponent, TransformComponent>();
        while (query.MoveNext(out _, out var apc, out var battery, out var xform))
        {
            if (xform.GridUid is not { } gridUid || !stationGrids.Contains(gridUid))
                continue;

            apcCount++;
            if (battery.CurrentSupply > apc.MaxLoad)
                overloaded++;
        }

        if (apcCount == 0)
            return (MapHealthStatus.NotEvaluated, 0, 0);

        return (overloaded == 0 ? MapHealthStatus.Pass : MapHealthStatus.Fail, apcCount, overloaded);
    }

    /// <summary>
    /// Readiness condition for check 6: true once every non-exempt station-grid spawn tile has a REAL,
    /// mutable gas mixture. Any-non-null is not sufficient: while a tile hasn't materialized,
    /// <c>AtmosphereSystem.GetTileMixture</c> can return the immutable <c>GasMixture.SpaceGas</c>
    /// sentinel (or an immutable map/space mixture), so a null/mutability-blind poll exits immediately
    /// and can false-RED slow initialization. Atmospherically exempt spawns (fixed non-breathing job
    /// entities, e.g. borgs -- which may legitimately sit on immutable space-like mixtures forever)
    /// are skipped so they cannot burn the whole budget.
    /// </summary>
    private static bool AllStationSpawnTileMixturesResolved(
        IEntityManager entMan,
        IPrototypeManager protoMan,
        AtmosphereSystem atmosSystem,
        HashSet<EntityUid> stationGrids)
    {
        var spawnPoints = entMan.AllEntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        while (spawnPoints.MoveNext(out var uid, out var spawn, out var xform))
        {
            if (spawn.SpawnType is not (SpawnPointType.Job or SpawnPointType.LateJoin))
                continue;
            if (xform.GridUid is not { } gridUid || !stationGrids.Contains(gridUid))
                continue;
            if (spawn.SpawnType == SpawnPointType.Job && IsAtmosphericallyExemptJobSpawn(protoMan, spawn.Job))
                continue;
            if (atmosSystem.GetTileMixture((uid, xform), excite: false) is not { Immutable: false })
                return false;
        }

        var containerSpawnPoints = entMan.AllEntityQueryEnumerator<ContainerSpawnPointComponent, TransformComponent>();
        while (containerSpawnPoints.MoveNext(out var uid, out var spawn, out var xform))
        {
            if (spawn.SpawnType is not (SpawnPointType.Job or SpawnPointType.LateJoin or SpawnPointType.Unset))
                continue;
            if (xform.GridUid is not { } gridUid || !stationGrids.Contains(gridUid))
                continue;
            if (spawn.SpawnType is SpawnPointType.Job or SpawnPointType.Unset
                && IsAtmosphericallyExemptJobSpawn(protoMan, spawn.Job))
            {
                continue;
            }
            if (atmosSystem.GetTileMixture((uid, xform), excite: false) is not { Immutable: false })
                return false;
        }

        return true;
    }

    /// <summary>
    /// A spawn is exempt from the pressure/temperature/oxygen portions of check 6 only when it spawns
    /// a FIXED job entity with no atmospheric needs: nothing to asphyxiate (<c>Respirator</c>), no
    /// pressure damage (<c>Barotrauma</c>), no ambient temperature damage (<c>TemperatureDamage</c>).
    /// Latejoin/AnyJob markers (<paramref name="job"/> null) and profile-based jobs
    /// (<c>JobPrototype.JobEntity</c> null, i.e. a humanoid built from the player profile) are NEVER
    /// exempt. Testing the entity prototype's actual capabilities -- rather than hardcoding
    /// job == Borg -- means a future breathing/pressure-sensitive JobEntity automatically re-enters
    /// the full check. Exempt spawns keep the station-grid membership and non-space walkable-tile
    /// requirements.
    /// </summary>
    private static bool IsAtmosphericallyExemptJobSpawn(IPrototypeManager protoMan, ProtoId<JobPrototype>? job)
    {
        if (job is not { } jobId || !protoMan.TryIndex(jobId, out var jobProto))
            return false;

        if (jobProto.JobEntity is not { } jobEntityId || !protoMan.TryIndex(jobEntityId, out var entityProto))
            return false;

        return !entityProto.Components.ContainsKey("Respirator")
               && !entityProto.Components.ContainsKey("Barotrauma")
               && !entityProto.Components.ContainsKey("TemperatureDamage");
    }

    /// <summary>
    /// Check 6: every distinct job/latejoin/container spawn TILE on a station grid must be a non-space
    /// tile; non-exempt tiles must additionally pass
    /// <see cref="AtmosphereSystem.IsTileMixtureProbablySafe"/> (pressure + temperature) and clear
    /// <see cref="MinimumSafeOxygenPartialPressureKpa"/> (composition -- deliberately not covered by
    /// <c>IsMixtureProbablySafe</c>, see its own remarks). Counts are per distinct tile, not per spawn
    /// entity, so duplicate markers on one tile cannot double-count; a tile shared by an exempt and a
    /// non-exempt spawn keeps the full check.
    /// </summary>
    private static (MapHealthStatus Status, int Evaluated, int Unsafe) EvaluateSpawnAtmosphere(
        IEntityManager entMan,
        IPrototypeManager protoMan,
        AtmosphereSystem atmosSystem,
        TurfSystem turfSystem,
        TransformSystem xformSystem,
        HashSet<EntityUid> stationGrids,
        List<string> failureCodes)
    {
        var tiles = new Dictionary<(EntityUid Grid, Vector2i Tile),
            (EntityUid Uid, TransformComponent Xform, bool FullAtmosCheck)>();

        void Collect(EntityUid uid, TransformComponent xform, ProtoId<JobPrototype>? job)
        {
            if (xform.GridUid is not { } gridUid || !stationGrids.Contains(gridUid))
                return;

            var tile = xformSystem.GetGridTilePositionOrDefault((uid, xform));
            var fullCheck = !IsAtmosphericallyExemptJobSpawn(protoMan, job);

            if (tiles.TryGetValue((gridUid, tile), out var existing))
                tiles[(gridUid, tile)] = (existing.Uid, existing.Xform, existing.FullAtmosCheck || fullCheck);
            else
                tiles[(gridUid, tile)] = (uid, xform, fullCheck);
        }

        var jobSpawnQuery = entMan.AllEntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        while (jobSpawnQuery.MoveNext(out var uid, out var spawn, out var xform))
        {
            if (spawn.SpawnType is SpawnPointType.Job or SpawnPointType.LateJoin)
                Collect(uid, xform, spawn.SpawnType == SpawnPointType.Job ? spawn.Job : null);
        }

        var containerSpawnQuery = entMan.AllEntityQueryEnumerator<ContainerSpawnPointComponent, TransformComponent>();
        while (containerSpawnQuery.MoveNext(out var uid, out var spawn, out var xform))
        {
            if (spawn.SpawnType is SpawnPointType.Job or SpawnPointType.LateJoin or SpawnPointType.Unset)
                Collect(uid, xform, spawn.SpawnType is SpawnPointType.Job or SpawnPointType.Unset ? spawn.Job : null);
        }

        var unsafeCount = 0;

        foreach (var ((gridUid, tile), (uid, xform, fullAtmosCheck)) in tiles)
        {
            // Station-grid membership was enforced at collection; the walkable non-space tile
            // requirement applies to EVERY spawn, exempt or not.
            var tileRef = turfSystem.GetTileRef(xform.Coordinates);
            if (tileRef is null || turfSystem.IsSpace(tileRef.Value))
            {
                unsafeCount++;
                AddFailureCodeOnce(failureCodes, MapHealthFailureCode.UnsafeSpawnTileSpace);
                continue;
            }

            if (!fullAtmosCheck)
                continue; // Atmospherically exempt (fixed non-breathing job entity, e.g. borg).

            if (xform.MapUid is not { } mapUid)
            {
                unsafeCount++;
                AddFailureCodeOnce(failureCodes, MapHealthFailureCode.UnsafeSpawnTileAtmosphere);
                continue;
            }

            if (!atmosSystem.IsTileMixtureProbablySafe(gridUid, mapUid, tile))
            {
                unsafeCount++;
                AddFailureCodeOnce(failureCodes, MapHealthFailureCode.UnsafeSpawnTileAtmosphere);
                continue;
            }

            var mixture = atmosSystem.GetTileMixture((uid, xform), excite: false);
            var oxygenPartialPressure = mixture is { TotalMoles: > 0 }
                ? mixture.Pressure * (mixture.GetMoles(Gas.Oxygen) / mixture.TotalMoles)
                : 0f;

            if (oxygenPartialPressure < MinimumSafeOxygenPartialPressureKpa)
            {
                unsafeCount++;
                AddFailureCodeOnce(failureCodes, MapHealthFailureCode.UnsafeSpawnTileOxygen);
            }
        }

        if (tiles.Count == 0)
            return (MapHealthStatus.NotEvaluated, 0, 0);

        return (unsafeCount == 0 ? MapHealthStatus.Pass : MapHealthStatus.Fail, tiles.Count, unsafeCount);
    }
}
