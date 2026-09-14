#nullable enable
using System.Reflection;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.GameTicking;
using Content.Server.NPC.HTN;
using NUnit.Framework;
using Robust.Shared.CPUJob.JobQueues.Queues;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     HANDOFF §10 v2 regression test for the HTN pool-race flake:
///     <c>NullReferenceException</c> at <c>NPCBlackboard.TryGetEntityDefault</c>, reached via
///     <c>CoordinatesNotInRangePrecondition.IsMet</c> from <c>HTNPlanJob.Process</c> on the CPU
///     job queue, always hitting whichever integration test happened to run next.
///
///     <b>Confirmed root cause</b> (traced through the real code, not just token bookkeeping):
///     <see cref="HTNSystem"/> keeps a single long-lived <c>Robust.Shared.CPUJob.JobQueues.Queues.
///     JobQueue</c> instance (<c>_planQueue</c>) for the lifetime of the server/EntityManager. An
///     in-flight <see cref="HTNPlanJob"/> sitting in that queue (e.g. still <c>Waiting</c> on a
///     pathfind) survives a round restart if nothing removes it. Cancelling
///     <c>HTNComponent.PlanningToken</c> on component/system shutdown only flips a cooperative
///     flag -- it does not dequeue the job -- so the job can still be <c>Run()</c> again on a
///     LATER tick. <c>Content.IntegrationTests/Pair/TestPair.Recycle.cs</c> hands a server back to
///     the pool for the next test by calling <c>gameTicker.RestartRound()</c> and pumping exactly
///     one tick (twice) -- it never confirms HTNSystem's JobQueue has actually drained. A pooled
///     server is therefore not guaranteed to arrive at the next test with zero leaked HTN plan
///     jobs, and a leaked job that resumes there dereferences a blackboard/EntityManager state the
///     new test doesn't own -- hence "rotating victim": whichever test happens to run next eats
///     the crash. (RobustToolbox's TaskManager/RobustSynchronizationContext rules out the
///     originally-suspected "resumes on a different thread" theory -- continuations always land
///     back on this server's own instance thread; the real gap is queue LIFETIME across a round
///     boundary, not threading.)
///
///     <b>Fix under test</b>: <c>HTNSystem</c> now subscribes to <see cref="RoundRestartCleanupEvent"/>
///     (in addition to system <c>Shutdown()</c>) and discards <c>_planQueue</c> outright --
///     replacing it with a brand new, empty <c>JobQueue</c> -- rather than just cancelling
///     tokens. Whatever was enqueued before the restart becomes permanently unreachable: nothing
///     can ever call <c>Run()</c> on it again, regardless of how many ticks or subsequent tests
///     go by. This test spawns a real HTN-driven NPC (so the queue has genuine plan-job traffic
///     in it), captures <c>_planQueue</c>'s identity via reflection (private field --
///     content-side, no engine changes), drives a real <c>RestartRound()</c> exactly the way the
///     pool does, and asserts the queue was actually replaced -- then keeps ticking to confirm
///     nothing surfaces as an exception through <c>HTNSystem.UpdateNPC</c>'s
///     <c>Log.Fatal</c>/throw path (see HTNSystem.cs), which is exactly how the original NRE
///     would have escaped a pooled test.
/// </summary>
[TestFixture]
public sealed class SolreignHtnPlanQueueRoundRestartTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        DummyTicker = false,
        Connected = true,
        // Manually drives RestartRound()/FlushEntities the way the pool's own recycle path does;
        // never hand this specific instance back for another test to reuse.
        Dirty = true,
    };

    private static readonly FieldInfo PlanQueueField =
        typeof(HTNSystem).GetField("_planQueue", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new System.MissingFieldException(
            "HTNSystem._planQueue field shape changed -- update this regression test's reflection target.");

    [Test]
    public async Task RoundRestart_DiscardsPlanQueue_SoALeakedHtnJobCanNeverResumeAgainstTheNextRound()
    {
        var server = Server;
        var entMan = server.EntMan;

        EntityUid carp = default;

        await server.WaitPost(() =>
        {
            var mapSystem = entMan.System<SharedMapSystem>();
            mapSystem.CreateMap(out var mapId);
            // A real HTN-driven mob (Resources/Prototypes/Entities/Mobs/NPCs/carp.yml), so the
            // planning queue actually has genuine HTNPlanJob traffic in it, not an empty queue.
            carp = entMan.SpawnEntity("MobCarp", new MapCoordinates(0, 0, mapId));
        });

        // Give the HTN system a few ticks to actually start planning for the carp
        // (ComponentStartup/MapInitEvent -> RequestPlan -> a real HTNPlanJob enqueued into
        // HTNSystem._planQueue).
        await server.WaitRunTicks(5);

        JobQueue? queueBeforeRestart = null;
        var gameTicker = entMan.System<GameTicker>();

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(carp), Is.True, "Carp should still exist before round restart.");

            var htn = entMan.System<HTNSystem>();
            queueBeforeRestart = (JobQueue?) PlanQueueField.GetValue(htn);
            Assert.That(queueBeforeRestart, Is.Not.Null);

            // This is exactly what Content.IntegrationTests/Pair/TestPair.Recycle.cs does to hand
            // a server back to the pool between tests: restart the round (which raises
            // RoundRestartCleanupEvent, then EntityManager.FlushEntities()), then pump ticks. It
            // does not, and per Job<T>/JobQueue's design cannot, wait out an in-flight CPU job
            // first -- see the class doc above.
            gameTicker.RestartRound();

            var htnAfter = entMan.System<HTNSystem>();
            var queueAfterRestart = (JobQueue?) PlanQueueField.GetValue(htnAfter);

            // The fix under test: HTNSystem.OnRoundRestartCleanup/DiscardPlanQueue (HTNSystem.cs)
            // replaces _planQueue synchronously, inside the SAME RoundRestartCleanupEvent dispatch
            // that RestartRound() raises -- i.e. before EntityManager.FlushEntities() even runs,
            // let alone before any further tick. If this were still the SAME JobQueue instance, a
            // job enqueued for the carp above (finished or not) could still be re-dequeued and
            // Run() against the new round's torn-down entities.
            Assert.That(queueAfterRestart, Is.Not.SameAs(queueBeforeRestart),
                "HTNSystem._planQueue must be a NEW JobQueue instance after RoundRestartCleanupEvent " +
                "-- see HTNSystem.DiscardPlanQueue. If this assertion fails, the HANDOFF §10 pool-race " +
                "flake's real leak site (a JobQueue instance surviving a round boundary) is back.");

            Assert.That(entMan.EntityExists(carp), Is.False,
                "RestartRound()'s FlushEntities should have deleted the carp along with everything else.");
        });

        // Belt and braces, simulating however many ticks the NEXT pooled test would pump: confirm
        // nothing throws. Before the fix, a leaked-but-still-queued HTNPlanJob resuming here is
        // exactly the mechanism that surfaced as a NullReferenceException in
        // NPCBlackboard.TryGetEntityDefault, rotating onto whichever test ran next.
        await server.WaitRunTicks(60);
    }
}
