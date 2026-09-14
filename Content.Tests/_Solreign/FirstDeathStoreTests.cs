#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._Solreign.SeasonLedger;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Unit coverage for the <c>first_death</c> table (SeasonLedgerStore.FirstDeath.cs) — the
///     exactly-once linchpin of the authored first-death scene (spec §3.1/§7):
///       * <c>TryClaimFirstDeathAsync</c> claims exactly once, including across racing concurrent
///         callers and across separate store instances against the same file;
///       * a season bump does NOT reset the claim (the table is deliberately career-scoped);
///       * <c>TryMarkRehireShownAsync</c> conditional-update semantics (true exactly once, false with
///         no claim row).
///     Same per-test temp-DB harness as <see cref="SeasonLedgerStoreTests"/> — never a bin path,
///     never a shared file (SeasonLedgerDbPath.Resolve is the production seam; tests pass explicit
///     unique temp paths).
/// </summary>
[TestFixture]
[TestOf(typeof(SeasonLedgerStore))]
public sealed class FirstDeathStoreTests
{
    private string _dbPath = default!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"solreign_first_death_test_{Guid.NewGuid():N}.db");
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = _dbPath + suffix;
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // ignored — temp dir, harmless if left behind
            }
        }
    }

    private Task<bool> ClaimAsync(SeasonLedgerStore store, Guid user, int roundId = 1)
    {
        return store.TryClaimFirstDeathAsync(
            user, roundId, "Juno Pike", "VACUUM", 0, "Probationary Asset", "01");
    }

    [Test]
    public async Task FirstClaim_Succeeds_AndRoundTripsEveryColumn()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        Assert.That(await store.TryClaimFirstDeathAsync(
            user, 42, "Juno Pike", "VIOLENCE", 7, "Quarterly Standout", "09"), Is.True);

        var record = await store.GetFirstDeathAsync(user);
        Assert.That(record, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(record!.User, Is.EqualTo(user));
            Assert.That(record.RoundId, Is.EqualTo(42));
            Assert.That(record.CharacterName, Is.EqualTo("Juno Pike"));
            Assert.That(record.Cause, Is.EqualTo("VIOLENCE"));
            Assert.That(record.ToursAtDeath, Is.EqualTo(7));
            Assert.That(record.TitleAtDeath, Is.EqualTo("Quarterly Standout"));
            Assert.That(record.EpitaphId, Is.EqualTo("09"));
            Assert.That(record.DiedAtUtc, Is.Not.Empty);
            Assert.That(record.RehireShown, Is.False, "a fresh claim must not be pre-stamped as rehired");
        });
    }

    [Test]
    public async Task SecondClaim_ReturnsFalse_AndNeverMutatesTheOriginalRow()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        Assert.That(await store.TryClaimFirstDeathAsync(
            user, 1, "First Name", "VACUUM", 0, "Probationary Asset", "01"), Is.True);
        Assert.That(await store.TryClaimFirstDeathAsync(
            user, 2, "Second Name", "BURN", 3, "Chain Closer", "11"), Is.False,
            "the second death of an account must never claim");

        var record = await store.GetFirstDeathAsync(user);
        Assert.Multiple(() =>
        {
            Assert.That(record!.RoundId, Is.EqualTo(1), "the losing claim must not overwrite the winner");
            Assert.That(record.CharacterName, Is.EqualTo("First Name"));
            Assert.That(record.Cause, Is.EqualTo("VACUUM"));
        });
    }

    [Test]
    public async Task RacingClaims_AcrossConcurrentCallers_HandExactlyOneWin()
    {
        // Two independent store instances against the SAME file — harder than in-process racing
        // (each instance has its own _lock), so the win must come from SQLite's INSERT ... ON
        // CONFLICT DO NOTHING itself, not from any process-local serialization.
        var storeA = new SeasonLedgerStore(_dbPath);
        var storeB = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        var results = await Task.WhenAll(ClaimAsync(storeA, user), ClaimAsync(storeB, user));

        Assert.That(results.Count(claimed => claimed), Is.EqualTo(1),
            "racing claims must hand the win to exactly one caller");
    }

    [Test]
    public async Task SeasonBump_DoesNotResurrectAnyone()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        Assert.That(await ClaimAsync(store, user), Is.True);

        await store.BumpSeasonAsync();

        Assert.That(await ClaimAsync(store, user, roundId: 99), Is.False,
            "a first death is a career event — a season bump must never reset the claim");
        Assert.That((await store.GetFirstDeathAsync(user))!.RoundId, Is.EqualTo(1));
    }

    [Test]
    public async Task DistinctAccounts_ClaimIndependently()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        Assert.That(await ClaimAsync(store, userA), Is.True);
        Assert.That(await ClaimAsync(store, userB), Is.True,
            "one account's claim must never consume another's");
    }

    [Test]
    public async Task GetFirstDeath_UnknownAccount_ReturnsNull()
    {
        var store = new SeasonLedgerStore(_dbPath);
        Assert.That(await store.GetFirstDeathAsync(Guid.NewGuid()), Is.Null);
    }

    [Test]
    public async Task MarkRehireShown_TrueExactlyOnce_ThenFalseForever()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();
        await ClaimAsync(store, user);

        Assert.That(await store.TryMarkRehireShownAsync(user), Is.True,
            "the first mark after a claim must win");
        Assert.That(await store.TryMarkRehireShownAsync(user), Is.False,
            "the conditional UPDATE's rowcount guard must reject a second mark");
        Assert.That((await store.GetFirstDeathAsync(user))!.RehireShown, Is.True);
    }

    [Test]
    public async Task MarkRehireShown_WithoutAClaimRow_ReturnsFalse()
    {
        var store = new SeasonLedgerStore(_dbPath);
        Assert.That(await store.TryMarkRehireShownAsync(Guid.NewGuid()), Is.False,
            "an account that never died must have nothing to stamp");
    }

    [Test]
    public async Task RacingRehireMarks_HandExactlyOneWin()
    {
        var storeA = new SeasonLedgerStore(_dbPath);
        var storeB = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();
        await ClaimAsync(storeA, user);

        var results = await Task.WhenAll(
            storeA.TryMarkRehireShownAsync(user),
            storeB.TryMarkRehireShownAsync(user));

        Assert.That(results.Count(marked => marked), Is.EqualTo(1),
            "a racing double-spawn must deliver the rehire beat exactly once");
    }

    // --- Echoes of the Departed projection read (v14, EOD-spec §5/§13.2) --------------------------

    [Test]
    public async Task GetAllFirstDeaths_ReturnsMostRecentDeathFirst_AndHonorsTheCapacityLimit()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var userC = Guid.NewGuid();

        // Claim all three, then pin died_at_utc explicitly (SetFirstDeathDiedAtUtcForTests) so
        // ordering is asserted against controlled timestamps, not wall-clock claim timing.
        Assert.That(await ClaimAsync(store, userA), Is.True);
        Assert.That(await ClaimAsync(store, userB), Is.True);
        Assert.That(await ClaimAsync(store, userC), Is.True);

        await store.SetFirstDeathDiedAtUtcForTests(userA, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToString("o"));
        await store.SetFirstDeathDiedAtUtcForTests(userB, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc).ToString("o"));
        await store.SetFirstDeathDiedAtUtcForTests(userC, new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc).ToString("o"));

        var all = await store.GetAllFirstDeathsAsync(10);
        Assert.That(all.Select(r => r.User), Is.EqualTo(new[] { userC, userB, userA }),
            "the echo projection read must come back most-recent-death-first (DESC by died_at_utc)");

        var capped = await store.GetAllFirstDeathsAsync(2);
        Assert.That(capped.Select(r => r.User), Is.EqualTo(new[] { userC, userB }),
            "over-capacity records must not come back — the most RECENT deaths project, not the oldest");
    }

    [Test]
    public async Task GetAllFirstDeaths_NonPositiveLimit_ReturnsEmpty()
    {
        var store = new SeasonLedgerStore(_dbPath);
        await ClaimAsync(store, Guid.NewGuid());

        Assert.That(await store.GetAllFirstDeathsAsync(0), Is.Empty);
        Assert.That(await store.GetAllFirstDeathsAsync(-5), Is.Empty);
    }

    [Test]
    public async Task GetAllFirstDeaths_NoClaims_ReturnsEmpty()
    {
        var store = new SeasonLedgerStore(_dbPath);
        Assert.That(await store.GetAllFirstDeathsAsync(10), Is.Empty);
    }

    [Test]
    public async Task GetAllFirstDeaths_EqualTimestamps_TieBreaksByUserId()
    {
        var store = new SeasonLedgerStore(_dbPath);
        // Lexicographically ordered Guid strings so ASC on user_id is deterministic and obvious.
        var userA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var userB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        Assert.That(await ClaimAsync(store, userB), Is.True);
        Assert.That(await ClaimAsync(store, userA), Is.True);

        var sameUtc = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc).ToString("o");
        await store.SetFirstDeathDiedAtUtcForTests(userA, sameUtc);
        await store.SetFirstDeathDiedAtUtcForTests(userB, sameUtc);

        var all = await store.GetAllFirstDeathsAsync(10);
        Assert.That(all.Select(r => r.User), Is.EqualTo(new[] { userA, userB }),
            "equal died_at_utc must order by user_id ASC — slot membership at the limit is never random");

        var capped = await store.GetAllFirstDeathsAsync(1);
        Assert.That(capped.Select(r => r.User), Is.EqualTo(new[] { userA }),
            "the limit membership itself must use the same tie-break (A wins over B at equal timestamps)");
    }
}
