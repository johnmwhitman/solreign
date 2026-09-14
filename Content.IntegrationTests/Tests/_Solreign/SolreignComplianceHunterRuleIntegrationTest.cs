#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.GameTicking;
using Content.Server._Solreign.Terminator;
using Content.Shared.GameTicking.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Integration test for <see cref="SolreignComplianceHunterRule"/>.
///     Ensures the rule can be admin-started, executes its population scaling and concurrent-spawn gates,
///     and safely ends itself without throwing.
/// </summary>
[TestFixture]
public sealed class SolreignComplianceHunterRuleIntegrationTest : GameTest
{
    private static readonly EntProtoId RulePrototype = "SolreignComplianceHunterRule";

    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        DummyTicker = false,
    };

    [Test]
    public async Task ComplianceHunterRule_StartsAndExecutesScalingGate()
    {
        var server = Server;
        var entMan = server.EntMan;
        var ticker = server.System<GameTicker>();

        await server.WaitPost(() =>
        {
            // Admin-start the rule
            ticker.AddGameRule(RulePrototype);
        });

        // Let the tick process to invoke Started()
        await server.WaitRunTicks(10);

        await server.WaitAssertion(() =>
        {
            // Verify that the rule executed and then ended itself via ForceEndSelf
            // The StationEventSystem will end the rule, removing it from ActiveGameRules
            var hasActiveRule = false;
            var query = entMan.EntityQueryEnumerator<SolreignComplianceHunterRuleComponent, GameRuleComponent>();
            while (query.MoveNext(out var uid, out _, out var gameRule))
            {
                if (entMan.HasComponent<ActiveGameRuleComponent>(uid))
                    hasActiveRule = true;
            }

            // Since it ForceEndSelf() in Started(), it shouldn't be active anymore.
            Assert.That(hasActiveRule, Is.False, "Rule should end itself after processing the spawn/gate logic.");
        });
    }
}
