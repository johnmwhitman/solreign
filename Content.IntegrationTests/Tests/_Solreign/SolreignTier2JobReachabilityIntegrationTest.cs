#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.GameTicking;
using Content.Server.Maps;
using Content.Server.Spawners.Components;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared.CCVar;
using Content.Shared.Inventory;
using Content.Shared.Maps;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Content.Shared.StatusIcon;
using Robust.Shared.EntitySerialization;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Solreign;

[TestFixture]
public sealed class SolreignTier2JobReachabilityIntegrationTest : GameTest
{
    private static readonly ProtoId<JobPrototype>[] Jobs =
    {
        "SolreignSectorFreightMaster",
        "SolreignExpeditionSpecialist",
    };

    private static readonly ProtoId<DepartmentPrototype> SolreignDepartment = "Solreign";
    private static readonly ProtoId<DepartmentPrototype> CargoDepartment = "Cargo";

    private static readonly IReadOnlyDictionary<ProtoId<JobPrototype>, JobContract> ExpectedContracts =
        new Dictionary<ProtoId<JobPrototype>, JobContract>
        {
            ["SolreignSectorFreightMaster"] = new(
                TimeSpan.FromHours(3),
                new Dictionary<string, string>
                {
                    ["jumpsuit"] = "ClothingUniformJumpsuitCargo",
                    ["shoes"] = "ClothingShoesBootsWork",
                    ["id"] = "CargoPDA",
                    ["ears"] = "ClothingHeadsetCargo",
                    ["outerClothing"] = "ClothingOuterHardsuitSpatio",
                    ["belt"] = "BoxFolderClipboardThreePapers",
                    ["pocket1"] = "AppraisalTool",
                }),
            ["SolreignExpeditionSpecialist"] = new(
                TimeSpan.FromHours(2),
                new Dictionary<string, string>
                {
                    ["jumpsuit"] = "ClothingUniformJumpsuitSalvageSpecialist",
                    ["shoes"] = "ClothingShoesBootsSalvage",
                    ["id"] = "SalvagePDA",
                    ["ears"] = "ClothingHeadsetCargo",
                    ["outerClothing"] = "ClothingOuterHardsuitSalvage",
                    ["belt"] = "ClothingBeltSalvageWebbing",
                    ["pocket1"] = "HandheldGPSBasic",
                    ["pocket2"] = "GeigerCounter",
                }),
        };

    public override PoolSettings PoolSettings => new()
    {
        Dirty = true,
    };

