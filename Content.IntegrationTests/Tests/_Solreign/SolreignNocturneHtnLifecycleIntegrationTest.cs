#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.GameTicking;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.Preconditions;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Maps;
using NUnit.Framework;
using Robust.Shared.CPUJob.JobQueues;
using Robust.Shared.CPUJob.JobQueues.Queues;
using Robust.Shared.EntitySerialization;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
/// Classifies the Nocturne HTN lifecycle fatal into prototype initialization,
/// entity-flush cancellation, or round-restart queue retirement.
/// </summary>
[TestFixture]
public sealed partial class SolreignNocturneHtnLifecycleIntegrationTest : GameTest
{
    private const int RecordedServerSeed = 736584703;
    private const string NocturneMap = "SolreignNocturne";
    private const string TestCompoundId = "SolreignNocturneLifecycleTestCompound";
    private static readonly ProtoId<HTNCompoundPrototype> FollowCompound = "FollowCompound";
    private static readonly ProtoId<HTNCompoundPrototype> TestCompoundPrototype = TestCompoundId;

    [TestPrototypes]
    private const string TestCompound = """
- type: htnCompound
  id: SolreignNocturneLifecycleTestCompound
  branches:
  - tasks:
    - !type:HTNPrimitiveTask
      operator: !type:WaitOperator
        key: IdleTime
""";

    public override PoolSettings PoolSettings => new()
    {
        Connected = false,
        DummyTicker = false,
        Dirty = true,
        ServerSeed = RecordedServerSeed,
    };

