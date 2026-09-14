#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.Season1;
using Content.Server.GameTicking;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Server.Storage.EntitySystems;
using Content.Shared.Dataset;
using Content.Shared.Paper;
using Content.Shared.Station;
using Content.Shared.Station.Components;
using Content.Shared.Storage.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Synthetic runtime-logic evidence only. These tests do not load a SOLREIGN production map
///     and make no exact-map, Atlas, discoverability, or Showrunner eligibility claim.
/// </summary>
[TestFixture]
public sealed class Season1GhostCabinetsDeliveryObservationIntegrationTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        Fresh = true,
        DummyTicker = true,
    };

    private const string RuleId = "SolreignGhostCabinets";
    private const string MissingDatasetRuleId = "SolreignGhostCabinetsMissingDatasetTest";
    private const string EmptyDatasetRuleId = "SolreignGhostCabinetsEmptyDatasetTest";
    private const string FolderPrototypeId = "SolreignPaperGhostCabinetFolder";
    private static readonly ProtoId<LocalizedDatasetPrototype> TranscriptDatasetId =
        "Season1GhostCabinetTranscripts";
    private const string DatasetFailureFixtures = """
        - type: localizedDataset
          id: SolreignEmptyGhostCabinetDatasetTest
          values:
            prefix: solreign-empty-ghost-cabinet-dataset-test-
            count: 0

        - type: entity
          id: SolreignGhostCabinetsMissingDatasetTest
          parent: SolreignGhostCabinets
          components:
          - type: SolreignGhostCabinetsRule
            transcriptDataset: SolreignMissingGhostCabinetDatasetTest

        - type: entity
          id: SolreignGhostCabinetsEmptyDatasetTest
          parent: SolreignGhostCabinets
          components:
          - type: SolreignGhostCabinetsRule
            transcriptDataset: SolreignEmptyGhostCabinetDatasetTest
        """;

    private static EntityUid MakeStation(IEntityManager entMan, EntityUid? memberGrid = null)
    {
        var station = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
        var stationData = entMan.EnsureComponent<StationDataComponent>(station);
        entMan.EnsureComponent<StationEventEligibleComponent>(station);

        if (memberGrid is not { } grid)
            return station;

#pragma warning disable RA0002
        stationData.Grids.Add(grid);
#pragma warning restore RA0002
        entMan.Dirty(station, stationData);

        entMan.EnsureComponent<StationMemberComponent>(grid, out var member);
        member.Station = station;
        entMan.Dirty(grid, member);
        return station;
    }

    private static HashSet<EntityUid> GetFolderUids(IEntityManager entMan)
    {
        var folders = new HashSet<EntityUid>();
        var query = entMan.EntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out var uid, out var meta))
        {
            if (meta.EntityPrototype?.ID == FolderPrototypeId)
                folders.Add(uid);
        }

        return folders;
    }

    [Test]
    public async Task StartWithoutStation_RecordsBoundedNoStationOutcome()
    {
        var server = Server;

        await server.WaitAssertion(() =>
        {
            var foldersBefore = GetFolderUids(server.EntMan);
            var ticker = server.System<GameTicker>();
            Assert.That(ticker.StartGameRule(RuleId, out var rule), Is.True);

            var component = server.EntMan.GetComponent<SolreignGhostCabinetsRuleComponent>(rule);
            Assert.That(component.LastObservation, Is.EqualTo(SolreignGhostCabinetsDeliveryOutcome.NoStation));
            Assert.That(GetFolderUids(server.EntMan).Except(foldersBefore), Is.Empty);
        });
    }

    [Test]
    public async Task StartWithStationButNoInsertableStorage_RecordsBoundedOutcome()
    {
        var server = Server;
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            MakeStation(server.EntMan, map.Grid.Owner);
            var foldersBefore = GetFolderUids(server.EntMan);

            var ticker = server.System<GameTicker>();
            Assert.That(ticker.StartGameRule(RuleId, out var rule), Is.True);

            var component = server.EntMan.GetComponent<SolreignGhostCabinetsRuleComponent>(rule);
            Assert.That(component.LastObservation, Is.EqualTo(SolreignGhostCabinetsDeliveryOutcome.NoInsertableStorage));
            Assert.That(GetFolderUids(server.EntMan).Except(foldersBefore), Is.Empty);
        });

        await server.WaitPost(() => server.System<SharedMapSystem>().DeleteMap(map.MapId));
    }

    [Test]
    public async Task StartWithStorageStation_InsertsExactlyOneStaticDatasetTranscript()
    {
        var server = Server;
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var station = MakeStation(server.EntMan, map.Grid.Owner);
            var locker = server.EntMan.SpawnEntity("LockerSteel", map.GridCoords);
            var foldersBefore = GetFolderUids(server.EntMan);

            var ticker = server.System<GameTicker>();
            Assert.That(ticker.StartGameRule(RuleId, out var rule), Is.True);

            var component = server.EntMan.GetComponent<SolreignGhostCabinetsRuleComponent>(rule);
            Assert.That(component.LastObservation, Is.EqualTo(SolreignGhostCabinetsDeliveryOutcome.Inserted));

            var folders = new List<(EntityUid Uid, PaperComponent Paper)>();
            var query = server.EntMan.EntityQueryEnumerator<MetaDataComponent, PaperComponent>();
            while (query.MoveNext(out var uid, out var meta, out var paper))
            {
                if (meta.EntityPrototype?.ID == FolderPrototypeId && !foldersBefore.Contains(uid))
                    folders.Add((uid, paper));
            }

            Assert.That(folders, Has.Count.EqualTo(1));
            var container = server.EntMan.GetComponent<TransformComponent>(folders[0].Uid).ParentUid;
            Assert.That(container, Is.EqualTo(locker));
            Assert.That(server.System<StationSystem>().GetOwningStation(container), Is.EqualTo(station));
            var storage = server.EntMan.GetComponent<EntityStorageComponent>(locker);
            Assert.That(storage.Contents.ContainedEntities, Does.Contain(folders[0].Uid));

            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var dataset = prototypes.Index(TranscriptDatasetId);
            var allowedTexts = dataset.Values.Select(Loc.GetString).ToArray();
            Assert.That(folders[0].Paper.Content, Is.AnyOf(allowedTexts));
        });

        await server.WaitPost(() => server.System<SharedMapSystem>().DeleteMap(map.MapId));
    }

    [Test]
    public async Task StartWithOpenStorage_RecordsDroppedAdjacentRatherThanInserted()
    {
        var server = Server;
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var station = MakeStation(server.EntMan, map.Grid.Owner);
            var locker = server.EntMan.SpawnEntity("LockerSteel", map.GridCoords);
            server.System<EntityStorageSystem>().OpenStorage(locker);
            var foldersBefore = GetFolderUids(server.EntMan);

            var ticker = server.System<GameTicker>();
            Assert.That(ticker.StartGameRule(RuleId, out var rule), Is.True);

            var component = server.EntMan.GetComponent<SolreignGhostCabinetsRuleComponent>(rule);
            Assert.That(component.LastObservation, Is.EqualTo(SolreignGhostCabinetsDeliveryOutcome.DroppedAdjacent));

            var newFolders = GetFolderUids(server.EntMan).Except(foldersBefore).ToArray();
            Assert.That(newFolders, Has.Length.EqualTo(1));
            var folderUid = newFolders[0];
            Assert.That(server.EntMan.HasComponent<InsideEntityStorageComponent>(folderUid), Is.False);
            Assert.That(server.System<StationSystem>().GetOwningStation(folderUid), Is.EqualTo(station));
            var storage = server.EntMan.GetComponent<EntityStorageComponent>(locker);
            Assert.That(storage.Contents.ContainedEntities, Does.Not.Contain(folderUid));
        });

        await server.WaitPost(() => server.System<SharedMapSystem>().DeleteMap(map.MapId));
    }

    [Test]
    public async Task StartWithMissingDataset_RecordsClosedDatasetFailure()
    {
        var server = Server;

        await server.WaitPost(() =>
        {
            server.ProtoMan.LoadString(DatasetFailureFixtures);
            server.ProtoMan.ResolveResults();
        });

        await Client.WaitPost(() =>
        {
            Client.ProtoMan.LoadString(DatasetFailureFixtures);
            Client.ProtoMan.ResolveResults();
        });

        await server.WaitAssertion(() =>
        {
            MakeStation(server.EntMan);
            var foldersBefore = GetFolderUids(server.EntMan);
            var ticker = server.System<GameTicker>();
            Assert.That(ticker.StartGameRule(MissingDatasetRuleId, out var rule), Is.True);

            var component = server.EntMan.GetComponent<SolreignGhostCabinetsRuleComponent>(rule);
            Assert.That(
                component.LastObservation,
                Is.EqualTo(SolreignGhostCabinetsDeliveryOutcome.TranscriptDatasetUnavailable));
            Assert.That(GetFolderUids(server.EntMan).Except(foldersBefore), Is.Empty);
        });
    }

    [Test]
    public async Task StartWithEmptyDataset_RecordsClosedDatasetFailure()
    {
        var server = Server;

        await server.WaitPost(() =>
        {
            server.ProtoMan.LoadString(DatasetFailureFixtures);
            server.ProtoMan.ResolveResults();
        });

        await Client.WaitPost(() =>
        {
            Client.ProtoMan.LoadString(DatasetFailureFixtures);
            Client.ProtoMan.ResolveResults();
        });

        await server.WaitAssertion(() =>
        {
            MakeStation(server.EntMan);
            var foldersBefore = GetFolderUids(server.EntMan);
            var ticker = server.System<GameTicker>();
            Assert.That(ticker.StartGameRule(EmptyDatasetRuleId, out var rule), Is.True);

            var component = server.EntMan.GetComponent<SolreignGhostCabinetsRuleComponent>(rule);
            Assert.That(
                component.LastObservation,
                Is.EqualTo(SolreignGhostCabinetsDeliveryOutcome.TranscriptDatasetUnavailable));
            Assert.That(GetFolderUids(server.EntMan).Except(foldersBefore), Is.Empty);
        });
    }
}
