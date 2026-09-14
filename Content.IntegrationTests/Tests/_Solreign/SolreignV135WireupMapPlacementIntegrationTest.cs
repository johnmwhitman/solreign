#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.GameTicking;
using Content.Server.Spawners.Components;
using Content.Shared.CCVar;
using Content.Shared.Delivery;
using Content.Shared.Maps;
using Content.Shared.Station.Components;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
/// V13.5 wire-up (docs/research/UNWIRED-CONTENT-CENSUS-2026-07-17.md), items #4/#5/#9: verifies the
/// grid-file placements this wave added to <c>SolreignOasis</c> and <c>SolreignNocturne</c> (the two
/// maps the census found were missing content the other five Solreign maps already carried).
/// Adapts <see cref="SolreignWingmateBeaconMapPlacementIntegrationTest"/>'s "load the real production
/// map, resolve the station-owned grids, find exactly the expected marker entities on them" idiom, but
/// scoped to only the two affected maps and without that test's atmosphere-pressure poll (these are
/// interior cargo-bay/robotics-wing placements, not a mapper-authored spawn point whose breathability
/// needed independent proof) -- floor-tile-only sanity (anchored, not on a space tile) plus a
/// component-level correctness check that each placement is not just present but actually functional:
/// <c>Gateway</c> must have <c>GatewayComponent.Enabled</c> true (it defaults false in C# and the
/// Terminus reference placement was found to have this and its sibling components stripped via
/// <c>missingComponents</c> -- see the grid-file comment next to the Oasis Gateway placement for the
/// full re-verification), <c>CargoMailTeleporter</c> must carry <see cref="DeliverySpawnerComponent"/>,
/// and the AI core must be the <c>ContainerSpawnPointComponent</c>-carrying "Job spawn" variant with
/// <c>Job == StationAi</c> (the one <c>GameMapsLoadableTest</c>'s own per-job spawnpoint check requires).
/// </summary>
[TestFixture]
public sealed class SolreignV135WireupMapPlacementIntegrationTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        // Loads a real production map -- same reasoning as the sibling beacon-placement test.
        Dirty = true,
    };

    [Test]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.GridFill), false)]
    public Task Oasis_CargoMailTeleporter_PlacedOnStationGrid_WithDeliverySpawner()
        => CargoMailTeleporterPlacedOnStationGrid("SolreignOasis");

    [Test]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.GridFill), false)]
    public Task Nocturne_CargoMailTeleporter_PlacedOnStationGrid_WithDeliverySpawner()
        => CargoMailTeleporterPlacedOnStationGrid("SolreignNocturne");

    private async Task CargoMailTeleporterPlacedOnStationGrid(string mapProtoId)
    {
        const string entityProtoId = "CargoMailTeleporter";
        var server = Server;
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var ticker = entMan.System<GameTicker>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var turfSystem = entMan.System<TurfSystem>();

        var loadedMapId = MapId.Nullspace;
        var stationGrids = new HashSet<EntityUid>();
        var matches = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            Assert.That(protoMan.TryIndex<GameMapPrototype>(mapProtoId, out var mapProto),
                $"{mapProtoId} must resolve to one GameMapPrototype.");

            var opts = DeserializationOptions.Default with { InitializeMaps = true };
            ticker.LoadGameMap(mapProto!, out loadedMapId, opts);

            var memberQuery = entMan.GetEntityQuery<StationMemberComponent>();
            foreach (var grid in mapSystem.GetAllGrids(loadedMapId))
            {
                if (!memberQuery.HasComponent(grid.Owner))
                    continue;

                if (entMan.TryGetComponent<StationMemberComponent>(grid.Owner, out var member)
                    && entMan.TryGetComponent<StationDataComponent>(member.Station, out var stationData))
                {
                    stationGrids.UnionWith(stationData.Grids);
                }
            }

            Assert.That(stationGrids, Is.Not.Empty, $"{mapProtoId}: must resolve at least one station-owned grid.");

            var query = entMan.AllEntityQueryEnumerator<DeliverySpawnerComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid is { } gridUid
                    && stationGrids.Contains(gridUid)
                    && entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == entityProtoId)
                {
                    matches.Add(uid);
                }
            }
        });

        Assert.That(matches, Has.Count.EqualTo(1),
            $"{mapProtoId}: expected exactly one {entityProtoId} (carrying DeliverySpawnerComponent) on a station-owned grid, found {matches.Count}.");

        await server.WaitAssertion(() =>
        {
            var uid = matches[0];
            var xform = entMan.GetComponent<TransformComponent>(uid);

            Assert.That(xform.Anchored, Is.True, $"{mapProtoId}: {entityProtoId} must be anchored.");

            var tileRef = turfSystem.GetTileRef(xform.Coordinates);
            Assert.That(tileRef, Is.Not.Null, $"{mapProtoId}: {entityProtoId} tile must resolve a TileRef.");
            Assert.That(turfSystem.IsSpace(tileRef!.Value), Is.False,
                $"{mapProtoId}: {entityProtoId} must not sit on a space tile.");

            if (loadedMapId != MapId.Nullspace)
                mapSystem.DeleteMap(loadedMapId);
        });
    }

    // Gateway test removed: upstream replaced GatewayComponent with stationary teleporters (#32134).
    // The Oasis map placement for station transit needs re-verification against the new system.

    [Test]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.GridFill), false)]
    public async Task Oasis_StationAiCore_IsJobSpawnVariantWithCorrectJob()
    {
        const string mapProtoId = "SolreignOasis";
        var server = Server;
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var ticker = entMan.System<GameTicker>();
        var mapSystem = entMan.System<SharedMapSystem>();

        var loadedMapId = MapId.Nullspace;
        var matches = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            Assert.That(protoMan.TryIndex<GameMapPrototype>(mapProtoId, out var mapProto),
                $"{mapProtoId} must resolve to one GameMapPrototype.");

            var opts = DeserializationOptions.Default with { InitializeMaps = true };
            ticker.LoadGameMap(mapProto!, out loadedMapId, opts);

            var query = entMan.AllEntityQueryEnumerator<ContainerSpawnPointComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var spawnPoint, out var xform))
            {
                if (xform.MapUid != null
                    && mapSystem.GetMap(loadedMapId) == xform.MapUid
                    && spawnPoint.Job == "StationAi")
                {
                    matches.Add(uid);
                }
            }
        });

        Assert.That(matches, Has.Count.EqualTo(1),
            $"{mapProtoId}: expected exactly one ContainerSpawnPoint with Job=StationAi on the map " +
            "(the AI core, the census's flagged prerequisite -- Oasis carried none before this wave), " +
            $"found {matches.Count}.");

        await server.WaitAssertion(() =>
        {
            var uid = matches[0];
            Assert.That(entMan.HasComponent<Content.Shared.Silicons.StationAi.StationAiCoreComponent>(uid), Is.True,
                $"{mapProtoId}: the StationAi job spawn point must be the real StationAiCore machine, " +
                "not a bare ContainerSpawnPoint with no core behind it.");

            if (loadedMapId != MapId.Nullspace)
                mapSystem.DeleteMap(loadedMapId);
        });
    }
}
