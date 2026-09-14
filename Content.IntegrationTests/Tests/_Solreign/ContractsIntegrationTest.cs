#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.Contracts;
using Content.Shared._Solreign.Contracts;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Solreign;

[TestFixture]
public sealed class ContractsIntegrationTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
    };

    [Test]
    public async Task ContractsSystem_StationInit_FillsContracts()
    {
        var server = Server;
        var entMan = server.EntMan;
        
        await server.WaitPost(() =>
        {
            // BaseStation is abstract (unspawnable); a bare entity carries the component just as
            // OnStationPostInit's EnsureComp does, and FillContracts is the same path that event hits.
            var stationUid = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            var sys = entMan.System<ContractsSystem>();

            var comp = entMan.EnsureComponent<StationSolreignContractsComponent>(stationUid);
            sys.FillContracts(stationUid, comp);
            Assert.That(comp.Contracts.Count, Is.GreaterThan(0), "Station init should issue at least one contract");
        });
    }
}
