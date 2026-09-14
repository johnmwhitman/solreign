using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.GameTicking;
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
/// Guards the map-specific front door for the Contracts loop. A board without its explainer is
/// technically reachable but leaves a first-shift player to infer Standing, fulfillment and
/// claiming from UI alone. Every rotation map therefore carries exactly one board and one nearby
/// <c>SolreignSignContractsHowTo</c> on the same station-owned grid.
/// </summary>
[TestFixture]
public sealed class SolreignContractsSignMapPlacementIntegrationTest : GameTest
{
    private const float MaximumExplainerDistanceTiles = 5f;
    private const string LegacyOverlappingPlacementMap = "SolreignLeviathan";

    public override PoolSettings PoolSettings => new()
    {
        // Loading and deleting a production map mutates global engine state. Match the existing
        // map-health and content-seed fixtures and never recycle this pair.
        Dirty = true,
    };

    [Test]
    [TestCaseSource(typeof(SolreignMapTestCatalog), nameof(SolreignMapTestCatalog.MapIds))]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.GridFill), false)]
    public async Task EveryContractsBoardHasOneNearbyExplainer(string mapProtoId)
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var ticker = entMan.System<GameTicker>();
        var mapSystem = entMan.System<SharedMapSystem>();

        var loadedMapId = MapId.Nullspace;
        var stationGrids = new HashSet<EntityUid>();
        var boards = new List<(EntityUid Grid, Vector2 Position)>();
        var explainers = new List<(EntityUid Grid, Vector2 Position)>();

        await server.WaitPost(() =>
        {
            Assert.That(protoMan.TryIndex<GameMapPrototype>(mapProtoId, out var mapProto),
                $"{mapProtoId} must resolve to one GameMapPrototype.");

            var opts = DeserializationOptions.Default with { InitializeMaps = true };
            ticker.LoadGameMap(mapProto!, out loadedMapId, opts);

            var memberQuery = entMan.GetEntityQuery<StationMemberComponent>();
            foreach (var grid in mapSystem.GetAllGrids(loadedMapId))
            {
                if (!memberQuery.TryGetComponent(grid.Owner, out var member)
                    || !entMan.TryGetComponent<StationDataComponent>(member.Station, out var stationData))
                {
                    continue;
                }

                stationGrids.UnionWith(stationData.Grids);
            }

            Assert.That(stationGrids, Is.Not.Empty,
                $"{mapProtoId}: must resolve at least one station-owned grid.");

            var query = entMan.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out _, out var meta, out var xform))
            {
                if (xform.GridUid is not { } grid || !stationGrids.Contains(grid))
                    continue;

                switch (meta.EntityPrototype?.ID)
                {
                    case "SolreignContractsBoard":
                        boards.Add((grid, xform.LocalPosition));
                        break;
                    case "SolreignSignContractsHowTo":
                        explainers.Add((grid, xform.LocalPosition));
                        break;
                }
            }

            if (loadedMapId != MapId.Nullspace)
                mapSystem.DeleteMap(loadedMapId);
        });

        Assert.Multiple(() =>
        {
            Assert.That(boards, Has.Count.EqualTo(1),
                $"{mapProtoId}: expected exactly one Contracts Board on a station-owned grid, found {boards.Count}.");
            Assert.That(explainers, Has.Count.EqualTo(1),
                $"{mapProtoId}: expected exactly one Contracts How-To sign on a station-owned grid, found {explainers.Count}.");
        });

        if (boards.Count != 1 || explainers.Count != 1)
            return;

        Assert.That(explainers[0].Grid, Is.EqualTo(boards[0].Grid),
            $"{mapProtoId}: Contracts Board and explainer must share a station grid.");

        var distance = Vector2.Distance(boards[0].Position, explainers[0].Position);
        if (mapProtoId != LegacyOverlappingPlacementMap)
        {
            Assert.That(distance, Is.GreaterThan(0f),
                $"{mapProtoId}: Contracts Board and explainer must not overlap; "
                + $"{LegacyOverlappingPlacementMap} is the one documented legacy exception.");
        }

        Assert.That(distance, Is.LessThanOrEqualTo(MaximumExplainerDistanceTiles),
            $"{mapProtoId}: Contracts explainer is {distance:F1} tiles from its board; "
            + $"the reviewed discoverability ceiling is {MaximumExplainerDistanceTiles:F0}.");
    }
}
