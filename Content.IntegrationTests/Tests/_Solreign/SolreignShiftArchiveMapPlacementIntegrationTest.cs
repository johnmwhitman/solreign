using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.GameTicking;
using Content.Shared._Solreign.ShiftArchive;
using Content.Shared.CCVar;
using Content.Shared.Maps;
using Content.Shared.Station.Components;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
/// Shift Archive rollout: verifies the "place the board on all 7 maps" step. Reuses
/// <see cref="SolreignMapTestCatalog"/> (the same seven-map source the Wingmate beacon and
/// Corporate Projects placements are tested from) and the beacon test's station-grid resolution.
/// Unlike the beacon test there is no atmosphere/oxygen assertion: the board is a wallmount read
/// from an adjacent tile, not a spawn location, and two of its placements deliberately sit on
/// wall tiles (the records-bank walls) where a tile-mixture check is meaningless. The board ships
/// behind <c>solreign.shift_archive.enabled</c> default-false — this test proves the seven
/// production maps carry exactly one anchored board each, not that the feature is live.
/// </summary>
[TestFixture]
public sealed class SolreignShiftArchiveMapPlacementIntegrationTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        // Loading a real production map mutates enough global engine state that this pair must
        // never be recycled — same reasoning as the map-health scorecard.
        Dirty = true,
    };

    [Test]
    [TestCaseSource(typeof(SolreignMapTestCatalog), nameof(SolreignMapTestCatalog.MapIds))]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.GridFill), false)]
    public async Task ExactlyOneAnchoredStationGridBoard(string mapProtoId)
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var ticker = entMan.System<GameTicker>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var turfSystem = entMan.System<TurfSystem>();

        var loadedMapId = MapId.Nullspace;
        var stationGrids = new HashSet<EntityUid>();
        var boardUids = new List<EntityUid>();

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

            var boardQuery = entMan.AllEntityQueryEnumerator<ShiftArchiveBoardComponent, TransformComponent>();
            while (boardQuery.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid is { } gridUid && stationGrids.Contains(gridUid))
                    boardUids.Add(uid);
            }
        });

        Assert.That(boardUids, Has.Count.EqualTo(1),
            $"{mapProtoId}: expected exactly one SolreignShiftArchiveBoard on a station-owned grid, found {boardUids.Count}.");

        await server.WaitAssertion(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(boardUids[0]);

            Assert.That(xform.Anchored, Is.True, $"{mapProtoId}: board must be anchored.");
            Assert.That(xform.GridUid is { } gridUid && stationGrids.Contains(gridUid), Is.True,
                $"{mapProtoId}: board must remain on a station-owned grid.");

            var tileRef = turfSystem.GetTileRef(xform.Coordinates);
            Assert.That(tileRef, Is.Not.Null, $"{mapProtoId}: board tile must resolve a TileRef.");
            Assert.That(turfSystem.IsSpace(tileRef!.Value), Is.False,
                $"{mapProtoId}: board must not sit on a space tile.");

            if (loadedMapId != MapId.Nullspace)
                mapSystem.DeleteMap(loadedMapId);
        });
    }
}
