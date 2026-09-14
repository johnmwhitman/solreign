#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.GameTicking;
using Content.Server.StationEvents.Components;
using Content.Shared.GameTicking.Components;
using NUnit.Framework;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
/// V13.5 wire-up (docs/research/UNWIRED-CONTENT-CENSUS-2026-07-17.md), items #3/#6/#7: scheduler-path
/// smoke tests for the game rules this wave un-gated or newly wired into a reachable table, mirroring
/// <see cref="SolreignAcidStormIntegrationTest"/>'s positive-path shape (start the real rule via
/// <see cref="GameTicker.StartGameRule(string, out Robust.Shared.GameObjects.EntityUid)"/>, assert it
/// doesn't throw) rather than a chat-capture or CVar-guard test -- none of these five rules carry a CVar
/// kill switch the way <c>SolreignAcidStorm</c> does (verified by reading each rule's system before
/// writing this: <c>BreakerFlipRule</c>/<c>ClericalErrorRule</c>/<c>VentClogRule</c> are plain upstream
/// events with no <c>Added</c>/<c>Ended</c> override that gates anything; <c>DerelictEngineerCyborgSpawn</c>'s
/// <c>BaseDerelictCyborgSpawn</c> parent has no CVar either; <c>RespawnDeadRule</c> is a bare component
/// pair, not even a <c>StationEvent</c>), so a CVar-off counterpart test like the Acid Storm one's second
/// method would have nothing to cover.
///
/// The naive "start it, assert IsGameRuleActive two ticks later" shape (which is what
/// <c>SolreignAcidStormIntegrationTest</c> uses, and what this file's first draft used uniformly) does
/// NOT hold for two of these five, and re-running against a first draft caught both -- documented per
/// rule below rather than silently loosened:
///   * <b>VentClog</b> inherits <c>BaseStationEventLongDelay</c> (<c>GameRule.delay.min: 40</c>,
///     pre-existing, untouched by this wave's minimumPlayers edit) -- <c>GameTicker.StartGameRule</c>
///     queues a <c>DelayedStartRuleComponent</c> instead of activating immediately when
///     <c>GameRuleComponent.Delay</c> is set. Asserting <c>IsGameRuleActive</c> right after starting it
///     is testing the wrong thing; the correct assertion is that it was accepted (active OR queued), not
///     rejected.
///   * <b>DerelictEngineerCyborgSpawn</b> force-ends itself inside <c>SpaceSpawnRule.Added()</c>
///     (<c>TryGetRandomStation</c> returning false in this bare, pool-recycled test pair -- unrelated to
///     <c>minimumPlayers</c>, which <c>StartGameRule</c> never consults; <c>StationEventEligibleComponent</c>
///     eligibility is a pool-timing detail, not something the census edit touched) before this test's
///     activity check ever runs. Chasing full pool-eligibility plumbing here was out of scope for this
///     wave, so this rule's coverage instead reads <c>StationEventComponent.MinimumPlayers</c> directly
///     off the spawned rule entity -- a direct, environment-independent proof the census #3 edit (4 -> 2)
///     landed on the entity actually used by the scheduler, plus the same DoesNotThrow smoke coverage
///     the other four get.
/// </summary>
[TestFixture]
public sealed class SolreignV135WireupSchedulerPathIntegrationTest : GameTest
{
    // Starts real, persistent game-rule entities on the server -- same "never hand this pair back to
    // the pool" reasoning SolreignAcidStormIntegrationTest documents.
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
    };

    /// <summary>Census #7: BreakerFlip minimumPlayers 15 -> 1 (Resources/Prototypes/GameRules/events.yml).</summary>
    private const string BreakerFlipRuleId = "BreakerFlip";

    /// <summary>Census #7: ClericalError minimumPlayers 15 -> 1.</summary>
    private const string ClericalErrorRuleId = "ClericalError";

    /// <summary>Census #7: VentClog minimumPlayers 15 -> 1. Also delayed-start, see class remarks.</summary>
    private const string VentClogRuleId = "VentClog";

    /// <summary>
    /// Census #3: BaseDerelictCyborgSpawn.minimumPlayers 4 -> 2. Exercised via one concrete variant
    /// (Engineer) since all Derelict*CyborgSpawn ids share the same abstract parent and StationEvent
    /// override -- starting one is a direct proxy for the shared minimumPlayers/duration/SpaceSpawnRule
    /// wiring the edit actually touched. See class remarks for why this one's coverage differs.
    /// </summary>
    private const string DerelictCyborgSpawnRuleId = "DerelictEngineerCyborgSpawn";

    /// <summary>
    /// Census #6: added to SolreignAntagsOnSpawn's rules list
    /// (Resources/Prototypes/_Solreign/GameRules/antags_on_spawn.yml). Not a StationEvent -- this
    /// exercises the bare rule directly, same as how a preset's `rules:` list starts each entry.
    /// </summary>
    private const string RespawnDeadRuleId = "RespawnDeadRule";

    [Test]
    [TestCase(BreakerFlipRuleId)]
    [TestCase(ClericalErrorRuleId)]
    [TestCase(RespawnDeadRuleId)]
    public async Task StartGameRule_LiveRound_ActivatesWithoutThrowing(string ruleId)
    {
        var server = Server;
        var ticker = server.System<GameTicker>();

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.DoesNotThrow(() => ticker.StartGameRule(ruleId),
                $"Starting the {ruleId} game rule must never throw -- this is the same scheduler-path " +
                "the Basic/Ramping event schedulers (and, for RespawnDeadRule, a gamePreset's `rules:` " +
                "list) use to bring a table entry online once its minimumPlayers gate is cleared.");
        });

        // Let Started() (and any queued spawns) actually run.
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.IsGameRuleActive(ruleId), Is.True,
                $"{ruleId} must actually be running once started -- proves the V13.5 wire-up edit " +
                "(the minimumPlayers change or in-table addition) didn't leave the rule unable to start.");
        });
    }

    [Test]
    public async Task StartGameRule_VentClog_AcceptedAsActiveOrDelayQueued()
    {
        var server = Server;
        var ticker = server.System<GameTicker>();
        var entMan = server.EntMan;

        await server.WaitRunTicks(1);

        Robust.Shared.GameObjects.EntityUid ruleEntity = default;

        await server.WaitAssertion(() =>
        {
            var started = false;
            Assert.DoesNotThrow(() => started = ticker.StartGameRule(VentClogRuleId, out ruleEntity),
                "Starting VentClog must never throw.");
            Assert.That(started, Is.True,
                "StartGameRule must report success (either immediate activation or a queued delayed " +
                "start) for VentClog -- false would mean it was rejected outright.");
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var isActive = entMan.HasComponent<ActiveGameRuleComponent>(ruleEntity);
            var isQueued = entMan.HasComponent<DelayedStartRuleComponent>(ruleEntity);

            Assert.That(isActive || isQueued, Is.True,
                "VentClog must be either actively running or queued for a delayed start (its " +
                "BaseStationEventLongDelay parent sets GameRule.delay.min: 40, pre-existing and " +
                "untouched by the V13.5 minimumPlayers edit) -- neither would mean the rule was " +
                "force-ended/rejected instead.");
        });
    }

    [Test]
    public async Task StartGameRule_DerelictCyborgSpawn_DoesNotThrow_AndMinimumPlayersIsTwo()
    {
        var server = Server;
        var ticker = server.System<GameTicker>();
        var entMan = server.EntMan;

        await server.WaitRunTicks(1);

        Robust.Shared.GameObjects.EntityUid ruleEntity = default;

        await server.WaitAssertion(() =>
        {
            Assert.DoesNotThrow(() => ticker.StartGameRule(DerelictCyborgSpawnRuleId, out ruleEntity),
                "Starting DerelictEngineerCyborgSpawn must never throw.");
        });

        await server.WaitAssertion(() =>
        {
            var stationEvent = entMan.GetComponent<StationEventComponent>(ruleEntity);

            Assert.That(stationEvent.MinimumPlayers, Is.EqualTo(2),
                "Census #3: BaseDerelictCyborgSpawn.minimumPlayers must be 2 (was 4) on the entity the " +
                "scheduler actually spawns -- read directly off the started rule entity rather than via " +
                "IsGameRuleActive, because SpaceSpawnRule.Added()'s TryGetRandomStation call force-ends " +
                "this rule in this bare test pair independent of minimumPlayers (which StartGameRule " +
                "never consults in the first place) -- see class remarks.");
        });
    }
}
