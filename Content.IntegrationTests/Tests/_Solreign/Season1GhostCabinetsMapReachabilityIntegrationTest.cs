#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Solreign.Season1;
using Content.Server.GameTicking;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Server.Storage.EntitySystems;
using Content.Shared.CCVar;
using Content.Shared.Dataset;
using Content.Shared.Maps;
using Content.Shared.Paper;
using Content.Shared.Station.Components;
using Content.Shared.Storage.Components;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Exact-map characterization for Season 1 Beat 3. This proves only that
///     the existing manually started rule can deliver its one static-dataset
///     clue into storage or as an uncontained delivery on a station-owned grid,
///     on every canonical rotation map. It does
///     not prove player discoverability, persistence, Showrunner eligibility,
///     automatic execution, or production activation.
/// </summary>
[TestFixture]
public sealed class Season1GhostCabinetsMapReachabilityIntegrationTest : GameTest
{
    private static readonly EntProtoId RuleId = "SolreignGhostCabinets";
    private static readonly EntProtoId FolderPrototypeId = "SolreignPaperGhostCabinetFolder";
    private static readonly ProtoId<LocalizedDatasetPrototype> TranscriptDatasetId =
        "Season1GhostCabinetTranscripts";

    public override PoolSettings PoolSettings => new()
    {
        Dirty = true,
    };

