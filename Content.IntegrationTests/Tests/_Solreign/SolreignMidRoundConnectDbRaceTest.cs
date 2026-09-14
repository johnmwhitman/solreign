#nullable enable
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Shared.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Configuration;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Serialization.Manager;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     v13.4 regression: a brand-new player connecting to a round already IN PROGRESS could crash the
///     whole round. <see cref="GameTicker.PlayerStatusChanged"/>'s local <c>AddPlayerToDb</c> function
///     fires on <see cref="Robust.Shared.Enums.SessionStatus.Connected"/> and, once a round has started
///     (<c>RunLevel != PreRoundLobby</c>), calls <see cref="Content.Server.Database.IServerDbManager"/>'s
///     <c>AddRoundPlayers</c> for the connecting user's id. <c>ServerDbBase.AddRoundPlayers</c> looks that
///     id up in a <c>Dictionary&lt;Guid, int&gt;</c> built from the Player table and uses the plain
///     indexer (<c>players[player]</c>) -- if this brand new user's Player row has not been
///     created/committed yet, that throws <see cref="System.Collections.Generic.KeyNotFoundException"/>.
///     The caller, <c>AddPlayerToDb</c>, is declared <c>async void</c>, so nothing observes that
///     exception -- it escapes onto the server's main-thread synchronization context and (outside of
///     <c>EXCEPTION_TOLERANCE</c> release/tools builds, i.e. in normal dev/test builds) crashes the
///     server instance outright.
///     <para>
///     This only bites a genuinely new connection: joining BEFORE round start is safe, because
///     <c>AddPlayerToDb</c> early-returns while <c>RunLevel == PreRoundLobby</c> and never touches the
///     DB. Connecting after <see cref="GameTicker.StartRound"/> (<c>RunLevel == InRound</c>) is what
///     hits the missing-row lookup.
///     </para>
///     <para>
///     A follow-up merge-safety review found the original fix (blank-placeholder insert + broad
///     <c>catch (DbUpdateException)</c>) traded the crash for quieter bugs: the placeholder row silently
///     poisoned ban-audit/"is this a new player" data, the same race could still leave
///     <c>UpdatePlayerRecord</c>'s authoritative write stranded behind a duplicate-key failure, and the
///     broad catch could mask unrelated DB errors. <see cref="SolreignMidRoundConnectDbRaceRecoveryTest"/>
///     covers those fixes with deterministic (non-timing-dependent) reproductions of both race orderings.
///     </para>
/// </summary>
[TestFixture]
public sealed class SolreignMidRoundConnectDbRaceTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        DummyTicker = false,
        Connected = true,
        InLobby = true,
        Dirty = true,
    };

    /// <summary>
    /// Baseline / guardrail: connecting a brand-new session BEFORE the round starts must never hit the
    /// DB race -- AddPlayerToDb early-returns during PreRoundLobby.
    /// </summary>
    [Test]
    public async Task NewSessionConnectingBeforeRoundStartDoesNotCrash()
    {
        var pair = Pair;
        var ticker = pair.Server.System<GameTicker>();

        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby));

        var joiner = await pair.Server.AddDummySession("solreign-prelobby-new-connect");
        await pair.RunTicksSync(10);

        Assert.That(pair.Server.UnhandledException, Is.Null,
            "connecting before round start must never crash the server.");
        Assert.That(pair.Server.IsAlive, Is.True);

        await pair.Server.WaitPost(() => ticker.RestartRound());
    }

    /// <summary>
    /// THE BUG: a brand-new player connecting AFTER <see cref="GameTicker.StartRound"/> (RunLevel ==
    /// InRound) crashes the server, because their Player DB row does not exist yet when
    /// <c>AddRoundPlayers</c> looks it up.
    /// </summary>
    [Test]
    public async Task NewSessionConnectingMidRoundDoesNotCrash()
    {
        var pair = Pair;
        var ticker = pair.Server.System<GameTicker>();

        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby));

        await pair.Server.WaitPost(() => ticker.StartRound());
        await pair.RunTicksSync(15);
        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound), "round did not enter InRound");
        Assert.That(ticker.RoundId, Is.Not.EqualTo(0));

        // A genuinely brand-new session (never seen before by this server/test pair) connecting after
        // round start. This is the exact "friend joins after the round already started" scenario.
        var joiner = await pair.Server.AddDummySession("solreign-midround-new-connect");
        await pair.RunTicksSync(15);

        Assert.That(pair.Server.UnhandledException, Is.Null,
            "BUG REPRODUCED (mid-round connect DB race): a brand-new player connecting mid-round " +
            $"crashed the server. UnhandledException: {pair.Server.UnhandledException}");
        Assert.That(pair.Server.IsAlive, Is.True,
            "BUG REPRODUCED (mid-round connect DB race): the server instance died after a brand-new " +
            "player connected mid-round.");

        // The fix must not just avoid crashing -- the round-player association has to actually be
        // recorded once the player's row exists, not silently dropped on the fallback path.
        var dbMan = pair.Server.ResolveDependency<IServerDbManager>();
        var round = await dbMan.GetRound(ticker.RoundId);
        Assert.That(round.Players.Any(p => p.UserId == joiner.UserId.UserId), Is.True,
            "the mid-round joiner was never linked to the round in the database " +
            "(round-player association silently dropped).");

        await pair.Server.WaitPost(() => ticker.RestartRound());
    }
}

