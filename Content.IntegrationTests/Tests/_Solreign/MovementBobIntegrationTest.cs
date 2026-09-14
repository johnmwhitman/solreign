#nullable enable
using System;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._Solreign.MovementBob;
using Content.IntegrationTests.Fixtures;
using Content.Server.Gravity;
using Content.Shared.CCVar;
using Content.Shared.Gravity;
using Content.Shared.Humanoid;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using NUnit.Framework;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     SR-W-083 procedural humanoid movement bob — wire + behavior proof over a real
///     connected server/client pair:
///     (1) the feature ships enabled (CVar true on the server AND on the client),
///     (2) flipping <c>solreign.movement_bob.enabled</c> off and back on at the server replicates to the connected
///         client with zero custom netcode (the CVar is REPLICATED | SERVER — this test IS
///         the wire proof; there is no per-player state, so a second client adds nothing),
///     (3) while the kill switch is explicitly off, movement events create no client bookkeeping,
///     (4) once live, a moving humanoid's sprite visibly bobs above its baseline in
///         client FrameUpdate, stops cleanly back at baseline, and turning the feature
///         off sweeps the bookkeeping component away.
///     The client-side SpriteMoveEvent raise below stands in for SharedMoverController's
///     own raise sites (input prediction / networked mover state / NPC steering) — same
///     event, same bus, same subscription.
/// </summary>
[TestFixture]
public sealed class MovementBobIntegrationTest : GameTest
{
    // Dirty: these tests flip replicated CVars via server.CfgMan (the precedented
    // ProvidenceVoiceSystemIntegrationTest idiom) — never hand the pair back to the pool.
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
    };

    private IDisposable PreserveCVarState()
    {
        var enabled = Server.CfgMan.GetCVar(CCVars.SolreignMovementBobEnabled);
        var amplitude = Server.CfgMan.GetCVar(CCVars.SolreignMovementBobAmplitudePx);
        var reducedMotion = Client.CfgMan.GetCVar(CCVars.ReducedMotion);
        return new RestoreScope(() =>
        {
            Server.CfgMan.SetCVar(CCVars.SolreignMovementBobEnabled, enabled);
            Server.CfgMan.SetCVar(CCVars.SolreignMovementBobAmplitudePx, amplitude);
            Client.CfgMan.SetCVar(CCVars.ReducedMotion, reducedMotion);
        });
    }

    private sealed class RestoreScope : IDisposable
    {
        private readonly Action _restore;

        public RestoreScope(Action restore)
        {
            _restore = restore;
        }

        public void Dispose() => _restore();
    }

    [Test]
    public async Task ShipsEnabled_DefaultTrue_OnServerAndClient()
    {
        await Pair.RunTicksSync(1);

        Assert.Multiple(() =>
        {
            Assert.That(Server.CfgMan.GetCVar(CCVars.SolreignMovementBobEnabled), Is.True,
                "the activation pass intentionally ships movement bob enabled on the server");
            Assert.That(Client.CfgMan.GetCVar(CCVars.SolreignMovementBobEnabled), Is.True,
                "the activation pass intentionally ships movement bob enabled on connected clients");
        });
    }

    [Test]
    public async Task ServerFlip_ReplicatesToConnectedClient()
    {
        using var cvars = PreserveCVarState();

        await Server.WaitPost(() => Server.CfgMan.SetCVar(CCVars.SolreignMovementBobEnabled, false));
        await Pair.RunTicksSync(10);
        Assert.That(Client.CfgMan.GetCVar(CCVars.SolreignMovementBobEnabled), Is.False,
            "server-side disable did not replicate to the connected client");

        await Server.WaitPost(() =>
        {
            Server.CfgMan.SetCVar(CCVars.SolreignMovementBobEnabled, true);
            Server.CfgMan.SetCVar(CCVars.SolreignMovementBobAmplitudePx, 2f);
        });

        await Pair.RunTicksSync(10);

        Assert.Multiple(() =>
        {
            Assert.That(Client.CfgMan.GetCVar(CCVars.SolreignMovementBobEnabled), Is.True,
                "server-side enable did not replicate to the connected client");
            Assert.That(Client.CfgMan.GetCVar(CCVars.SolreignMovementBobAmplitudePx), Is.EqualTo(2f),
                "server-side amplitude change did not replicate to the connected client");
        });
    }

    [Test]
    public async Task MovingHumanoid_Bobs_StopsAtBaseline_AndSweepsWhenDisabled()
    {
        using var cvars = PreserveCVarState();
        await Server.WaitPost(() => Server.CfgMan.SetCVar(CCVars.SolreignMovementBobEnabled, false));
        await Pair.RunTicksSync(10);

        var map = await Pair.CreateTestMap();

        EntityUid serverMob = default;
        NetEntity mobNet = default;
        await Server.WaitPost(() =>
        {
            // Give the test grid gravity BEFORE spawning: a weightless mob runs the floating
            // animation on the same sprite-offset channel, and the bob correctly yields to it
            // (see the floating guard in MovementBobSystem) — grounded is the case under test.
            var gravity = Server.EntMan.EnsureComponent<GravityComponent>(map.Grid.Owner);
            Server.System<GravitySystem>().EnableGravity(map.Grid.Owner, gravity);

            serverMob = Server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            mobNet = Server.EntMan.GetNetEntity(serverMob);
        });
        await Pair.RunTicksSync(10);

        var clientMob = Client.EntMan.GetEntity(mobNet);

        // (3) Kill switch off: movement events must not create any bookkeeping.
        await Client.WaitPost(() =>
        {
            var ev = new SpriteMoveEvent(true);
            Client.EntMan.EventBus.RaiseLocalEvent(clientMob, ref ev);
        });
        await Pair.RunTicksSync(5);
        await Client.WaitAssertion(() =>
        {
            Assert.That(Client.EntMan.HasComponent<MovementBobComponent>(clientMob), Is.False,
                "disabled feature must not attach MovementBobComponent on movement");
        });

        // Go live.
        await Server.WaitPost(() => Server.CfgMan.SetCVar(CCVars.SolreignMovementBobEnabled, true));
        await Pair.RunTicksSync(10);

        var baseline = 0f;
        await Client.WaitAssertion(() =>
        {
            baseline = Client.EntMan.GetComponent<SpriteComponent>(clientMob).Offset.Y;
            var ev = new SpriteMoveEvent(true);
            Client.EntMan.EventBus.RaiseLocalEvent(clientMob, ref ev);
        });

        // (4a) Sample the sprite offset across client frames: the rectified sine must lift
        // the sprite above baseline within a fraction of its period (2.5 Hz default).
        var maxLift = 0f;
        for (var i = 0; i < 15; i++)
        {
            await Pair.RunTicksSync(1);
            await Client.WaitAssertion(() =>
            {
                var lift = Client.EntMan.GetComponent<SpriteComponent>(clientMob).Offset.Y - baseline;
                maxLift = MathF.Max(maxLift, lift);
                Assert.That(lift, Is.GreaterThanOrEqualTo(-1e-4f),
                    "bob must never push the sprite below its baseline");
            });
        }

        Assert.That(maxLift, Is.GreaterThan(0.01f),
            "moving humanoid never lifted above baseline — bob is not rendering");
        Assert.That(maxLift, Is.LessThanOrEqualTo(
            MovementBobMath.MaxAmplitudePixels / MovementBobMath.PixelsPerUnit + 1e-4f),
            "bob exceeded the defensive amplitude cap");

        // (4b) Stop moving: the sprite must settle exactly back at its baseline.
        await Client.WaitPost(() =>
        {
            var ev = new SpriteMoveEvent(false);
            Client.EntMan.EventBus.RaiseLocalEvent(clientMob, ref ev);
        });
        await Pair.RunTicksSync(5);
        await Client.WaitAssertion(() =>
        {
            Assert.That(Client.EntMan.GetComponent<SpriteComponent>(clientMob).Offset.Y,
                Is.EqualTo(baseline).Within(1e-5f),
                "sprite offset was not restored to baseline after movement stopped");
        });

        // (4c) Disable live: bookkeeping sweeps away and the offset stays at baseline.
        await Server.WaitPost(() => Server.CfgMan.SetCVar(CCVars.SolreignMovementBobEnabled, false));
        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(Client.EntMan.HasComponent<MovementBobComponent>(clientMob), Is.False,
                    "disabling the feature must remove the client bookkeeping component");
                Assert.That(Client.EntMan.GetComponent<SpriteComponent>(clientMob).Offset.Y,
                    Is.EqualTo(baseline).Within(1e-5f),
                    "sprite offset must remain at baseline after the feature is disabled");
            });
        });
    }

    /// <summary>
    ///     Lifecycle proof for the two gate paths cdx review flagged:
    ///     (a) the client-only accessibility.reduced_motion opt-out sweeps an active bob
    ///         back to baseline, and clearing it re-seeds a still-moving humanoid WITHOUT
    ///         any fresh movement event (input never changed, so none would ever come);
    ///     (b) removing the mover on the server retires the bob instead of leaving the
    ///         sprite bobbing forever, with the offset restored.
    /// </summary>
    [Test]
    public async Task GateTransitions_ReducedMotionReseeds_AndMoverRemovalRetires()
    {
        using var cvars = PreserveCVarState();
        var map = await Pair.CreateTestMap();

        EntityUid serverMob = default;
        NetEntity mobNet = default;
        await Server.WaitPost(() =>
        {
            var gravity = Server.EntMan.EnsureComponent<GravityComponent>(map.Grid.Owner);
            Server.System<GravitySystem>().EnableGravity(map.Grid.Owner, gravity);
            serverMob = Server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            mobNet = Server.EntMan.GetNetEntity(serverMob);
        });
        await Server.WaitPost(() => Server.CfgMan.SetCVar(CCVars.SolreignMovementBobEnabled, true));
        await Pair.RunTicksSync(10);

        var clientMob = Client.EntMan.GetEntity(mobNet);
        var baseline = 0f;

        // Make the mob "really" moving from the client's point of view: held buttons on the
        // mover (what the reseed sweep reads) plus the event the mover controller would raise.
        await Client.WaitAssertion(() =>
        {
            baseline = Client.EntMan.GetComponent<SpriteComponent>(clientMob).Offset.Y;
            Client.EntMan.GetComponent<InputMoverComponent>(clientMob).HeldMoveButtons = MoveButtons.Up;
            var ev = new SpriteMoveEvent(true);
            Client.EntMan.EventBus.RaiseLocalEvent(clientMob, ref ev);
        });
        await Pair.RunTicksSync(3);
        await Client.WaitAssertion(() =>
        {
            Assert.That(Client.EntMan.HasComponent<MovementBobComponent>(clientMob), Is.True,
                "bob bookkeeping should exist while moving with the feature live");
        });

        // (a) Reduced motion on: sweep to baseline. This is CLIENTONLY — set client-side.
        await Client.WaitPost(() => Client.CfgMan.SetCVar(CCVars.ReducedMotion, true));
        await Pair.RunTicksSync(5);
        await Client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(Client.EntMan.HasComponent<MovementBobComponent>(clientMob), Is.False,
                    "reduced motion must sweep the bob bookkeeping");
                Assert.That(Client.EntMan.GetComponent<SpriteComponent>(clientMob).Offset.Y,
                    Is.EqualTo(baseline).Within(1e-5f),
                    "reduced motion must restore the sprite to baseline");
            });
        });

        // Reduced motion off again: the mob is STILL holding a move key and no new
        // SpriteMoveEvent will ever fire — the gate-transition reseed must revive the bob.
        await Client.WaitPost(() => Client.CfgMan.SetCVar(CCVars.ReducedMotion, false));
        await Pair.RunTicksSync(5);
        await Client.WaitAssertion(() =>
        {
            Assert.That(Client.EntMan.HasComponent<MovementBobComponent>(clientMob), Is.True,
                "clearing reduced motion must re-seed a still-moving humanoid without a new input event");
        });

        // (a2) Amplitude cycle live→zero→live (cdx r2): zero amplitude sweeps the bookkeeping,
        // and raising it again must reseed the still-moving humanoid — again with no new event.
        await Server.WaitPost(() => Server.CfgMan.SetCVar(CCVars.SolreignMovementBobAmplitudePx, 0f));
        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(Client.EntMan.HasComponent<MovementBobComponent>(clientMob), Is.False,
                    "zero amplitude must sweep the bob bookkeeping");
                Assert.That(Client.EntMan.GetComponent<SpriteComponent>(clientMob).Offset.Y,
                    Is.EqualTo(baseline).Within(1e-5f),
                    "zero amplitude must restore the sprite to baseline");
            });
        });

        await Server.WaitPost(() => Server.CfgMan.SetCVar(CCVars.SolreignMovementBobAmplitudePx, 1.5f));
        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            Assert.That(Client.EntMan.HasComponent<MovementBobComponent>(clientMob), Is.True,
                "restoring a positive amplitude must re-seed a still-moving humanoid without a new input event");
        });

        // (a3) Offset-animation episode during an applied bob (cdx r3 drift finding): a real
        // server-driven jitter runs its client animation on the same sprite offset. The bob
        // must suspend (no fighting), resume from its RETAINED baseline when the animation
        // ends, and settle back at EXACTLY the original baseline on stop — the drift bug was
        // adopting the animation's leftover (bob-poisoned) offset as a new baseline, leaking
        // up to one amplitude per episode.
        var baselineVec = Vector2.Zero;
        await Client.WaitAssertion(() =>
        {
            var bob = Client.EntMan.GetComponent<MovementBobComponent>(clientMob);
            Assert.That(bob.HasBaseline, Is.True, "an active bob must hold a baseline");
            baselineVec = bob.BaseOffset;
        });

        await Server.WaitPost(() =>
        {
            Server.System<Content.Shared.Jittering.SharedJitteringSystem>()
                .DoJitter(serverMob, TimeSpan.FromSeconds(0.6), refresh: true, amplitude: 10f, frequency: 4f);
        });
        await Pair.RunTicksSync(5);

        // Prove the suspension is REAL: the jitter animation is running on the client and
        // the bob has yielded (not applied) while retaining its baseline. (cdx r4)
        await Client.WaitAssertion(() =>
        {
            var bob = Client.EntMan.GetComponent<MovementBobComponent>(clientMob);
            Assert.Multiple(() =>
            {
                Assert.That(Client.System<AnimationPlayerSystem>().HasRunningAnimation(clientMob, "jittering"),
                    Is.True, "test setup failure: the jitter animation never ran on the client");
                Assert.That(bob.Applied, Is.False, "bob must suspend while the jitter animation owns the offset");
                Assert.That(bob.HasBaseline, Is.True, "bob must retain its baseline across the suspension");
            });
        });

        await Pair.RunTicksSync(30); // ~1s at 30 tps: jitter expires, bob resumes while still moving
        await Client.WaitAssertion(() =>
        {
            Assert.That(Client.EntMan.HasComponent<MovementBobComponent>(clientMob), Is.True,
                "bob bookkeeping must survive an offset-animation episode while still moving");
        });
        await Client.WaitPost(() =>
        {
            var ev = new SpriteMoveEvent(false);
            Client.EntMan.EventBus.RaiseLocalEvent(clientMob, ref ev);
        });
        await Pair.RunTicksSync(5);
        await Client.WaitAssertion(() =>
        {
            Assert.That(Client.EntMan.GetComponent<SpriteComponent>(clientMob).Offset,
                Is.EqualTo(baselineVec),
                "offset must return EXACTLY (full vector) to the pre-bob baseline after a jitter episode — any residue is the r3 drift bug");
        });
        // resume moving for the mover-removal leg below
        await Client.WaitPost(() =>
        {
            var ev = new SpriteMoveEvent(true);
            Client.EntMan.EventBus.RaiseLocalEvent(clientMob, ref ev);
        });
        await Pair.RunTicksSync(3);

        // (b) Server removes the mover mid-bob: bob must retire and restore, not run forever.
        await Server.WaitPost(() => Server.EntMan.RemoveComponent<InputMoverComponent>(serverMob));
        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(Client.EntMan.HasComponent<MovementBobComponent>(clientMob), Is.False,
                    "losing the mover must retire the bob (no stop event will ever arrive)");
                Assert.That(Client.EntMan.GetComponent<SpriteComponent>(clientMob).Offset.Y,
                    Is.EqualTo(baseline).Within(1e-5f),
                    "sprite offset must be restored after the mover disappears");
            });
        });
    }

    /// <summary>
    ///     The deferred-retirement path (<see cref="MovementBobComponent.Retiring"/>, cdx r4):
    ///     an entity can stop qualifying for the bob (its humanoid profile shuts down) WHILE
    ///     another system's animation owns the sprite offset channel. Retiring right then would
    ///     restore the baseline mid-animation, and the animation's own end would immediately
    ///     re-poison the offset — permanently, because the bookkeeping holding the true
    ///     baseline is gone by that point. So retirement defers to the first unoccupied frame.
    ///
    ///     This drives it with a real server-driven jitter: client-side JitteringSystem
    ///     captures the CURRENT (bob-lifted) offset as its StartOffset on startup and writes
    ///     it back on shutdown, which is exactly the re-poisoning the deferral must outlive.
    ///     Proof is the full-vector equality at the end — the sprite lands back on the exact
    ///     pre-bob baseline, not one amplitude above it.
    /// </summary>
    [Test]
    public async Task HumanoidShutdownDuringAnimation_DefersRetirement_ThenRestoresBaseline()
    {
        using var cvars = PreserveCVarState();
        var map = await Pair.CreateTestMap();

        EntityUid serverMob = default;
        NetEntity mobNet = default;
        await Server.WaitPost(() =>
        {
            // Gravity before spawning: a weightless mob floats on this same offset channel
            // and the bob correctly yields to it — grounded is the case under test.
            var gravity = Server.EntMan.EnsureComponent<GravityComponent>(map.Grid.Owner);
            Server.System<GravitySystem>().EnableGravity(map.Grid.Owner, gravity);
            serverMob = Server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            mobNet = Server.EntMan.GetNetEntity(serverMob);
        });
        await Server.WaitPost(() => Server.CfgMan.SetCVar(CCVars.SolreignMovementBobEnabled, true));
        await Pair.RunTicksSync(10);

        var clientMob = Client.EntMan.GetEntity(mobNet);

        // Start bobbing and let it actually apply, so the jitter below captures a
        // bob-lifted offset as its StartOffset (the residue the deferral must survive).
        var baselineVec = Vector2.Zero;
        await Client.WaitAssertion(() =>
        {
            baselineVec = Client.EntMan.GetComponent<SpriteComponent>(clientMob).Offset;
            var ev = new SpriteMoveEvent(true);
            Client.EntMan.EventBus.RaiseLocalEvent(clientMob, ref ev);
        });
        await Pair.RunTicksSync(3);
        await Client.WaitAssertion(() =>
        {
            var bob = Client.EntMan.GetComponent<MovementBobComponent>(clientMob);
            Assert.Multiple(() =>
            {
                Assert.That(bob.Applied, Is.True, "test setup failure: the bob never applied before the jitter");
                Assert.That(bob.BaseOffset, Is.EqualTo(baselineVec),
                    "the bob must have captured the true pre-bob offset as its baseline");
            });
        });

        // A real jitter episode takes the offset channel.
        await Server.WaitPost(() =>
        {
            Server.System<Content.Shared.Jittering.SharedJitteringSystem>()
                .DoJitter(serverMob, TimeSpan.FromSeconds(0.6), refresh: true, amplitude: 10f, frequency: 4f);
        });
        await Pair.RunTicksSync(4);
        await Client.WaitAssertion(() =>
        {
            var bob = Client.EntMan.GetComponent<MovementBobComponent>(clientMob);
            Assert.Multiple(() =>
            {
                Assert.That(Client.System<AnimationPlayerSystem>().HasRunningAnimation(clientMob, "jittering"),
                    Is.True, "test setup failure: the jitter animation never ran on the client");
                Assert.That(bob.Applied, Is.False, "bob must suspend while the jitter animation owns the offset");
                Assert.That(bob.HasBaseline, Is.True, "bob must retain its baseline across the suspension");
            });
        });

        // Disqualify the entity mid-animation: no humanoid profile, no bob. The shutdown
        // handler must NOT remove the bookkeeping yet — the jitter still owns the channel.
        await Server.WaitPost(() => Server.EntMan.RemoveComponent<HumanoidProfileComponent>(serverMob));
        await Pair.RunTicksSync(3);
        await Client.WaitAssertion(() =>
        {
            Assert.That(Client.EntMan.HasComponent<HumanoidProfileComponent>(clientMob), Is.False,
                "test setup failure: the server-side profile removal never replicated to the client");
            Assert.That(Client.System<AnimationPlayerSystem>().HasRunningAnimation(clientMob, "jittering"),
                Is.True, "test setup failure: the jitter expired before the profile removal landed");

            Assert.That(Client.EntMan.TryGetComponent<MovementBobComponent>(clientMob, out var bob), Is.True,
                "retirement must DEFER while an offset animation is running — removing the bookkeeping "
                + "here would drop the only record of the true baseline");
            Assert.Multiple(() =>
            {
                Assert.That(bob!.Retiring, Is.True, "the disqualified entity must be marked for deferred retirement");
                Assert.That(bob.HasBaseline, Is.True, "the deferred bob must still hold its baseline");
            });
        });

        // Jitter expires (0.6s ≈ 18 ticks at 30 tps): the first unoccupied frame retires the
        // bob, restoring the retained baseline over the offset the jitter left behind.
        await Pair.RunTicksSync(30);
        await Client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(Client.System<AnimationPlayerSystem>().HasRunningAnimation(clientMob, "jittering"),
                    Is.False, "test setup failure: the jitter animation never expired");
                Assert.That(Client.EntMan.HasComponent<MovementBobComponent>(clientMob), Is.False,
                    "the deferred retirement must complete once the animation releases the offset channel");
                Assert.That(Client.EntMan.GetComponent<SpriteComponent>(clientMob).Offset,
                    Is.EqualTo(baselineVec),
                    "offset must return EXACTLY (full vector) to the pre-bob baseline — the jitter writes its "
                    + "bob-lifted StartOffset back on shutdown, and only the deferred Reset cleans that residue");
            });
        });
    }
}
