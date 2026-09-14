#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Solreign.Sprint;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Phase-2 Track B2 (docs/plans/2026-07-11-ROADMAP-PHASE2.md, Track B, Wave B2): "sprint drain at
///     stamina boundaries". <c>SprintMathTests</c> (Content.Tests) already exhaustively covers the
///     pure boundary math (<c>IsAutoDropTriggered</c> at/above/below the line, cooldown edges, the
///     shipped-tuning "drains to the line in ~7 seconds" scenario) — what nothing exercised is
///     <see cref="SolreignSprintSystem"/>'s real <c>Update()</c>/<c>Tick</c> loop against a real
///     <see cref="StaminaComponent"/> over real ticks.
///
///     <see cref="Content.Shared._Solreign.Sprint.SprintComponent"/> is <c>[Access(typeof(SolreignSprintSystem))]</c>
///     -locked, so this file never reads/writes its fields directly — every interaction goes through
///     the system's own public API (<see cref="SolreignSprintSystem.SetSprintKeyHeld"/>) and every
///     assertion observes the effect through <see cref="StaminaComponent"/> instead (not access-locked,
///     confirmed before use), the same "observe through unlocked side effects" idiom
///     <c>WerewolfPolymorphTriggerTest</c> uses for its own access-locked antag component.
/// </summary>
[TestFixture]
public sealed class SprintDrainBoundaryIntegrationTest : GameTest
{
    // Dirty: every test spawns a MobHuman via entMan.SpawnEntity directly (not the SSpawn/Spawn proxy
    // methods GameTest tracks for automatic cleanup), matching WerewolfPolymorphTriggerTest's own
    // precedent — the server must never be handed back to the pool with an untracked leftover mob.
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
    };

    [Test]
    public async Task SprintKeyHeld_DrainsStaminaOverRealTicks()
    {
        var server = Server;
        var entMan = server.EntMan;
        var sprint = server.System<SolreignSprintSystem>();

        EntityUid mob = default;
        await server.WaitPost(() =>
        {
            mob = entMan.SpawnEntity("MobHuman", MapCoordinates.Nullspace);
            sprint.SetSprintKeyHeld(mob, true);
        });

        // The first drain lump lands ~1s after the key is first held; pad generously for the
        // arm-tick + rounding (same padding philosophy as WerewolfPolymorphTriggerTest).
        await server.WaitRunTicks(60);

        await server.WaitAssertion(() =>
        {
            var stamina = entMan.GetComponent<StaminaComponent>(mob);
            Assert.That(stamina.StaminaDamage, Is.GreaterThan(0f),
                "Holding the sprint key should have drained stamina after ~2s of real ticks — the " +
                "System-layer Tick() loop never fired.");
        });
    }

    [Test]
    public async Task KeyReleased_StaminaStaysAtZero()
    {
        var server = Server;
        var entMan = server.EntMan;
        var sprint = server.System<SolreignSprintSystem>();

        EntityUid mob = default;
        await server.WaitPost(() =>
        {
            mob = entMan.SpawnEntity("MobHuman", MapCoordinates.Nullspace);
            // Never call SetSprintKeyHeld — the control case for the drain test above.
        });

        await server.WaitRunTicks(60);

        await server.WaitAssertion(() =>
        {
            var stamina = entMan.GetComponent<StaminaComponent>(mob);
            Assert.That(stamina.StaminaDamage, Is.EqualTo(0f),
                "An entity that never held sprint must accrue no stamina damage from this system.");
        });
    }

    [Test]
    public async Task DrainNeverCrossesTheAutoDropBoundary_AcrossRepeatedCooldownCycles()
    {
        var server = Server;
        var entMan = server.EntMan;
        var sprint = server.System<SolreignSprintSystem>();

        EntityUid mob = default;
        await server.WaitPost(() =>
        {
            mob = entMan.SpawnEntity("MobHuman", MapCoordinates.Nullspace);
            var stamina = entMan.GetComponent<StaminaComponent>(mob);

            // Pre-load to just below the shipped 90% auto-drop line (100 crit threshold * 0.9) so the
            // very next 12/sec drain lump would predict an overshoot (84 + 12 = 96) and must be
            // refused before being applied — the exact boundary this wave targets.
            server.System<SharedStaminaSystem>().TakeStaminaDamage(mob, 84f, stamina);
            sprint.SetSprintKeyHeld(mob, true);
        });

        // Sample across several would-be drain lumps AND multiple 4s auto-drop cooldown cycles —
        // the predictive gate in SolreignSprintSystem.Tick must hold the line every time it's
        // re-evaluated, not just once. (Passive stamina decay, SharedStaminaSystem's own mechanic,
        // may pull the value down between checks; that's fine and expected — this test only asserts
        // the boundary is never CROSSED from below, which is the behavior this wave targets.)
        var checkpointTicks = new[] { 60, 240, 420 }; // ~2s, ~8s, ~14s
        var previousElapsed = 0;

        foreach (var totalTicks in checkpointTicks)
        {
            await server.WaitRunTicks(totalTicks - previousElapsed);
            previousElapsed = totalTicks;

            await server.WaitAssertion(() =>
            {
                var stamina = entMan.GetComponent<StaminaComponent>(mob);
                Assert.That(stamina.StaminaDamage, Is.LessThan(90f),
                    $"Stamina damage crossed the auto-drop boundary (90) at tick {totalTicks} while sprint " +
                    "was continuously key-held — the predictive gate in SolreignSprintSystem.Tick regressed.");
            });
        }
    }
}
