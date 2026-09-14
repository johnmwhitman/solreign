#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.Pets;
using Content.Shared.Interaction;
using Content.Shared.Nutrition.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Project-audit gap ("tests cover pure helper functions, not live ECS Systems"): Solreign Pets
///     v1's state-transition logic is fully unit-tested in isolation
///     (<c>Content.Tests/_Solreign/TamingRulesTests.cs</c> covers <see cref="TamingRules.Evaluate"/>),
///     but nothing drove the live <see cref="SolreignTameableSystem"/> -- the actual
///     <c>InteractUsingEvent</c> handler that resolves the food whitelist, decides ownership, and
///     mutates <see cref="SolreignTameableComponent.TamedBy"/> -- through a real feed interaction.
///     <c>grep SolreignTameableSystem Content.IntegrationTests/</c> returned nothing before this
///     file.
///
///     Drives the real, mapper-authored <c>MobSolreignTabby</c> prototype (Resources/Prototypes/
///     _Solreign/Entities/pets.yml, whitelist: components: [Edible]) rather than hand-assembling a
///     fixture entity -- <see cref="SolreignTameableComponent"/> is <c>[Access(typeof(SolreignTameableSystem))]</c>-locked
///     for writes, so its whitelist can only come from YAML, not a test-side field set (same
///     constraint <c>ContractClaimFlowIntegrationTest</c> documents for <c>StationDataComponent</c>).
///     Reads of <c>TamedBy</c> are fine (Access's default "Other" permission is Read-only, per
///     <c>RobustToolbox/Robust.Shared/Analyzers/AccessAttribute.cs</c>) -- only writes are blocked --
///     so every assertion below reads real post-interaction state rather than probing through a
///     side channel.
///
///     Feed items are bare entities with <c>EnsureComponent&lt;EdibleComponent&gt;</c> added directly
///     (no prototype, no solution container): <c>EntityWhitelist.IsWhitelistPass</c> only checks
///     component presence, and <c>SolreignTameableSystem.OnInteractUsing</c> never reads
///     <c>EdibleComponent</c>'s own fields, so this is the minimal real "counts as food" fixture.
/// </summary>
[TestFixture]
public sealed class SolreignTameableSystemIntegrationTest : GameTest
{
    // Dirty: spawns critters/feeders/feed-items via entMan.SpawnEntity directly (not the tracked
    // Spawn proxy) and drives HTN replanning on them -- same "must not hand back untracked mobs"
    // precedent as SolreignShoveSystemIntegrationTest / ContractClaimFlowIntegrationTest.
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
    };

    private const string TabbyProto = "MobSolreignTabby";

    private static EntityUid SpawnFood(IEntityManager entMan, MapCoordinates coords)
    {
        var food = entMan.SpawnEntity(null, coords);
        entMan.EnsureComponent<EdibleComponent>(food);
        return food;
    }

    private static void Feed(IEntityManager entMan, EntityUid critter, EntityUid feeder, EntityUid food)
    {
        var ev = new InteractUsingEvent(feeder, food, critter, new EntityCoordinates(critter, Vector2.Zero));
        entMan.EventBus.RaiseLocalEvent(critter, ev);
    }

    [Test]
    public async Task Feed_FirstValidFood_NewlyTames_AndConsumesTheItem()
    {
        var server = Server;
        var entMan = server.EntMan;

        EntityUid critter = default;
        EntityUid feeder = default;
        EntityUid food = default;

        await server.WaitAssertion(() =>
        {
            var mapSystem = entMan.System<Robust.Shared.GameObjects.SharedMapSystem>();
            mapSystem.CreateMap(out var mapId);
            var coords = new MapCoordinates(0f, 0f, mapId);

            critter = entMan.SpawnEntity(TabbyProto, coords);
            feeder = entMan.SpawnEntity(null, coords);
            food = SpawnFood(entMan, coords);

            Feed(entMan, critter, feeder, food);

            Assert.That(entMan.GetComponent<SolreignTameableComponent>(critter).TamedBy, Is.EqualTo(feeder),
                "A valid first feed must register the feeder as the new owner.");
        });

        // SolreignTameableSystem.OnInteractUsing eats the treat via QueueDel, not an immediate delete --
        // that queue is only drained during EntityManager's per-tick QueueDel pass (EntityManager.Update ->
        // ProcessQueueudDeletions), which WaitAssertion never triggers (it just runs the delegate on the
        // server thread with no RunTicksMessage). Checking entMan.Deleted(food) in the same WaitAssertion
        // block as the Feed() call raced the queue and read stale state -- confirmed deterministic (not
        // the pool/HTN flake class) across solo single-test reruns. One real tick is enough to flush it.
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(food), Is.True,
                "SolreignTameableComponent.ConsumeFood defaults true -- a successful tame must eat the treat.");
        });
    }

    [Test]
    public async Task Feed_NonFoodItem_IsRejected_CritterStaysUntamed_ItemSurvives()
    {
        var server = Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var mapSystem = entMan.System<Robust.Shared.GameObjects.SharedMapSystem>();
            mapSystem.CreateMap(out var mapId);
            var coords = new MapCoordinates(0f, 0f, mapId);

            var critter = entMan.SpawnEntity(TabbyProto, coords);
            var feeder = entMan.SpawnEntity(null, coords);
            var notFood = entMan.SpawnEntity(null, coords); // no EdibleComponent

            var ev = new InteractUsingEvent(feeder, notFood, critter, new EntityCoordinates(critter, Vector2.Zero));
            entMan.EventBus.RaiseLocalEvent(critter, ev);

            Assert.Multiple(() =>
            {
                Assert.That(ev.Handled, Is.False,
                    "A whitelist-failing item must not be marked handled by the taming system.");
                Assert.That(entMan.GetComponent<SolreignTameableComponent>(critter).TamedBy, Is.Null,
                    "A rejected feed must not tame the critter.");
                Assert.That(entMan.Deleted(notFood), Is.False,
                    "Rejected feeds never consume the item (TamingRules.ShouldConsumeFood).");
            });
        });
    }

    [Test]
    public async Task Feed_AlreadyTamed_BySameOwner_StaysAlreadyBonded_NoOwnerChange()
    {
        var server = Server;
        var entMan = server.EntMan;

        EntityUid critter = default;
        EntityUid owner = default;
        EntityUid secondTreat = default;

        await server.WaitAssertion(() =>
        {
            var mapSystem = entMan.System<Robust.Shared.GameObjects.SharedMapSystem>();
            mapSystem.CreateMap(out var mapId);
            var coords = new MapCoordinates(0f, 0f, mapId);

            critter = entMan.SpawnEntity(TabbyProto, coords);
            owner = entMan.SpawnEntity(null, coords);

            Feed(entMan, critter, owner, SpawnFood(entMan, coords));
            Assert.That(entMan.GetComponent<SolreignTameableComponent>(critter).TamedBy, Is.EqualTo(owner),
                "Setup failed: first feed must tame the critter to 'owner' before the re-feed under test.");

            secondTreat = SpawnFood(entMan, coords);
            Feed(entMan, critter, owner, secondTreat);

            Assert.That(entMan.GetComponent<SolreignTameableComponent>(critter).TamedBy, Is.EqualTo(owner),
                "TamingOutcome.AlreadyBonded is a happy top-up, not a state change -- owner must be unchanged.");
        });

        // Same QueueDel-vs-WaitAssertion race as Feed_FirstValidFood_NewlyTames_AndConsumesTheItem:
        // OnInteractUsing's QueueDel(secondTreat) only gets drained by EntityManager's per-tick
        // ProcessQueueudDeletions, which a bare WaitAssertion never triggers. One real tick flushes it
        // before we read Deleted(secondTreat).
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(secondTreat), Is.True,
                "AlreadyBonded still consumes the treat (ShouldConsumeFood is true for every outcome but Rejected).");
        });
    }

    [Test]
    public async Task Feed_AlreadyTamed_ByDifferentFeeder_Retames_TransfersOwnership()
    {
        var server = Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var mapSystem = entMan.System<Robust.Shared.GameObjects.SharedMapSystem>();
            mapSystem.CreateMap(out var mapId);
            var coords = new MapCoordinates(0f, 0f, mapId);

            var critter = entMan.SpawnEntity(TabbyProto, coords);
            var firstOwner = entMan.SpawnEntity(null, coords);
            var rival = entMan.SpawnEntity(null, coords);

            Feed(entMan, critter, firstOwner, SpawnFood(entMan, coords));
            Assert.That(entMan.GetComponent<SolreignTameableComponent>(critter).TamedBy, Is.EqualTo(firstOwner),
                "Setup failed: first feed must tame the critter to 'firstOwner' before the rival's feed under test.");

            Feed(entMan, critter, rival, SpawnFood(entMan, coords));

            Assert.That(entMan.GetComponent<SolreignTameableComponent>(critter).TamedBy, Is.EqualTo(rival),
                "TamingRules.Evaluate returns Retamed for a different feeder on an already-tamed critter -- " +
                "ownership must transfer to the rival, not stay with the original owner.");
        });
    }
}
