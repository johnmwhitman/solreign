#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.Client._Solreign.Ghost;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Solreign.Ghost;
using Content.Shared.Ghost.Components;
using NUnit.Framework;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Project-audit gap ("tests cover pure helper functions, not live ECS Systems"): the pure
///     sanitize/sort logic behind the ghost "Afterlife" menu is already covered
///     (<c>GhostActivityRulesTests</c> in Content.Tests), but nothing drove the live
///     <see cref="Content.Server._Solreign.Ghost.SolreignGhostActivitySystem"/> through a real
///     networked request -- <c>grep GhostActivit Content.IntegrationTests/</c> returned nothing
///     before this file.
///
///     This is the one Solreign fixture in this folder that exercises a genuine client -&gt; server
///     -&gt; client round trip instead of raising a directed event straight on the server bus: the
///     real client entry point is <see cref="Content.Client._Solreign.Ghost.SolreignGhostActivitySystem.RequestActivities"/>,
///     which calls the protected <c>RaiseNetworkEvent</c> that cannot be invoked directly from test
///     code, so driving it for real is the only way to exercise
///     <c>SolreignGhostActivitySystem.OnActivitiesRequest</c>'s own ghost gate on the server. Both
///     sides are ticked together (<see cref="GameTest.RunTicksSync"/>, which forwards to
///     <c>Pair.RunTicksSync</c>) so the request and its reply actually cross the loopback network
///     transport rather than racing a single <c>WaitPost</c>.
/// </summary>
[TestFixture]
public sealed class SolreignGhostActivitySystemIntegrationTest : GameTest
{
    // Dirty: mutates the pool-shared connected player's body (adds GhostComponent) and spawns
    // untracked activity-marker entities directly via entMan.SpawnEntity -- same "must not hand a
    // polluted server back to the pool" precedent as ContractClaimFlowIntegrationTest /
    // SolreignZoneGateIntegrationTest.
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        // DummyTicker=false so the connected session has a real AttachedEntity to flag as a ghost.
        DummyTicker = false,
    };

    private static EntityUid SpawnActivity(
        Robust.Shared.GameObjects.IEntityManager entMan, string name, string description, bool enabled)
    {
        var uid = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
        var comp = entMan.EnsureComponent<SolreignGhostActivityComponent>(uid);
        comp.Name = name;
        comp.Description = description;
        comp.Enabled = enabled;
        return uid;
    }

    [Test]
    public async Task RequestActivities_AsGhost_ReturnsSeededEnabledActivities_ExcludesDisabled()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();

        await server.WaitPost(() =>
        {
            var session = playerMan.Sessions.First();
            var player = session.AttachedEntity
                          ?? throw new System.InvalidOperationException(
                              "Connected player has no AttachedEntity -- DummyTicker must be false.");
            entMan.EnsureComponent<GhostComponent>(player);

            SpawnActivity(entMan, "Zebra Yoga", "Stretch it out.", enabled: true);
            SpawnActivity(entMan, "Arcade Cabinet", "Watch the high-score chase.", enabled: true);
            SpawnActivity(entMan, "Hidden Debug Room", "Should never surface.", enabled: false);
        });

        GhostActivitiesResponseEvent? response = null;
        var clientSystem = Client.System<SolreignGhostActivitySystem>();
        void OnResponse(GhostActivitiesResponseEvent ev) => response = ev;
        clientSystem.ActivitiesResponse += OnResponse;

        try
        {
            await Client.WaitPost(() => clientSystem.RequestActivities());

            await RunTicksSync(10);

            Assert.That(response, Is.Not.Null,
                "A connected ghost's GhostActivitiesRequestEvent must be answered with a " +
                "GhostActivitiesResponseEvent within 10 synchronized ticks.");

            var names = response!.Activities.Select(a => a.Name).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(names, Does.Contain("Arcade Cabinet"));
                Assert.That(names, Does.Contain("Zebra Yoga"));
                Assert.That(names, Does.Not.Contain("Hidden Debug Room"),
                    "Enabled=false activities must never be served to the client.");

                var arcadeIndex = names.IndexOf("Arcade Cabinet");
                var zebraIndex = names.IndexOf("Zebra Yoga");
                Assert.That(arcadeIndex, Is.LessThan(zebraIndex),
                    "GhostActivityRules.SortForDisplay orders case-insensitively by name -- " +
                    "'Arcade Cabinet' must come before 'Zebra Yoga'.");
            });
        }
        finally
        {
            clientSystem.ActivitiesResponse -= OnResponse;
        }
    }

    [Test]
    public async Task RequestActivities_AsNonGhost_ReceivesNoResponse()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();

        await server.WaitPost(() =>
        {
            var session = playerMan.Sessions.First();
            var player = session.AttachedEntity
                          ?? throw new System.InvalidOperationException(
                              "Connected player has no AttachedEntity -- DummyTicker must be false.");
            // Deliberately NOT adding GhostComponent -- a live, embodied crew member.
            Assert.That(entMan.HasComponent<GhostComponent>(player), Is.False,
                "Setup failed: player must not already be a ghost for this negative case.");

            SpawnActivity(entMan, "Arcade Cabinet", "Watch the high-score chase.", enabled: true);
        });

        GhostActivitiesResponseEvent? response = null;
        var clientSystem = Client.System<SolreignGhostActivitySystem>();
        void OnResponse(GhostActivitiesResponseEvent ev) => response = ev;
        clientSystem.ActivitiesResponse += OnResponse;

        try
        {
            await Client.WaitPost(() => clientSystem.RequestActivities());

            // Bounded, not sleep-and-pray: give the round trip every chance a real answer would have
            // taken in the positive-path test, then assert it never arrived.
            await RunTicksSync(10);

            Assert.That(response, Is.Null,
                "SolreignGhostActivitySystem.OnActivitiesRequest must reject a non-ghost sender " +
                "(logs a warning and returns) instead of answering with the activity list.");
        }
        finally
        {
            clientSystem.ActivitiesResponse -= OnResponse;
        }
    }
}