    private static readonly FieldInfo PlanQueueField =
        typeof(HTNSystem).GetField("_planQueue", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new MissingFieldException(
            "HTNSystem._planQueue changed shape; update the lifecycle regression.");

    [Test]
    public async Task NocturneHtnLifecycle_SeparatesInitializationFlushAndRestart()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var protoManager = server.ResolveDependency<IPrototypeManager>();
        var ticker = entManager.System<GameTicker>();
        var htnSystem = entManager.System<HTNSystem>();
        MapId loadedMapId = default;

        await server.WaitPost(() =>
        {
            var options = DeserializationOptions.Default with { InitializeMaps = true };
            ticker.LoadGameMap(
                protoManager.Index<GameMapPrototype>(NocturneMap),
                out loadedMapId,
                options);
        });

        EntityUid owner = default;
        HTNComponent? ownerHtn = null;

        await server.WaitAssertion(() =>
        {
            var query = entManager.AllEntityQueryEnumerator<HTNComponent, TransformComponent>();
            while (query.MoveNext(out var candidate, out var candidateHtn, out var transform))
            {
                if (transform.MapID != loadedMapId)
                    continue;

                owner = candidate;
                ownerHtn = candidateHtn;
                break;
            }

            Assert.That(ownerHtn, Is.Not.Null,
                "initialization arm: Nocturne must load at least one real HTN owner");

            var follow = protoManager.Index(FollowCompound);
            var coordinatePrecondition = follow.Branches
                .SelectMany(branch => branch.Tasks)
                .OfType<HTNPrimitiveTask>()
                .SelectMany(task => task.Preconditions)
                .OfType<CoordinatesNotInRangePrecondition>()
                .Single();

            var blackboard = new NPCBlackboard();
            blackboard.SetValue(NPCBlackboard.Owner, owner);
            blackboard.SetValue(
                coordinatePrecondition.TargetKey,
                entManager.GetComponent<TransformComponent>(owner).Coordinates);
            blackboard.SetValue(coordinatePrecondition.RangeKey, 1f);

            Assert.That(
                () => coordinatePrecondition.IsMet(blackboard),
                Throws.Nothing,
                "initialization arm: the server-loaded FollowCompound coordinate precondition must have " +
                "the current server's injected dependencies");
            Assert.That(coordinatePrecondition.IsMet(blackboard), Is.False,
                "initialization arm: an owner is not outside range of its own coordinates");
        });

        Assert.That(ownerHtn, Is.Not.Null);
        var selectedHtn = ownerHtn!;

        // Stop the owner's native planner and let its canceled queue entry settle before
        // installing the controlled component-owned job.
        await server.WaitPost(() => htnSystem.SetHTNEnabled((owner, selectedHtn), false));
        await server.WaitRunTicks(10);

        var barrier = new BarrierOperator();
        var sentinel = new SentinelPrecondition();
        HTNCompoundPrototype testCompound = null!;
        List<HTNBranch> originalBranches = null!;
        var branchesReplaced = false;
        var tokenSource = new CancellationTokenSource();
        HTNPlanJob? job = null;

        try
        {
            await server.WaitPost(() =>
            {
                testCompound = protoManager.Index(TestCompoundPrototype);
                originalBranches = testCompound.Branches;
                testCompound.Branches =
                [
                    new HTNBranch
                    {
                        Tasks =
                        [
                            new HTNPrimitiveTask { Operator = barrier },
                            new HTNPrimitiveTask
                            {
                                Preconditions = [sentinel],
                                Operator = new ImmediateOperator(),
                            },
                        ],
                    },
                ];
                branchesReplaced = true;

                job = new HTNPlanJob(
                    0.0,
                    protoManager,
                    new HTNCompoundTask { Task = TestCompoundId },
                    selectedHtn.Blackboard.ShallowClone(),
                    null,
                    tokenSource.Token);

                selectedHtn.PlanningJob = job;
                selectedHtn.PlanningToken = tokenSource;
                GetPlanQueue(htnSystem).EnqueueJob(job);
            });

            await server.WaitRunTicks(5);
            await server.WaitAssertion(() =>
            {
                Assert.That(job, Is.Not.Null);
                Assert.That(job!.Status, Is.EqualTo(JobStatus.Waiting),
                    "flush-before-restart arm: controlled HTN job must be waiting at the barrier");
                Assert.That(sentinel.EvaluationCount, Is.Zero);
            });

            // This is the pool's distinct second recycle phase, not RestartRound's
            // cleanup-before-flush path. ComponentShutdown must cancel the owned job.
            await server.WaitPost(entManager.FlushEntities);
            await server.WaitAssertion(() =>
            {
                Assert.That(tokenSource.IsCancellationRequested, Is.True,
                    "flush-before-restart arm: HTN ComponentShutdown must cancel the owned plan job");
            });

            await server.WaitPost(barrier.Release);
            await server.WaitRunTicks(5);
            await server.WaitAssertion(() =>
            {
                Assert.That(sentinel.EvaluationCount, Is.Zero,
                    "flush-before-restart arm: a component-owned job must not evaluate another " +
                    "precondition after its owner was flushed");
                Assert.That(job!.Exception, Is.Null,
                    "flush-before-restart arm: cancellation must retire the job without a fatal");
                Assert.That(job.Status, Is.EqualTo(JobStatus.Finished),
                    "flush-before-restart arm: the canceled job must terminate, not remain paused");
                Assert.That(job.AsTask.IsCanceled, Is.True,
                    "flush-before-restart arm: cancellation must settle the job task as canceled");
                Assert.That(job.Result, Is.Null,
                    "flush-before-restart arm: a cooperatively canceled plan must produce no plan");
            });

            JobQueue? queueBeforeRestart = null;
            JobQueue? queueAfterRestart = null;
            await server.WaitPost(() => queueBeforeRestart = GetPlanQueue(htnSystem));
            await server.WaitPost(ticker.RestartRound);
            await server.WaitPost(() => queueAfterRestart = GetPlanQueue(htnSystem));

            await server.WaitAssertion(() =>
            {
                Assert.That(queueAfterRestart, Is.Not.SameAs(queueBeforeRestart),
                    "restart arm: RoundRestartCleanupEvent must replace the HTN plan queue");
            });

        }
        finally
        {
            tokenSource.Cancel();
            barrier.Release();
            if (branchesReplaced)
                await server.WaitPost(() => testCompound.Branches = originalBranches);
            tokenSource.Dispose();
        }
    }

    private static JobQueue GetPlanQueue(HTNSystem system)
    {
        return (JobQueue?) PlanQueueField.GetValue(system)
            ?? throw new InvalidOperationException("HTNSystem._planQueue was unexpectedly null.");
    }

    private sealed partial class BarrierOperator : HTNOperator
    {
        private readonly TaskCompletionSource<bool> _release = new();

        public void Release()
        {
            _release.TrySetResult(true);
        }

        public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
            NPCBlackboard blackboard,
            CancellationToken cancelToken)
        {
            await _release.Task;
            return (true, null);
        }
    }

    private sealed partial class SentinelPrecondition : HTNPrecondition
    {
        public int EvaluationCount { get; private set; }

        public override bool IsMet(NPCBlackboard blackboard)
        {
            EvaluationCount++;
            return true;
        }
    }

    private sealed partial class ImmediateOperator : HTNOperator;
}
