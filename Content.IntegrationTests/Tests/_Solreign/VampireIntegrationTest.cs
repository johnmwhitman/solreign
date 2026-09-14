#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.Antags.Vampire;
using Content.Shared._Solreign.Antags;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Solreign;

[TestFixture]
public sealed class VampireIntegrationTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
    };

    [Test]
    public async Task Vampire_CanBeAddedToHumanoid()
    {
        var server = Server;
        var entMan = server.EntMan;
        
        await server.WaitPost(() =>
        {
            var humanUid = entMan.SpawnEntity("MobHuman", MapCoordinates.Nullspace);
            var comp = entMan.EnsureComponent<SolreignVampireComponent>(humanUid);
            Assert.That(comp, Is.Not.Null, "Vampire component successfully attached");
        });
    }
}
