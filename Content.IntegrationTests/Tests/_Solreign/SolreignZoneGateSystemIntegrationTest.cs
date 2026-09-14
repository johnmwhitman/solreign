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
///     Pair-drives <see cref="SolreignZoneGateSystem"/>'s three real component/event subscriptions
///     (<see cref="SolreignHellZoneEntryComponent"/> / <see cref="SolreignZoneReturnGateComponent"/> /
///     <see cref="SolreignHeavenZoneLadderComponent"/> x <see cref="ActivateInWorldEvent"/>). Pure
///     rule predicates already live in <c>ZoneGateRulesTests</c> (Content.Tests); this file is the
///     System-layer half — spawn a gate entity, raise the event the subscription declares, assert
///     traveler state transitions (coords, return-record stamp/consume, map-cache reuse).
///
///     Pattern studied from <c>BuckleTest.Interact</c>-style tests (raise
///     <see cref="ActivateInWorldEvent"/> directly via <c>EventBus.RaiseLocalEvent</c> instead of a
///     real click — the handlers only look at User/Target, never proximity).
///
///     <see cref="PoolSettings.Dirty"/> is set: the system caches hell/heaven map+grid as private
///     instance state on first use, so the server this mutates must never re-enter the pool.
///
///     Deliberate scope boundary, stated honestly: (1) the access-granted happy path with a real
///     Crimson Keycard / <c>AccessReader</c> allowance is not fabricated here — building a full
///     ID-card access graph for one admit is heavy, and the ungated bare-component entry path plus
///     the real <c>SolreignSublevelHGate</c> deny path already cover both branches of
///     <see cref="SolreignZoneGateRules.IsEntryAllowed"/> at the System layer; (2) the
///     <c>TryLoadGrid</c> failure throw inside <c>EnsureHellZoneLoaded</c>/<c>EnsureHeavenZoneLoaded</c>
///     is not driven (would require sabotaging map path resolution) — map-path existence
///     (Resources/Maps/_Solreign/zone_hell.yml / zone_heaven.yml) is verified to exist on disk and
///     exercised as a load-success path by the round-trip test below.
/// </summary>
[TestFixture]
public sealed class SolreignZoneGateSystemIntegrationTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
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
            // (SolreignZoneGateRules.IsEntryAllowed with hasAccessReader: false).
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

        // Second traveler must land on the SAME grid — proves EnsureHellZoneLoaded's cache-reuse
        // path (SolreignZoneGateRules.IsZoneCacheValid) fires instead of loading a brand-new grid.
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

            // Mid-trip Hell -> Heaven: must NOT touch the original return record.
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

            // Bare entity, no ID/access items — SolreignSublevelHGate's AccessReader (locked to
            // SolreignSublevelH) refuses, exactly like an unauthorized crew member at the real gate.
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
            Assert.That(entMan.HasComponent<SolreignZoneReturnComponent>(user), Is.False);
        });
    }

    [Test]
    public async Task EntryGate_AlreadyHandledEvent_IsIgnored()
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

            user = entMan.SpawnEntity(null, new MapCoordinates(0, 0, mapId));
            originalCoords = entMan.GetComponent<TransformComponent>(user).Coordinates;

            entryGate = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            entMan.EnsureComponent<SolreignHellZoneEntryComponent>(entryGate);
        });

        await server.WaitPost(() =>
        {
            var ev = new ActivateInWorldEvent(user, entryGate, complex: true) { Handled = true };
            entMan.EventBus.RaiseLocalEvent(entryGate, ev);

            var xform = entMan.GetComponent<TransformComponent>(user);
            Assert.That(xform.Coordinates, Is.EqualTo(originalCoords),
                "OnEntryActivate must no-op when args.Handled is already true.");
            Assert.That(entMan.HasComponent<SolreignZoneReturnComponent>(user), Is.False,
                "A pre-handled activation must not stamp a return record.");
        });
    }
}
