#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.SeasonLedger;
using Content.Shared._Solreign.SeasonLedger;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using NUnit.Framework;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Wave 23 / Phase-3 Tasks 4-5 (docs/plans/2026-07-11-phase3-season-ledger.md,
///     docs/ROADMAP-PHASE3.md Wave 23): the Season Ledger is the fork's moat, but its real write path
///     was only covered by store-level unit tests that construct <see cref="SeasonLedgerStore"/>
///     directly (<c>Content.Tests/_Solreign/SeasonLedgerStoreTests.cs</c>). Nothing fired a
///     <see cref="RoundEndMessageEvent"/> through the live <see cref="SeasonLedgerSystem"/>'s
///     <c>OnRoundEnd</c> handler (async void, swallow-and-continue — the deliberate "never block the
///     tick" design mirrored from <c>GameTicker.SendRoundEndDiscordMessage</c>) —
///     <c>grep SeasonLedger|RoundEndMessageEvent Content.IntegrationTests/</c> returned nothing before
///     this file.
///
///     This fixture drives that path end-to-end:
///       1. Raise a synthetic <see cref="RoundEndMessageEvent"/> on the real event bus and poll the
///          SQLite file (bounded, not Task.Delay-and-pray) until the async write lands.
///       2. Seed the same DB the system opens, re-raise <see cref="PlayerSpawnCompleteEvent"/>, and
///          assert <see cref="SeasonTitleComponent"/> is stamped on the main thread via Update's
///          pending-queue drain (title AND career rank, per the real <c>SeasonLedgerSystem.LoadTitle</c>
///          path — richer than the Phase-3 plan doc's original MVP shape).
///
///     DB path is resolved through <see cref="SeasonLedgerDbPath.Resolve"/> — the exact resolver
///     <c>SeasonLedgerSystem.Initialize</c> uses — so the test always opens the same file the live
///     system writes. In integration tests that path is a unique per-server-instance temp file
///     (<see cref="Content.IntegrationTests.Pair.TestPair.ServerOptions"/> sets
///     <c>CCVars.SolreignSeasonLedgerDbPath</c>), and rows are keyed by fresh GUIDs and fresh random
///     round ids per test run, so neither repeat runs nor other round-ending tests ever collide with
///     the ledger's round-envelope replay protection.
/// </summary>
[TestFixture]
public sealed class SeasonLedgerSystemIntegrationTest : GameTest
{
    // Dirty: this fixture writes directly to the real on-disk ledger DB (outside the entity-cleanup
    // machinery GameTest tracks) and fires synthetic round-end/spawn events the pool cannot safely
    // hand off to another test — same "Dirty=true" precedent as HotPotatoLifecycleIntegrationTest /
    // WerewolfPolymorphTriggerTest for exactly this class of side effect.
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        // DummyTicker=false so the connected session gets a real attached body (HotPotato /
        // ContractClaimFlow precedent) — required for the spawn/title test.
        DummyTicker = false,
    };

    /// <summary>
    ///     Resolves the ledger file through the SAME helper <see cref="SeasonLedgerSystem.Initialize"/>
    ///     uses, so the test opens exactly the file the live system writes (in tests: the unique
    ///     per-instance temp path the pool injects via <c>CCVars.SolreignSeasonLedgerDbPath</c>).
    /// </summary>
    private string ResolveLedgerDbPath()
    {
        var server = Server;
        return SeasonLedgerDbPath.Resolve(
            server.ResolveDependency<IConfigurationManager>(),
            server.ResolveDependency<IResourceManager>());
    }

    /// <summary>
    ///     Fresh round id per test run, same discipline as the fresh player GUIDs: the ledger's
    ///     round-envelope replay protection treats a repeated round id as a conflict
    ///     (StorageFailure), so a hardcoded id breaks the moment the same db sees a second run.
    /// </summary>
    private static int UniqueRoundId()
    {
        // Upper bound leaves headroom for callers that use a small consecutive block (base + i)
        // without overflowing int.
        return Random.Shared.Next(100_000, int.MaxValue - 16);
    }

    /// <summary>
    ///     Bounded poll for async-void completion. Advances the server by one tick between attempts so
    ///     <see cref="SeasonLedgerSystem.Update"/> can drain the pending-title queue (title test) while
    ///     also giving SQLite writers wall-clock time (round-end test). Never sleep-and-pray.
    /// </summary>
    private static async Task PollUntilAsync(
        RobustIntegrationTest.ServerIntegrationInstance server,
        Func<Task<bool>> predicate,
        string failureMessage,
        int maxAttempts = 150)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (await predicate())
                return;

            await server.WaitRunTicks(1);
        }

        Assert.Fail(failureMessage);
    }

    private static RoundEndMessageEvent.RoundEndPlayerInfo MakePlayerInfo(
        Guid userId,
        string oocName,
        string? job = "Passenger",
        bool antag = false,
        bool connected = true)
    {
        return new RoundEndMessageEvent.RoundEndPlayerInfo
        {
            PlayerOOCName = oocName,
            PlayerICName = oocName,
            PlayerGuid = new NetUserId(userId),
            Role = job ?? "Passenger",
            JobPrototypes = job is null ? Array.Empty<string>() : new[] { job },
            AntagPrototypes = Array.Empty<string>(),
            PlayerNetEntity = null,
            Antag = antag,
            Observer = false,
            Connected = connected,
        };
    }

    [Test]
    public async Task RoundEnd_ThroughRealOnRoundEnd_PersistsRowsToSqlite()
    {
        var server = Server;
        var entMan = server.EntMan;
        var dbPath = ResolveLedgerDbPath();

        // Force system discovery so Initialize has run and the store file path is live.
        await server.WaitPost(() => _ = server.System<SeasonLedgerSystem>());

        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var roundId = UniqueRoundId();
        const string gamemode = "Wave23LedgerIntegration";

        await server.WaitPost(() =>
        {
            var players = new[]
            {
                MakePlayerInfo(alice, "LedgerAlice", job: "Captain", antag: false, connected: true),
                MakePlayerInfo(bob, "LedgerBob", job: "Passenger", antag: true, connected: true),
            };

            var ev = new RoundEndMessageEvent(
                gamemodeTitle: gamemode,
                roundEndText: "Wave 23 synthetic round end",
                roundDuration: TimeSpan.FromMinutes(15),
                roundId: roundId,
                playerCount: players.Length,
                allPlayersEndInfo: players,
                restartSound: null);

            // Same broadcast path GameTicker uses after building the roster
            // (RaiseLocalEvent(roundEndMessageEvent) -> EventSource.Local).
            entMan.EventBus.RaiseEvent(EventSource.Local, ev);
        });

        // OnRoundEnd is async void: snapshot happens on the main thread, then the store write awaits
        // SQLite I/O off-tick. Poll the real file until both accounts land (or the bound expires).
        await PollUntilAsync(
            server,
            async () =>
            {
                if (!File.Exists(dbPath) || new FileInfo(dbPath).Length == 0)
                    return false;

                var store = new SeasonLedgerStore(dbPath);
                var aliceStats = await store.GetStatsAsync(alice);
                var bobStats = await store.GetStatsAsync(bob);
                return aliceStats.Tours >= 1 && bobStats.Tours >= 1;
            },
            $"SeasonLedgerSystem.OnRoundEnd did not persist rows for both players within the poll bound. " +
            $"Expected player_stats rows for {alice:N} and {bob:N} in {dbPath}.");

        var finalStore = new SeasonLedgerStore(dbPath);
        var aliceFinal = await finalStore.GetStatsAsync(alice);
        var bobFinal = await finalStore.GetStatsAsync(bob);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(dbPath), Is.True, "Ledger DB file must exist after round end.");
                Assert.That(new FileInfo(dbPath).Length, Is.GreaterThan(0), "Ledger DB must be non-empty.");

                Assert.That(aliceFinal.Tours, Is.GreaterThanOrEqualTo(1),
                    "Alice's captain-connected contribution must increment tours via the real OnRoundEnd path.");
                Assert.That(aliceFinal.CaptainClean, Is.GreaterThanOrEqualTo(1),
                    "Alice was a Connected Captain -- captain_clean must fold in (SeasonLedgerSystem.WasCaptain).");

                Assert.That(bobFinal.Tours, Is.GreaterThanOrEqualTo(1),
                    "Bob's antag contribution must increment tours.");
                Assert.That(bobFinal.AntagWins, Is.GreaterThanOrEqualTo(1),
                    "Bob had Antag=true -- antag_wins must fold in.");
            });
        });
    }

    [Test]
    public async Task PlayerSpawnComplete_AppliesSeasonTitleFromLedger()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var dbPath = ResolveLedgerDbPath();

        await server.WaitPost(() => _ = server.System<SeasonLedgerSystem>());

        EntityUid mob = default;
        ICommonSession session = default!;
        Guid userGuid = default;

        await server.WaitPost(() =>
        {
            session = playerMan.Sessions[0];
            mob = session.AttachedEntity
                  ?? throw new InvalidOperationException(
                      "Connected player has no AttachedEntity -- DummyTicker must be false.");
            userGuid = session.UserId.UserId;
        });

        // Seed the SAME SQLite file the live system opens (per-instance temp path, resolved through
        // the production helper). Three clean captaincies unlock TitleRules.Compute's
        // "Brand Ambassador" (CaptainClean >= 3, top of the priority ladder). Fresh round ids per
        // run — replay protection rejects a repeated (round id, account) envelope.
        var seedStore = new SeasonLedgerStore(dbPath);
        var seedRoundBase = UniqueRoundId();
        for (var i = 0; i < 3; i++)
        {
            await seedStore.AddRoundRecordAsync(
                userGuid,
                new RoundContribution(
                    WasCaptainClean: true,
                    AntagWin: false,
                    EarlyDeath: false,
                    RoundId: seedRoundBase + i,
                    Gamemode: "Wave23TitleSeed"));
        }

        var seeded = await seedStore.GetStatsAsync(userGuid);
        Assert.That(seeded.CaptainClean, Is.GreaterThanOrEqualTo(3),
            "Setup failed: seed must leave the account with >= 3 clean captaincies before the real spawn hook fires.");

        // Re-fire the real spawn hook. LoadTitle is async void -> enqueues a PendingTitle -> Update
        // applies SeasonTitleComponent (title, tours, AND career rank) on the main thread.
        await server.WaitPost(() =>
        {
            var spawnEv = new PlayerSpawnCompleteEvent(
                mob,
                session,
                jobId: "Passenger",
                lateJoin: false,
                silent: true,
                joinOrder: 1,
                station: EntityUid.Invalid,
                profile: new HumanoidCharacterProfile());

            // Directed + broadcast -- matches GameTicker.Spawning's RaiseLocalEvent(mob, aev, true).
            entMan.EventBus.RaiseLocalEvent(mob, spawnEv, broadcast: true);
        });

        await PollUntilAsync(
            server,
            async () =>
            {
                var applied = false;
                await server.WaitPost(() =>
                {
                    if (!entMan.TryGetComponent<SeasonTitleComponent>(mob, out var comp))
                        return;

                    applied = comp.Title == "Brand Ambassador" && comp.Tours >= 3;
                });
                return applied;
            },
            $"SeasonTitleComponent was not stamped with Brand Ambassador within the poll bound " +
            $"(mob={mob}, user={userGuid:N}, db={dbPath}).");

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.TryGetComponent<SeasonTitleComponent>(mob, out var title), Is.True,
                "LoadTitle -> Update must EnsureComp<SeasonTitleComponent> on the mob.");
            Assert.Multiple(() =>
            {
                Assert.That(title!.Title, Is.EqualTo("Brand Ambassador"),
                    "Three clean captaincies must compute Brand Ambassador via TitleRules.Compute.");
                Assert.That(title.Tours, Is.GreaterThanOrEqualTo(3),
                    "Tour count stamped on the component must reflect the seeded ledger stats.");
                Assert.That(title.Rank, Is.Not.Null.And.Not.Empty,
                    "Career rank must also be stamped, from GetCareerStatsAsync -> RankProgression.ComputeCareerRank.");
            });
        });
    }
}
