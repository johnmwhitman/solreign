#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.GameTicking;
using Content.Server.Mind;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
/// Regression coverage for the v13.4 live incident (2026-07-18): the first time
/// <c>game.secret_weight_prototype = "SolreignSecret"</c> ran in a real multiplayer round,
/// every player spawned as Passenger instead of getting their preferred job.
///
/// These tests drive round-start through the *actual* production pathway
/// (<see cref="Content.Server.GameTicking.Rules.SecretRuleSystem"/> rolling a preset from the
/// weighted table and dynamically adding that preset's rules), which
/// <c>Content.IntegrationTests.Tests.GameRules.AllGamePresetsStartTest</c> does not exercise —
/// that test always force-sets each preset directly via <c>setgamepreset</c>, bypassing
/// <see cref="Content.Server.GameTicking.Rules.SecretRuleSystem"/> entirely.
///
/// If job assignment silently breaks for everyone (the incident symptom), every dummy session
/// below — despite having a high-priority preference for an unlimited-slot job — will fall back
/// to the overflow job (Passenger) instead. That is exactly what these tests assert did NOT happen.
/// </summary>
[TestFixture]
public sealed class SolreignSecretRoundstartRegressionTest : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";
    private static readonly ProtoId<JobPrototype> Engineer = "StationEngineer";

    private const string MapId = "SolreignSecretRegressionTestMap";

    // A deterministic weighted table pointed 100% at SolreignAntagsOnSpawn, so the
    // AntagsOnSpawn-specific regression test doesn't depend on the 35% roll in the real
    // SolreignSecret table (Resources/Prototypes/_Solreign/GameRules/secret_solreign.yml).
    private const string ForceAntagsOnSpawnWeights = "SolreignSecretRegressionForceAntagsOnSpawn";

    [TestPrototypes]
    private static readonly string TestPrototypesYaml = $@"
- type: gameMap
  id: {MapId}
  mapName: {MapId}
  mapPath: /Maps/Test/empty.yml
  minPlayers: 0
  stations:
    Empty:
      stationProto: StandardNanotrasenStation
      components:
        - type: StationNameSetup
          mapNameTemplate: ""Empty""
        - type: StationJobs
          availableJobs:
            {Passenger}: [ -1, -1 ]
            {Engineer}: [ -1, -1 ]

- type: weightedRandom
  id: {ForceAntagsOnSpawnWeights}
  weights:
    SolreignAntagsOnSpawn: 1.0