    [Test]
    [TestCaseSource(typeof(SolreignMapTestCatalog), nameof(SolreignMapTestCatalog.MapIds))]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.GridFill), false)]
    public async Task ManualBeatDeliversOneStaticTranscriptOnEveryRotationMap(string mapProtoId)
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var ticker = entMan.System<GameTicker>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var stationSystem = entMan.System<StationSystem>();

        var loadedMapId = MapId.Nullspace;
        EntityUid? ruleUid = null;

        try
        {
            var loadedStations = new HashSet<EntityUid>();
            var loadedMapGrids = new HashSet<EntityUid>();
            var stationGrids = new HashSet<EntityUid>();
            var foldersBefore = new HashSet<EntityUid>();
            var candidateStorageCount = 0;
            var openCandidateStorageCount = 0;

            await server.WaitPost(() =>
            {
                var mapPrototype = protoMan.Index<GameMapPrototype>(mapProtoId);
                var options = DeserializationOptions.Default with { InitializeMaps = true };
                ticker.LoadGameMap(mapPrototype, out loadedMapId, options);

                var memberQuery = entMan.GetEntityQuery<StationMemberComponent>();
                foreach (var grid in mapSystem.GetAllGrids(loadedMapId))
                {
                    loadedMapGrids.Add(grid.Owner);

                    if (!memberQuery.TryGetComponent(grid.Owner, out var member) ||
                        !entMan.TryGetComponent<StationDataComponent>(member.Station, out var stationData))
                    {
                        continue;
                    }

                    loadedStations.Add(member.Station);
                    stationGrids.UnionWith(stationData.Grids);
                }

                Assert.That(loadedStations, Has.Count.EqualTo(1),
                    $"{mapProtoId}: Beat 3 target attribution requires exactly one loaded station.");
                Assert.That(stationGrids, Is.Not.Empty,
                    $"{mapProtoId}: the loaded station must own at least one grid.");

                var eligibleStations = new HashSet<EntityUid>();
                var eligibleQuery = entMan.EntityQueryEnumerator<StationEventEligibleComponent>();
                while (eligibleQuery.MoveNext(out var station, out _))
                    eligibleStations.Add(station);

                Assert.That(eligibleStations, Is.EquivalentTo(loadedStations),
                    $"{mapProtoId}: the test pair must expose only the loaded map's station to random station events.");

                var probe = entMan.SpawnEntity(FolderPrototypeId, MapCoordinates.Nullspace);
                try
                {
                    var storageSystem = entMan.System<EntityStorageSystem>();
                    var candidateQuery = entMan.EntityQueryEnumerator<EntityStorageComponent, TransformComponent>();
                    while (candidateQuery.MoveNext(out var uid, out var storage, out var transform))
                    {
                        var candidateStation = stationSystem.GetOwningStation(uid, transform);
                        if (candidateStation is not { } station ||
                            !loadedStations.Contains(station) ||
                            !storageSystem.CanInsert(probe, uid, storage))
                        {
                            continue;
                        }

                        candidateStorageCount++;
                        if (storage.Open)
                            openCandidateStorageCount++;

                        Assert.That(transform.MapID, Is.EqualTo(loadedMapId),
                            $"{mapProtoId}: every production-admitted storage candidate must be on the exact loaded map.");
                        Assert.That(transform.GridUid is { } grid && loadedMapGrids.Contains(grid), Is.True,
                            $"{mapProtoId}: every production-admitted storage candidate must be on a grid loaded from this map.");
                    }
                }
                finally
                {
                    entMan.DeleteEntity(probe);
                }

                Assert.That(candidateStorageCount, Is.GreaterThan(0),
                    $"{mapProtoId}: the real rule must have at least one insertable station-owned storage candidate.");

                var folderQuery = entMan.EntityQueryEnumerator<MetaDataComponent>();
                while (folderQuery.MoveNext(out var uid, out var metadata))
                {
                    if (metadata.EntityPrototype?.ID == FolderPrototypeId.Id)
                        foldersBefore.Add(uid);
                }
            });

            await server.WaitAssertion(() =>
            {
                Assert.That(ticker.StartGameRule(RuleId, out var startedRule), Is.True,
                    $"{mapProtoId}: the manually invoked Beat 3 rule must start.");
                ruleUid = startedRule;

                var rule = entMan.GetComponent<SolreignGhostCabinetsRuleComponent>(startedRule);
                Assert.That(
                    rule.LastObservation,
                    Is.AnyOf(
                        SolreignGhostCabinetsDeliveryOutcome.Inserted,
                        SolreignGhostCabinetsDeliveryOutcome.DroppedAdjacent),
                    $"{mapProtoId}: the clue must reach station-owned storage or remain uncontained on its station grid.");
                if (openCandidateStorageCount == 0)
                {
                    Assert.That(rule.LastObservation, Is.EqualTo(SolreignGhostCabinetsDeliveryOutcome.Inserted),
                        $"{mapProtoId}: without any open candidate, the delivery must be contained.");
                }

                var newFolders = new List<(EntityUid Uid, PaperComponent Paper)>();
                var folderQuery = entMan.EntityQueryEnumerator<MetaDataComponent, PaperComponent>();
                while (folderQuery.MoveNext(out var uid, out var metadata, out var paper))
                {
                    if (metadata.EntityPrototype?.ID == FolderPrototypeId.Id && !foldersBefore.Contains(uid))
                        newFolders.Add((uid, paper));
                }

                Assert.That(newFolders, Has.Count.EqualTo(1),
                    $"{mapProtoId}: the event must create exactly one new Beat 3 folder.");

                var folder = newFolders[0];
                var folderTransform = entMan.GetComponent<TransformComponent>(folder.Uid);
                Assert.That(folderTransform.MapID, Is.EqualTo(loadedMapId),
                    $"{mapProtoId}: the delivered clue must remain on the exact loaded map.");
                var folderStation = stationSystem.GetOwningStation(folder.Uid);
                Assert.That(folderStation, Is.Not.Null);
                Assert.That(loadedStations, Does.Contain(folderStation!.Value),
                    $"{mapProtoId}: the delivered clue must belong to the loaded station.");

                string destination;
                if (rule.LastObservation == SolreignGhostCabinetsDeliveryOutcome.Inserted)
                {
                    var inside = entMan.GetComponent<InsideEntityStorageComponent>(folder.Uid);
                    var storage = entMan.GetComponent<EntityStorageComponent>(inside.Storage);
                    Assert.That(storage.Contents.ContainedEntities, Does.Contain(folder.Uid),
                        $"{mapProtoId}: both sides of the storage relationship must contain the clue.");

                    var owningStation = stationSystem.GetOwningStation(inside.Storage);
                    Assert.That(owningStation, Is.Not.Null);
                    Assert.That(loadedStations, Does.Contain(owningStation!.Value),
                        $"{mapProtoId}: the selected storage must belong to the loaded station.");

                    var containerTransform = entMan.GetComponent<TransformComponent>(inside.Storage);
                    Assert.That(containerTransform.MapID, Is.EqualTo(loadedMapId));
                    Assert.That(
                        containerTransform.GridUid is { } grid && loadedMapGrids.Contains(grid) && stationGrids.Contains(grid),
                        Is.True,
                        $"{mapProtoId}: the selected storage must remain on an exact-map, station-owned grid.");

                    destination = entMan.GetComponent<MetaDataComponent>(inside.Storage).EntityPrototype?.ID
                        ?? "<map entity>";
                }
                else
                {
                    Assert.That(openCandidateStorageCount, Is.GreaterThan(0),
                        $"{mapProtoId}: an uncontained storage delivery requires at least one open candidate.");
                    Assert.That(entMan.HasComponent<InsideEntityStorageComponent>(folder.Uid), Is.False,
                        $"{mapProtoId}: DroppedAdjacent must not claim containment.");
                    Assert.That(
                        folderTransform.GridUid is { } grid && loadedMapGrids.Contains(grid) && stationGrids.Contains(grid),
                        Is.True,
                        $"{mapProtoId}: a dropped-adjacent clue must remain on an exact-map, station-owned grid.");
                    destination = "<uncontained storage delivery on station grid>";
                }

                var dataset = protoMan.Index(TranscriptDatasetId);
                var allowedTexts = dataset.Values.Select(Loc.GetString).ToArray();
                Assert.That(folder.Paper.Content, Is.AnyOf(allowedTexts),
                    $"{mapProtoId}: the clue must contain only curated static-dataset text.");

                TestContext.Progress.WriteLine(
                    $"{mapProtoId}: {candidateStorageCount} candidates ({openCandidateStorageCount} open); "
                    + $"{rule.LastObservation} at {destination}.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                try
                {
                    if (ruleUid is { } rule && entMan.EntityExists(rule))
                        ticker.EndGameRule(rule);
                }
                finally
                {
                    if (loadedMapId != MapId.Nullspace)
                        mapSystem.DeleteMap(loadedMapId);
                }
            });
        }
    }
}
