#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Mind;
using Content.Server.Preferences.Managers;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     v13.5 regression: the "ready-config no-retry gap". Two ready paths exist —
///     <c>game.lobbyenabled=false</c> gets an automatic post-load retry via SpawnWaitDb
///     (<see cref="GameTicker"/>.Player.cs), but <c>lobbyenabled=true</c> readied through a mass-ready
///     (<see cref="GameTicker.ToggleReadyAll"/>) had NONE: <see cref="GameTicker.StartRound"/>'s
///     load-gate correctly skips a player whose user-DB/preferences load is still in flight (so they
///     don't get force-spawned with a Random()/overflow Passenger profile — the v13.4 incident), but
///     nothing then re-attempted the spawn once their load actually finished. That player would sit
///     showing <see cref="PlayerGameStatus.ReadyToPlay"/> forever with no automatic way into the round.
///     <para>
///     This drives that exact race for real: a dummy session is connected with an artificial hold on
///     its overall user-DB load-completion gate (registered via <see cref="UserDbDataManager.AddOnLoadPlayer"/>)
///     while its job PREFERENCE is set normally in parallel (preferences load independently of the
///     overall gate, exactly like the live incident where cached prefs can exist before the broader
///     load task resolves). The player is mass-readied and the round started while still held, then the
///     hold is released — asserting the fix's <c>QueueLateLoadedReadySpawn</c> retry spawns them
///     automatically with their real (Engineer) job, not Passenger, and not never.
///     </para>
/// </summary>
[TestFixture]
public sealed class SolreignMassReadyLateLoadRetryTest : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";
    private static readonly ProtoId<JobPrototype> Engineer = "StationEngineer";

    private const string HeldPlayerName = "solreign-held-prefs-massready";

    private static readonly string MapId = "SolreignMassReadyLateLoadMap";

    [TestPrototypes]
    private static readonly string TestMapPrototype = $@"
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
";

    public override PoolSettings PoolSettings => new()
    {
        DummyTicker = false,
        Connected = true,
        InLobby = true,
        // A real round-start mutates enough global engine state that this pair must never be
        // recycled — same discipline as the other Solreign round-start regressions.
        Dirty = true,
    };

    [Test]
    public async Task MassReadyPlayerWithLateLoadCompletionStillSpawnsWithRealJob()
    {
        var pair = Pair;
        var ticker = pair.Server.System<GameTicker>();
        var jobSys = pair.Server.System<SharedJobSystem>();
        var mindSys = pair.Server.System<MindSystem>();
        var userDb = pair.Server.ResolveDependency<UserDbDataManager>();

        pair.Server.CfgMan.SetCVar(CCVars.GameMap, MapId);
        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby));

        // Establish the held player's REAL preference first, through a normal, fully-loaded
        // connection (ServerPreferencesManager.SetProfile refuses writes until PrefsLoaded, so this
        // cannot be done while the load-hold below is active). Robust's dummy sessions default to
        // LoginType.GuestAssigned, which HasStaticUserId() -> preferences ARE persisted for them, so
        // this profile survives the disconnect/reconnect below exactly like a returning account.
        var setupSession = await pair.Server.AddDummySession(HeldPlayerName);
        await pair.RunTicksSync(5);
        await pair.SetJobPriorities(setupSession.UserId, (Passenger, JobPriority.Medium), (Engineer, JobPriority.High));
        await pair.Server.RemoveDummySession(setupSession);
        await pair.RunTicksSync(5);

        // Now hold the held player's OVERALL user-DB load-completion gate open (what IsLoadComplete /
        // WaitLoadComplete track) for their NEXT (round-start) connection, while everything else about
        // the reconnection proceeds normally. This mirrors a real slow on-load hook (ban lookup,
        // playtime tracking, etc.) racing round start while their character preferences (a SEPARATE,
        // parallel on-load hook that re-fetches their now-persisted profile from the DB) are already
        // cached.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await pair.Server.WaitPost(() =>
        {
            userDb.AddOnLoadPlayer((session, _) =>
                session.Name == HeldPlayerName ? gate.Task : Task.CompletedTask);
        });

        var heldSession = await pair.Server.AddDummySession(HeldPlayerName);
        await pair.RunTicksSync(5);

        Assert.That(userDb.IsLoadComplete(heldSession), Is.False,
            "test precondition: the held player's overall load gate must still be open");
        Assert.That(pair.Server.ResolveDependency<IServerPreferencesManager>()
                .TryGetCachedPreferences(heldSession.UserId, out var heldPrefs), Is.True,
            "test precondition: the held player's preferences must already be cached (parallel on-load hook), " +
            "even though their overall load gate is still open");
        var heldJobPriorities = ((HumanoidCharacterProfile)heldPrefs!.SelectedCharacter).JobPriorities;
        Assert.That(heldJobPriorities.TryGetValue(Engineer, out var heldEngineerPriority) && heldEngineerPriority == JobPriority.High,
            "test precondition: the re-fetched cached profile must carry the persisted Engineer preference");

        // Mass-ready: marks EVERY session ReadyToPlay, including the still-loading one, without the
        // per-player IsLoadComplete check that single-player ToggleReady enforces — the bypass path.
        ticker.ToggleReadyAll(true);
        Assert.That(ticker.PlayerGameStatuses[heldSession.UserId], Is.EqualTo(PlayerGameStatus.ReadyToPlay));

        await pair.Server.WaitPost(() => ticker.StartRound());
        await pair.RunTicksSync(10);

        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound), "round did not enter InRound");

        // Immediately after StartRound: the held player must have been skipped, not force-spawned
        // with a Random/overflow (Passenger) profile — the v13.4 fingerprint.
        Assert.That(ticker.PlayerGameStatuses[heldSession.UserId], Is.EqualTo(PlayerGameStatus.ReadyToPlay),
            "held player must remain un-spawned until their load completes");
        Assert.That(heldSession.AttachedEntity, Is.Null,
            "held player must not have an entity yet — they have not been spawned");

        // Release the hold — their load now completes, exactly like a slow query finally resolving
        // after round start. THE ASSERTION: this alone must be enough to spawn them with their real
        // (Engineer) job. Before the fix, nothing observed this completion and they stayed stuck.
        await pair.Server.WaitPost(() => gate.SetResult());
        await pair.RunTicksSync(20);

        Assert.That(ticker.PlayerGameStatuses[heldSession.UserId], Is.EqualTo(PlayerGameStatus.JoinedGame),
            "BUG REPRODUCED (ready-config no-retry gap): the held player's load completed but they " +
            "were never automatically spawned.");

        var uid = heldSession.AttachedEntity;
        Assert.That(pair.Server.EntMan.EntityExists(uid), "held player never got an entity after their load completed");
        var mind = mindSys.GetMind(uid!.Value);
        Assert.That(pair.Server.EntMan.EntityExists(mind));
        Assert.That(jobSys.MindTryGetJobId(mind, out var actualJob));
        Assert.That(actualJob, Is.EqualTo(Engineer),
            $"held player's Engineer preference was not honored on the late-load retry spawn (got '{actualJob}').");

        await pair.Server.WaitPost(() => ticker.RestartRound());
    }
}