";

    public override PoolSettings PoolSettings => new()
    {
        Dirty = true,
        DummyTicker = false,
        Connected = true,
        InLobby = true,
    };

    /// <summary>
    /// Gives every ready session a real (non-Passenger) high-priority job preference, sets the
    /// server up to the given secret weight table, forces the "Secret" preset, and starts the
    /// round. Returns the set of connected user ids for the caller to assert on.
    /// </summary>
    /// <param name="secretWeightPrototype">The weighted table to point <c>game.secret_weight_prototype</c> at.</param>
    /// <param name="extraDummies">How many dummy sessions to add on top of the primary client.</param>
    /// <param name="optIntoTraitor">
    /// When true, every session (including the primary client) opts into the "Traitor" antag
    /// preference before ready-up. This is required to actually exercise
    /// SolreignTraitorOnSpawn's real antag-selection/entity-initialization code path
    /// (<see cref="Content.Server.Antag.AntagSelectionSystem"/>'s <c>PreAssignAntag</c> /
    /// <c>TryInitializeAntag</c>) — sessions with no antag preference bail out of that pathway
    /// at the very first preference check and never reach it, same as real players who never
    /// opted into Traitor.
    /// </param>
    private async Task<NetUserId[]> ReadyAndStartUnderSecret(string secretWeightPrototype, int extraDummies, bool optIntoTraitor = false)
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();

        server.CfgMan.SetCVar(CCVars.GameMap, MapId);
        server.CfgMan.SetCVar(CCVars.SecretWeightPrototype, secretWeightPrototype);

        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby));

        await server.AddDummySessions(extraDummies);
        await pair.RunTicksSync(5);

        var users = server.PlayerMan.Sessions.Select(x => x.UserId).ToArray();
        Assert.That(users, Has.Length.EqualTo(extraDummies + 1));

        // Every player prefers the unlimited-slot Engineer job over Passenger. If AssignJobs
        // works, everyone should end up as Engineer, not Passenger.
        foreach (var user in users)
        {
            await pair.SetJobPriorities(user, (Engineer, JobPriority.High), (Passenger, JobPriority.Low));

            if (optIntoTraitor)
                await pair.SetAntagPreference("Traitor", true, user);
        }

        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("Secret");
            Assert.That(ticker.Preset, Is.Not.Null, "Failed to resolve the 'Secret' game preset.");
            ticker.ToggleReadyAll(true);
            Assert.That(ticker.PlayerGameStatuses.Values.All(x => x == PlayerGameStatus.ReadyToPlay));
            ticker.StartRound();
        });
        await pair.RunTicksSync(15);

        return users;
    }

    private void AssertEveryoneGotRealJobs(NetUserId[] users)
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var jobSys = server.System<SharedJobSystem>();
        var mindSys = server.System<MindSystem>();

        // The round must have actually started (no exception aborted / restarted it).
        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));

        Assert.Multiple(() =>
        {
            foreach (var user in users)
            {
                Assert.That(ticker.PlayerGameStatuses[user], Is.EqualTo(PlayerGameStatus.JoinedGame),
                    $"{user} never joined the game (stuck in lobby / no job available).");

                var session = server.PlayerMan.SessionsDict.GetValueOrDefault(user);
                Assert.That(session, Is.Not.Null);

                var uid = session!.AttachedEntity;
                Assert.That(server.EntMan.EntityExists(uid), $"{user} was never spawned into a body.");

                var mind = mindSys.GetMind(uid!.Value);
                Assert.That(server.EntMan.EntityExists(mind), $"{user} has no mind.");
                Assert.That(jobSys.MindTryGetJobId(mind, out var actualJob), $"{user} has no job role at all.");

                // The regression: everyone silently falling back to the overflow job despite a
                // high-priority preference for an unlimited-slot job that should have won.
                Assert.That(actualJob, Is.EqualTo(Engineer),
                    $"{user} was assigned '{actualJob}' instead of their high-priority '{Engineer}' pick — " +
                    "this is the v13.4 SolreignSecret 'everyone spawns Passenger' regression.");
            }
        });
    }

    /// <summary>
    /// Repro for the reported incident: start a round under the real production
    /// "SolreignSecret" weighted table (Traitor/SolreignAntagsOnSpawn/Survival/KesslerSyndrome/
    /// SolreignChangelingPreset — the proper gamePreset wrapper; see secret_solreign.yml) with several
    /// players, and confirm normal job assignment happens for the rolled preset (one roll per
    /// test run — not an exhaustive sweep of every preset).
    /// </summary>
    [Test]
    public async Task SolreignSecret_RealWeights_AssignsRealJobsNotAllPassenger()
    {
        var users = await ReadyAndStartUnderSecret("SolreignSecret", extraDummies: 9);
        AssertEveryoneGotRealJobs(users);

        await Pair.Server.WaitPost(() => Pair.Server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Deterministically forces the SolreignSecret roll onto SolreignAntagsOnSpawn — the preset
    /// flagged as the prime suspect for the incident (higher antag ratio, PrePlayerSpawn antag
    /// selection, SolreignSubGamemodesChaos, SolreignCorporate, and the event schedulers all
    /// stacking on top of normal roundstart job assignment). Confirms job assignment still works
    /// when *specifically* routed there through SecretRuleSystem, not through a direct
    /// `setgamepreset SolreignAntagsOnSpawn` (which AllGamePresetsStartTest already covers and
    /// does not reproduce this incident).
    /// </summary>
    [Test]
    public async Task SolreignSecret_ForcedAntagsOnSpawn_AssignsRealJobsNotAllPassenger()
    {
        var users = await ReadyAndStartUnderSecret(ForceAntagsOnSpawnWeights, extraDummies: 9);
        AssertEveryoneGotRealJobs(users);

        await Pair.Server.WaitPost(() => Pair.Server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Same as <see cref="SolreignSecret_ForcedAntagsOnSpawn_AssignsRealJobsNotAllPassenger"/>, but
    /// every player has opted into the "Traitor" antag preference — matching real veteran players
    /// on a live server, unlike bare dummy sessions with no antag preferences. This actually drives
    /// SolreignTraitorOnSpawn's higher antag ratio (playerRatio 4 vs upstream's 10) through
    /// AntagSelectionSystem's real pre-spawn selection + post-spawn delayed-antag-initialization
    /// pipeline, which the other two tests above never touch (no session has any antag preference,
    /// so <c>PreAssignAntag</c> bails out before reaching antag-entity creation for any of them).
    /// </summary>
    [Test]
    public async Task SolreignSecret_ForcedAntagsOnSpawn_TraitorPreferring_AssignsRealJobsNotAllPassenger()
    {
        var users = await ReadyAndStartUnderSecret(ForceAntagsOnSpawnWeights, extraDummies: 9, optIntoTraitor: true);
        AssertEveryoneGotRealJobs(users);

        await Pair.Server.WaitPost(() => Pair.Server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Pins the SolreignCorporate singleton guard (cdx re-review 2026-07-19): under
    /// SolreignAntagsOnSpawn — a preset that already lists SolreignCorporate in its own
    /// <c>rules:</c> — the corporate layer's RoundStartingEvent hook must NOT add a second,
    /// independent SolreignCorporate rule entity (the duplicate fired the HR keynote twice and
    /// double-credited the Season Ledger via a second SubmitRoundStandings at round end).
    /// Without the <c>IsGameRuleAdded</c> guard in SolreignCorporateLayerSystem this asserts 2.
    /// </summary>
    [Test]
    public async Task SolreignSecret_ForcedAntagsOnSpawn_CorporateLayerRuleIsSingleton()
    {
        await ReadyAndStartUnderSecret(ForceAntagsOnSpawnWeights, extraDummies: 4);

        var server = Pair.Server;
        var ticker = server.System<GameTicker>();
        await server.WaitAssertion(() =>
        {
            var corporateRules = ticker.GetAddedGameRules()
                .Count(uid => server.EntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "SolreignCorporate");
            Assert.That(corporateRules, Is.EqualTo(1),
                "expected exactly one added SolreignCorporate rule entity — either the singleton " +
                "guard regressed (2+) or the preset/layer stopped adding the corporate rule at all (0)");
        });

        await server.WaitPost(() => ticker.RestartRound());
    }
}
