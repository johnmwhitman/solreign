#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Solreign.Combat;
using Content.Shared.CombatMode;
using Content.Shared.Damage.Components;
using Content.Shared.Throwing;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Pair-drives <c>Content.Server._Solreign.Combat.SolreignShoveSystem</c>'s real
///     <see cref="DisarmedEvent"/> subscription (hooked on
///     <c>MovementSpeedModifierComponent</c>, after Hands/Stamina). Pure push math already lives
///     in <c>ShoveMathTests</c> (Content.Tests); this file is the System-layer gap the Phase-3
///     backlog called out — specifically (a) the <c>Vector2.Zero</c> co-located degenerate guard
///     and (b) a handled disarm that actually reaches <c>ThrowingSystem.TryThrow</c> and
///     <c>SharedStaminaSystem.TakeStaminaDamage</c>.
///
///     Deliberate scope boundary, stated honestly: the full melee disarm pipeline
///     (<c>SharedMeleeWeaponSystem.DoDisarm</c> — combat mode, fail-chance roll, popups, audio) is
///     NOT simulated end-to-end. That path is infrastructure-heavy and non-deterministic at the
///     fail-chance branch. Instead we raise a directed <see cref="DisarmedEvent"/> (a
///     <c>[ByRefEvent]</c> record struct) on the shove target with <c>Handled = true</c> (the exact
///     precondition SolreignShoveSystem requires after upstream Hands/Stamina mark a shove that
///     landed) — matching <c>SolreignZoneGateIntegrationTest</c>'s "raise the subscription event
///     directly" idiom. Pre-setting <c>Handled</c> also isolates SolreignShove's own bonus stamina
///     from <c>SharedStaminaSystem.OnDisarmed</c>'s separate <c>pushProb x CritThreshold</c>
///     application (both upstream <c>HandsSystem.OnDisarmed</c> and
///     <c>SharedStaminaSystem.OnDisarmed</c> check <c>if (args.Handled) return;</c> at entry —
///     verified against their source before relying on it) so the asserted delta is exactly
///     <see cref="ShoveMath.BonusStaminaDamage"/>.
/// </summary>
[TestFixture]
public sealed class SolreignShoveSystemIntegrationTest : GameTest
{
    // Dirty: spawns MobHumans via entMan.SpawnEntity directly (not the SSpawn/Spawn proxy methods
    // GameTest tracks for automatic cleanup), matching WerewolfPolymorphTriggerTest /
    // SprintDrainBoundaryIntegrationTest — the server must never be handed back to the pool with
    // untracked leftover mobs.
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
    };

    /// <summary>
    ///     Source and target share exact map coordinates → direction is <c>Vector2.Zero</c> →
    ///     the guard must skip <c>TryThrow</c> (no <see cref="ThrownItemComponent"/>) and must
    ///     not throw / produce a NaN direction from <c>Normalized()</c>. Bonus stamina still
    ///     applies on a handled shove regardless of the push skip.
    /// </summary>
    [Test]
    public async Task CoLocatedSourceAndTarget_DoesNotThrowAndSkipsKnockback_ButStillAppliesBonusStamina()
    {
        var server = Server;
        var entMan = server.EntMan;

        const float pushProb = 1f;
        EntityUid source = default;
        EntityUid target = default;
        float staminaBefore = 0f;

        await server.WaitPost(() =>
        {
            var mapSystem = entMan.System<SharedMapSystem>();
            mapSystem.CreateMap(out var mapId);

            // Identical coordinates: the only way OnAnyDisarmed sees direction == Vector2.Zero.
            var coords = new MapCoordinates(0f, 0f, mapId);
            source = entMan.SpawnEntity("MobHuman", coords);
            target = entMan.SpawnEntity("MobHuman", coords);

            // MobBase already carries MovementSpeedModifier + Stamina + Physics (needed for the
            // subscription filter, TakeStaminaDamage, and TryThrow respectively).
            staminaBefore = entMan.GetComponent<StaminaComponent>(target).StaminaDamage;
        });

        await server.WaitAssertion(() =>
        {
            var ev = new DisarmedEvent(target, source, pushProb) { Handled = true };

            Assert.DoesNotThrow(() => entMan.EventBus.RaiseLocalEvent(target, ref ev),
                "Co-located source/target must not throw from direction.Normalized() on Vector2.Zero — " +
                "that is the entire point of the explicit zero-direction guard in OnAnyDisarmed.");

            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<ThrownItemComponent>(target), Is.False,
                    "Zero-direction guard must skip TryThrow entirely; ThrownItemComponent would mean " +
                    "the knockback branch still ran (or ran with a NaN/zero vector that somehow landed).");

                var staminaAfter = entMan.GetComponent<StaminaComponent>(target).StaminaDamage;
                var expectedBonus = ShoveMath.BonusStaminaDamage(pushProb);
                Assert.That(staminaAfter - staminaBefore, Is.EqualTo(expectedBonus).Within(1e-3f),
                    "Bonus stamina must still apply on a handled shove even when the push is skipped.");
            });
        });
    }

    /// <summary>
    ///     Source and target offset on the same map → non-zero direction → TryThrow runs (target
    ///     gains <see cref="ThrownItemComponent"/> / InAir) and bonus stamina equals
    ///     <see cref="ShoveMath.BonusStaminaDamage"/>.
    /// </summary>
    [Test]
    public async Task OffsetSourceAndTarget_HandledDisarm_AppliesTryThrowAndBonusStamina()
    {
        var server = Server;
        var entMan = server.EntMan;

        const float pushProb = 0.5f;
        EntityUid source = default;
        EntityUid target = default;
        float staminaBefore = 0f;

        await server.WaitPost(() =>
        {
            var mapSystem = entMan.System<SharedMapSystem>();
            mapSystem.CreateMap(out var mapId);

            source = entMan.SpawnEntity("MobHuman", new MapCoordinates(0f, 0f, mapId));
            // One tile east — enough for a clean non-zero direction vector.
            target = entMan.SpawnEntity("MobHuman", new MapCoordinates(1f, 0f, mapId));

            staminaBefore = entMan.GetComponent<StaminaComponent>(target).StaminaDamage;
        });

        await server.WaitAssertion(() =>
        {
            var ev = new DisarmedEvent(target, source, pushProb) { Handled = true };

            Assert.DoesNotThrow(() => entMan.EventBus.RaiseLocalEvent(target, ref ev));

            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<ThrownItemComponent>(target), Is.True,
                    "A handled disarm with a non-zero source->target direction must call TryThrow " +
                    "(ThrownItemComponent is the observable side effect ThrowingSystem adds).");

                var physics = entMan.GetComponent<PhysicsComponent>(target);
                Assert.That(physics.BodyStatus, Is.EqualTo(BodyStatus.InAir),
                    "TryThrow should put the target's physics body InAir for a non-trivial fly time.");

                var staminaAfter = entMan.GetComponent<StaminaComponent>(target).StaminaDamage;
                var expectedBonus = ShoveMath.BonusStaminaDamage(pushProb);
                Assert.That(staminaAfter - staminaBefore, Is.EqualTo(expectedBonus).Within(1e-3f),
                    "SolreignShoveSystem must apply exactly ShoveMath.BonusStaminaDamage(pushProb) " +
                    "on top of whatever upstream already did (here: nothing, because Handled was " +
                    "pre-set so SharedStaminaSystem.OnDisarmed short-circuits).");
            });
        });
    }

    /// <summary>
    ///     Unhandled disarm = upstream aborted the shove. SolreignShoveSystem must no-op entirely
    ///     (no knockback, no bonus stamina) — the dual of the handled path above.
    /// </summary>
    [Test]
    public async Task UnhandledDisarm_IsANoOp()
    {
        var server = Server;
        var entMan = server.EntMan;

        EntityUid source = default;
        EntityUid target = default;

        await server.WaitPost(() =>
        {
            var mapSystem = entMan.System<SharedMapSystem>();
            mapSystem.CreateMap(out var mapId);

            source = entMan.SpawnEntity("MobHuman", new MapCoordinates(0f, 0f, mapId));
            target = entMan.SpawnEntity("MobHuman", new MapCoordinates(1f, 0f, mapId));
        });

        await server.WaitAssertion(() =>
        {
            // Handled left false — SolreignShoveSystem returns before any push or stamina work.
            // (SharedStaminaSystem.OnDisarmed will itself set Handled and apply its own damage when
            // it runs first; to keep this assertion about SolreignShove's early-return only, we
            // strip StaminaComponent so that upstream handler never fires either. MovementSpeedModifier
            // stays so SolreignShove's subscription still matches.)
            entMan.RemoveComponent<StaminaComponent>(target);

            var ev = new DisarmedEvent(target, source, 1f);
            Assert.That(ev.Handled, Is.False);

            Assert.DoesNotThrow(() => entMan.EventBus.RaiseLocalEvent(target, ref ev));

            Assert.That(entMan.HasComponent<ThrownItemComponent>(target), Is.False,
                "Unhandled DisarmedEvent must never reach TryThrow.");
            // StaminaComponent was removed; no bonus path could have re-added damage tracking.
            Assert.That(entMan.HasComponent<StaminaComponent>(target), Is.False);
        });
    }
}
