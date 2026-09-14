#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.HotPotato;
using Content.Shared._Solreign.HotPotato;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Solreign;

[TestFixture]
public sealed class HotPotatoIntegrationTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
    };

    [Test]
    public async Task ArmedPotato_DetonatesAfterFuse()
    {
        var server = Server;
        var entMan = server.EntMan;
        var potatoSys = server.System<SolreignHotPotatoSystem>();
        var timing = server.ResolveDependency<IGameTiming>();

        EntityUid potatoUid = default;

        await server.WaitPost(() =>
        {
            potatoUid = entMan.SpawnEntity("Paper", MapCoordinates.Nullspace);
            var comp = entMan.EnsureComponent<SolreignHotPotatoComponent>(potatoUid);
            comp.Armed = true;
            comp.DetonateAt = timing.CurTime + System.TimeSpan.FromSeconds(1);
        });

        // Wait 1.5 seconds to ensure fuse expires
        await server.WaitRunTicks(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(potatoUid), Is.True, "Potato should have detonated and been deleted.");
        });
    }
}
