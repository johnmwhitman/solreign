#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Solreign.HotPotato;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using NUnit.Framework;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Phase-2 Track B2 (docs/plans/2026-07-11-ROADMAP-PHASE2.md, Track B, Wave B2): the Mandatory
///     Team-Building Exercise is named as the single highest round-frequency system with only
///     Rules-layer (<c>HotPotatoTests.cs</c>) coverage — the actual ECS System
///     (<see cref="SolreignHotPotatoSystem"/> / <see cref="SharedSolreignHotPotatoSystem"/>) that
///     fires every live round had no test exercising the real component/event path. This file closes
///     that gap via real pickups on a connected player (<c>HandTests.TestPickupDrop</c> idiom) and
///     real fuse-expiry ticks (<c>WerewolfPolymorphTriggerTest</c>'s <c>WaitRunTicks</c> idiom).
///
///     Deliberate scope boundary, stated honestly rather than silently skipped: the COLLISION-
///     triggered half of a transfer (<c>SolreignHotPotatoSystem.OnHolderCollide</c>, subscribed to
///     <see cref="Robust.Shared.Physics.Events.StartCollideEvent"/>) is NOT exercised here.
///     <c>StartCollideEvent</c>'s constructor is <c>internal</c> to Robust.Shared, so it cannot be
///     fabricated from this assembly the way <c>SolreignZoneGateIntegrationTest</c> fabricates
///     <c>ActivateInWorldEvent</c> (public ctor); driving two real physics bodies into an actual
///     collision was judged too fragile to add without the ability to compile/run it locally (this
///     lane has no dotnet-build access — verification is the CI gate's job, not this pass's). The
///     forced hand-off used below (<see cref="SharedHandsSystem.TryForcePickupAnyHand"/>) is the
///     exact call <c>OnHolderCollide</c> itself makes once a collision is accepted, so it exercises
///     everything downstream of "a collision was accepted" — arm/no-reset/no-drop/fuse timing — while
///     the collision-acceptance decision itself remains covered only by the pure
///     <c>HotPotatoFuseMath.CanTransfer</c>/<c>NextTransferTime</c> tests in Content.Tests.
///
///     <see cref="SolreignHotPotatoComponent"/> carries no <c>[Access]</c> restriction, so its fields
///     are read/written directly here (confirmed against the component source before use).
/// </summary>
[TestFixture]
public sealed class HotPotatoLifecycleIntegrationTest : GameTest
{
    // Dirty: every test spawns entities via entMan.SpawnEntity directly (not the SSpawn/Spawn proxy
    // methods GameTest tracks for automatic cleanup), matching WerewolfPolymorphTriggerTest's and
    // SolreignZoneGateIntegrationTest's own precedent for the same reason — the server must never be
    // handed back to the pool with untracked leftover entities for another test to trip over.
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        // DummyTicker=false so the connected player session actually gets a spawned body attached
        // (HandTests.TestPickupDrop's precedent) — otherwise Sessions.First().AttachedEntity stays
        // null and every GetPlayerWithHands() call below throws InvalidOperationException.
        DummyTicker = false,
    };

    private const string PotatoProto = "SolreignHotPotatoBomb";

    private static async Task<(EntityUid Player, HandsComponent Hands)> GetPlayerWithHands(
        RobustIntegrationTest.ServerIntegrationInstance server, IPlayerManager playerMan, IEntityManager entMan)
    {
        EntityUid player = default;
        HandsComponent hands = default!;
        await server.WaitPost(() =>
        {
            player = playerMan.Sessions.First().AttachedEntity!.Value;
            hands = entMan.GetComponent<HandsComponent>(player);
        });
        return (player, hands);
    }

    private static EntityUid SpawnBareHandsEntity(IEntityManager entMan, SharedHandsSystem handSys, EntityUid near)
    {
        var coords = entMan.GetComponent<TransformComponent>(near).Coordinates;
        var uid = entMan.SpawnEntity(null, coords);
        entMan.EnsureComponent<HandsComponent>(uid);
        handSys.AddHand(uid, "hand", HandLocation.Middle);
        return uid;
    }

    [Test]
    public async Task Pickup_ArmsExercise_SetsArmedAndDetonateAt()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var handSys = entMan.System<SharedHandsSystem>();

        var (player, hands) = await GetPlayerWithHands(server, playerMan, entMan);

        EntityUid potato = default;
        var beforeArm = TimeSpan.Zero;
        await server.WaitPost(() =>
        {
            potato = entMan.SpawnEntity(PotatoProto, entMan.GetComponent<TransformComponent>(player).Coordinates);
            beforeArm = SGameTiming.CurTime;
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(handSys.TryPickup(player, potato, hands.ActiveHandId!), Is.True,
                "Setup failed: could not pick up the exercise.");

            var comp = entMan.GetComponent<SolreignHotPotatoComponent>(potato);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Armed, Is.True, "First pickup must arm the exercise.");
                Assert.That(comp.DetonateAt, Is.GreaterThan(beforeArm),
                    "DetonateAt must be set to a future time from the fuse duration.");
                Assert.That(comp.DetonateAt, Is.EqualTo(beforeArm + comp.FuseDuration).Within(TimeSpan.FromSeconds(1)),
                    "DetonateAt should be approximately ArmTime + FuseDuration.");
            });
        });
    }

    [Test]
    public async Task ForcedHandoff_WhileArmed_DoesNotResetDetonateAt()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var handSys = entMan.System<SharedHandsSystem>();

        var (player, hands) = await GetPlayerWithHands(server, playerMan, entMan);

        EntityUid potato = default;
        EntityUid colleague = default;
        await server.WaitPost(() =>
        {
            potato = entMan.SpawnEntity(PotatoProto, entMan.GetComponent<TransformComponent>(player).Coordinates);
            colleague = SpawnBareHandsEntity(entMan, handSys, player);
        });

        TimeSpan detonateAtAfterArm = default;
        await server.WaitPost(() =>
        {
            Assert.That(handSys.TryPickup(player, potato, hands.ActiveHandId!), Is.True);
            detonateAtAfterArm = entMan.GetComponent<SolreignHotPotatoComponent>(potato).DetonateAt;
        });

        // Let a few real seconds pass while it sits armed in the first holder's hand — the fuse
        // must NOT reset just because time is passing (it only ever counts down to DetonateAt).
        await server.WaitRunTicks(90); // ~3s at the default 30 tick/s

        await server.WaitAssertion(() =>
        {
            // Mirrors exactly what SolreignHotPotatoSystem.OnHolderCollide does once a collision
            // hand-off is accepted (briefly open the no-drop gate, force the pickup) — see this
            // file's class doc comment for why the collision trigger itself isn't driven here.
            var comp = entMan.GetComponent<SolreignHotPotatoComponent>(potato);
            var colleagueHands = entMan.GetComponent<HandsComponent>(colleague);
            comp.CanTransfer = true;

            Assert.That(handSys.TryForcePickupAnyHand(colleague, potato, checkActionBlocker: false, handsComp: colleagueHands),
                Is.True, "Setup failed: forced hand-off did not succeed.");

            comp.CanTransfer = false;

            Assert.That(comp.DetonateAt, Is.EqualTo(detonateAtAfterArm),
                "A hand-off must never reset the fuse — it only changes who is holding the responsibility.");
            Assert.That(comp.Armed, Is.True, "The exercise must remain armed after a hand-off.");
        });
    }

    [Test]
    public async Task TryDrop_WhileArmed_Fails()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var handSys = entMan.System<SharedHandsSystem>();

        var (player, hands) = await GetPlayerWithHands(server, playerMan, entMan);

        EntityUid potato = default;
        await server.WaitPost(() =>
        {
            potato = entMan.SpawnEntity(PotatoProto, entMan.GetComponent<TransformComponent>(player).Coordinates);
            Assert.That(handSys.TryPickup(player, potato, hands.ActiveHandId!), Is.True);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(handSys.TryDrop(player, potato), Is.False,
                "An armed exercise must never be droppable — the only way out is a sanctioned hand-off.");
            Assert.That(handSys.IsHolding((player, hands), potato, out _), Is.True,
                "The exercise should still be in the player's hand after the refused drop.");
        });
    }

    [Test]
    public async Task FuseExpiry_AfterDeadlineElapses_DetonatesAndDeletesEntity()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var handSys = entMan.System<SharedHandsSystem>();

        var (player, hands) = await GetPlayerWithHands(server, playerMan, entMan);

        EntityUid potato = default;
        await server.WaitPost(() =>
        {
            potato = entMan.SpawnEntity(PotatoProto, entMan.GetComponent<TransformComponent>(player).Coordinates);
            // Shrink the fuse so the test doesn't need to simulate the shipped 60s duration.
            entMan.GetComponent<SolreignHotPotatoComponent>(potato).FuseDuration = TimeSpan.FromSeconds(1);
            Assert.That(handSys.TryPickup(player, potato, hands.ActiveHandId!), Is.True);
        });

        // 1s fuse at the default 30 tick/s is 30 ticks; pad generously for the arm-tick + rounding.
        await server.WaitRunTicks(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(potato), Is.False,
                "The exercise should have detonated (and been queue-deleted) well after its fuse expired.");
        });
    }

    [Test]
    public async Task FuseExpiry_WellBeforeDeadline_ExerciseStillExists()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var handSys = entMan.System<SharedHandsSystem>();

        var (player, hands) = await GetPlayerWithHands(server, playerMan, entMan);

        EntityUid potato = default;
        await server.WaitPost(() =>
        {
            potato = entMan.SpawnEntity(PotatoProto, entMan.GetComponent<TransformComponent>(player).Coordinates);
            entMan.GetComponent<SolreignHotPotatoComponent>(potato).FuseDuration = TimeSpan.FromSeconds(2);
            Assert.That(handSys.TryPickup(player, potato, hands.ActiveHandId!), Is.True);
        });

        // Well under half the 2s fuse — the exercise must still be alive and armed.
        await server.WaitRunTicks(15);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(potato), Is.True,
                "The exercise detonated well before its fuse should have expired.");
            Assert.That(entMan.GetComponent<SolreignHotPotatoComponent>(potato).Armed, Is.True);
        });
    }

    [Test]
    public async Task IndependentFuses_OnlyTheExpiredOneDetonates()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var handSys = entMan.System<SharedHandsSystem>();

        var (player, hands) = await GetPlayerWithHands(server, playerMan, entMan);

        EntityUid shortFused = default;
        EntityUid longFused = default;
        EntityUid colleague = default;
        await server.WaitPost(() =>
        {
            var coords = entMan.GetComponent<TransformComponent>(player).Coordinates;
            colleague = SpawnBareHandsEntity(entMan, handSys, player);

            shortFused = entMan.SpawnEntity(PotatoProto, coords);
            entMan.GetComponent<SolreignHotPotatoComponent>(shortFused).FuseDuration = TimeSpan.FromSeconds(1);
            Assert.That(handSys.TryPickup(player, shortFused, hands.ActiveHandId!), Is.True);

            longFused = entMan.SpawnEntity(PotatoProto, coords);
            entMan.GetComponent<SolreignHotPotatoComponent>(longFused).FuseDuration = TimeSpan.FromSeconds(30);
            var colleagueHands = entMan.GetComponent<HandsComponent>(colleague);
            Assert.That(handSys.TryPickup(colleague, longFused, colleagueHands.ActiveHandId!), Is.True);
        });

        await server.WaitRunTicks(90); // well past the 1s fuse, nowhere near the 30s one

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(shortFused), Is.False,
                "The short-fused exercise should have independently detonated.");
            Assert.That(entMan.EntityExists(longFused), Is.True,
                "The long-fused exercise must not be affected by another entity's Update() iteration.");
            Assert.That(entMan.GetComponent<SolreignHotPotatoComponent>(longFused).Armed, Is.True);
        });
    }
}
