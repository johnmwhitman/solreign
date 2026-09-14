#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.Zones;
using Content.Shared.Interaction;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Drives a real Sublevel H / Atrium round trip through <see cref="SolreignZoneGateSystem"/>'s
///     actual event handlers — the part <c>ZoneGateRulesTests</c> (Content.Tests) can't reach,
///     because <c>EnsureHellZoneLoaded</c>/<c>EnsureHeavenZoneLoaded</c> genuinely load
///     zone_hell.yml/zone_heaven.yml onto fresh maps the first time through.
///
///     Pair-driving pattern studied from <c>SolreignPerfBaselineTest</c> (spawn real prototypes onto
///     a live server, WaitPost) and <c>BuckleTest.Interact</c> (raise the interaction event directly
///     on the target entity via <c>EventBus.RaiseLocalEvent</c> instead of simulating an actual
///     click — SolreignZoneGateSystem's handlers only look at the event's User/Target fields, never
///     proximity, exactly like Buckle's InteractHandEvent handlers).
///
///     <see cref="PoolSettings.Dirty"/> is set: this system caches the hell/heaven map+grid as
///     private instance state the first time anyone enters, so the server this test mutates must
///     never be handed back to the pool for another test to reuse.
/// </summary>
[TestFixture]
public sealed class SolreignZoneGateIntegrationTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        // The stale-EntityManager HTN NPC race (HANDOFF §10) repeatedly picks this test as its
        // victim in full-battery runs — zone_hell.yml brings live NPCs. Same mitigation as the
        // antag-cycle fixtures: never accept a recycled server instance.
        Fresh = true,
    };

    [Test]
    public async Task HellEntry_ThenLadder_ThenReturn_RoundTripsTraveler()
    {
        var server = Server;
        var entMan = server.EntMan;

        EntityUid user = default;
        EntityCoordinates originalCoords = default;
        EntityUid entryGate = default;

        await server.WaitPost(() =>
        {
            var mapSystem = entMan.System<SharedMapSystem>();
            mapSystem.CreateMap(out var travelerMapId);

            user = entMan.SpawnEntity(null, new MapCoordinates(0, 0, travelerMapId));
            originalCoords = entMan.GetComponent<TransformComponent>(user).Coordinates;

            // Bare entry gate — no AccessReaderComponent, exercising the "ungated" admit path
            // (SolreignZoneGateRules.IsEntryAllowed with hasAccessReader: false). The access-gated
            // path is covered separately below using the real SolreignSublevelHGate prototype.
            entryGate = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            entMan.EnsureComponent<SolreignHellZoneEntryComponent>(entryGate);
        });

        EntityUid hellGrid = default;
        await server.WaitPost(() =>
        {
            var ev = new ActivateInWorldEvent(user, entryGate, complex: true);
            entMan.EventBus.RaiseLocalEvent(entryGate, ev);

            Assert.That(ev.Handled, Is.True, "Entry gate did not handle the activation.");

            var xform = entMan.GetComponent<TransformComponent>(user);
            Assert.That(xform.GridUid, Is.Not.Null, "Traveler did not land on a grid after entering Sublevel H.");
            hellGrid = xform.GridUid!.Value;

            Assert.That(entMan.TryGetComponent<SolreignZoneReturnComponent>(user, out var ret), Is.True,
                "Entry gate did not stamp a return record.");
            Assert.That(ret!.ReturnCoordinates, Is.EqualTo(originalCoords));
        });

        // A second traveler entering afterwards must land on the SAME grid — proves
        // EnsureHellZoneLoaded's cache-reuse path (SolreignZoneGateRules.IsZoneCacheValid) actually
        // fires on the second call instead of loading a brand new grid every time.
        await server.WaitPost(() =>
        {
            var mapSystem = entMan.System<SharedMapSystem>();
            mapSystem.CreateMap(out var secondMapId);
            var secondUser = entMan.SpawnEntity(null, new MapCoordinates(0, 0, secondMapId));

            var ev = new ActivateInWorldEvent(secondUser, entryGate, complex: true);
            entMan.EventBus.RaiseLocalEvent(entryGate, ev);

            var xform = entMan.GetComponent<TransformComponent>(secondUser);
            Assert.That(xform.GridUid, Is.EqualTo(hellGrid),
                "Second entry loaded a brand new Sublevel H grid instead of reusing the cached one.");
        });

        EntityUid ladderGate = default;
        await server.WaitPost(() =>
        {
            ladderGate = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            entMan.EnsureComponent<SolreignHeavenZoneLadderComponent>(ladderGate);
        });

        await server.WaitPost(() =>
        {
            var ev = new ActivateInWorldEvent(user, ladderGate, complex: true);
            entMan.EventBus.RaiseLocalEvent(ladderGate, ev);

            Assert.That(ev.Handled, Is.True, "Ladder gate did not handle the activation.");

            var xform = entMan.GetComponent<TransformComponent>(user);
            Assert.That(xform.GridUid, Is.Not.Null);
            Assert.That(xform.GridUid, Is.Not.EqualTo(hellGrid),
                "Ladder did not move the traveler onto a new (Heaven) grid.");

            // The ladder is mid-trip (Hell -> Heaven): it must NOT touch the original return
            // record, per SolreignHeavenZoneLadderComponent's doc comment.
            Assert.That(entMan.TryGetComponent<SolreignZoneReturnComponent>(user, out var ret), Is.True,
                "Ladder must leave the traveler's return record in place.");
            Assert.That(ret!.ReturnCoordinates, Is.EqualTo(originalCoords));
        });

        EntityUid returnGate = default;
        await server.WaitPost(() =>
        {
            returnGate = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            entMan.EnsureComponent<SolreignZoneReturnGateComponent>(returnGate);
        });

        await server.WaitPost(() =>
        {
            var ev = new ActivateInWorldEvent(user, returnGate, complex: true);
            entMan.EventBus.RaiseLocalEvent(returnGate, ev);

            Assert.That(ev.Handled, Is.True, "Return gate did not handle the activation.");

            var xform = entMan.GetComponent<TransformComponent>(user);
            Assert.That(xform.Coordinates, Is.EqualTo(originalCoords),
                "Return gate did not send the traveler all the way back to their original entry point.");
            Assert.That(entMan.HasComponent<SolreignZoneReturnComponent>(user), Is.False,
                "Return record was not consumed on exit.");
        });
    }

    [Test]
    public async Task HellEntry_AccessDenied_DoesNotTeleportOrStampReturn()
    {
        var server = Server;
        var entMan = server.EntMan;

        EntityUid user = default;
        EntityCoordinates originalCoords = default;
        EntityUid entryGate = default;

        await server.WaitPost(() =>
        {
            var mapSystem = entMan.System<SharedMapSystem>();
            mapSystem.CreateMap(out var mapId);

            // A bare entity with no ID card/access items — FindPotentialAccessItems finds nothing,
            // so the real SolreignSublevelHGate prototype's AccessReader (locked to the
            // SolreignSublevelH access level, same as the Crimson Keycard) refuses it, exactly like
            // an unauthorized crew member walking up to the real gate.
            user = entMan.SpawnEntity(null, new MapCoordinates(0, 0, mapId));
            originalCoords = entMan.GetComponent<TransformComponent>(user).Coordinates;

            entryGate = entMan.SpawnEntity("SolreignSublevelHGate", MapCoordinates.Nullspace);
        });

        await server.WaitPost(() =>
        {
            var ev = new ActivateInWorldEvent(user, entryGate, complex: true);
            entMan.EventBus.RaiseLocalEvent(entryGate, ev);

            Assert.That(ev.Handled, Is.True, "A denied entry attempt should still be marked handled.");

            var xform = entMan.GetComponent<TransformComponent>(user);
            Assert.That(xform.Coordinates, Is.EqualTo(originalCoords), "A denied traveler should not have moved.");
            Assert.That(entMan.HasComponent<SolreignZoneReturnComponent>(user), Is.False,
                "A denied traveler should not have a return record stamped.");
        });
    }

    [Test]
    public async Task ReturnGate_WithNoRecordedTrip_RefusesAndLeavesTravelerInPlace()
    {
        var server = Server;
        var entMan = server.EntMan;

        EntityUid user = default;
        EntityCoordinates originalCoords = default;
        EntityUid returnGate = default;

        await server.WaitPost(() =>
        {
            var mapSystem = entMan.System<SharedMapSystem>();
            mapSystem.CreateMap(out var mapId);

            user = entMan.SpawnEntity(null, new MapCoordinates(0, 0, mapId));
            originalCoords = entMan.GetComponent<TransformComponent>(user).Coordinates;

            returnGate = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            entMan.EnsureComponent<SolreignZoneReturnGateComponent>(returnGate);
        });

        await server.WaitPost(() =>
        {
            var ev = new ActivateInWorldEvent(user, returnGate, complex: true);
            entMan.EventBus.RaiseLocalEvent(returnGate, ev);

            Assert.That(ev.Handled, Is.True);

            var xform = entMan.GetComponent<TransformComponent>(user);
            Assert.That(xform.Coordinates, Is.EqualTo(originalCoords),
                "A traveler with no recorded trip should never move through the return gate.");
        });
    }
}
