#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._Solreign.SeasonLedger;
using Content.Server._Solreign.Social;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Unit coverage for the <c>social_firsts</c> table (SeasonLedgerStore.SocialFirsts.cs) — the
///     exactly-once linchpin of the social-first milestone toasts and the third-visit wingmate
///     prompt:
///       * <c>TryClaimSocialFirstAsync</c> claims exactly once per (account, flag), including
///         across racing concurrent callers and across separate store instances against the same
///         file;
///       * distinct flags and distinct accounts claim independently;
///       * a season bump does NOT reset any claim (career-scoped, the first_death law).
///     Same per-test temp-DB harness as <see cref="FirstDeathStoreTests"/> — never a bin path,
///     never a shared file.
/// </summary>
[TestFixture]
[TestOf(typeof(SeasonLedgerStore))]
public sealed class SocialFirstsStoreTests
{
    private string _dbPath = default!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"solreign_social_firsts_test_{Guid.NewGuid():N}.db");
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

    [Test]
    public async Task FirstClaim_Succeeds_AndRoundTripsTheFlag()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        Assert.That(await store.TryClaimSocialFirstAsync(user, SolreignSocialFirstFlags.ChirpAnswered, 42), Is.True);

        var flags = await store.GetSocialFirstFlagsAsync(user);
        Assert.That(flags, Is.EqualTo(new[] { SolreignSocialFirstFlags.ChirpAnswered }));
    }

    [Test]
    public async Task SecondClaim_SameFlag_ReturnsFalse()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        Assert.That(await store.TryClaimSocialFirstAsync(user, SolreignSocialFirstFlags.HealedByAnother, 1), Is.True);
        Assert.That(await store.TryClaimSocialFirstAsync(user, SolreignSocialFirstFlags.HealedByAnother, 2), Is.False,
            "the second detection of an account's milestone must never claim");

        Assert.That(await store.GetSocialFirstFlagsAsync(user), Has.Count.EqualTo(1),
            "the losing claim must not add a row");
    }

    [Test]
    public async Task DistinctFlags_ClaimIndependently_ForOneAccount()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        Assert.That(await store.TryClaimSocialFirstAsync(user, SolreignSocialFirstFlags.ChirpAnswered, 1), Is.True);
        Assert.That(await store.TryClaimSocialFirstAsync(user, SolreignSocialFirstFlags.HealedByAnother, 1), Is.True,
            "one milestone's claim must never consume another's");
        Assert.That(await store.TryClaimSocialFirstAsync(user, SolreignSocialFirstFlags.ItemReceived, 1), Is.True);
        Assert.That(await store.TryClaimSocialFirstAsync(user, SolreignSocialFirstFlags.WingmatePrompt, 1), Is.True);

        var flags = await store.GetSocialFirstFlagsAsync(user);
        Assert.That(flags, Is.EquivalentTo(new[]
        {
            SolreignSocialFirstFlags.ChirpAnswered,
            SolreignSocialFirstFlags.HealedByAnother,
            SolreignSocialFirstFlags.ItemReceived,
            SolreignSocialFirstFlags.WingmatePrompt,
        }));
    }

    [Test]
    public async Task DistinctAccounts_ClaimIndependently()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        Assert.That(await store.TryClaimSocialFirstAsync(userA, SolreignSocialFirstFlags.ItemReceived, 1), Is.True);
        Assert.That(await store.TryClaimSocialFirstAsync(userB, SolreignSocialFirstFlags.ItemReceived, 1), Is.True,
            "one account's claim must never consume another's");
    }

    [Test]
    public async Task RacingClaims_AcrossConcurrentCallers_HandExactlyOneWin()
    {
        // Two independent store instances against the SAME file — harder than in-process racing
        // (each instance has its own _lock), so the win must come from SQLite's INSERT ... ON
        // CONFLICT DO NOTHING itself, not from any process-local serialization. (The
        // FirstDeathStoreTests technique, verbatim.)
        var storeA = new SeasonLedgerStore(_dbPath);
        var storeB = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        var results = await Task.WhenAll(
            storeA.TryClaimSocialFirstAsync(user, SolreignSocialFirstFlags.ChirpAnswered, 1),
            storeB.TryClaimSocialFirstAsync(user, SolreignSocialFirstFlags.ChirpAnswered, 1));

        Assert.That(results.Count(claimed => claimed), Is.EqualTo(1),
            "racing claims must hand the win to exactly one caller");
    }

    [Test]
    public async Task SeasonBump_DoesNotResetAnyClaim()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        Assert.That(await store.TryClaimSocialFirstAsync(user, SolreignSocialFirstFlags.WingmatePrompt, 1), Is.True);

        await store.BumpSeasonAsync();

        Assert.That(await store.TryClaimSocialFirstAsync(user, SolreignSocialFirstFlags.WingmatePrompt, 99), Is.False,
            "a social first is a career event — a season bump must never reset the claim");
    }

    [Test]
    public async Task GetFlags_UnknownAccount_ReturnsEmpty()
    {
        var store = new SeasonLedgerStore(_dbPath);
        Assert.That(await store.GetSocialFirstFlagsAsync(Guid.NewGuid()), Is.Empty);
    }
}
