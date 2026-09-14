#nullable enable
using System;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.Recreation;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.CCVar;
using Content.Shared.Trigger;
using NUnit.Framework;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
/// Proves repeatable heats through the real buckle lifecycle and checkpoint event subscription.
/// Pure transition edge cases live in <c>SolreignKartHeatRulesTests</c>.
/// </summary>
[TestFixture]
public sealed class SolreignKartHeatLifecycleIntegrationTest : GameTest
{
    private const string KartId = "SolreignKartHeatTestKart";
    private const string DriverId = "SolreignKartHeatTestDriver";
    private const string CheckpointZeroId = "SolreignKartHeatTestCheckpointZero";
    private const string CheckpointOneId = "SolreignKartHeatTestCheckpointOne";

    [TestPrototypes]
    private const string Prototypes = $@"
- type: entity
  id: {KartId}
  components:
  - type: MobMover
  - type: InputMover
  - type: Physics
    bodyType: KinematicController
  - type: Appearance
  - type: Strap
    position: Stand
  - type: SolreignDriverSeat
  - type: SolreignLapTracker
    checkpointCount: 2
    totalLaps: 1

- type: entity
  id: {DriverId}
  components:
  - type: Buckle
  - type: Hands
  - type: ComplexInteraction
  - type: InputMover
  - type: Physics
    bodyType: KinematicController
  - type: Body
    prototype: Human
  - type: StandingState

- type: entity
  id: {CheckpointZeroId}
  components:
  - type: SolreignLapCheckpoint
    ordinal: 0

- type: entity
  id: {CheckpointOneId}
  components:
  - type: SolreignLapCheckpoint
    ordinal: 1
";

    public override PoolSettings PoolSettings => new()
    {
        Dirty = true,
    };

