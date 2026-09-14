#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server.GameTicking;
using Content.Server.Mind;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     CUT GATE (v13.5+): a per-map LIVE-SMOKE round-start test — the permanent lock that would have
///     caught the v13.4 "all-passenger" incident.
///     <para>
///     The hard lesson from that incident: <b>battery-green ≠ works in a live round.</b> Every unit
///     test and the map-health scorecard were green while the LIVE box demoted every player to
///     Passenger, because the failure only surfaces when a REAL multiplayer round actually starts on
///     a Solreign station map and players with genuine job preferences are spread across jobs. This
///     fixture drives exactly that, once per reviewed Solreign map
///     (<see cref="SolreignMapTestCatalog.MapIds"/>), and asserts:
///     </para>
///     <list type="number">
///         <item>(a) the round enters <see cref="GameRunLevel.InRound"/> — no restart-loop / abort;</item>
///         <item>(b) players with DIVERSE job preferences get spread across real jobs — a healthy
///               majority hold non-Passenger jobs and several distinct real jobs are filled;</item>
///         <item>(c) no round-start errors — every player received an entity (spawn did not abort), and
///               the integration harness fails the fixture on any Error-level server log at teardown.</item>
///     </list>
///     <para>
///     KNOWN LIMIT (documented, not a gap): the incident's live trigger was a preference-LOAD race —
///     <c>game.lobbyenabled=false</c> skips the readiness path and the DEBUG-only
///     <c>_userDb.IsLoadComplete</c> assert is compiled out of the RELEASE build, so a player who is
///     readied before their cached prefs arrive falls through to <c>Random()</c> (whose only job pref
///     is the Passenger overflow). See <c>GameTicker.RoundFlow.cs</c>'s SOLREIGN diagnostic branch.
///     In-test preferences load SYNCHRONOUSLY, so this fixture cannot reproduce the un-loaded case —
///     that is why the diagnostic <c>_sawmill.Warning</c> exists on the live path. What this gate
///     locks is the ACHIEVABLE, high-value invariant: <b>on every Solreign map, a real round starts
///     and produces a healthy job spread</b> — the observable shape the incident destroyed. A
///     regression that reintroduces a content-side all-Passenger demotion (a bad overflow job, a
///     broken station-jobs prototype, a preset that can't seat crew) is caught here on every map.
///     </para>
/// </summary>
/// <seealso cref="SolreignSecretAllPassengerReproTest"/>
[TestFixture]
public sealed class SolreignRoundStartJobSpreadGateTest : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";
    private static readonly ProtoId<JobPrototype> Engineer = "StationEngineer";

    /// <summary>
    ///     Diverse crew job preferences, one distinct High job per player. Ordered so index 0 is the
    ///     incident canary (<see cref="Engineer"/>). These are standard departmental jobs present on
    ///     every full Solreign station map (all seven pass the map-health scorecard's job-spawn check),
    ///     so with one player preferring each, a healthy round seats nearly all of them.
    /// </summary>
    private static readonly ProtoId<JobPrototype>[] DiverseJobs =
    {
        Engineer, // canary — index 0
        "AtmosphericTechnician",
        "MedicalDoctor",
        "Chemist",
        "Botanist",
        "Chef",
        "Bartender",
        "Janitor",
        "CargoTechnician",
    };

    // 1 real client + 8 dummies = 9 players, each preferring a DISTINCT job from DiverseJobs.
    // SolreignAntagsOnSpawn min is 3; at pop 9 it seats 2 traitors — who KEEP their crew job — so a
    // healthy round still assigns real jobs to all nine. Bounded + fast: one heavy round per map.
    private const int DummyCount = 8;

    // Healthy-spread thresholds. The all-passenger bug drove non-Passenger ~0 / distinct-jobs ~0.
    // These floors sit well below the healthy outcome (~9 / ~9) but decisively above the incident,
    // so they catch a regression without flaking on normal slot contention. Upstream v286 fills
    // required minimum roles first, so a nine-player round can validly seat no StationEngineer while
    // still assigning nine distinct real jobs; the dedicated preference repro retains that canary.
    private const int MinNonPassengers = 5;
    private const int MinDistinctRealJobs = 4;

    public override PoolSettings PoolSettings => new()
    {
        DummyTicker = false,
        Connected = true,
        InLobby = true,
        // A real round-start mutates enough global engine state (maps, stations, minds, power,
        // atmos) that this pair must never be recycled — same discipline as the repro/scorecard.
        Dirty = true,
    };

    [Test]
    [TestCaseSource(typeof(SolreignMapTestCatalog), nameof(SolreignMapTestCatalog.MapIds))]
    public async Task MapStartsRoundWithHealthyJobSpread(string mapProtoId)
    {
        var pair = Pair;
        var ticker = pair.Server.System<GameTicker>();
        var jobSys = pair.Server.System<SharedJobSystem>();
        var mindSys = pair.Server.System<MindSystem>();

        // Drive the map under test with a deterministic, real-crew preset. SolreignAntagsOnSpawn
        // removes Secret's roll randomness (the repro proves the assignment path is identical) so the
        // job tally is stable per map; the point of THIS gate is per-map coverage, not the roll.
        pair.Server.CfgMan.SetCVar(CCVars.GameMap, mapProtoId);
        pair.Server.CfgMan.SetCVar(CCVars.GameLobbyDefaultPreset, "SolreignAntagsOnSpawn");

        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby),
            $"[{mapProtoId}] expected to start from the pre-round lobby");

        await pair.Server.AddDummySessions(DummyCount);
        await pair.RunTicksSync(5);

        // Give every player a DIFFERENT high-priority job (round-robin over DiverseJobs), with
        // Passenger only as a Medium fallback — the "diverse preferences" input the gate checks.
        var users = pair.Server.PlayerMan.Sessions.Select(x => x.UserId).ToList();
        for (var i = 0; i < users.Count; i++)
        {
            var job = DiverseJobs[i % DiverseJobs.Length];
            await pair.SetJobPriorities(users[i], (Passenger, JobPriority.Medium), (job, JobPriority.High));
        }

        ticker.ToggleReadyAll(true);
        await pair.Server.WaitPost(() => ticker.StartRound());
        await pair.RunTicksSync(15);

        // (a) The round actually entered play — no restart-loop, no aborted start.
        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound),
            $"[{mapProtoId}] round did not enter InRound (restart-loop / aborted start)");

        // Tally the assigned jobs across every player.
        var engineers = 0;
        var passengers = 0;
        var noEntity = 0;
        var realJobIds = new HashSet<string>();
        await pair.Server.WaitPost(() =>
        {
            foreach (var session in pair.Server.PlayerMan.Sessions)
            {
                var uid = session.AttachedEntity;
                if (!pair.Server.EntMan.EntityExists(uid))
                {
                    noEntity++;
                    continue;
                }

                var mind = mindSys.GetMind(uid!.Value);
                if (!jobSys.MindTryGetJobId(mind, out var job) || job is not { } jobId)
                    continue;

                if (jobId == Passenger)
                {
                    passengers++;
                    continue;
                }

                realJobIds.Add(jobId);
                if (jobId == Engineer)
                    engineers++;
            }
        });

        var nonPassengers = users.Count - passengers - noEntity;
        TestContext.Out.WriteLine(
            $"[SOLREIGN-CUTGATE] map={mapProtoId} players={users.Count} nonPassengers={nonPassengers} " +
            $"passengers={passengers} engineers={engineers} distinctRealJobs={realJobIds.Count} noEntity={noEntity}");

        // (c) Spawn did not abort — every player got a body.
        Assert.That(noEntity, Is.Zero,
            $"[{mapProtoId}] {noEntity} player(s) never got an entity — spawn aborted on this map");

        // (b) Healthy job spread — the exact shape the all-passenger incident destroyed.
        Assert.That(nonPassengers, Is.GreaterThanOrEqualTo(MinNonPassengers),
            $"[{mapProtoId}] job spread collapsed: only {nonPassengers}/{users.Count} players got a real " +
            $"(non-Passenger) job — the all-passenger fingerprint (passengers={passengers}).");
        Assert.That(realJobIds.Count, Is.GreaterThanOrEqualTo(MinDistinctRealJobs),
            $"[{mapProtoId}] diverse preferences were NOT spread: only {realJobIds.Count} distinct real " +
            $"jobs assigned across {users.Count} players preferring {DiverseJobs.Length} different jobs.");

        await pair.Server.WaitPost(() => ticker.RestartRound());
    }
}
