#nullable enable
using System;
using Content.IntegrationTests.Fixtures;
using Content.Server.GameTicking;
using Content.Server.Station.Systems;
using Content.Shared.Materials;
using Content.Shared.Materials.OreSilo;
using Content.Shared.Stacks;
using Content.Shared.Station;
using Content.Shared.Station.Components;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests.Materials;

/// <summary>
/// Adversarial-review hardening pass for the unified station-wide material pool (see
/// <see cref="OreSiloStationPoolTest"/> for the baseline "silos share one bank" coverage, which a
/// DO-NOT-MERGE review correctly flagged as false reassurance - it never exercised any of the
/// exploit paths below). Covers:
///  1. Material duplication via a stale local balance surviving alongside the pool.
///  2. A forged <see cref="EjectMaterialMessage"/> addressed directly at the (globally networked)
///     station pool entity.
///  3. Non-lossy behavior across a mid-round station deletion, a real round restart, and a grid
///     leaving a station.
///  4. A foreign/unregistered grid never sharing another station's pool.
/// </summary>
[TestFixture]
[TestOf(typeof(OreSiloComponent))]
public sealed class OreSiloHardeningTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        // DummyTicker=false: one case drives a real GameTicker.RestartRound(), which early-returns
        // under a dummy ticker - the round-restart teardown only actually runs on a non-dummy
        // ticker (same setting SolreignHtnPlanQueueRoundRestartTest uses for the same reason).
        DummyTicker = false,
        Connected = true,
        Dirty = true,
    };

    private const string Material = "Steel";

    // One sheet of Steel is worth 100 material (Resources/Prototypes/Entities/Objects/Materials/
    // Sheets/metal.yml). Test amounts below are chosen as multiples of this so eject/spawn math
    // has no sub-sheet remainder to reason about.
    private const int SheetVolume = 100;

    // EntitySessionMessage<T> is `internal` to Robust.Shared ("internal is Content can't touch
    // this" - see RobustToolbox/Robust.Shared/AssemblyInfo.cs), so content code cannot construct
    // one directly even to fabricate a legitimate-looking incoming network message for a test.
    // The *public* untyped IEventBus.RaiseEvent(EventSource, object) overload dispatches purely
    // off toRaise.GetType() though (see EntityEventBus.Broadcast.cs), which is exactly what
    // ServerEntityManager itself does for every real incoming network message:
    // `ReceivedSystemMessage += (_, systemMsg) => EventBus.RaiseEvent(EventSource.Network, systemMsg);`
    // (RobustToolbox/Robust.Server/GameObjects/ServerEntityManager.cs). So this reflects only the
    // internal *type* (not any private member) and reproduces the real server-side dispatch path
    // a modified client's forged message would actually take, rather than approximating it.
    private static readonly Type SessionMessageType =
        typeof(EntitySystem).Assembly.GetType("Robust.Shared.GameObjects.EntitySessionMessage`1")
        ?? throw new MissingMemberException(
            "Robust.Shared.GameObjects.EntitySessionMessage<T> shape changed - update this test's reflection target.");

    private static void RaiseForgedSessionMessage<T>(IEntityManager entMan, ICommonSession session, T message)
        where T : notnull
    {
        var closed = SessionMessageType.MakeGenericType(typeof(T));
        var sessionMsg = Activator.CreateInstance(closed, new EntitySessionEventArgs(session), message)!;
        entMan.EventBus.RaiseEvent(EventSource.Network, sessionMsg);
    }

    /// <summary>
    /// Sets up a minimal "station" for redirect-logic testing: <see cref="SharedStationSystem.GetOwningStation"/>
    /// only ever looks at <see cref="StationMemberComponent"/>, so (like <see cref="OreSiloStationPoolTest"/>)
    /// a bare entity stands in unless a test specifically needs <see cref="StationDataComponent"/>
    /// (deletion/round-restart tests do, since our new cleanup handler keys off it - see
    /// <see cref="SetUpRealStation"/>).
    /// </summary>
    private static EntityUid SetUpBareStation(IEntityManager entMan, EntityUid grid, EntityCoordinates? at = null)
    {
        var station = at is { } coords
            ? entMan.SpawnEntity(null, coords)
            : entMan.SpawnEntity(null, MapCoordinates.Nullspace);

        entMan.EnsureComponent<StationMemberComponent>(grid, out var member);
        member.Station = station;
        entMan.Dirty(grid, member);
        return station;
    }

    /// <summary>
    /// Sets up a real station (with <see cref="StationDataComponent"/>) for tests that exercise
    /// station deletion / round-restart cleanup, which key off that component.
    /// </summary>
    private static EntityUid SetUpRealStation(IEntityManager entMan, EntityUid grid)
    {
        var station = entMan.SpawnEntity(null, MapCoordinates.Nullspace);

#pragma warning disable RA0002
        var stationData = entMan.EnsureComponent<StationDataComponent>(station);
        stationData.Grids.Add(grid);
#pragma warning restore RA0002
        entMan.Dirty(station, stationData);

        entMan.EnsureComponent<StationMemberComponent>(grid, out var member);
        member.Station = station;
        entMan.Dirty(grid, member);

        return station;
    }

    private static EntityUid SpawnSilo(IEntityManager entMan, EntityCoordinates coords)
    {
        return entMan.SpawnEntity("MachineMaterialSilo", coords);
    }

    [Test]
    public async Task MixedLocalAndPoolBalance_DebitCannotDoubleSpend()
    {
        // Reproduces the exploit's arithmetic directly: a silo whose LOCAL storage still holds
        // material (500) while its station's shared pool ALSO holds material (300, deposited via
        // a second silo). Before the fix, a single debit for the combined amount (800) would
        // succeed by clamping the pool-side debit against local storage's `existing`, silently
        // leaving the pool's 300 untouched even though the caller was told the full 800 was spent.
        var server = Server;
        var entMan = server.EntMan;
        var materialStorage = server.System<SharedMaterialStorageSystem>();

        var map = await Pair.CreateTestMap();

        EntityUid silo1 = default, silo2 = default;

        await server.WaitAssertion(() =>
        {
            silo1 = SpawnSilo(entMan, map.GridCoords);
            silo2 = SpawnSilo(entMan, map.GridCoords);

            // silo1 accumulates a LOCAL balance while its grid has no station yet.
            Assert.That(materialStorage.TryChangeMaterialAmount(silo1, Material, 500), Is.True);
            Assert.That(materialStorage.GetMaterialAmount(silo1, Material, localOnly: true), Is.EqualTo(500));

            // Now the grid gains a station. SetUpBareStation pokes StationMemberComponent directly
            // (it does NOT go through AddGridToStation), so the eager ownership-acquisition
            // migration hook doesn't fire here - silo1's local 500 is still sitting in local
            // storage. That's the exact hazardous state: local=500 AND pool populated separately.
            SetUpBareStation(entMan, map.Grid.Owner);

            // silo2 deposits straight into the (now-existing) shared pool.
            Assert.That(materialStorage.TryChangeMaterialAmount(silo2, Material, 300), Is.True);

            // The combined read is a single, consistent 800 (local 500 + pool 300) - NOT the
            // 1300 the old read-time migration produced by double-counting the local snapshot.
            Assert.That(materialStorage.GetMaterialAmount(silo1, Material), Is.EqualTo(800));

            // The actual exploit: debit the full combined amount in one shot, mimicking
            // EjectMaterial's debit call. The consume path migrates local 500 into the pool FIRST,
            // so the pool holds the whole 800 and the debit clears it. Before the fix this returned
            // true while leaving 300 stuck in the pool (the pool-side debit clamped against local).
            Assert.That(materialStorage.TryChangeMaterialAmount(silo1, Material, -800), Is.True);

            Assert.That(materialStorage.GetMaterialAmount(silo1, Material), Is.Zero);
            Assert.That(materialStorage.GetMaterialAmount(silo1, Material, localOnly: true), Is.Zero,
                "silo1's local storage must be empty after the migrating debit - it's not a second wallet.");
            Assert.That(materialStorage.GetMaterialAmount(silo2, Material), Is.Zero,
                "silo2 shares the same pool as silo1 - if any of the 800 were still sitting in the " +
                "pool uncollected, it would show up here.");
        });

        await server.WaitPost(() => entMan.System<SharedMapSystem>().DeleteMap(map.MapId));
    }

    [Test]
    public async Task HotMigration_EjectDoesNotDuplicateMaterial()
    {
        // The literal exploit path from the review: local=500, pool=300, eject "everything" (800).
        // Before the fix this spawned 800 worth of physical sheets while the pool's 300 was left
        // completely intact - i.e. 300 material duplicated out of thin air. This test drives the
        // real EjectMaterial() API (spawns real stack entities) rather than only checking amounts.
        var server = Server;
        var entMan = server.EntMan;
        var materialStorage = server.System<SharedMaterialStorageSystem>();
        var materialStorageServer = server.System<Content.Server.Materials.MaterialStorageSystem>();

        var map = await Pair.CreateTestMap();

        EntityUid silo1 = default, silo2 = default;
        var spawnedSheets = 0;

        await server.WaitAssertion(() =>
        {
            silo1 = SpawnSilo(entMan, map.GridCoords);
            silo2 = SpawnSilo(entMan, map.GridCoords);

            Assert.That(materialStorage.TryChangeMaterialAmount(silo1, Material, 500), Is.True);
            SetUpBareStation(entMan, map.Grid.Owner);
            Assert.That(materialStorage.TryChangeMaterialAmount(silo2, Material, 300), Is.True);

            // Nothing has touched silo1 since the station was set up - this is exactly the
            // "silo already had materials when it gains a station owner" scenario.
            var spawned = materialStorageServer.EjectMaterial(silo1, Material);

            foreach (var ent in spawned)
            {
                Assert.That(entMan.TryGetComponent<StackComponent>(ent, out var stack), Is.True);
                spawnedSheets += stack!.Count;
            }
        });

        // No duplication: exactly 8 sheets (800 / 100) worth of material exist in the world, and
        // NOTHING remains claimable through either silo (i.e. no leftover "phantom" pool balance).
        Assert.That(spawnedSheets, Is.EqualTo(800 / SheetVolume));

        await server.WaitAssertion(() =>
        {
            Assert.That(materialStorage.GetMaterialAmount(silo1, Material), Is.Zero);
            Assert.That(materialStorage.GetMaterialAmount(silo2, Material), Is.Zero);
        });

        await server.WaitPost(() => entMan.System<SharedMapSystem>().DeleteMap(map.MapId));
    }

    [Test]
    public async Task ForgedEjectMessage_AgainstStationPool_IsRejected()
    {
        // A modified client can send an EjectMaterialMessage naming ANY entity UID - including the
        // station entity itself, which is globally PVS-overridden (every client always has it
        // networked). Before the fix, the lazily-created pool defaulted
        // CanEjectStoredMaterials=true and the server handler checked only consciousness/action-
        // blockers - no range, no LOS, no "is this actually a silo's own authorized flow" check -
        // so this alone could drain the station's entire shared bank from anywhere.
        var server = Server;
        var entMan = server.EntMan;
        var materialStorage = server.System<SharedMaterialStorageSystem>();

        var map = await Pair.CreateTestMap();

        EntityUid silo = default, pool = default, mob = default;

        await server.WaitAssertion(() =>
        {
            silo = SpawnSilo(entMan, map.GridCoords);
            // Put the "station" stand-in at the SAME coordinates as the player below, so a range
            // check alone would NOT explain a rejection - isolating CanEjectStoredMaterials (the
            // hard requirement) as the thing actually blocking this.
            var station = SetUpBareStation(entMan, map.Grid.Owner, map.GridCoords);

            Assert.That(materialStorage.TryChangeMaterialAmount(silo, Material, 300), Is.True);

            var stationSystem = server.System<SharedStationSystem>();
            pool = stationSystem.GetOwningStation(silo)!.Value;
            Assert.That(pool, Is.EqualTo(station));

            var poolStorage = entMan.GetComponent<MaterialStorageComponent>(pool);
            Assert.That(poolStorage.CanEjectStoredMaterials, Is.False,
                "LockDownPool should have locked the pool's eject flag off the moment it was touched.");
            Assert.That(poolStorage.InsertOnInteract, Is.False);
            Assert.That(poolStorage.DropOnDeconstruct, Is.False);

            mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var players = server.ResolveDependency<IPlayerManager>();
            Assert.That(ServerSession, Is.Not.Null, "This test needs the pool's connected client session.");
            Assert.That(players.SetAttachedEntity(ServerSession!, mob), Is.True);
        });

        await Pair.RunTicksSync(5);

        var stackCountBefore = 0;
        await server.WaitAssertion(() =>
        {
            var query = entMan.EntityQueryEnumerator<StackComponent>();
            while (query.MoveNext(out _, out var stack))
                stackCountBefore += stack.Count;
        });

        await server.WaitAssertion(() =>
        {
            var forged = new EjectMaterialMessage(entMan.GetNetEntity(pool), Material, 3);
            RaiseForgedSessionMessage(entMan, ServerSession!, forged);
        });

        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            // The forged message must be a complete no-op: the pool's balance is untouched, and no
            // sheets were spawned as a side effect of the (rejected) eject.
            Assert.That(materialStorage.GetMaterialAmount(pool, Material, localOnly: true), Is.EqualTo(300));

            var stackCountAfter = 0;
            var query = entMan.EntityQueryEnumerator<StackComponent>();
            while (query.MoveNext(out _, out var stack))
                stackCountAfter += stack.Count;

            Assert.That(stackCountAfter, Is.EqualTo(stackCountBefore),
                "A forged EjectMaterialMessage against the station pool must not spawn anything.");
        });

        await server.WaitPost(() => entMan.System<SharedMapSystem>().DeleteMap(map.MapId));
    }

    [Test]
    public async Task MidRoundStationDeletion_DumpsPoolOntoSurvivingGrid_NoDupeNoDestroy()
    {
        // A station can be deleted mid-round (admin smite, a game rule tearing it down, etc)
        // without its grids necessarily going with it. Before the fix the pool's
        // MaterialStorageComponent - and everything in it - just vanished along with the station
        // entity. It must instead land on a surviving member grid, exactly once.
        var server = Server;
        var entMan = server.EntMan;
        var materialStorage = server.System<SharedMaterialStorageSystem>();

        var map = await Pair.CreateTestMap();

        EntityUid silo = default, station = default;

        await server.WaitAssertion(() =>
        {
            silo = SpawnSilo(entMan, map.GridCoords);
            station = SetUpRealStation(entMan, map.Grid.Owner);

            Assert.That(materialStorage.TryChangeMaterialAmount(silo, Material, 300), Is.True);
        });

        // Count only stacks physically on our own test grid - with a real (non-dummy) ticker the
        // wider world has unrelated stacks, so a world-wide count would be noisy.
        var testGrid = map.Grid.Owner;

        int CountSheetsOnTestGrid()
        {
            var total = 0;
            var query = entMan.EntityQueryEnumerator<StackComponent, TransformComponent>();
            while (query.MoveNext(out _, out var stack, out var xform))
            {
                if (xform.GridUid == testGrid)
                    total += stack.Count;
            }
            return total;
        }

        var sheetsBefore = 0;
        await server.WaitAssertion(() => sheetsBefore = CountSheetsOnTestGrid());

        await server.WaitPost(() => entMan.QueueDeleteEntity(station));
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(station), Is.False, "The station should actually be gone.");
            Assert.That(entMan.EntityExists(testGrid), Is.True, "Deleting the station must not take its grids with it.");

            // Exactly the 3 sheets (300 / 100) that were in the pool - never zero (destroyed),
            // never more (duplicated).
            Assert.That(CountSheetsOnTestGrid() - sheetsBefore, Is.EqualTo(300 / SheetVolume));
        });

        await server.WaitPost(() => entMan.System<SharedMapSystem>().DeleteMap(map.MapId));
    }

    [Test]
    public async Task RealRoundRestart_StationCleanup_DoesNotThrowOrLeak()
    {
        // Drives the actual round-restart machinery (the same call TestPair.Recycle.cs uses to
        // hand a server back to the pool between tests), which deletes every StationDataComponent
        // entity via GameRunLevelChangedEvent -> StationSystem.OnRoundEnd -> QueueDel. This proves
        // the new cleanup handler survives the real, everything-at-once teardown of a round
        // restart (grids/silos/station all going away together, in whatever order the flush
        // happens to process them) without throwing or leaving anything duplicated.
        var server = Server;
        var entMan = server.EntMan;
        var materialStorage = server.System<SharedMaterialStorageSystem>();

        var map = await Pair.CreateTestMap();

        EntityUid silo = default, station = default;

        await server.WaitAssertion(() =>
        {
            silo = SpawnSilo(entMan, map.GridCoords);
            station = SetUpRealStation(entMan, map.Grid.Owner);
            Assert.That(materialStorage.TryChangeMaterialAmount(silo, Material, 300), Is.True);
        });

        var gameTicker = entMan.System<GameTicker>();

        await server.WaitAssertion(() =>
        {
            // This is exactly what a real round restart does - see
            // SolreignHtnPlanQueueRoundRestartTest for the same pattern applied to a different
            // leaked-state class of bug.
            gameTicker.RestartRound();

            Assert.That(entMan.EntityExists(station), Is.False,
                "RestartRound()'s FlushEntities should have deleted the station along with everything else.");
            Assert.That(entMan.EntityExists(silo), Is.False);
        });

        // Belt and braces: confirm nothing throws on subsequent ticks (e.g. a component that
        // survived teardown in a half-migrated state resurfacing as an exception later).
        await server.WaitRunTicks(20);
    }

    [Test]
    public async Task UnregisteredForeignGrid_StaysLocal_DoesNotShareStationPool()
    {
        // A grid that is physically present but never registered as a station member (an
        // unrecognized/foreign docked shuttle, an unclaimed derelict, etc) must never be able to
        // read or write another station's shared pool, and must never expose its own local stock
        // to that station either. GetOwningStation() only ever looks at StationMemberComponent, so
        // this is a membership check, not a proximity one - a second, entirely un-stationed grid
        // is a faithful stand-in for "docked but unregistered" here without needing real
        // docking-joint machinery.
        var server = Server;
        var entMan = server.EntMan;
        var materialStorage = server.System<SharedMaterialStorageSystem>();

        var stationMap = await Pair.CreateTestMap();
        var foreignMap = await Pair.CreateTestMap();

        EntityUid stationSilo = default, foreignSilo = default;

        await server.WaitAssertion(() =>
        {
            stationSilo = SpawnSilo(entMan, stationMap.GridCoords);
            SetUpBareStation(entMan, stationMap.Grid.Owner);
            Assert.That(materialStorage.TryChangeMaterialAmount(stationSilo, Material, 500), Is.True);

            // foreignMap.Grid deliberately never gets a StationMemberComponent.
            foreignSilo = SpawnSilo(entMan, foreignMap.GridCoords);
            Assert.That(materialStorage.TryChangeMaterialAmount(foreignSilo, Material, 50), Is.True);

            // The foreign silo's deposit stayed local - it was never redirected anywhere.
            Assert.That(materialStorage.GetMaterialAmount(foreignSilo, Material, localOnly: true), Is.EqualTo(50));
            Assert.That(materialStorage.GetMaterialAmount(foreignSilo, Material), Is.EqualTo(50));

            // Neither pool leaks into the other.
            Assert.That(materialStorage.GetMaterialAmount(stationSilo, Material), Is.EqualTo(500));
            Assert.That(materialStorage.GetMaterialAmount(foreignSilo, Material), Is.EqualTo(50));
        });

        await server.WaitPost(() =>
        {
            var mapSystem = entMan.System<SharedMapSystem>();
            mapSystem.DeleteMap(stationMap.MapId);
            mapSystem.DeleteMap(foreignMap.MapId);
        });
    }

    [Test]
    public async Task GridRemovedFromStation_SiloRevertsToLocal_RemainingPoolUnaffected()
    {
        // A grid can leave a station mid-round (undocked, grid-split, etc) while the station
        // itself and its other member grids survive. The departing silo must not keep reading/
        // writing a pool it no longer belongs to (which would let it silently siphon a bank it
        // left), and the remaining station members' balance must be completely unaffected -
        // nothing duplicated, nothing destroyed.
        var server = Server;
        var entMan = server.EntMan;
        var materialStorage = server.System<SharedMaterialStorageSystem>();
        var stationSystem = server.System<StationSystem>();

        var mapKeep = await Pair.CreateTestMap();
        var mapLeaves = await Pair.CreateTestMap();

        EntityUid siloKeep = default, siloLeaves = default, station = default;

        await server.WaitAssertion(() =>
        {
            siloKeep = SpawnSilo(entMan, mapKeep.GridCoords);
            siloLeaves = SpawnSilo(entMan, mapLeaves.GridCoords);

            station = SetUpRealStation(entMan, mapKeep.Grid.Owner);
            stationSystem.AddGridToStation(station, mapLeaves.Grid.Owner);

            Assert.That(materialStorage.TryChangeMaterialAmount(siloKeep, Material, 400), Is.True);
            Assert.That(materialStorage.TryChangeMaterialAmount(siloLeaves, Material, 200), Is.True);

            // Shared pool: both silos see the combined 600 right now.
            Assert.That(materialStorage.GetMaterialAmount(siloKeep, Material), Is.EqualTo(600));
            Assert.That(materialStorage.GetMaterialAmount(siloLeaves, Material), Is.EqualTo(600));

            stationSystem.RemoveGridFromStation(station, mapLeaves.Grid.Owner);

            // The departing silo is back to local-only and reports zero (its own local storage was
            // always kept empty per the migrate-and-keep-empty invariant - it has nothing of its
            // own to fall back to, which is correct: it left its contribution behind in the pool
            // it can no longer reach).
            Assert.That(materialStorage.GetMaterialAmount(siloLeaves, Material), Is.Zero);
            Assert.That(materialStorage.GetMaterialAmount(siloLeaves, Material, localOnly: true), Is.Zero);

            // The remaining station member's balance is untouched: not duplicated, not destroyed.
            Assert.That(materialStorage.GetMaterialAmount(siloKeep, Material), Is.EqualTo(600));
        });

        await server.WaitPost(() =>
        {
            var mapSystem = entMan.System<SharedMapSystem>();
            mapSystem.DeleteMap(mapKeep.MapId);
            mapSystem.DeleteMap(mapLeaves.MapId);
        });
    }
}