/// <summary>
///     Deterministic (non-timing-dependent) reproductions of both possible winner orderings of the
///     <c>AddRoundPlayers</c> / <c>UpdatePlayerRecord</c> insert race described on
///     <see cref="SolreignMidRoundConnectDbRaceTest"/>, added for the merge-safety review's requirement
///     that the race be tested in both directions.
///     <para>
///     Why this can't just spin up two concurrent dummy sessions on the shared integration-test server and
///     hope they interleave: the standard <see cref="GameTest"/> pool runs with
///     <c>database.sync = true</c> (<c>PoolManager.Cvars.cs</c>), and even setting that aside,
///     <see cref="ServerDbSqlite"/> pins in-memory SQLite to a single connection at a time (the
///     <c>inMemory ? 1 : ...</c> concurrency clamp) -- the two writers can never naturally have their reads
///     interleave under that harness, so a wall-clock-timing test here would either never reproduce the
///     race or would be flaky (reproduce it only sometimes), neither of which is acceptable for a battery
///     that has to stay green.
///     </para>
///     <para>
///     Instead, each test here spins up its own standalone <see cref="ServerDbSqlite"/> against a real
///     temp-file database (not <c>:memory:</c>) with <c>synchronous: false</c>, giving genuine
///     multi-connection concurrency (default concurrency 3, see <c>CCVars.DatabaseSqliteConcurrency</c>) --
///     the same file-backed configuration production SQLite deployments use. The actual interleaving is
///     then pinned down deterministically using <see cref="ServerDbBase.TestBeforeAddRoundPlayersInsert"/>
///     / <see cref="ServerDbBase.TestBeforeUpdatePlayerRecordInsert"/>, two test-only hooks that pause one
///     writer immediately after it has confirmed "no row exists yet" and before it saves its own INSERT --
///     letting the test force the other writer to commit first, guaranteeing a real unique-constraint
///     collision on the recovering side instead of hoping timing produces one.
///     </para>
///     <para>
///     <see cref="NonParallelizableAttribute"/>: the two hook fields are static on <see cref="ServerDbBase"/>
///     (shared by every DB engine instance in the process, including the pooled integration-test server's
///     own real database), so this fixture must not run while any other test that might call the real
///     <c>AddRoundPlayers</c>/<c>UpdatePlayerRecord</c> is also running.
///     </para>
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class SolreignMidRoundConnectDbRaceRecoveryTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        DummyTicker = true,
        Connected = false,
    };

    private static readonly IPAddress RealAddress = IPAddress.Parse("203.0.113.42");
    private const string RealUserName = "RaceRealIdentity";

    private static ImmutableTypedHwid RealHwid()
        => new(ImmutableArray.Create<byte>(1, 2, 3, 4, 5, 6, 7, 8), HwidType.Modern);

    /// <summary>
    /// Builds a standalone <see cref="ServerDbSqlite"/> against a fresh temp-file SQLite database, with
    /// real (not <c>:memory:</c>-clamped) multi-connection concurrency. Caller owns cleanup of the
    /// returned path.
    /// </summary>
    private static ServerDbSqlite GetConcurrentDb(RobustIntegrationTest.ServerIntegrationInstance server, out string dbPath)
    {
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var serialization = server.ResolveDependency<ISerializationManager>();
        var opsLog = server.ResolveDependency<ILogManager>().GetSawmill("db.ops.race-test");

        dbPath = Path.Combine(Path.GetTempPath(), $"solreign-midround-race-{Guid.NewGuid():N}.db");
        var path = dbPath;

        DbContextOptions<SqliteServerDbContext> ContextFunc()
        {
            var builder = new DbContextOptionsBuilder<SqliteServerDbContext>();
            builder.UseSqlite(new SqliteConnection($"Data Source={path}"));
            return builder.Options;
        }

        // inMemory: false + synchronous: false => real file-backed concurrency (default concurrency 3).
        return new ServerDbSqlite(ContextFunc, false, cfg, false, opsLog, serialization);
    }

    private static void DeleteDbFile(string dbPath)
    {
        try
        {
            File.Delete(dbPath);
        }
        catch (IOException)
        {
            // Best-effort cleanup; a stray temp file is not worth failing the test over.
        }
    }

    /// <summary>
    /// Ordering A: <c>AddRoundPlayers</c>' row-creation wins the physical INSERT (using the connecting
    /// session's real identity, exactly as GameTicker's callers do). <c>UpdatePlayerRecord</c> -- the
    /// connection handshake's authoritative write -- starts first (so its own "row doesn't exist" check
    /// runs before AddRoundPlayers commits), then loses the insert race and must recover.
    /// </summary>
    [Test]
    public async Task AddRoundPlayersWinsInsert_UpdatePlayerRecordRecovers()
    {
        var pair = Pair;
        var db = GetConcurrentDb(pair.Server, out var dbPath);
        try
        {
            var (server, _) = await db.AddOrGetServer("race-test-server-a");
            var roundId = await db.AddNewRound(server);
            var userId = new NetUserId(Guid.NewGuid());
            var hwid = RealHwid();

            var reachedHook = new TaskCompletionSource();
            var releaseUpdate = new TaskCompletionSource();
            ServerDbBase.TestBeforeUpdatePlayerRecordInsert = async () =>
            {
                reachedHook.TrySetResult();
                await releaseUpdate.Task;
            };

            try
            {
                // Starts UpdatePlayerRecord; it runs its "no row exists" check, then pauses in the hook
                // just before its own SaveChangesAsync.
                var updateTask = db.UpdatePlayerRecord(userId, RealUserName, RealAddress, hwid);
                await reachedHook.Task;

                // AddRoundPlayers runs to completion first, winning the physical INSERT -- with the same
                // real identity GameTicker's callers now supply.
                await db.AddRoundPlayers(roundId, userId, RealUserName, RealAddress, hwid);

                // Release UpdatePlayerRecord: its insert now collides with the row AddRoundPlayers just
                // created, and it must recover instead of throwing.
                releaseUpdate.SetResult();
                Assert.DoesNotThrowAsync(async () => await updateTask,
                    "UpdatePlayerRecord must recover from losing the insert race, not throw.");
            }
            finally
            {
                ServerDbBase.TestBeforeUpdatePlayerRecordInsert = null;
            }

            var record = await db.GetPlayerRecordByUserId(userId, default);
            Assert.That(record, Is.Not.Null);
            Assert.That(record!.LastSeenUserName, Is.EqualTo(RealUserName),
                "the player's row must hold their real username, not a blank placeholder.");
            Assert.That(record.LastSeenAddress, Is.EqualTo(RealAddress),
                "the player's row must hold their real address, not the 0.0.0.0 placeholder sentinel " +
                "(this is exactly the ban-audit / PlayerLocator identity-poisoning bug the review flagged).");
            Assert.That(record.HWId?.Hwid, Is.EqualTo(hwid.Hwid));

            var round = await db.GetRound(roundId);
            Assert.That(round.Players.Any(p => p.UserId == userId.UserId), Is.True,
                "the round-player association must exist even though AddRoundPlayers had to create the row.");
        }
        finally
        {
            DeleteDbFile(dbPath);
        }
    }

    /// <summary>
    /// Ordering B: <c>UpdatePlayerRecord</c> (the connection handshake's authoritative write) wins the
    /// physical INSERT. <c>AddRoundPlayers</c> starts first (so its own "row doesn't exist" check runs
    /// before UpdatePlayerRecord commits), then loses the insert race, must recover, and must not
    /// overwrite the authoritative row that already exists.
    /// </summary>
    [Test]
    public async Task UpdatePlayerRecordWinsInsert_AddRoundPlayersRecoversWithoutOverwriting()
    {
        var pair = Pair;
        var db = GetConcurrentDb(pair.Server, out var dbPath);
        try
        {
            var (server, _) = await db.AddOrGetServer("race-test-server-b");
            var roundId = await db.AddNewRound(server);
            var userId = new NetUserId(Guid.NewGuid());
            var hwid = RealHwid();

            var reachedHook = new TaskCompletionSource();
            var releaseAddRoundPlayers = new TaskCompletionSource();
            ServerDbBase.TestBeforeAddRoundPlayersInsert = async () =>
            {
                reachedHook.TrySetResult();
                await releaseAddRoundPlayers.Task;
            };

            try
            {
                // Starts AddRoundPlayers; it runs its "no row exists" check, then pauses in the hook just
                // before its own SaveChangesAsync.
                var addRoundPlayersTask = db.AddRoundPlayers(roundId, userId, RealUserName, RealAddress, hwid);
                await reachedHook.Task;

                // UpdatePlayerRecord runs to completion first, winning the physical INSERT.
                await db.UpdatePlayerRecord(userId, RealUserName, RealAddress, hwid);

                // Release AddRoundPlayers: its insert now collides with the row UpdatePlayerRecord just
                // created, and it must recover (adopting the existing row) instead of throwing, and it
                // must NOT clobber the authoritative fields UpdatePlayerRecord just wrote.
                releaseAddRoundPlayers.SetResult();
                Assert.DoesNotThrowAsync(async () => await addRoundPlayersTask,
                    "AddRoundPlayers must recover from losing the insert race, not throw.");
            }
            finally
            {
                ServerDbBase.TestBeforeAddRoundPlayersInsert = null;
            }

            var record = await db.GetPlayerRecordByUserId(userId, default);
            Assert.That(record, Is.Not.Null);
            Assert.That(record!.LastSeenUserName, Is.EqualTo(RealUserName),
                "AddRoundPlayers's recovery path must not overwrite the authoritative row UpdatePlayerRecord " +
                "already wrote.");
            Assert.That(record.LastSeenAddress, Is.EqualTo(RealAddress));
            Assert.That(record.HWId?.Hwid, Is.EqualTo(hwid.Hwid));

            var round = await db.GetRound(roundId);
            Assert.That(round.Players.Any(p => p.UserId == userId.UserId), Is.True,
                "the round-player association must still be recorded even though AddRoundPlayers lost the " +
                "row-creation race and only had to adopt the existing row.");
        }
        finally
        {
            DeleteDbFile(dbPath);
        }
    }

    /// <summary>
    /// Requirement 2 (final hardening round): the array-based (no-identity) <c>AddRoundPlayers</c>
    /// overload -- used when no live session identity is available -- must never commit a blank
    /// ("", 0.0.0.0) placeholder row for a player it doesn't already have a row for. Before this fix it
    /// papered over the missing identity with exactly such a placeholder, which could permanently poison
    /// ban-audit / "is this a new player" data for a guest connection that never calls
    /// <c>UpdatePlayerRecord</c> to overwrite it. After this fix, a missing row is logged and that
    /// player's round association is skipped -- no row is created at all.
    /// </summary>
    [Test]
    public async Task ArrayOverloadForMissingPlayer_DoesNotCreateBlankRow()
    {
        var pair = Pair;
        var db = GetConcurrentDb(pair.Server, out var dbPath);
        try
        {
            var (server, _) = await db.AddOrGetServer("race-test-server-c");
            var roundId = await db.AddNewRound(server);
            var userId = new NetUserId(Guid.NewGuid());

            // No Player row exists yet for this user, and the array overload has no identity to create
            // one with -- it must not throw, and it must not manufacture a blank row.
            Assert.DoesNotThrowAsync(async () => await db.AddRoundPlayers(roundId, new[] { userId.UserId }));

            var record = await db.GetPlayerRecordByUserId(userId, default);
            Assert.That(record, Is.Null,
                "BUG (requirement 2, blank-insert footgun): the identity-less AddRoundPlayers overload " +
                "must never commit a Player row for a player it has no identity for.");

            var round = await db.GetRound(roundId);
            Assert.That(round.Players.Any(p => p.UserId == userId.UserId), Is.False,
                "a player with no Player row and no identity available must not be associated with the " +
                "round either -- there is nothing legitimate to associate.");

            // The connection handshake's authoritative write later lands normally, with real identity and
            // no blank row in its way.
            var hwid = RealHwid();
            await db.UpdatePlayerRecord(userId, RealUserName, RealAddress, hwid);

            var afterUpdate = await db.GetPlayerRecordByUserId(userId, default);
            Assert.That(afterUpdate, Is.Not.Null);
            Assert.That(afterUpdate!.LastSeenUserName, Is.EqualTo(RealUserName));
            Assert.That(afterUpdate.LastSeenAddress, Is.EqualTo(RealAddress));
        }
        finally
        {
            DeleteDbFile(dbPath);
        }
    }

    /// <summary>
    /// Requirement 1 (final hardening round): two concurrent <c>UpdatePlayerRecord</c> calls for the SAME
    /// brand-new user but DIFFERENT identities (e.g. two racing connection attempts) must resolve so the
    /// chronologically NEWER call's identity survives -- an older call that loses the physical INSERT race
    /// must not be allowed to clobber a newer winner's username/address/HWID/LastSeenTime on recovery, and
    /// the row's FirstSeenTime &lt;= LastSeenTime invariant must hold throughout.
    /// </summary>
    [Test]
    public async Task UpdatePlayerRecordVsUpdatePlayerRecord_NewerIdentityWins()
    {
        var pair = Pair;
        var db = GetConcurrentDb(pair.Server, out var dbPath);
        try
        {
            var userId = new NetUserId(Guid.NewGuid());
            var oldHwid = new ImmutableTypedHwid(ImmutableArray.Create<byte>(9, 9, 9, 9, 9, 9, 9, 9), HwidType.Modern);
            var newHwid = RealHwid();
            var oldAddress = IPAddress.Parse("198.51.100.7");

            var hookCalls = 0;
            var reachedHook = new TaskCompletionSource();
            var releaseOlder = new TaskCompletionSource();
            ServerDbBase.TestBeforeUpdatePlayerRecordInsert = async () =>
            {
                // Only the FIRST (older) call's insert attempt pauses here. The second (newer) call's
                // insert attempt -- which also passes through this hook, since it's also inserting a
                // brand-new row -- must sail straight through so it wins the physical INSERT.
                if (System.Threading.Interlocked.Increment(ref hookCalls) == 1)
                {
                    reachedHook.TrySetResult();
                    await releaseOlder.Task;
                }
            };

            try
            {
                // Start the OLDER call first, so its `now` (captured at the very top of
                // UpdatePlayerRecord) is chronologically earlier. It pauses just before its own
                // SaveChangesAsync.
                var olderTask = db.UpdatePlayerRecord(userId, "OlderIdentity", oldAddress, oldHwid);
                await reachedHook.Task;

                // Guarantee a real wall-clock gap so the newer call's `now` is unambiguously later --
                // DateTime.UtcNow's resolution is coarse enough on some platforms that back-to-back calls
                // could otherwise tie.
                await Task.Delay(20);

                // The NEWER call proceeds straight through (second hook invocation is a no-op) and wins
                // the physical INSERT, creating the row with its own identity.
                await db.UpdatePlayerRecord(userId, RealUserName, RealAddress, newHwid);

                // Release the older call: its insert now collides with the row the newer call just
                // committed. It must recover (not throw) -- and, because it is older, it must NOT
                // overwrite the newer winner's identity.
                releaseOlder.SetResult();
                Assert.DoesNotThrowAsync(async () => await olderTask,
                    "UpdatePlayerRecord must recover from losing the insert race, not throw.");
            }
            finally
            {
                ServerDbBase.TestBeforeUpdatePlayerRecordInsert = null;
            }

            var record = await db.GetPlayerRecordByUserId(userId, default);
            Assert.That(record, Is.Not.Null);
            Assert.That(record!.LastSeenUserName, Is.EqualTo(RealUserName),
                "BUG (requirement 1, ordering-unsafe recovery): an OLDER losing connection attempt must " +
                "not overwrite a NEWER winner's username.");
            Assert.That(record.LastSeenAddress, Is.EqualTo(RealAddress),
                "BUG (requirement 1, ordering-unsafe recovery): an OLDER losing connection attempt must " +
                "not overwrite a NEWER winner's address.");
            Assert.That(record.HWId?.Hwid, Is.EqualTo(newHwid.Hwid),
                "BUG (requirement 1, ordering-unsafe recovery): an OLDER losing connection attempt must " +
                "not overwrite a NEWER winner's HWID.");
            Assert.That(record.FirstSeenTime, Is.LessThanOrEqualTo(record.LastSeenTime),
                "BUG (requirement 1, ordering-unsafe recovery): FirstSeenTime must never end up after " +
                "LastSeenTime.");
        }
        finally
        {
            DeleteDbFile(dbPath);
        }
    }
}
