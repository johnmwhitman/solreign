using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.Operations;
using Content.Server.GameTicking;
using Content.Server.Maps;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using System.Linq;

namespace Content.IntegrationTests.Tests._Solreign;

[TestFixture]
public sealed class SolreignShowPreflightCommandIntegrationTest : GameTest
{
    [Test]
    public async Task CommandRegistersAndExecutionDoesNotMutateObservedRoundState()
    {
        var server = Pair.Server;
        var console = server.ResolveDependency<IConsoleHost>();
        var ticker = server.System<GameTicker>();
        var maps = server.ResolveDependency<IGameMapManager>();
        var players = server.ResolveDependency<IPlayerManager>();
        var config = server.ResolveDependency<IConfigurationManager>();

        await server.WaitAssertion(() =>
        {
            Assert.That(console.AvailableCommands.TryGetValue("solreignshowpreflight", out var command), Is.True);
            Assert.That(command, Is.TypeOf<SolreignShowPreflightCommand>());
        });

        var runLevel = ticker.RunLevel;
        var roundId = ticker.RoundId;
        var selectedMap = maps.GetSelectedMap()?.ID;
        var playerCount = players.PlayerCount;
        var activeRules = ticker.GetActiveGameRules()
            .Select(uid => uid.Id)
            .Order()
            .ToArray();
        var featureFlags = SolreignShowPreflightCommand.SignatureCVars
            .ToDictionary(definition => definition.Name, config.GetCVar);

        console.ExecuteCommand("solreignshowpreflight");

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(ticker.RunLevel, Is.EqualTo(runLevel));
                Assert.That(ticker.RoundId, Is.EqualTo(roundId));
                Assert.That(maps.GetSelectedMap()?.ID, Is.EqualTo(selectedMap));
                Assert.That(players.PlayerCount, Is.EqualTo(playerCount));
                Assert.That(ticker.GetActiveGameRules().Select(uid => uid.Id).Order().ToArray(),
                    Is.EqualTo(activeRules));
                Assert.That(
                    SolreignShowPreflightCommand.SignatureCVars
                        .ToDictionary(definition => definition.Name, config.GetCVar),
                    Is.EqualTo(featureFlags));
            });
        });
    }
}
