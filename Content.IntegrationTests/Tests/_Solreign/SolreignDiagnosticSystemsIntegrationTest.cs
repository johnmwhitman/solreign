#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.IntegrationTests.Tests.Atmos;
using Content.Server._Solreign.Diagnostics;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Station.Systems;
using Content.Shared.Atmos;
using Content.Shared.CCVar;
using Content.Shared.Coordinates;
using Content.Shared.Power.Components;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Proves the recovered power diagnostic is reached through the real post-solver event rather
///     than only through its direct unit-test API.
/// </summary>
[TestFixture]
public sealed class SolreignPowerDiagnosticIntegrationTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = """
- type: entity
  id: SolreignDiagnosticPowerConsumer
  components:
  - type: Transform
    anchored: true
  - type: NodeContainer
    nodes:
      input:
        !type:CableDeviceNode
        nodeGroupID: HVPower
  - type: PowerConsumer
""";

    public override PoolSettings PoolSettings => new()
    {
        Connected = false,
        Dirty = true,
    };

    [Test]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.SolreignPowerDiagnosticsEnabled), true)]
    public async Task PostSolverEventInvokesDiagnosticCaller()
    {
        var raised = new List<object>();
        var map = await Pair.CreateTestMap();
        EntityUid smes = default;
        PowerNetworkBatteryComponent networkBattery = default!;

        await Server.WaitPost(() =>
        {
            var entMan = Server.EntMan;
            var mapSystem = entMan.System<SharedMapSystem>();
            var stationSystem = Server.System<StationSystem>();
            var batterySystem = Server.System<BatterySystem>();

            var station = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            entMan.EnsureComponent<StationDataComponent>(station);
            stationSystem.AddGridToStation(station, map.Grid);

            for (var y = 0; y < 3; y++)
            {
                mapSystem.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(0, y), new Tile(1));
                entMan.SpawnEntity("CableHV", map.Grid.Owner.ToCoordinates(0, y));
            }

            smes = entMan.SpawnEntity("SMESBasic", map.Grid.Owner.ToCoordinates());
            var consumer = entMan.SpawnEntity("SolreignDiagnosticPowerConsumer", map.Grid.Owner.ToCoordinates(0, 2));
            entMan.GetComponent<PowerConsumerComponent>(consumer).DrawRate = 50_000f;

            networkBattery = entMan.GetComponent<PowerNetworkBatteryComponent>(smes);
            networkBattery.Enabled = true;
            networkBattery.CanDischarge = true;
            networkBattery.MaxSupply = 50_000f;
            networkBattery.SupplyRampTolerance = 50_000f;
            networkBattery.SupplyRampRate = 50_000f;

            var battery = entMan.GetComponent<BatteryComponent>(smes);
            batterySystem.SetMaxCharge((smes, battery), 8_000_000f);
            batterySystem.SetCharge((smes, battery), 8_000_000f);
        });

        await Server.WaitRunTicks(5);

        await Server.WaitPost(() =>
        {
            var system = Server.System<SolreignPowerDiagnosticSystem>();
            Assert.That(system.IsEnabled, Is.True);
            system.ResetMetrics();
            system.OnEventRaised = raised.Add;
            Server.System<PowerNetSystem>().Update(1f / 30f);
        });

        await Server.WaitAssertion(() =>
        {
            var snapshot = Server.System<SolreignPowerDiagnosticSystem>().GetSnapshot();
            Assert.That(raised, Has.Some.InstanceOf<SolreignPowerDiagnosticMetricEvent>(),
                $"{nameof(NetworkBatteryPostSync)} did not reach the recovered power diagnostic.");
            Assert.Multiple(() =>
            {
                Assert.That(snapshot.GridPowerDrawkW, Is.EqualTo(50d).Within(0.001));
                Assert.That(snapshot.GridPowerSupplykW, Is.GreaterThan(0d));
                Assert.That(
                    snapshot.BatteryCapacitykWh,
                    Is.EqualTo(networkBattery.NetworkBattery.Capacity / 3_600_000d).Within(0.001));
                Assert.That(
                    snapshot.BatteryReservekWh,
                    Is.EqualTo(networkBattery.NetworkBattery.CurrentStorage / 3_600_000d).Within(0.001));
            });
        });
    }
}

/// <summary>
///     Proves a real atmos-device processing pass feeds the recovered diagnostic from the tile's
///     live <see cref="Content.Shared.Atmos.GasMixture"/>.
/// </summary>
[TestFixture]
public sealed class SolreignAtmosDiagnosticIntegrationTest : AtmosTest
{
    protected override ResPath? TestMapPath =>
        new("Maps/Test/Atmospherics/DeltaPressure/deltapressuretest.yml");

    [Test]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.SolreignAtmosDiagnosticsEnabled), true)]
    public async Task AtmosDeviceUpdateFeedsLiveTileMixture()
    {
        var raised = new List<object>();
        EntityUid sensor = default;

        await Server.WaitPost(() =>
        {
            SEntMan.EnsureComponent<StationMemberComponent>(MapData.Grid);

            var system = Server.System<SolreignAtmosDiagnosticSystem>();
            system.ResetMetrics();
            system.OnEventRaised = raised.Add;

            sensor = SEntMan.SpawnEntity("AirSensor", MapData.GridCoords);
        });

        SAtmos.RunProcessingFull(ProcessEnt, MapData.Grid.Owner, SAtmos.AtmosTickRate);

        await Server.WaitAssertion(() =>
        {
            var mixture = SAtmos.GetTileMixture(sensor);
            Assert.That(mixture, Is.Not.Null);

            var snapshot = Server.System<SolreignAtmosDiagnosticSystem>().GetSnapshot();
            Assert.Multiple(() =>
            {
                Assert.That(raised, Has.Some.InstanceOf<SolreignAtmosDiagnosticMetricEvent>());
                Assert.That(snapshot.RoomPressurekPa, Is.EqualTo(mixture!.Pressure).Within(0.001));
                Assert.That(
                    snapshot.OxygenRatioPercent,
                    Is.EqualTo(SolreignAtmosDiagnosticEvaluator.CalculateOxygenRatioPercent(
                        mixture.GetMoles(Gas.Oxygen),
                        mixture.TotalMoles)).Within(0.001));
            });
        });
    }
}