    [Test]
    public async Task PrototypesResolveWithBoundedPrivilegesAndGear()
    {
        await Server.WaitAssertion(() =>
        {
            var solreignDepartment = SProtoMan.Index(SolreignDepartment);
            var cargoDepartment = SProtoMan.Index(CargoDepartment);
            var allDepartments = SProtoMan.EnumeratePrototypes<DepartmentPrototype>().ToArray();
            var roleSystem = SEntMan.System<SharedRoleSystem>();
            var stationJobs = SEntMan.System<StationJobsSystem>();

            Assert.Multiple(() =>
            {
                foreach (var jobId in Jobs)
                {
                    var job = SProtoMan.Index<JobPrototype>(jobId);
                    var contract = ExpectedContracts[jobId];
                    var expectedAccess = new[] { "Cargo", "Salvage", "Maintenance", "External" };
                    var gear = SProtoMan.Index<StartingGearPrototype>(job.StartingGear!.Value);
                    var requirements = roleSystem.GetRoleRequirements(job);
                    var cargoRequirement = requirements?.OfType<DepartmentTimeRequirement>().Single();

                    Assert.That(stationJobs.TryGetJobWeight(job, null, out var jobWeight), Is.True,
                        $"{jobId}: default assignment weight resolves");
                    Assert.That(jobWeight, Is.EqualTo(5), $"{jobId}: assignment weight");
                    Assert.That(job.Access.Select(access => access.Id), Is.EquivalentTo(expectedAccess), $"{jobId}: exact access");
                    Assert.That(job.Special, Is.Empty, $"{jobId}: no privileged job specials");
                    Assert.That(job.StartingGear, Is.Not.Null);
                    Assert.That(
                        gear.Equipment.ToDictionary(entry => entry.Key, entry => entry.Value.Id),
                        Is.EqualTo(contract.Equipment),
                        $"{jobId}: exact field kit");
                    Assert.That(SProtoMan.HasIndex<JobIconPrototype>(job.Icon), Is.True);
                    Assert.That(SProtoMan.HasIndex<RoleLoadoutPrototype>($"Job{jobId}"), Is.True);
                    Assert.That(job.LocalizedName, Is.Not.Empty.And.Not.EqualTo(job.Name), $"{jobId}: localized display name");
                    Assert.That(job.LocalizedDescription, Is.Not.Empty.And.Not.EqualTo(job.Description), $"{jobId}: localized description");
                    Assert.That(cargoRequirement, Is.Not.Null, $"{jobId}: exactly one Cargo time requirement");
                    Assert.That(cargoRequirement!.Department, Is.EqualTo(CargoDepartment), $"{jobId}: requirement department");
                    Assert.That(cargoRequirement.Time, Is.EqualTo(contract.RequiredCargoTime), $"{jobId}: requirement threshold");
                    Assert.That(
                        SProtoMan.Index<RoleLoadoutPrototype>($"Job{jobId}").Groups.Select(group => group.Id),
                        Does.Not.Contain("GroupTankHarness"),
                        $"{jobId}: tank harness would occupy Vox outerClothing before StartingGear");
                    Assert.That(solreignDepartment.Roles, Contains.Item(jobId), $"{jobId}: visible in Solreign department");
                    Assert.That(cargoDepartment.Roles, Contains.Item(jobId), $"{jobId}: Cargo owns progression");
                    Assert.That(
                        allDepartments.Where(department => department.Primary && department.Roles.Contains(jobId)).Select(department => department.ID),
                        Is.EqualTo(new[] { "Cargo" }),
                        $"{jobId}: exactly one primary department");
                }
            });
        });
    }

    [Test]
    public async Task CargoPlaytimeRequirementsRejectBelowAndAcceptAtBoundary()
    {
        await Server.WaitAssertion(() =>
        {
            var roleSystem = SEntMan.System<SharedRoleSystem>();

            foreach (var (jobId, contract) in ExpectedContracts)
            {
                var job = SProtoMan.Index<JobPrototype>(jobId);
                var requirement = roleSystem.GetRoleRequirements(job)!.OfType<DepartmentTimeRequirement>().Single();
                var playTimes = new Dictionary<string, TimeSpan>
                {
                    [job.PlayTimeTracker.Id] = contract.RequiredCargoTime - TimeSpan.FromSeconds(1),
                };

                Assert.That(
                    requirement.Check(SEntMan, SProtoMan, null, playTimes, out _),
                    Is.False,
                    $"{jobId}: one second below the Cargo threshold must be rejected");

                playTimes[job.PlayTimeTracker.Id] = contract.RequiredCargoTime;
                Assert.That(
                    requirement.Check(SEntMan, SProtoMan, null, playTimes, out _),
                    Is.True,
                    $"{jobId}: the exact Cargo threshold must be accepted");
            }
        });
    }

    [Test]
    public async Task ConfiguredSolreignPoolResolvesExactlyTheSevenMaps()
    {
        await OverrideCVar(Side.Server, CCVars.GameMapPool, SolreignMapTestCatalog.MapPoolId);
        var manager = Server.ResolveDependency<IGameMapManager>();

        await Server.WaitAssertion(() =>
        {
            var runtimeIds = manager.AllVotableMaps().Select(map => map.ID).Order().ToArray();
            Assert.That(runtimeIds, Is.EqualTo(SolreignMapTestCatalog.MapIds.Order().ToArray()));
        });
    }