    [Test]
    public async Task CVarTransitionsAndDriverResetsFollowRealBuckleLifecycle()
    {
        var server = Server;
        var entMan = server.EntMan;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var buckleSystem = server.System<SharedBuckleSystem>();
        var testMap = await Pair.CreateTestMap();

        EntityUid kart = default;
        EntityUid firstDriver = default;
        EntityUid secondDriver = default;
        EntityUid checkpointZero = default;
        EntityUid checkpointOne = default;
        SolreignLapTrackerComponent tracker = default!;
        SolreignDriverSeatComponent seat = default!;

        await server.WaitPost(() =>
        {
            _ = server.System<SolreignLapTrackerSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(
                    CCVars.SolreignKartRepeatableHeatsEnabled.DefaultValue,
                    Is.False,
                    "repeatable heats must remain opt-in");
                Assert.That(
                    cfg.GetCVar(CCVars.SolreignKartRepeatableHeatsEnabled),
                    Is.False,
                    "the real server configuration must begin with repeatable heats disabled");
            });

            kart = entMan.SpawnEntity(KartId, testMap.GridCoords);
            firstDriver = entMan.SpawnEntity(DriverId, testMap.GridCoords);
            secondDriver = entMan.SpawnEntity(DriverId, testMap.GridCoords);
            checkpointZero = entMan.SpawnEntity(CheckpointZeroId, testMap.GridCoords);
            checkpointOne = entMan.SpawnEntity(CheckpointOneId, testMap.GridCoords);
            tracker = entMan.GetComponent<SolreignLapTrackerComponent>(kart);
            seat = entMan.GetComponent<SolreignDriverSeatComponent>(kart);

            Assert.That(buckleSystem.TryBuckle(firstDriver, firstDriver, kart), Is.True);
        });

        await server.WaitAssertion(() =>
            Assert.That(seat.Driver, Is.EqualTo(firstDriver),
                "the real StrappedEvent must wire the driver-seat system"));

        // Enabling after a legacy heat has started must not arm timing or move its finish line.
        await RaiseCheckpoint(server, entMan, checkpointZero, kart);
        await server.WaitAssertion(() =>
        {
            Assert.That(tracker.NextCheckpointOrdinal, Is.EqualTo(1));
            Assert.That(tracker.HeatStartedAt, Is.Null);
        });

        await server.WaitPost(() =>
            cfg.SetCVar(CCVars.SolreignKartRepeatableHeatsEnabled, true));
        await RaiseCheckpoint(server, entMan, checkpointOne, kart);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(tracker.Finished, Is.True,
                    "mid-heat enable must preserve the legacy final-checkpoint boundary");
                Assert.That(tracker.FinishedElapsed, Is.Null);
                Assert.That(tracker.FinishingDriver, Is.Null);
            });
        });

        // The feature was not armed for the legacy result, so a real new-driver buckle may reset.
        await server.WaitPost(() =>
        {
            buckleSystem.Unbuckle(firstDriver, firstDriver);
            Assert.That(buckleSystem.TryBuckle(secondDriver, secondDriver, kart), Is.True);
        });
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(seat.Driver, Is.EqualTo(secondDriver));
                Assert.That(tracker.Finished, Is.False);
                Assert.That(tracker.LapsCompleted, Is.Zero);
                Assert.That(tracker.NextCheckpointOrdinal, Is.Zero);
            });
        });

        // A heat that starts while enabled arms authoritative timing and waits for ordinal zero.
        await RaiseCheckpoint(server, entMan, checkpointZero, kart);
        await server.WaitAssertion(() => Assert.That(tracker.HeatStartedAt, Is.Not.Null));
        await server.WaitRunTicks(2);
        await RaiseCheckpoint(server, entMan, checkpointOne, kart);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(tracker.LapsCompleted, Is.EqualTo(1));
                Assert.That(tracker.NextCheckpointOrdinal, Is.Zero);
                Assert.That(tracker.Finished, Is.False);
            });
        });

        // Disabling at this exact boundary must normalize to legacy-finished, not require a lap.
        await server.WaitPost(() =>
            cfg.SetCVar(CCVars.SolreignKartRepeatableHeatsEnabled, false));
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(tracker.Finished, Is.True);
                Assert.That(tracker.HeatStartedAt, Is.Null);
                Assert.That(tracker.FinishedElapsed, Is.Null);
                Assert.That(tracker.FinishingDriver, Is.Null,
                    "the kill switch discards optional timing identity");
            });
        });

        // Re-enabling intentionally treats that discarded result as unowned; the same physical
        // driver can begin a clean heat only after a real unstrap/restrap lifecycle.
        await server.WaitPost(() =>
        {
            cfg.SetCVar(CCVars.SolreignKartRepeatableHeatsEnabled, true);
            buckleSystem.Unbuckle(secondDriver, secondDriver);
            Assert.That(buckleSystem.TryBuckle(secondDriver, secondDriver, kart), Is.True);
        });
        await server.WaitAssertion(() =>
        {
            Assert.That(tracker.Finished, Is.False);
            Assert.That(tracker.LapsCompleted, Is.Zero);
        });

        await RaiseCheckpoint(server, entMan, checkpointZero, kart);
        await server.WaitAssertion(() =>
        {
            Assert.That(tracker.HeatStartedAt, Is.Not.Null);
            Assert.That(tracker.HeatDriver, Is.EqualTo(secondDriver),
                "the timed heat must bind to the driver who crossed its start line");
        });

        // Leaving mid-heat invalidates the sample. A replacement driver must start from a clean
        // course state instead of inheriting the previous driver's progress or elapsed time.
        await server.WaitPost(() => buckleSystem.Unbuckle(secondDriver, secondDriver));
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(seat.Driver, Is.Null);
                Assert.That(tracker.NextCheckpointOrdinal, Is.Zero);
                Assert.That(tracker.LapsCompleted, Is.Zero);
                Assert.That(tracker.HeatStartedAt, Is.Null);
                Assert.That(tracker.HeatDriver, Is.Null);
            });
        });

        await server.WaitPost(() =>
            Assert.That(buckleSystem.TryBuckle(firstDriver, firstDriver, kart), Is.True));
        await server.WaitAssertion(() =>
        {
            Assert.That(seat.Driver, Is.EqualTo(firstDriver));
            Assert.That(tracker.NextCheckpointOrdinal, Is.Zero);
            Assert.That(tracker.HeatStartedAt, Is.Null);
        });

        await RaiseCheckpoint(server, entMan, checkpointZero, kart);
        TimeSpan startedAt = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(tracker.HeatStartedAt, Is.Not.Null);
            Assert.That(tracker.HeatDriver, Is.EqualTo(firstDriver));
            startedAt = tracker.HeatStartedAt!.Value;
        });
        await server.WaitRunTicks(2);
        await RaiseCheckpoint(server, entMan, checkpointOne, kart);
        await server.WaitRunTicks(2);
        await RaiseCheckpoint(server, entMan, checkpointZero, kart);

        TimeSpan frozenElapsed = default;
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(tracker.Finished, Is.True);
                Assert.That(tracker.HeatStartedAt, Is.EqualTo(startedAt));
                Assert.That(tracker.FinishedElapsed, Is.GreaterThan(TimeSpan.Zero));
                Assert.That(tracker.FinishingDriver, Is.EqualTo(firstDriver));
            });
            frozenElapsed = tracker.FinishedElapsed!.Value;
        });

        await server.WaitRunTicks(2);
        await RaiseCheckpoint(server, entMan, checkpointZero, kart);
        await server.WaitAssertion(() =>
            Assert.That(tracker.FinishedElapsed, Is.EqualTo(frozenElapsed),
                "post-finish checkpoint events cannot mutate the authoritative result"));

        // Real same-driver restrap is idempotent; a different driver's buckle starts a clean heat.
        await server.WaitPost(() =>
        {
            buckleSystem.Unbuckle(firstDriver, firstDriver);
            Assert.That(buckleSystem.TryBuckle(firstDriver, firstDriver, kart), Is.True);
        });
        await server.WaitAssertion(() =>
        {
            Assert.That(tracker.Finished, Is.True);
            Assert.That(tracker.FinishedElapsed, Is.EqualTo(frozenElapsed));
        });

        await server.WaitPost(() =>
        {
            buckleSystem.Unbuckle(firstDriver, firstDriver);
            Assert.That(buckleSystem.TryBuckle(secondDriver, secondDriver, kart), Is.True);
        });
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(seat.Driver, Is.EqualTo(secondDriver));
                Assert.That(tracker.Finished, Is.False);
                Assert.That(tracker.LapsCompleted, Is.Zero);
                Assert.That(tracker.NextCheckpointOrdinal, Is.Zero);
                Assert.That(tracker.HeatStartedAt, Is.Null);
                Assert.That(tracker.FinishedElapsed, Is.Null);
                Assert.That(tracker.FinishingDriver, Is.Null);
                Assert.That(tracker.HeatDriver, Is.Null);
            });
        });

        // Disabling an incomplete timed circuit keeps its ordinal progress but discards optional
        // timing state. Re-enabling alone must not reinterpret that partial circuit as timed.
        await RaiseCheckpoint(server, entMan, checkpointZero, kart);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(tracker.NextCheckpointOrdinal, Is.EqualTo(1));
                Assert.That(tracker.LapsCompleted, Is.Zero);
                Assert.That(tracker.HeatStartedAt, Is.Not.Null);
            });
        });

        await server.WaitPost(() =>
            cfg.SetCVar(CCVars.SolreignKartRepeatableHeatsEnabled, false));
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(tracker.Finished, Is.False);
                Assert.That(tracker.NextCheckpointOrdinal, Is.EqualTo(1));
                Assert.That(tracker.LapsCompleted, Is.Zero);
                Assert.That(tracker.HeatStartedAt, Is.Null);
                Assert.That(tracker.FinishedElapsed, Is.Null);
                Assert.That(tracker.FinishingDriver, Is.Null);
            });
        });

        await server.WaitPost(() =>
            cfg.SetCVar(CCVars.SolreignKartRepeatableHeatsEnabled, true));
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(tracker.Finished, Is.False);
                Assert.That(tracker.NextCheckpointOrdinal, Is.EqualTo(1));
                Assert.That(tracker.HeatStartedAt, Is.Null);
            });
        });

        await RaiseCheckpoint(server, entMan, checkpointOne, kart);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(tracker.Finished, Is.True,
                    "an interrupted circuit must retain the legacy final-checkpoint boundary");
                Assert.That(tracker.LapsCompleted, Is.EqualTo(1));
                Assert.That(tracker.FinishedElapsed, Is.Null);
                Assert.That(tracker.FinishingDriver, Is.Null);
            });
        });
    }

    [Test]
    public async Task RepeatableHeat_UnmannedKartCannotProgressOrSeedALaterDriver()
    {
        var server = Server;
        var entMan = server.EntMan;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var buckleSystem = server.System<SharedBuckleSystem>();
        var testMap = await Pair.CreateTestMap();

        EntityUid kart = default;
        EntityUid driver = default;
        EntityUid checkpointZero = default;
        EntityUid checkpointOne = default;
        SolreignLapTrackerComponent tracker = default!;
        SolreignDriverSeatComponent seat = default!;

        await server.WaitPost(() =>
        {
            _ = server.System<SolreignLapTrackerSystem>();
            cfg.SetCVar(CCVars.SolreignKartRepeatableHeatsEnabled, true);
            kart = entMan.SpawnEntity(KartId, testMap.GridCoords);
            driver = entMan.SpawnEntity(DriverId, testMap.GridCoords);
            checkpointZero = entMan.SpawnEntity(CheckpointZeroId, testMap.GridCoords);
            checkpointOne = entMan.SpawnEntity(CheckpointOneId, testMap.GridCoords);
            tracker = entMan.GetComponent<SolreignLapTrackerComponent>(kart);
            seat = entMan.GetComponent<SolreignDriverSeatComponent>(kart);
        });

        await RaiseCheckpoint(server, entMan, checkpointZero, kart);
        await RaiseCheckpoint(server, entMan, checkpointOne, kart);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(seat.Driver, Is.Null);
                Assert.That(tracker.NextCheckpointOrdinal, Is.Zero);
                Assert.That(tracker.LapsCompleted, Is.Zero);
                Assert.That(tracker.Finished, Is.False);
                Assert.That(tracker.HeatStartedAt, Is.Null);
                Assert.That(tracker.HeatDriver, Is.Null);
            });
        });

        await server.WaitPost(() =>
            Assert.That(buckleSystem.TryBuckle(driver, driver, kart), Is.True));
        await server.WaitAssertion(() =>
        {
            Assert.That(seat.Driver, Is.EqualTo(driver));
            Assert.That(tracker.NextCheckpointOrdinal, Is.Zero);
            Assert.That(tracker.HeatStartedAt, Is.Null);
        });

        await RaiseCheckpoint(server, entMan, checkpointZero, kart);
        await server.WaitAssertion(() =>
        {
            Assert.That(tracker.NextCheckpointOrdinal, Is.EqualTo(1));
            Assert.That(tracker.HeatStartedAt, Is.Not.Null);
            Assert.That(tracker.HeatDriver, Is.EqualTo(driver));
        });
    }

    private static async Task RaiseCheckpoint(
        RobustIntegrationTest.ServerIntegrationInstance server,
        IEntityManager entMan,
        EntityUid checkpoint,
        EntityUid kart)
    {
        await server.WaitPost(() =>
        {
            var trigger = new TriggerEvent(kart);
            entMan.EventBus.RaiseLocalEvent(checkpoint, ref trigger);
        });
    }
}
