#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.Client._Solreign.FX;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.FX;
using Content.Shared.CCVar;
using NUnit.Framework;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._Solreign.FX;

[TestFixture]
public sealed class SolreignFxMissingAnchorFailSoftIntegrationTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
    };

    [Test]
    public async Task MissingBroadcastAnchor_IsDeliveredButNotAcceptedOrWarningFatal()
    {
        EntityUid observer = default;

        await Server.WaitAssertion(() =>
        {
            var mapSystem = Server.EntMan.System<SharedMapSystem>();
            mapSystem.CreateMap(out var mapId);

            observer = Server.EntMan.SpawnEntity("MobHuman", new MapCoordinates(0f, 0f, mapId));
            var players = Server.ResolveDependency<IPlayerManager>();
            Assert.That(ServerSession, Is.Not.Null);
            Assert.That(players.SetAttachedEntity(ServerSession!, observer), Is.True);
            Server.CfgMan.SetCVar(CCVars.SolreignFxCueV1Enabled, true);
        });

        await Pair.RunTicksSync(5);
        await Client.WaitPost(() => Client.System<SolreignFxCueSystem>().ClearTestVisibilityStateForTests());

        await Server.WaitAssertion(() =>
        {
            var anchor = Server.EntMan.SpawnEntity("MobHuman", Server.Transform(observer).Coordinates);
            var raised = Server.EntMan.System<SolreignFxServerSystem>().RaiseCue(
                "dust",
                coordinates: null,
                Server.EntMan.GetNetEntity(anchor),
                intensity: 0.5f,
                scale: 1f,
                duration: 3f,
                paletteIndex: null,
                phase: null,
                pvsSource: anchor);

            Assert.That(raised, Is.True);
            Server.EntMan.DeleteEntity(anchor);
        });

        await Pair.RunTicksSync(5);
        await Client.WaitAssertion(() =>
        {
            var system = Client.System<SolreignFxCueSystem>();
            Assert.That(system.RawReceivedEffectIdsForTests.Count(id => id == "dust"), Is.EqualTo(1),
                "the wire event must still be observable even though its ephemeral anchor is gone");
            Assert.That(system.AcceptedCuesForTests.Count(cue => cue.EffectId == "dust"), Is.Zero,
                "a cue whose entity anchor never existed locally must never render");
        });
    }
}