    [Test]
    public async Task VoxReceivesRoleDefiningHardsuit()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var stationSpawning = entMan.System<StationSpawningSystem>();
        var inventory = entMan.System<InventorySystem>();
        var testMap = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            foreach (var (jobId, contract) in ExpectedContracts)
            {
                var profile = HumanoidCharacterProfile.DefaultWithSpecies("Vox");
                var body = stationSpawning.SpawnPlayerMob(testMap.GridCoords, jobId, profile, station: null);

                Assert.That(
                    inventory.TryGetSlotEntity(body, "outerClothing", out var outerClothing),
                    Is.True,
                    $"{jobId}: Vox spawned without role-defining outer clothing");
                Assert.That(
                    entMan.GetComponent<MetaDataComponent>(outerClothing!.Value).EntityPrototype?.ID,
                    Is.EqualTo(contract.Equipment["outerClothing"]),
                    $"{jobId}: Vox role-defining hardsuit was displaced by its species loadout");

                entMan.DeleteEntity(body);
            }
        });
    }

    [Test]
    [TestCaseSource(typeof(SolreignMapTestCatalog), nameof(SolreignMapTestCatalog.MapIds))]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.GridFill), false)]
    public async Task EveryProductionMapOffersOneSlotAndOneStationSpawnPerJob(string mapProtoId)
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var ticker = entMan.System<GameTicker>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var loadedMapId = MapId.Nullspace;
        var stationGrids = new HashSet<EntityUid>();
        StationJobsComponent? stationJobs = null;
        var spawnCounts = Jobs.ToDictionary(job => job, _ => 0);

        await server.WaitPost(() =>
        {
            var mapProto = server.ProtoMan.Index<GameMapPrototype>(mapProtoId);
            ticker.LoadGameMap(
                mapProto,
                out loadedMapId,
                DeserializationOptions.Default with { InitializeMaps = true });

            var memberQuery = entMan.GetEntityQuery<StationMemberComponent>();
            foreach (var grid in mapSystem.GetAllGrids(loadedMapId))
            {
                if (!memberQuery.TryGetComponent(grid.Owner, out var member))
                    continue;

                if (!entMan.TryGetComponent<StationDataComponent>(member.Station, out var stationData))
                    continue;

                stationGrids.UnionWith(stationData.Grids);
                entMan.TryGetComponent(member.Station, out stationJobs);
            }

            var spawnQuery = entMan.AllEntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
            while (spawnQuery.MoveNext(out _, out var spawn, out var xform))
            {
                if (spawn.SpawnType != SpawnPointType.Job
                    || spawn.Job is not { } job
                    || !spawnCounts.ContainsKey(job)
                    || xform.GridUid is not { } grid
                    || !stationGrids.Contains(grid))
                {
                    continue;
                }

                spawnCounts[job]++;
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(stationGrids, Is.Not.Empty, $"{mapProtoId}: station grids");
            Assert.That(stationJobs, Is.Not.Null, $"{mapProtoId}: StationJobs");

            foreach (var job in Jobs)
            {
                Assert.That(stationJobs!.SetupAvailableJobs, Contains.Key(job), $"{mapProtoId}: roster contains {job}");
                Assert.That(stationJobs.SetupAvailableJobs[job], Is.EqualTo(new[] { 1, 1 }), $"{mapProtoId}: exact slots for {job}");
                Assert.That(spawnCounts[job], Is.EqualTo(1), $"{mapProtoId}: exact station-grid spawn count for {job}");
            }
        });

        await server.WaitPost(() =>
        {
            if (loadedMapId != MapId.Nullspace)
                mapSystem.DeleteMap(loadedMapId);
        });
    }

    private sealed record JobContract(
        TimeSpan RequiredCargoTime,
        IReadOnlyDictionary<string, string> Equipment);
}
