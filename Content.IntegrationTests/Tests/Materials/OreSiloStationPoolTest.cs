#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server.Power.Components;
using Content.Shared.Materials;
using Content.Shared.Materials.OreSilo;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Materials;

/// <summary>
/// Covers the "unified station-wide material pool" behavior: every <see cref="OreSiloComponent"/>
/// on the same station shares one material bank, so a machine linked to one silo can use
/// materials deposited through a different silo elsewhere on the station. Separate stations must
/// never see each other's materials.
/// </summary>
[TestFixture]
[TestOf(typeof(OreSiloComponent))]
public sealed class OreSiloStationPoolTest : GameTest
{
    private const string Material = "Steel";

    [Test]
    public async Task SiloMaterials_AreSharedAcrossStation_AndIsolatedAcrossStations()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var materialStorage = server.System<SharedMaterialStorageSystem>();

        // Two independent grids - each will become the sole grid of its own station.
        var mapA = await pair.CreateTestMap();
        var mapB = await pair.CreateTestMap();

        EntityUid silo1 = default, silo2 = default, silo3 = default;
        EntityUid clientOnSilo2 = default, clientOnSilo3 = default;

        await server.WaitAssertion(() =>
        {
            // --- Station A: two silos, plus a lathe (OreSiloClient) linked to the *second* silo.
            // (GetOwningStation() only cares about StationMemberComponent.Station, so a bare
            // entity stands in for the "station" here rather than dragging in full StationSystem
            // setup.)
            var stationA = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            entMan.EnsureComponent<StationMemberComponent>(mapA.Grid.Owner, out var memberA);
            memberA.Station = stationA;
            entMan.Dirty(mapA.Grid.Owner, memberA);

            silo1 = entMan.SpawnEntity("MachineMaterialSilo", mapA.GridCoords);
            silo2 = entMan.SpawnEntity("MachineMaterialSilo", mapA.GridCoords);

            clientOnSilo2 = entMan.SpawnEntity("Autolathe", mapA.GridCoords);

            // Fake power so silo2 -> client transmission checks pass without a real power grid.
            entMan.GetComponent<ApcPowerReceiverComponent>(silo2).Powered = true;

            // Link the lathe to silo2 (end-state of the normal UI toggle flow). Both fields are
            // [Access]-restricted to SharedOreSiloSystem, so directly poking them to set up the
            // scenario needs an explicit opt-out here (same pattern used by e.g. BuckleMovementTest).
#pragma warning disable RA0002
            var siloComp2 = entMan.GetComponent<OreSiloComponent>(silo2);
            var clientComp2 = entMan.GetComponent<OreSiloClientComponent>(clientOnSilo2);
            siloComp2.Clients.Add(clientOnSilo2);
            clientComp2.Silo = silo2;
#pragma warning restore RA0002
            entMan.Dirty(silo2, siloComp2);
            entMan.Dirty(clientOnSilo2, clientComp2);

            // --- Station B: a single, entirely separate silo + lathe.
            var stationB = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            entMan.EnsureComponent<StationMemberComponent>(mapB.Grid.Owner, out var memberB);
            memberB.Station = stationB;
            entMan.Dirty(mapB.Grid.Owner, memberB);

            silo3 = entMan.SpawnEntity("MachineMaterialSilo", mapB.GridCoords);
            clientOnSilo3 = entMan.SpawnEntity("Autolathe", mapB.GridCoords);

            entMan.GetComponent<ApcPowerReceiverComponent>(silo3).Powered = true;

#pragma warning disable RA0002
            var siloComp3 = entMan.GetComponent<OreSiloComponent>(silo3);
            var clientComp3 = entMan.GetComponent<OreSiloClientComponent>(clientOnSilo3);
            siloComp3.Clients.Add(clientOnSilo3);
            clientComp3.Silo = silo3;
#pragma warning restore RA0002
            entMan.Dirty(silo3, siloComp3);
            entMan.Dirty(clientOnSilo3, clientComp3);
        });

        await server.WaitAssertion(() =>
        {
            // Deposit directly into silo1. Nothing has ever touched silo2 or clientOnSilo2.
            Assert.That(materialStorage.TryChangeMaterialAmount(silo1, Material, 500), Is.True);

            // The deposit must NOT sit in silo1's own local storage - it should be redirected to
            // the shared station pool (otherwise silo2/its client would never see it).
            Assert.That(materialStorage.GetMaterialAmount(silo1, Material, localOnly: true), Is.Zero);

            // silo2, a completely different silo on the same station, reports the full amount.
            Assert.That(materialStorage.GetMaterialAmount(silo2, Material), Is.EqualTo(500));

            // The lathe linked to silo2 can see the materials deposited via silo1.
            Assert.That(materialStorage.GetMaterialAmount(clientOnSilo2, Material), Is.EqualTo(500));

            // Consuming through the lathe (linked to silo2) draws down the shared pool...
            Assert.That(materialStorage.TryChangeMaterialAmount(clientOnSilo2, Material, -200), Is.True);

            // ...which is visible back through silo1.
            Assert.That(materialStorage.GetMaterialAmount(silo1, Material), Is.EqualTo(300));

            // Station B starts empty and is completely unaffected by station A's activity.
            Assert.That(materialStorage.GetMaterialAmount(silo3, Material), Is.Zero);
            Assert.That(materialStorage.GetMaterialAmount(clientOnSilo3, Material), Is.Zero);

            // Depositing into station B's silo must not leak into (or out of) station A.
            Assert.That(materialStorage.TryChangeMaterialAmount(silo3, Material, 50), Is.True);
            Assert.That(materialStorage.GetMaterialAmount(clientOnSilo3, Material), Is.EqualTo(50));
            Assert.That(materialStorage.GetMaterialAmount(silo1, Material), Is.EqualTo(300));
            Assert.That(materialStorage.GetMaterialAmount(silo2, Material), Is.EqualTo(300));
        });
    }
}
