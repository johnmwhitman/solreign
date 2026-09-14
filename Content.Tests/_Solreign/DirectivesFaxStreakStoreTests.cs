#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using Content.Server._Solreign.SeasonLedger;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Unit coverage for the <c>directives_fax_streak</c> table
///     (SeasonLedgerStore.DirectivesFax.cs) — the persistent per-account compliance streak:
///       * present + met -&gt; streak + 1;
///       * present + unmet -&gt; streak resets to 0;
///       * ABSENT (this method simply never called for that account/round) -&gt; no change at all —
///         the presence-aware discipline, never punish an absent player;
///       * a retried write for an already-recorded round is idempotent (no double-increment);
///       * a season bump does NOT reset a streak (career-scoped, the first_death/social_firsts law).
///     Same per-test temp-DB harness as <see cref="SocialFirstsStoreTests"/> — never a bin path, never
///     a shared file.
/// </summary>
[TestFixture]
[TestOf(typeof(SeasonLedgerStore))]
public sealed class DirectivesFaxStreakStoreTests
{
    private string _dbPath = default!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"solreign_directives_fax_streak_test_{Guid.NewGuid():N}.db");
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
    public async Task FirstOutcome_UnknownAccount_MetTrue_StartsStreakAtOne()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        var (streak, best, isNewBest) = await store.RecordDirectivesFaxOutcomeAsync(user, met: true, roundId: 1);

        Assert.That(streak, Is.EqualTo(1));
        Assert.That(best, Is.EqualTo(1));
        Assert.That(isNewBest, Is.True);
    }

    [Test]
    public async Task PresentAndMet_ConsecutiveRounds_IncrementsTheStreak()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.RecordDirectivesFaxOutcomeAsync(user, met: true, roundId: 1);
        await store.RecordDirectivesFaxOutcomeAsync(user, met: true, roundId: 2);
        var (streak, best, _) = await store.RecordDirectivesFaxOutcomeAsync(user, met: true, roundId: 3);

        Assert.That(streak, Is.EqualTo(3));
        Assert.That(best, Is.EqualTo(3));
    }

    [Test]
    public async Task PresentAndUnmet_ResetsTheStreakToZero()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.RecordDirectivesFaxOutcomeAsync(user, met: true, roundId: 1);
        await store.RecordDirectivesFaxOutcomeAsync(user, met: true, roundId: 2);
        var (streak, best, isNewBest) = await store.RecordDirectivesFaxOutcomeAsync(user, met: false, roundId: 3);

        Assert.That(streak, Is.EqualTo(0), "a present-and-unmet shift must reset the streak to zero");
        Assert.That(best, Is.EqualTo(2), "the best streak achieved must survive a reset");
        Assert.That(isNewBest, Is.False);
    }

    [Test]
    public async Task PresentAndUnmet_ThenMetAgain_StreakRebuildsFromZero()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.RecordDirectivesFaxOutcomeAsync(user, met: true, roundId: 1);
        await store.RecordDirectivesFaxOutcomeAsync(user, met: false, roundId: 2);
        var (streak, _, _) = await store.RecordDirectivesFaxOutcomeAsync(user, met: true, roundId: 3);

        Assert.That(streak, Is.EqualTo(1));
    }

    [Test]
    public async Task Absent_AccountNeverTouched_LeavesStreakUnchanged()
    {
        // "Absent" in production means DirectivesFaxRuleSystem simply never calls this method for the
        // account that round (see SnapshotPresentEligibleAccounts) -- so the store-level contract to
        // verify is: a round this account's row is never written for does not appear in its history,
        // i.e. its streak reflects only the rounds actually recorded, in order, with no phantom
        // resets from skipped rounds.
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.RecordDirectivesFaxOutcomeAsync(user, met: true, roundId: 1);
        // Round 2: account was absent -- production code never calls RecordDirectivesFaxOutcomeAsync
        // for it. Nothing to do here; that omission IS the test.
        var (streak, _, _) = await store.RecordDirectivesFaxOutcomeAsync(user, met: true, roundId: 3);

        Assert.That(streak, Is.EqualTo(2), "skipping an absent round must not reset the streak -- it " +
            "must simply continue counting the rounds the account was actually present+met for");
    }

    [Test]
    public async Task RetriedWrite_SameRoundId_IsIdempotent_DoesNotDoubleIncrement()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.RecordDirectivesFaxOutcomeAsync(user, met: true, roundId: 1);
        var first = await store.RecordDirectivesFaxOutcomeAsync(user, met: true, roundId: 2);
        // A retried write for the SAME round id (e.g. a crash-and-replay) must not increment again.
        var retried = await store.RecordDirectivesFaxOutcomeAsync(user, met: true, roundId: 2);

        Assert.That(retried.CurrentStreak, Is.EqualTo(first.CurrentStreak));
        Assert.That(retried.CurrentStreak, Is.EqualTo(2));
    }

    [Test]
    public async Task DistinctAccounts_TrackStreaksIndependently()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await store.RecordDirectivesFaxOutcomeAsync(userA, met: true, roundId: 1);
        await store.RecordDirectivesFaxOutcomeAsync(userA, met: true, roundId: 2);
        await store.RecordDirectivesFaxOutcomeAsync(userB, met: false, roundId: 1);

        var (streakA, _) = await store.GetDirectivesFaxStreakAsync(userA);
        var (streakB, _) = await store.GetDirectivesFaxStreakAsync(userB);

        Assert.That(streakA, Is.EqualTo(2));
        Assert.That(streakB, Is.EqualTo(0));
    }

    [Test]
    public async Task SeasonBump_DoesNotResetTheStreak()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.RecordDirectivesFaxOutcomeAsync(user, met: true, roundId: 1);
        await store.RecordDirectivesFaxOutcomeAsync(user, met: true, roundId: 2);

        await store.BumpSeasonAsync();

        var (streak, _) = await store.GetDirectivesFaxStreakAsync(user);
        Assert.That(streak, Is.EqualTo(2), "a compliance streak is a career event -- a season bump must never reset it");
    }

    [Test]
    public async Task GetStreak_UnknownAccount_ReturnsZeroes()
    {
        var store = new SeasonLedgerStore(_dbPath);

        var (streak, best) = await store.GetDirectivesFaxStreakAsync(Guid.NewGuid());

        Assert.That(streak, Is.EqualTo(0));
        Assert.That(best, Is.EqualTo(0));
    }
}
