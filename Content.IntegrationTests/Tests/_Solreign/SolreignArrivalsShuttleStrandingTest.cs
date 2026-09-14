#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     v13.5 regression: "loadgate x arrivals stranding". <see cref="ArrivalsSystem.HandlePlayerSpawning"/>
///     used to spawn every InRound late joiner at the GLOBAL arrivals terminal as long as SOME arrivals
///     source existed, without checking whether the SPECIFIC station they're joining actually has a
///     working arrivals shuttle right now. <see cref="ArrivalsSystem.SetupShuttle"/> deliberately
///     swallows a per-station shuttle load/dock failure (so one broken station doesn't abort the whole
///     round) — which means a late joiner directed at that station could be placed at the arrivals
///     terminal with no shuttle ever scheduled to fetch them: permanently off-station, no way in.
///     <para>
///     Two cases, matching the fix: (a) arrivals still works normally end-to-end when the station's
///     shuttle is healthy; (b) when this station's shuttle is gone/unusable, a late joiner falls back to
///     a normal on-station spawn instead of being marooned. Case (b) removes the shuttle entity the same
///     way a failed/aborted shuttle setup leaves <c>StationArrivalsComponent.Shuttle</c> — Deleted — and
///     is driven through the EXACT real trigger the review flagged: a load-gated player readied via
///     mass-ready (<see cref="GameTicker.ToggleReadyAll"/>) whose preference load completes after round
///     start, which <see cref="GameTicker.RoundFlow"/>'s fix spawns as a genuine late join.
///     </para>
/// </summary>
[TestFixture]
public sealed class SolreignArrivalsShuttleStrandingTest : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";
    private static readonly ProtoId<JobPrototype> Engineer = "StationEngineer";

    private const string HeldPlayerName = "solreign-held-prefs-arrivals";

    private static readonly string MapId = "SolreignArrivalsShuttleStrandingMap";

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
        Dirty = true,
    };

    /// <summary>
    /// Baseline / guardrail: with a healthy station shuttle, a normal late joiner still spawns at
    /// arrivals as before. Guards against the new shuttle-usability check being too aggressive.
    /// </summary>
    [Test]
    public async Task LateJoinWithWorkingStationShuttleSpawnsAtArrivals()
    {
        var pair = Pair;
        var ticker = pair.Server.System<GameTicker>();

        pair.Server.CfgMan.SetCVar(CCVars.GameMap, MapId);
        // ArrivalsShuttles' OnValueChanged handler touches entities/maps directly and must run on the
        // server's main thread, unlike a plain data CVar like GameMap.
        await pair.Server.WaitPost(() => pair.Server.CfgMan.SetCVar(CCVars.ArrivalsShuttles, true));

        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby));

        // Connect the late joiner BEFORE round start (fully loads, stays NotReadyToPlay so they're not
        // part of the round-start spawn batch) — connecting a brand-new session mid-round hits an
        // unrelated pre-existing DB race in GameTicker.Player.cs's AddPlayerToDb (Player DB row not yet
        // committed when AddRoundPlayers looks it up), which is out of scope for this fix.
        var lateJoiner = await pair.Server.AddDummySession("solreign-late-working-shuttle");
        await pair.RunTicksSync(5);

        // Normal round start with just the pool's connected client; the late joiner stays behind.
        ticker.ToggleReadyAll(true);
        Assert.That(ticker.PlayerGameStatuses[lateJoiner.UserId], Is.EqualTo(PlayerGameStatus.ReadyToPlay),
            "ToggleReadyAll mass-readies everyone, including the late joiner -- explicitly un-ready them below");
        await pair.Server.WaitPost(() => ticker.ToggleReady(lateJoiner, false));
        await pair.Server.WaitPost(() => ticker.StartRound());
        await pair.RunTicksSync(15);
        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound), "round did not enter InRound");
        Assert.That(lateJoiner.AttachedEntity, Is.Null, "late joiner must not have been spawned at round start");

        // Now send them in via the normal join-game path -- a genuine late join.
        await pair.Server.WaitPost(() => ticker.MakeJoinGame(lateJoiner, EntityUid.Invalid));
        await pair.RunTicksSync(10);

        var uid = lateJoiner.AttachedEntity;
        Assert.That(pair.Server.EntMan.EntityExists(uid), "late joiner never got an entity");
        Assert.That(pair.Server.EntMan.HasComponent<PendingClockInComponent>(uid!.Value), Is.True,
            "with a healthy station shuttle, a late joiner must still spawn at arrivals (regression guard).");

        await pair.Server.WaitPost(() => ticker.RestartRound());
    }

    /// <summary>
    /// THE FIX: a late joiner directed at a station whose arrivals shuttle is unusable must NOT be
    /// stranded at the arrivals terminal — they fall back to a normal on-station spawn. The shuttle is
    /// removed after a normal, successful setup — leaving <c>StationArrivalsComponent.Shuttle</c>
    /// Deleted, the exact same observable state a swallowed <see cref="ArrivalsSystem.SetupShuttle"/>
    /// load/dock failure leaves behind — so this exercises the fix's guard directly without needing a
    /// broken map file (which would also log an expected-but-noisy Error the harness would flag).
    /// Driven through the real trigger: a mass-readied player whose preference load completes after
    /// round start (GameTicker's ready-config no-retry fix), which becomes a genuine late join.
    /// </summary>
    [Test]
    public async Task MassReadyLateLoadPlayerWithGoneStationShuttleDoesNotStrand()
    {
        var pair = Pair;
        var ticker = pair.Server.System<GameTicker>();
        var userDb = pair.Server.ResolveDependency<UserDbDataManager>();

        pair.Server.CfgMan.SetCVar(CCVars.GameMap, MapId);
        // ArrivalsShuttles' OnValueChanged handler touches entities/maps directly and must run on the
        // server's main thread, unlike a plain data CVar like GameMap.
        await pair.Server.WaitPost(() => pair.Server.CfgMan.SetCVar(CCVars.ArrivalsShuttles, true));

        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby));

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

        // (No explicit job-preference setup here — ServerPreferencesManager.SetProfile refuses writes
        // until the player's load fully completes, and this test only cares whether the fix's on-station
        // fallback avoids arrivals, not which specific job they land. The default profile alone is
        // enough to exercise the spawn path; job-honoring on a late load is covered separately by
        // SolreignMassReadyLateLoadRetryTest.)

        // Mass-ready bypass: everyone (including the still-loading held player) becomes ReadyToPlay.
        ticker.ToggleReadyAll(true);
        await pair.Server.WaitPost(() => ticker.StartRound());
        await pair.RunTicksSync(15);
        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound), "round did not enter InRound");

        // Held player correctly skipped, not yet spawned.
        Assert.That(heldSession.AttachedEntity, Is.Null, "held player must not be spawned before their load completes");

        // Now knock out THIS station's arrivals shuttle, post-setup, leaving
        // StationArrivalsComponent.Shuttle Deleted — exactly what a swallowed SetupShuttle failure
        // leaves behind, just reached a different way (no bogus map file / expected error log needed).
        await pair.Server.WaitPost(() =>
        {
            var query = pair.Server.EntMan.EntityQueryEnumerator<StationArrivalsComponent>();
            while (query.MoveNext(out _, out var comp))
            {
                if (!pair.Server.EntMan.Deleted(comp.Shuttle))
                    pair.Server.EntMan.DeleteEntity(comp.Shuttle);
            }
        });
        await pair.RunTicksSync(5);

        // Release the hold: GameTicker's QueueLateLoadedReadySpawn retry now spawns them as a genuine
        // late join (RunLevel is already InRound) against a station whose arrivals shuttle is gone.
        await pair.Server.WaitPost(() => gate.SetResult());
        await pair.RunTicksSync(20);

        var uid = heldSession.AttachedEntity;
        Assert.That(pair.Server.EntMan.EntityExists(uid),
            "BUG REPRODUCED: held player was never spawned at all after their load completed.");
        Assert.That(pair.Server.EntMan.HasComponent<PendingClockInComponent>(uid!.Value), Is.False,
            "BUG REPRODUCED (loadgate x arrivals stranding): player was placed at the arrivals terminal " +
            "for a station with no usable shuttle — they would be permanently stranded off-station.");

        await pair.Server.WaitPost(() => ticker.RestartRound());
    }
}
