using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Atmos.EntitySystems;
using Content.Server.GameTicking;
using Content.Shared._Solreign.PlayerDelight.Vista;
using Content.Shared.Atmos;
using Content.Shared.CCVar;
using Content.Shared.Maps;
using Content.Shared.Station.Components;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
/// Vista beat rollout (2026-07-17, council memo docs/council/2026-07-16-design-magnetism.md item 5):
/// verifies the "one composed vista per map" placement step. Mirrors
/// <see cref="SolreignWingmateBeaconMapPlacementIntegrationTest"/> exactly — the same
/// <see cref="SolreignMapTestCatalog"/> seven-map source and the same adapted SR-W-012 spawn-tile
/// safety idiom (station-grid membership, anchored, non-space tile,
/// <see cref="AtmosphereSystem.IsTileMixtureProbablySafe"/>, oxygen partial-pressure floor) — because
/// the marker's tile is a spot a first-shift player is meant to WALK PAST: a marker on a wall, space
/// tile, or unbreathable tile would be a vista nobody can reach. Additionally asserts each marker's
/// map-set <see cref="SolreignVistaMarkerComponent.LineId"/> resolves to a real localized PROVIDENCE
/// line (vista-beat.ftl) and that no two maps share a line — the lines each reference that map's
/// actual composed spot, so sharing one would be a copy fabrication.
/// </summary>
[TestFixture]
public sealed class SolreignVistaMarkerMapPlacementIntegrationTest : GameTest
{
    /// <summary>Bounded readiness budget for the atmosphere poll — same reasoning as
    /// <c>SolreignWingmateBeaconMapPlacementIntegrationTest.AtmosphereReadinessBudgetSeconds</c>.</summary>
    private const float AtmosphereReadinessBudgetSeconds = 8f;

    /// <summary>Poll interval for the atmosphere readiness condition.</summary>
    private const float AtmosphereReadinessPollSeconds = 0.5f;

    /// <summary>Same reviewed, test-local breathable-oxygen partial-pressure floor the map-health
    /// scorecard and the wingmate placement test use.</summary>
    private const float MinimumSafeOxygenPartialPressureKpa = 16f;

    /// <summary>Line ids observed across the per-map cases, for the cross-map uniqueness assertion.
    /// NUnit runs a fixture's cases sequentially on one instance, so a plain dictionary is safe.</summary>
    private static readonly Dictionary<string, string> SeenLineIds = new();

    public override PoolSettings PoolSettings => new()
    {
        // Loading a real production map and letting atmos settle mutates enough global engine state
        // that this pair must never be recycled -- same reasoning as the wingmate placement test.
        Dirty = true,
    };

    [Test]
    [TestCaseSource(typeof(SolreignMapTestCatalog), nameof(SolreignMapTestCatalog.MapIds))]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.GridFill), false)]
    public async Task ExactlyOneSafelyPlacedStationGridVistaMarkerWithResolvableLine(string mapProtoId)
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var loc = server.ResolveDependency<ILocalizationManager>();
        var ticker = entMan.System<GameTicker>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var atmosSystem = entMan.System<AtmosphereSystem>();
        var turfSystem = entMan.System<TurfSystem>();
        var xformSystem = entMan.System<TransformSystem>();

        var loadedMapId = MapId.Nullspace;
        var stationGrids = new HashSet<EntityUid>();
        var markerUids = new List<EntityUid>();

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

            var markerQuery = entMan.AllEntityQueryEnumerator<SolreignVistaMarkerComponent, TransformComponent>();
            while (markerQuery.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid is { } gridUid && stationGrids.Contains(gridUid))
                    markerUids.Add(uid);
            }
        });

        Assert.That(markerUids, Has.Count.EqualTo(1),
            $"{mapProtoId}: expected exactly one SolreignVistaMarker on a station-owned grid, found {markerUids.Count}.");

        var markerUid = markerUids[0];

        // The map-baked line must exist, resolve, and be unique to this map.
        await server.WaitAssertion(() =>
        {
            var marker = entMan.GetComponent<SolreignVistaMarkerComponent>(markerUid);

            Assert.That(marker.LineId, Is.Not.Empty,
                $"{mapProtoId}: the map must override SolreignVistaMarker.lineId with its map-specific line.");
            Assert.That(loc.TryGetString(marker.LineId, out _), Is.True,
                $"{mapProtoId}: lineId '{marker.LineId}' must resolve to a localized PROVIDENCE line (vista-beat.ftl).");
            Assert.That(marker.Range, Is.GreaterThan(0f), $"{mapProtoId}: marker range must be positive.");

            if (SeenLineIds.TryGetValue(marker.LineId, out var otherMap) && otherMap != mapProtoId)
                Assert.Fail($"{mapProtoId}: lineId '{marker.LineId}' is already used by {otherMap} — vista lines are map-specific.");
            SeenLineIds[marker.LineId] = mapProtoId;
        });

        // Poll (bounded) until the marker's own tile mixture resolves -- same readiness idiom as the
        // wingmate placement test.
        var waited = 0f;
        while (waited < AtmosphereReadinessBudgetSeconds)
        {
            var ready = false;
            await server.WaitPost(() =>
            {
                var xform = entMan.GetComponent<TransformComponent>(markerUid);
                ready = atmosSystem.GetTileMixture((markerUid, xform), excite: false) is { Immutable: false };
            });

            if (ready)
                break;

            await pair.RunSeconds(AtmosphereReadinessPollSeconds);
            waited += AtmosphereReadinessPollSeconds;
        }

        await server.WaitAssertion(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(markerUid);

            Assert.That(xform.Anchored, Is.True, $"{mapProtoId}: vista marker must be anchored.");
            Assert.That(xform.GridUid is { } gridUid && stationGrids.Contains(gridUid), Is.True,
                $"{mapProtoId}: vista marker must remain on a station-owned grid.");

            // Non-space, pressure/temperature-safe, breathable tile: the vista is a spot a player
            // stands on, so it inherits the full spawn-tile safety gate.
            var tileRef = turfSystem.GetTileRef(xform.Coordinates);
            Assert.That(tileRef, Is.Not.Null, $"{mapProtoId}: vista marker tile must resolve a TileRef.");
            Assert.That(turfSystem.IsSpace(tileRef!.Value), Is.False,
                $"{mapProtoId}: vista marker must not sit on a space tile.");

            Assert.That(xform.MapUid, Is.Not.Null, $"{mapProtoId}: vista marker must resolve a map.");
            var tile = xformSystem.GetGridTilePositionOrDefault((markerUid, xform));

            Assert.That(atmosSystem.IsTileMixtureProbablySafe(xform.GridUid!.Value, xform.MapUid!.Value, tile), Is.True,
                $"{mapProtoId}: vista marker tile must pass the pressure/temperature safety check.");

            var mixture = atmosSystem.GetTileMixture((markerUid, xform), excite: false);
            var oxygenPartialPressure = mixture is { TotalMoles: > 0 }
                ? mixture.Pressure * (mixture.GetMoles(Gas.Oxygen) / mixture.TotalMoles)
                : 0f;

            Assert.That(oxygenPartialPressure, Is.GreaterThanOrEqualTo(MinimumSafeOxygenPartialPressureKpa),
                $"{mapProtoId}: vista marker tile oxygen partial pressure ({oxygenPartialPressure:F1} kPa) must clear the {MinimumSafeOxygenPartialPressureKpa} kPa floor.");

            if (loadedMapId != MapId.Nullspace)
                mapSystem.DeleteMap(loadedMapId);
        });
    }
}
