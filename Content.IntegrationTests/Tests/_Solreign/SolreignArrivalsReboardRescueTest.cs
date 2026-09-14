#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.Shuttles;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Shared.CCVar;
using Content.Shared.Mobs.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     THIS GUARDS A DEFECT A PLAYER FOUND. On 2026-07-30 banditofdoom reported that re-boarding the
///     arrivals shuttle gets you "shunted directly outside it in the middle of nowhere", bricks the
///     buckle, and then "if it re-enters on top of you you just become a smear".
///
///     ArrivalsSystem dumps every occupant WITHOUT <see cref="PendingClockInComponent"/> to the
///     shuttle's old pose on the map it left — open space at the dock — and its only coupon-processing
///     loop also calls the late-join antagonist selector. A re-boarder must therefore be rescued
///     without ever receiving that coupon: otherwise every shuttle trip becomes a fresh antag roll.
///
///     This test guards both player-visible effects together. The established player has no coupon
///     after re-boarding, but after vanilla performs its dump the Solreign rescue moves them onto a
///     station grid rather than leaving them parented to the bare departure map.
/// </summary>
[TestFixture]
public sealed class SolreignArrivalsReboardRescueTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        DummyTicker = false,
        Connected = true,
        InLobby = true,
        Dirty = true,
    };

    [Test]
    public async Task ReboarderIsRescuedAfterVanillaDumpWithoutClockInCoupon()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var entMan = server.EntMan;

        // Both CVar writes go through WaitPost. Touching the server's config or entity manager from
        // the test thread while the instance is not idle throws "Cannot perform this operation
        // without ensuring the instance is idle", which poisons the shared pool — every later test
        // then fails in SetUp with "Pool manager has not been initialized". Measured: doing this to
        // one CVar produced 22 cascade failures (13 InvalidOperation + 12 NullReference, ZERO
        // assertion failures) in an otherwise green suite.
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.ArrivalsShuttles, true);
            // The dump only runs when returns are disabled. This is the live production value
            // (default false, no box override), so the guarded path is the one players actually hit.
            server.CfgMan.SetCVar(CCVars.ArrivalsReturns, false);
        });

        await server.WaitPost(() => ticker.StartRound());
        await pair.RunTicksSync(20);

        // Find the arrivals shuttle the round set up.
        EntityUid shuttle = default;
        var found = false;
        await server.WaitPost(() =>
        {
            var query = entMan.EntityQueryEnumerator<ArrivalsShuttleComponent>();
            while (query.MoveNext(out var uid, out _))
            {
                shuttle = uid;
                found = true;
                break;
            }
        });

        Assert.That(found, Is.True, "no arrivals shuttle was created — this test would pass vacuously.");

        var returnsDisabled = false;
        await server.WaitPost(() => returnsDisabled = !server.CfgMan.GetCVar(CCVars.ArrivalsReturns));
        Assert.That(returnsDisabled, Is.True,
            "returns must be disabled or vanilla never dumps and the guard correctly does nothing.");

        // The dump only happens on the leg AWAY FROM THE STATION. At round start the shuttle is
        // docked at arrivals, so using its own map here would exercise the "leaving arrivals" path,
        // which correctly does nothing — the first version of this test did exactly that and failed
        // for that reason, not because the fix was broken.
        EntityUid? arrivalsMap = null;
        await server.WaitPost(() =>
        {
            var q = entMan.EntityQueryEnumerator<ArrivalsSourceComponent>();
            while (q.MoveNext(out var uid, out _))
            {
                arrivalsMap = entMan.GetComponent<TransformComponent>(uid).MapUid;
                break;
            }
        });
        Assert.That(arrivalsMap, Is.Not.Null, "no arrivals source — the guard would early-return.");

        // Spawn the mob OFF the shuttle and strip its coupon: exactly a player who has already
        // clocked in and walked away.
        EntityUid mob = default;
        EntityUid normalExitMob = default;
        EntityUid noSpawnMob = default;
        EntityUid? fromMap = null;
        await server.WaitPost(() =>
        {
            var mapQuery = entMan.EntityQueryEnumerator<MapComponent>();
            while (mapQuery.MoveNext(out var mapUid, out _))
            {
                if (mapUid == arrivalsMap!.Value)
                    continue;
                fromMap = mapUid;
                break;
            }

            if (fromMap != null)
            {
                mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(fromMap.Value, Vector2.Zero));
                normalExitMob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(fromMap.Value, Vector2.Zero));
                noSpawnMob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(fromMap.Value, Vector2.Zero));
                entMan.RemoveComponent<PendingClockInComponent>(mob);
                entMan.RemoveComponent<PendingClockInComponent>(normalExitMob);
                entMan.RemoveComponent<PendingClockInComponent>(noSpawnMob);
            }
        });
        await pair.RunTicksSync(3);

        Assert.That(fromMap, Is.Not.Null, "no non-arrivals map to depart from — test would be vacuous.");
        Assert.That(entMan.HasComponent<MobStateComponent>(mob), Is.True,
            "spawned entity is not a mob, so nothing would consider it.");
        Assert.That(entMan.HasComponent<PendingClockInComponent>(mob), Is.False,
            "precondition: an already-clocked-in player holds NO coupon, or the fix is untested.");

        // RE-BOARD. An established player must remain distinguishable from a genuine late joiner.
        // Granting PendingClockInComponent here is the regression: ArrivalsSystem's coupon loop
        // later calls TryMakeLateJoinAntag for an attached session.
        await server.WaitPost(() =>
        {
            entMan.GetComponent<TransformComponent>(mob).Coordinates =
                new EntityCoordinates(shuttle, Vector2.Zero);
            entMan.GetComponent<TransformComponent>(normalExitMob).Coordinates =
                new EntityCoordinates(shuttle, Vector2.Zero);
            entMan.GetComponent<TransformComponent>(noSpawnMob).Coordinates =
                new EntityCoordinates(shuttle, Vector2.Zero);
        });
        await pair.RunTicksSync(3);

        Assert.That(entMan.GetComponent<TransformComponent>(mob).GridUid, Is.EqualTo(shuttle),
            "precondition: the mob must actually be ON the shuttle grid after re-boarding.");
        Assert.That(entMan.HasComponent<PendingClockInComponent>(mob), Is.False,
            "an established re-boarder must never receive the genuine-latejoin coupon; " +
            "ArrivalsSystem processes that coupon by attempting a fresh late-join antag roll.");

        // An intentional exit into off-grid space is not a vanilla dump. Leaving the shuttle before
        // FTL must clear the marker so the rescue cannot teleport an unrelated spacewalker.
        await server.WaitPost(() =>
        {
            entMan.GetComponent<TransformComponent>(normalExitMob).Coordinates =
                new EntityCoordinates(fromMap!.Value, new Vector2(50f, 50f));

            // Force the no-spawn branch without mutating the station fixture: a map entity cannot
            // own a LateJoin spawn, so the rescue must fall back to keeping this mob aboard.
            entMan.GetComponent<SolreignArrivalsReboardRescueComponent>(noSpawnMob).Station = fromMap.Value;
        });
        await pair.RunTicksSync(3);

        Assert.That(entMan.HasComponent<SolreignArrivalsReboardRescueComponent>(normalExitMob), Is.False,
            "leaving the arrivals shuttle normally must clear the rescue marker before FTL.");

        // END TO END: depart FROM the station side — the leg on which vanilla dumps. Without the
        // fix this exact call moved an uncouponed mob off the shuttle grid onto the departure map
        // (measured: grid 1130 -> 1124), which is the vacuum the player reported.
        await server.WaitPost(() =>
        {
            var ev = new FTLStartedEvent(
                shuttle,
                new EntityCoordinates(shuttle, Vector2.Zero),
                fromMap,
                Matrix3x2.Identity,
                Angle.Zero);
            // Production raises this as a directed+broadcast event. The broadcast is required for
            // the Solreign post-dump rescue handler and keeps this test on the real event path.
            entMan.EventBus.RaiseLocalEvent(shuttle, ref ev, true);
        });
        await pair.RunTicksSync(5);

        // Vanilla first dumps an uncouponed mob to the bare departure map. The Solreign handler,
        // ordered after ArrivalsSystem, must then move the marked re-boarder onto a station grid.
        var parentAfter = entMan.GetComponent<TransformComponent>(mob).ParentUid;
        Assert.That(parentAfter, Is.Not.EqualTo(fromMap!.Value),
            "the re-boarder remained parented directly to the departure MAP after vanilla's dump — " +
            "left floating in open space at the dock, exactly as the player reported.");
        Assert.That(entMan.GetComponent<TransformComponent>(normalExitMob).ParentUid, Is.EqualTo(fromMap.Value),
            "an intentional off-grid exit was misclassified as a vanilla dump and teleported away.");
        Assert.That(entMan.GetComponent<TransformComponent>(noSpawnMob).GridUid, Is.EqualTo(shuttle),
            "with no usable station spawn, the rescue must put the player back aboard instead of " +
            "leaving them on the bare departure map.");

        await server.WaitPost(() => ticker.RestartRound());
    }
}
