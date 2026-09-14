using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Solreign.Library;
using Content.Server._Solreign.Noticeboards;
using Content.Server.GameTicking;
using Content.Shared._Solreign.PlayerDelight.Wingmates;
using Content.Shared.CCVar;
using Content.Shared.Maps;
using Content.Shared.Station.Components;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
/// Proves that every production SOLREIGN map exposes the persistent Library Annex placed by
/// <c>Tools/_Solreign/MapPatch/manifests/community-fixtures-v1.json</c>. The source-side patcher
/// pins exact UIDs, floor tiles, occupants, and coordinates; this runtime test closes the other
/// half of the contract by loading each map and verifying that entity deserialization attaches
/// the expected components on the station grid near the established Wingmate beacon.
/// </summary>
[TestFixture]
public sealed class SolreignCommunityFixtureMapPlacementIntegrationTest : GameTest
{
    private const int MaximumBeaconDistanceTiles = 4;

    public override PoolSettings PoolSettings => new()
    {
        // Real production map loading mutates enough engine state that pairs must not be recycled.
        Dirty = true,
    };

    [Test]
    [TestCaseSource(typeof(SolreignMapTestCatalog), nameof(SolreignMapTestCatalog.MapIds))]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.GridFill), false)]
    public async Task ExactlyOneLibraryAnnexAndZeroDormantNoticeboardsOnStationGridNearBeacon(string mapProtoId)
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var ticker = entMan.System<GameTicker>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var turfSystem = entMan.System<TurfSystem>();
        var xformSystem = entMan.System<TransformSystem>();

        var loadedMapId = MapId.Nullspace;
        var stationGrids = new HashSet<EntityUid>();
        var libraryUids = new List<EntityUid>();
        var noticeboardUids = new List<EntityUid>();
        var beaconUids = new List<EntityUid>();

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

            Assert.That(stationGrids, Is.Not.Empty,
                $"{mapProtoId}: must resolve at least one station-owned grid.");

            var libraryQuery =
                entMan.AllEntityQueryEnumerator<SolreignLibraryAnnexComponent, TransformComponent>();
            while (libraryQuery.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid is { } gridUid && stationGrids.Contains(gridUid))
                    libraryUids.Add(uid);
            }

            var noticeboardQuery =
                entMan.AllEntityQueryEnumerator<SolreignNoticeboardComponent, TransformComponent>();
            while (noticeboardQuery.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid is { } gridUid && stationGrids.Contains(gridUid))
                    noticeboardUids.Add(uid);
            }

            var beaconQuery = entMan.AllEntityQueryEnumerator<WingmateBeaconComponent, TransformComponent>();
            while (beaconQuery.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid is { } gridUid && stationGrids.Contains(gridUid))
                    beaconUids.Add(uid);
            }
        });

        Assert.That(libraryUids, Has.Count.EqualTo(1),
            $"{mapProtoId}: expected exactly one station-grid SolreignLibraryAnnex.");
        Assert.That(noticeboardUids, Is.Empty,
            $"{mapProtoId}: expected zero station-grid dormant SolreignNoticeboards.");
        Assert.That(beaconUids, Has.Count.EqualTo(1),
            $"{mapProtoId}: expected exactly one station-grid SolreignWingmateBeacon anchor.");

        await server.WaitAssertion(() =>
        {
            var libraryUid = libraryUids[0];
            var beaconUid = beaconUids[0];
            var library = entMan.GetComponent<SolreignLibraryAnnexComponent>(libraryUid);
            var libraryXform = entMan.GetComponent<TransformComponent>(libraryUid);
            var beaconXform = entMan.GetComponent<TransformComponent>(beaconUid);

            Assert.That(library.ArchiveId, Is.EqualTo("main"),
                $"{mapProtoId}: library must project the shared main archive.");
            Assert.That(libraryXform.Anchored, Is.True, $"{mapProtoId}: library must be anchored.");

            AssertFixtureTile(mapProtoId, "library", libraryXform, stationGrids, turfSystem);

            var beaconTile = xformSystem.GetGridTilePositionOrDefault((beaconUid, beaconXform));
            var libraryTile = xformSystem.GetGridTilePositionOrDefault((libraryUid, libraryXform));

            Assert.That(ManhattanDistance(libraryTile, beaconTile), Is.LessThanOrEqualTo(MaximumBeaconDistanceTiles),
                $"{mapProtoId}: library must remain near its reviewed Wingmate beacon anchor.");
            if (loadedMapId != MapId.Nullspace)
                mapSystem.DeleteMap(loadedMapId);
        });
    }

    private static void AssertFixtureTile(
        string mapProtoId,
        string fixture,
        TransformComponent xform,
        IReadOnlySet<EntityUid> stationGrids,
        TurfSystem turfSystem)
    {
        Assert.That(xform.GridUid is { } gridUid && stationGrids.Contains(gridUid), Is.True,
            $"{mapProtoId}: {fixture} must remain on a station-owned grid.");
        var tileRef = turfSystem.GetTileRef(xform.Coordinates);
        Assert.That(tileRef, Is.Not.Null, $"{mapProtoId}: {fixture} tile must resolve a TileRef.");
        Assert.That(turfSystem.IsSpace(tileRef!.Value), Is.False,
            $"{mapProtoId}: {fixture} must not sit on a space tile.");
    }

    private static int ManhattanDistance(Vector2i left, Vector2i right)
    {
        return Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y);
    }
}
