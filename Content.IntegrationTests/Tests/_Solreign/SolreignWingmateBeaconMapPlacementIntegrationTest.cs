using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Atmos.EntitySystems;
using Content.Server.GameTicking;
using Content.Shared._Solreign.PlayerDelight.Wingmates;
using Content.Shared.Atmos;
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
/// Wingmates rollout, 2026-07-15: verifies the "map the beacon into all 7 maps" step from the v12
/// handoff. Reuses <see cref="SolreignMapTestCatalog"/> (the same seven-map source
/// <see cref="SolreignMapPoolIntegrationTest"/> and the SR-W-012 map-health scorecard
/// (<see cref="SolreignMapHealthScorecardIntegrationTest"/>) already share) and adapts that
/// scorecard's check-6 spawn-tile safety idiom (station-grid membership, non-space tile,
/// <see cref="AtmosphereSystem.IsTileMixtureProbablySafe"/>, and an oxygen partial-pressure floor)
/// to the <c>SolreignWingmateBeacon</c> entity instead of a job/latejoin spawn point. The beacon is
/// admin-spawned-only while <c>solreign.wingmates_enabled</c> stays default-off (see the prototype's
/// own comment in <c>wingmate_beacon.yml</c>) -- this test only proves the seven production maps now
/// carry exactly one correctly-placed beacon each, not that the feature is live.
/// </summary>
[TestFixture]
public sealed class SolreignWingmateBeaconMapPlacementIntegrationTest : GameTest
{
    /// <summary>
    /// Bounded readiness budget for the atmosphere poll, mirroring
    /// <c>SolreignMapHealthScorecardIntegrationTest.AtmosphereReadinessBudgetSeconds</c>: gas mixtures
    /// populate through <c>AtmosphereSystem</c>'s tick-budgeted invalidation queue, not synchronously
    /// at map load, so this test polls instead of sleeping a fixed amount. Expiry is a failure path,
    /// never a skip -- the tile is evaluated with whatever mixture state exists once the budget runs out.
    /// </summary>
    private const float AtmosphereReadinessBudgetSeconds = 8f;

    /// <summary>Poll interval for the atmosphere readiness condition.</summary>
    private const float AtmosphereReadinessPollSeconds = 0.5f;

    /// <summary>
    /// Same reviewed, test-local breathable-oxygen partial-pressure floor the map-health scorecard
    /// uses (see its own remarks on <c>AtmosphereSystem.IsMixtureProbablySafe</c> not checking gas
    /// composition). 16 kPa is the conventional real-world hypoxia-onset partial pressure.
    /// </summary>
    private const float MinimumSafeOxygenPartialPressureKpa = 16f;

    public override PoolSettings PoolSettings => new()
    {
        // Loading a real production map and letting atmos settle mutates enough global engine state
        // that this pair must never be recycled -- same reasoning as the map-health scorecard.
        Dirty = true,
    };

    [Test]
    [TestCaseSource(typeof(SolreignMapTestCatalog), nameof(SolreignMapTestCatalog.MapIds))]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.GridFill), false)]
    public async Task ExactlyOneSafelyPlacedStationGridBeacon(string mapProtoId)
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var ticker = entMan.System<GameTicker>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var atmosSystem = entMan.System<AtmosphereSystem>();
        var turfSystem = entMan.System<TurfSystem>();
        var xformSystem = entMan.System<TransformSystem>();

        var loadedMapId = MapId.Nullspace;
        var stationGrids = new HashSet<EntityUid>();
        var beaconUids = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            Assert.That(protoMan.TryIndex<GameMapPrototype>(mapProtoId, out var mapProto),
                $"{mapProtoId} must resolve to one GameMapPrototype.");

            var opts = DeserializationOptions.Default with { InitializeMaps = true };
            ticker.LoadGameMap(mapProto!, out loadedMapId, opts);

            // Same "largest station-member grid" resolution the map-health scorecard and
            // GameMapsLoadableTest use, generalized to collect every station-owned grid (a map may
            // have more than one, e.g. Terminus's second, non-station grid).
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

            var beaconQuery = entMan.AllEntityQueryEnumerator<WingmateBeaconComponent, TransformComponent>();
            while (beaconQuery.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid is { } gridUid && stationGrids.Contains(gridUid))
                    beaconUids.Add(uid);
            }
        });

        Assert.That(beaconUids, Has.Count.EqualTo(1),
            $"{mapProtoId}: expected exactly one SolreignWingmateBeacon on a station-owned grid, found {beaconUids.Count}.");

        var beaconUid = beaconUids[0];

        // Poll (bounded) until the beacon's own tile mixture resolves -- same readiness idiom as the
        // map-health scorecard's check 6, just scoped to a single tile instead of every spawn tile.
        var waited = 0f;
        while (waited < AtmosphereReadinessBudgetSeconds)
        {
            var ready = false;
            await server.WaitPost(() =>
            {
                var xform = entMan.GetComponent<TransformComponent>(beaconUid);
                ready = atmosSystem.GetTileMixture((beaconUid, xform), excite: false) is { Immutable: false };
            });

            if (ready)
                break;

            await pair.RunSeconds(AtmosphereReadinessPollSeconds);
            waited += AtmosphereReadinessPollSeconds;
        }

        await server.WaitAssertion(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(beaconUid);

            Assert.That(xform.Anchored, Is.True, $"{mapProtoId}: beacon must be anchored.");
            Assert.That(xform.GridUid is { } gridUid && stationGrids.Contains(gridUid), Is.True,
                $"{mapProtoId}: beacon must remain on a station-owned grid.");

            // Non-space tile -- same predicate EvaluateSpawnAtmosphere uses for its walkable-floor gate.
            var tileRef = turfSystem.GetTileRef(xform.Coordinates);
            Assert.That(tileRef, Is.Not.Null, $"{mapProtoId}: beacon tile must resolve a TileRef.");
            Assert.That(turfSystem.IsSpace(tileRef!.Value), Is.False,
                $"{mapProtoId}: beacon must not sit on a space tile.");

            Assert.That(xform.MapUid, Is.Not.Null, $"{mapProtoId}: beacon must resolve a map.");
            var tile = xformSystem.GetGridTilePositionOrDefault((beaconUid, xform));

            Assert.That(atmosSystem.IsTileMixtureProbablySafe(xform.GridUid!.Value, xform.MapUid!.Value, tile), Is.True,
                $"{mapProtoId}: beacon tile must pass the pressure/temperature safety check.");

            var mixture = atmosSystem.GetTileMixture((beaconUid, xform), excite: false);
            var oxygenPartialPressure = mixture is { TotalMoles: > 0 }
                ? mixture.Pressure * (mixture.GetMoles(Gas.Oxygen) / mixture.TotalMoles)
                : 0f;

            Assert.That(oxygenPartialPressure, Is.GreaterThanOrEqualTo(MinimumSafeOxygenPartialPressureKpa),
                $"{mapProtoId}: beacon tile oxygen partial pressure ({oxygenPartialPressure:F1} kPa) must clear the {MinimumSafeOxygenPartialPressureKpa} kPa floor.");

            if (loadedMapId != MapId.Nullspace)
                mapSystem.DeleteMap(loadedMapId);
        });
    }
}
