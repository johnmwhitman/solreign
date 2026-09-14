#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.Antags.Werewolf;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Wave 24 slice: two System-layer gaps <c>WerewolfPolymorphTriggerTest</c> deliberately left open
///     (that file's own scope is narrow — "the prototype resolves, and Stirring produces a wolf").
///     This file covers what happens on the OTHER side of Transformed:
///
///     (1) the full round trip — moon window closes mid-transform, <see cref="WerewolfStateMachine"/>
///     carries the episode through Waning and all the way back to Dormant, and the polymorphed wolf
///     body is actually reverted (not just cosmetically "supposed to" revert) — driven entirely by the
///     real <see cref="SolreignWerewolfSystem.Update"/> tick loop, no direct state pokes; and
///
///     (2) the missing-prototype fail-safe branch itself (<c>SolreignWerewolfSystem.Transform.cs</c>'s
///     <c>OnEnterTransformed</c> <c>TryIndex</c> guard) — WITHOUT weakening the guard or hand-writing a
///     bogus <see cref="SolreignWerewolfComponent.WolfPolymorphPrototype"/> value onto the component
///     (that field is <c>[Access(typeof(SolreignWerewolfSystem))]</c>-locked to Read for this test
///     class — confirmed by compiler error RA0002 when attempted directly).
///
///     FIRST DRAFT of this test tried removing the real "SolreignWerewolfPolymorph" prototype via
///     <c>IPrototypeManager.RemoveString</c> (the technique RobustToolbox's own
///     <c>PrototypeManager_Test.TestCircleException</c> uses). That was WRONG for this harness: a full
///     `--filter ~Solreign` run showed the removal leaking across the shared, pool-wide
///     <see cref="Robust.Shared.Prototypes.IPrototypeManager"/> instance — <c>PoolSettings.Dirty</c>
///     only marks ONE recycled server pair as unfit for reuse, it does not scope prototype-table
///     mutations, and a *different*, previously-green test (<c>WerewolfPolymorphTriggerTest
///     .MoonWindowOpen_StirringElapses_ActuallyPolymorphsToWolf</c>) started failing the SAME run,
///     logging the exact "'SolreignWerewolfPolymorph' not found" warning this test intentionally
///     provokes. Confirmed via the real battery run, not assumed — removed that approach rather than
///     ship a test that corrupts its neighbors depending on execution order.
///
///     This version instead ADDS a brand-new, uniquely-named entity prototype
///     (<see cref="MissingPrototypeFixtureYaml"/>) via <c>LoadString</c> — the same additive mechanism
///     <c>PrototypeManager_Test.TestLoadString</c> demonstrates — that overrides
///     <c>SolreignWerewolfComponent.wolfPolymorphPrototype</c> to a deliberately nonexistent id via YAML
///     data-field assignment (which the Access analyzer never sees; it only guards hand-written C#
///     read/write call sites, not serialization). Nothing pre-existing is touched or removed, so there
///     is nothing to leak: any other test in the same run that doesn't spawn this exact fixture id is
///     completely unaffected. The fixture <c>parent: MobHuman</c>s (rather than a bare entity) so it
///     carries the full mob component set (Stamina/MobState/Physics/etc.) <c>OnEnterWaning</c>'s
///     unconditional <c>SharedStunSystem.TryKnockdown</c> call needs — a first draft used a bare entity
///     and the run showed it breaking mid-teardown once Waning's knockdown ran against a mob missing
///     that machinery.
///
///     SECOND thing the real battery run caught: both tests originally spawned at
///     <see cref="MapCoordinates.Nullspace"/> (the idiom <c>WerewolfPolymorphTriggerTest</c> uses — but
///     that file never drives Waning, only up to Transformed). <c>PolymorphSystem.Revert</c> reparents
///     the original entity back onto the polymorphed child's CURRENT map; nullspace has no stable parent
///     chain for that reparent, so <c>OnEnterWaning</c>'s <c>wolf.WolfForm = null</c> line ran
///     unconditionally (as written) while the actual <c>PolymorphSystem.Revert</c> call silently
///     no-opped underneath it — the wolf-form entity's own <c>SolreignWolfFormComponent</c> marker never
///     actually disappeared. Both tests now create a real map first (same
///     <c>SharedMapSystem.CreateMap</c> idiom <c>SolreignZoneGateSystemIntegrationTest</c> uses) so
///     Revert has a real place to send the human back to.
///
///     Both tests read <see cref="SolreignWerewolfComponent.State"/> directly — permitted (component
///     Access defaults <c>Other</c> to Read-only, not none) — rather than only inferring phase from
///     <see cref="SolreignWolfFormComponent"/> presence, since Dormant/Waning both lack that marker and
///     need to be told apart.
/// </summary>
[TestFixture]
public sealed class SolreignWerewolfCycleAndFailSafeIntegrationTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true, // mutates SolreignWerewolfSystem.MoonWindowActive
        // A real battery run showed a RECYCLED pair occasionally carrying a leftover async
        // Content.Server.NPC.HTN job from whatever earlier test last used it -- that job's own
        // (unrelated, pre-existing) null-ref crash landed inside THIS test's long WaitRunTicks window
        // purely by timing coincidence, not anything werewolf-related. Fresh forces a brand-new pair
        // instead of "Suitable pair found... Cleaning existing pair" reuse, so nothing lingers.
        Fresh = true,
    };

    private const string MissingPrototypeFixtureId = "ZZTestFixtureSolreignWerewolfMissingPolymorph";

    /// <summary>
    ///     Additive-only test fixture prototype: a full <c>MobHuman</c> carrying a
    ///     <see cref="SolreignWerewolfComponent"/> whose <c>wolfPolymorphPrototype</c> is deliberately
    ///     set to an id that exists nowhere in Resources/Prototypes. Loaded via <c>LoadString</c>
    ///     (in-memory only, never written to disk) inside the test itself — see the type doc comment
    ///     for why this replaced an earlier, leakier <c>RemoveString</c>-based draft, and why it parents
    ///     <c>MobHuman</c> rather than staying bare.
    /// </summary>
    private const string MissingPrototypeFixtureYaml = $$"""
        - type: entity
          parent: MobHuman
          id: {{MissingPrototypeFixtureId}}
          components:
          - type: SolreignWerewolf
            wolfPolymorphPrototype: ZZNonexistentPolymorphIdForTestOnly
        """;

    [Test]
    public async Task RealCycle_MoonWindowClosesMidTransform_RevertsThroughWaningAllTheWayToDormant()
    {
        var server = Server;
        var entMan = server.EntMan;
        var werewolf = server.System<SolreignWerewolfSystem>();

        EntityUid human = default;
        await server.WaitPost(() =>
        {
            // A real map, not MapCoordinates.Nullspace: PolymorphSystem.Revert reparents the human back
            // onto the wolf-form's current map, which needs a real, non-degenerate parent chain to
            // succeed (see the type doc comment — nullspace silently no-ops the revert).
            var mapSystem = entMan.System<SharedMapSystem>();
            mapSystem.CreateMap(out var mapId);

            human = entMan.SpawnEntity("MobHuman", new MapCoordinates(0f, 0f, mapId));
            entMan.EnsureComponent<SolreignWerewolfComponent>(human);
            werewolf.MoonWindowActive = true;
        });

        // Stirring (default 30s = 900 ticks) -> Transformed. Pad for the Dormant->Stirring hop.
        await server.WaitRunTicks(960);

        await server.WaitAssertion(() =>
        {
            var wolf = entMan.GetComponent<SolreignWerewolfComponent>(human);
            Assert.That(wolf.State, Is.EqualTo(WerewolfState.Transformed),
                "Stirring elapsed under an open moon window but the state machine never reached Transformed.");

            var query = entMan.EntityQueryEnumerator<SolreignWolfFormComponent>();
            Assert.That(query.MoveNext(out _, out _), Is.True,
                "Transformed but no SolreignWolfFormComponent entity exists — the real polymorph prototype " +
                "resolves in this test (see the separate fail-safe test for the missing-prototype case), so " +
                "the body swap should have actually happened.");
        });

        // Close the moon window: Transformed -> Waning immediately (WerewolfStateMachine: `!moonActive` bounds
        // Transformed regardless of the per-window cap).
        await server.WaitPost(() => werewolf.MoonWindowActive = false);
        await server.WaitRunTicks(30); // one second is plenty for the next Update tick to notice

        await server.WaitAssertion(() =>
        {
            var wolf = entMan.GetComponent<SolreignWerewolfComponent>(human);
            Assert.That(wolf.State, Is.EqualTo(WerewolfState.Waning),
                "Closing the moon window mid-Transformed did not move the state machine to Waning.");
        });

        // Waning (default 10s = 300 ticks) -> Dormant (no CureApplied, so it resolves to Dormant not Cured).
        await server.WaitRunTicks(330);

        await server.WaitAssertion(() =>
        {
            var wolf = entMan.GetComponent<SolreignWerewolfComponent>(human);
            Assert.That(wolf.State, Is.EqualTo(WerewolfState.Dormant),
                "Waning elapsed but the episode never resolved back to Dormant.");
            Assert.That(wolf.WolfForm, Is.Null,
                "OnEnterWaning should have reverted and cleared WolfForm on the way to Dormant.");

            var query = entMan.EntityQueryEnumerator<SolreignWolfFormComponent>();
            Assert.That(query.MoveNext(out _, out _), Is.False,
                "The wolf-form entity should have been reverted (PolymorphSystem.Revert queue-deletes the " +
                "polymorphed child) once Waning elapsed — a SolreignWolfFormComponent entity surviving past " +
                "Dormant means the revert never actually happened.");
        });
    }

    [Test]
    public async Task MissingPolymorphPrototype_TransformedFailsSafe_NoWolfFormAndCycleKeepsRunning()
    {
        var server = Server;
        var entMan = server.EntMan;
        var werewolf = server.System<SolreignWerewolfSystem>();

        EntityUid human = default;
        await server.WaitPost(() =>
        {
            // Additive-only: registers a brand-new, uniquely-named prototype: nothing pre-existing is
            // touched, so (unlike the RemoveString draft this replaced) this cannot leak into any other
            // test in the same run.
            SProtoMan.LoadString(MissingPrototypeFixtureYaml);
            SProtoMan.ResolveResults();

            var mapSystem = entMan.System<SharedMapSystem>();
            mapSystem.CreateMap(out var mapId);

            human = entMan.SpawnEntity(MissingPrototypeFixtureId, new MapCoordinates(0f, 0f, mapId));
            werewolf.MoonWindowActive = true;
        });

        // PoolSettings.Connected means a real client shares this pair and state-syncs against the
        // server; the client's OWN prototype manager needs the fixture too or replicating this entity
        // throws once the client tries to resolve a prototype id it never loaded.
        await Client.WaitPost(() =>
        {
            CProtoMan.LoadString(MissingPrototypeFixtureYaml);
            CProtoMan.ResolveResults();
        });

        await server.WaitAssertion(() =>
        {
            var wolf = entMan.GetComponent<SolreignWerewolfComponent>(human);
            Assert.That(wolf.WolfPolymorphPrototype.Id, Is.EqualTo("ZZNonexistentPolymorphIdForTestOnly"),
                "The fixture prototype's YAML override didn't take — this test would otherwise exercise the " +
                "real default id (already covered) instead of the missing-prototype branch.");
        });

        await server.WaitRunTicks(960); // Stirring (30s) elapses under the (still) open moon window

        await server.WaitAssertion(() =>
        {
            var wolf = entMan.GetComponent<SolreignWerewolfComponent>(human);
            Assert.That(wolf.State, Is.EqualTo(WerewolfState.Transformed),
                "The state machine itself must still advance to Transformed on the clock alone — a missing " +
                "prototype is a cosmetic no-op for OnEnterTransformed, not a state-machine stall.");
            Assert.That(wolf.WolfForm, Is.Null,
                "WolfForm must stay unset — OnEnterTransformed's TryIndex miss must return before ever " +
                "calling PolymorphSystem.PolymorphEntity.");

            var query = entMan.EntityQueryEnumerator<SolreignWolfFormComponent>();
            Assert.That(query.MoveNext(out _, out _), Is.False,
                "No SolreignWolfFormComponent entity should exist — the fail-safe must return before the " +
                "marker component is ever attached to anything.");

            Assert.That(entMan.EntityExists(human), Is.True,
                "The original human entity must still exist — a missing prototype must fail safe, not throw " +
                "mid-tick and leave the round in a broken state.");
        });

        // Prove the episode isn't stuck: closing the moon window still carries Transformed -> Waning ->
        // Dormant on the clock alone, exactly as if a real wolf had been there (OnEnterWaning's knockdown
        // runs unconditionally; only the polymorph-revert half of it was already a no-op).
        await server.WaitPost(() => werewolf.MoonWindowActive = false);
        await server.WaitRunTicks(360); // one tick to notice the closed window + Waning's 10s (300 ticks)

        await server.WaitAssertion(() =>
        {
            var wolf = entMan.GetComponent<SolreignWerewolfComponent>(human);
            Assert.That(wolf.State, Is.EqualTo(WerewolfState.Dormant),
                "Even with the transform fail-safe having fired, the episode must still resolve back to " +
                "Dormant once the moon window closes and Waning elapses — the state machine must never get " +
                "stuck because a downstream presentation step no-opped.");
        });
    }
}
