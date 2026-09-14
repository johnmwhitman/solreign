#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server.GameTicking;
using Content.Server.Mind;
using Content.Server.Roles;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     LIVE-INCIDENT repro (v13.4 "all passenger"): the live box runs
///     <c>game.secret_weight_prototype = "SolreignSecret"</c> on a Solreign station map — a
///     combination NO existing test exercises (SecretStartsTest uses upstream "Secret";
///     AllGamePresetsStartTest uses the default test map). This test drives a real round-start on a
///     Solreign map under the live weight table and asserts a player's job preference is honored
///     (i.e. they do NOT fall through to Passenger). It exists to reproduce + then lock the fix.
/// </summary>
[TestFixture]
public sealed class SolreignSecretAllPassengerReproTest : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";
    private static readonly ProtoId<JobPrototype> Engineer = "StationEngineer";

    public override PoolSettings PoolSettings => new()
    {
        DummyTicker = false,
        Connected = true,
        InLobby = true,
        Dirty = true,
    };

    [Test]
    public async Task LivePassengerReproTest()
    {
        var pair = Pair;
        var ticker = pair.Server.System<GameTicker>();
        var jobSys = pair.Server.System<SharedJobSystem>();
        var mindSys = pair.Server.System<MindSystem>();

        // Reproduce the live map and secret-table config. Upstream v286 now fills required minimum
        // roles before preference allocation; disable that independent fallback in this one-player
        // probe so the assertion continues to isolate the original preference-to-Passenger defect.
        // The multiplayer cases below retain the live SameDepartment fallback and cover the full path.
        pair.Server.CfgMan.SetCVar(CCVars.GameMap, "SolreignOasis");
        pair.Server.CfgMan.SetCVar(CCVars.SecretWeightPrototype, "SolreignSecret");
        pair.Server.CfgMan.SetCVar(CCVars.GameLobbyDefaultPreset, "secret");
        pair.Server.CfgMan.SetCVar(CCVars.GameMinimumJobFallback, MinimumJobFallback.None);

        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby));

        // Player wants Engineer, not Passenger.
        await pair.SetJobPriorities((Passenger, JobPriority.Medium), (Engineer, JobPriority.High));
        ticker.ToggleReadyAll(true);
        await pair.Server.WaitPost(() => ticker.StartRound());
        await pair.RunTicksSync(10);

        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound), "round did not enter InRound");

        var user = pair.Client.User!.Value;
        var uid = pair.Server.PlayerMan.SessionsDict.GetValueOrDefault(user)?.AttachedEntity;
        Assert.That(pair.Server.EntMan.EntityExists(uid), "player never got an entity — spawn aborted");
        var mind = mindSys.GetMind(uid!.Value);
        Assert.That(jobSys.MindTryGetJobId(mind, out var actualJob));

        // THE ASSERTION: Engineer preference must be honored, not silently demoted to Passenger.
        Assert.That(actualJob, Is.EqualTo(Engineer),
            $"BUG REPRODUCED: player preferred Engineer (High) but was assigned '{actualJob}' — the v13.4 all-passenger incident.");

        await pair.Server.WaitPost(() => ticker.RestartRound());
    }

    /// <summary>
    ///     MULTIPLAYER repro. The 1-player case above never actually starts a preset (Secret removes
    ///     itself when players &lt; GetMinimumPlayerCount). This drives a real round on the Solreign
    ///     map with enough players that a preset actually STARTS, then tallies every player's job. The
    ///     all-passenger incident = zero non-antag crew got their preferred (Engineer) job.
    /// </summary>
    [Test]
    public async Task MultiplayerForcedAntagsOnSpawnTest()
    {
        var pair = Pair;
        var ticker = pair.Server.System<GameTicker>();
        var jobSys = pair.Server.System<SharedJobSystem>();
        var mindSys = pair.Server.System<MindSystem>();

        pair.Server.CfgMan.SetCVar(CCVars.GameMap, "SolreignOasis");
        // Force the chaos preset directly — removes Secret's roll randomness.
        // NOTE: enabling CCVars.ArrivalsShuttles here was investigated (live runs arrivals ON) but
        // (1) it only triggers a test-only IoC artifact (BlockGame arcade on the arrivals terminal),
        // and (2) ArrivalsSystem.HandlePlayerSpawning no-ops at round start (RunLevel != InRound) and
        // passes ev.Job through regardless — arrivals cannot cause a round-start passenger demotion.
        pair.Server.CfgMan.SetCVar(CCVars.GameLobbyDefaultPreset, "SolreignAntagsOnSpawn");

        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby));

        // 1 client + 8 dummies = 9 players. SolreignAntagsOnSpawn min is 3; 9 pop -> 2 traitors.
        await pair.Server.AddDummySessions(8);
        await pair.RunTicksSync(5);

        var users = pair.Server.PlayerMan.Sessions.Select(x => x.UserId).ToList();
        foreach (var u in users)
            await pair.SetJobPriorities(u, (Passenger, JobPriority.Medium), (Engineer, JobPriority.High));

        ticker.ToggleReadyAll(true);
        await pair.Server.WaitPost(() => ticker.StartRound());
        await pair.RunTicksSync(15);

        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound), "round did not enter InRound");

        var engineers = 0;
        var passengers = 0;
        var other = 0;
        var noEntity = 0;
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
                if (!jobSys.MindTryGetJobId(mind, out var job))
                {
                    other++;
                    continue;
                }

                if (job == Engineer)
                    engineers++;
                else if (job == Passenger)
                    passengers++;
                else
                    other++;
            }
        });

        TestContext.Out.WriteLine(
            $"[SOLREIGN-REPRO] players={users.Count} engineers={engineers} passengers={passengers} other={other} noEntity={noEntity}");

        // THE ASSERTION: with 9 engineer-preferring players and only 2 antag seats, the healthy
        // outcome is ~7 engineers. The bug = everyone demoted to Passenger (engineers == 0).
        Assert.That(engineers, Is.GreaterThan(0),
            $"BUG REPRODUCED: no player got Engineer — all demoted (engineers={engineers} passengers={passengers} other={other} noEntity={noEntity}).");

        await pair.Server.WaitPost(() => ticker.RestartRound());
    }

    /// <summary>
    ///     MULTIPLAYER repro under the exact LIVE Secret config with a large-enough pop that Secret
    ///     always resolves to a real preset.
    /// </summary>
    [Test]
    public async Task MultiplayerLiveSecretTest()
    {
        var pair = Pair;
        var ticker = pair.Server.System<GameTicker>();
        var jobSys = pair.Server.System<SharedJobSystem>();
        var mindSys = pair.Server.System<MindSystem>();

        pair.Server.CfgMan.SetCVar(CCVars.GameMap, "SolreignOasis");
        pair.Server.CfgMan.SetCVar(CCVars.SecretWeightPrototype, "SolreignSecret");
        pair.Server.CfgMan.SetCVar(CCVars.GameLobbyDefaultPreset, "secret");

        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby));

        await pair.Server.AddDummySessions(11); // 12 players — above every reachable preset's min.
        await pair.RunTicksSync(5);

        var users = pair.Server.PlayerMan.Sessions.Select(x => x.UserId).ToList();
        foreach (var u in users)
            await pair.SetJobPriorities(u, (Passenger, JobPriority.Medium), (Engineer, JobPriority.High));

        ticker.ToggleReadyAll(true);
        await pair.Server.WaitPost(() => ticker.StartRound());
        await pair.RunTicksSync(15);

        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound), "round did not enter InRound");

        var engineers = 0;
        var passengers = 0;
        var other = 0;
        await pair.Server.WaitPost(() =>
        {
            foreach (var session in pair.Server.PlayerMan.Sessions)
            {
                var uid = session.AttachedEntity;
                if (!pair.Server.EntMan.EntityExists(uid))
                    continue;
                var mind = mindSys.GetMind(uid!.Value);
                if (!jobSys.MindTryGetJobId(mind, out var job))
                    continue;
                if (job == Engineer)
                    engineers++;
                else if (job == Passenger)
                    passengers++;
                else
                    other++;
            }
        });

        TestContext.Out.WriteLine(
            $"[SOLREIGN-REPRO-SECRET] players={users.Count} engineers={engineers} passengers={passengers} other={other}");

        Assert.That(engineers, Is.GreaterThan(0),
            $"BUG REPRODUCED (live secret): no player got Engineer (engineers={engineers} passengers={passengers} other={other}).");

        await pair.Server.WaitPost(() => ticker.RestartRound());
    }
}
