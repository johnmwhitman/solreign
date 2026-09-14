#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using Content.Server._Solreign.SeasonLedger;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     STATION-LIBRARY persistence tests (this lane's design brief). The
///     <c>NoticeboardStoreTests</c>/<c>SeasonLedgerStoreTests</c> temp-file idiom: every rule from
///     the design brief is exercised directly against <see cref="SeasonLedgerStore.TryPostWorkAsync"/>'s
///     atomic transaction — no ECS, no daemon, no HTTP, just the store's own rule engine. This is
///     the "submission -> pending -> approved" flow with mocked screening the task's TESTS section
///     asks for: every call here writes as though the daemon's classifier already approved the
///     text (exactly the precondition <c>SolreignLibrarySystem.FireSubmit</c> establishes before
///     ever calling this method) — the classifier gate itself is proven separately by the daemon's
///     own contract (documented in the receipt) and, game-side, by
///     <c>LibraryIntegrationTest</c>'s "unconfigured channel -&gt; nothing written -&gt; pending
///     forever" case.
/// </summary>
[TestFixture]
[TestOf(typeof(SeasonLedgerStore))]
public sealed class LibraryWorkStoreTests
{
    private string _dbPath = default!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"solreign_library_test_{Guid.NewGuid():N}.db");
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

    private static string Now() => DateTime.UtcNow.ToString("o");

    private static Task<(bool Posted, int Id, LibraryPostRejection? Rejection)> Submit(
        SeasonLedgerStore store, string archive, Guid author, string authorDisplay, string title, string body,
        int roundId = 1, string? nowUtc = null)
    {
        return store.TryPostWorkAsync(archive, author, false, authorDisplay, title, body, roundId, nowUtc ?? Now());
    }

    private static Task<(bool Posted, int Id, LibraryPostRejection? Rejection)> SubmitProvidence(
        SeasonLedgerStore store, string archive, string authorDisplay, string title, string body,
        int roundId = 1, string? nowUtc = null, int seedTarget = 4)
    {
        return store.TryPostWorkAsync(archive, Guid.Empty, true, authorDisplay, title, body, roundId, nowUtc ?? Now(), seedTarget);
    }

    // --- basic submit + read (submission -> pending [mocked-approved] -> approved) -----------------

    [Test]
    public async Task Submit_ThenRead_AppearsRecent()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        var (posted, id, rejection) = await Submit(store, "main", user, "Kolton", "My First Book", "Once upon a time.");

        Assert.That(posted, Is.True);
        Assert.That(rejection, Is.Null);
        Assert.That(id, Is.GreaterThan(0));

        var recent = await store.GetRecentWorksAsync("main", 10);
        Assert.That(recent, Has.Count.EqualTo(1));
        Assert.That(recent[0].Title, Is.EqualTo("My First Book"));
        Assert.That(recent[0].Body, Is.EqualTo("Once upon a time."));
        Assert.That(recent[0].AuthorDisplay, Is.EqualTo("Kolton"));
        Assert.That(recent[0].IsProvidence, Is.False);
    }

    [Test]
    public async Task GetRecentWorks_ScopedToArchiveId()
    {
        var store = new SeasonLedgerStore(_dbPath);

        await Submit(store, "main", Guid.NewGuid(), "A", "Title A", "body on main");
        await Submit(store, "other", Guid.NewGuid(), "B", "Title B", "body on other");

        Assert.That(await store.GetRecentWorksAsync("main", 10), Has.Count.EqualTo(1));
        Assert.That(await store.GetRecentWorksAsync("other", 10), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task GetRecentWorks_NewestFirst()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var author = Guid.NewGuid();

        await Submit(store, "main", author, "A", "First", "first body", roundId: 1, nowUtc: "2026-07-17T10:00:00.000Z");
        await Submit(store, "main", Guid.NewGuid(), "B", "Second", "second body", roundId: 1, nowUtc: "2026-07-17T11:00:00.000Z");

        var recent = await store.GetRecentWorksAsync("main", 10);
        Assert.That(recent, Has.Count.EqualTo(2));
        Assert.That(recent[0].Title, Is.EqualTo("Second"), "most-recently-submitted first");
        Assert.That(recent[1].Title, Is.EqualTo("First"));
    }

    [Test]
    public async Task GetRecentWorks_RespectsLimit_KeepsRowsUntouched()
    {
        var store = new SeasonLedgerStore(_dbPath);
        for (var i = 0; i < 5; i++)
            await Submit(store, "main", Guid.NewGuid(), $"P{i}", $"Title {i}", $"body {i}", roundId: i + 1);

        var recent = await store.GetRecentWorksAsync("main", 2);
        Assert.That(recent, Has.Count.EqualTo(2));

        // Beyond-cap rows keep their row — a re-query with a higher limit still finds all 5.
        Assert.That(await store.GetRecentWorksAsync("main", 100), Has.Count.EqualTo(5));
    }

    [Test]
    public async Task GetRecentWorks_NonPositiveLimit_ReturnsEmpty()
    {
        var store = new SeasonLedgerStore(_dbPath);
        await Submit(store, "main", Guid.NewGuid(), "A", "Title", "body");

        Assert.That(await store.GetRecentWorksAsync("main", 0), Is.Empty);
        Assert.That(await store.GetRecentWorksAsync("main", -1), Is.Empty);
    }

    // --- once-per-account-per-round author quota ----------------------------------------------------

    [Test]
    public async Task SecondSubmit_SameRound_Refused()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await Submit(store, "main", user, "Kolton", "First Work", "first body", roundId: 5);
        var (posted, _, rejection) = await Submit(store, "main", user, "Kolton", "Second Work", "second body", roundId: 5);

        Assert.That(posted, Is.False);
        Assert.That(rejection, Is.EqualTo(LibraryPostRejection.AuthorAlreadySubmittedThisRound));
        Assert.That(await store.GetRecentWorksAsync("main", 10), Has.Count.EqualTo(1), "the refused submission must not land");
    }

    [Test]
    public async Task SecondSubmit_NextRound_Allowed()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await Submit(store, "main", user, "Kolton", "First Work", "first body", roundId: 5);
        var (posted, _, rejection) = await Submit(store, "main", user, "Kolton", "Second Work", "second body", roundId: 6);

        Assert.That(posted, Is.True, rejection?.ToString());
        Assert.That(await store.GetRecentWorksAsync("main", 10), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Quota_StillApplies_EvenAfterTheFirstWorkWasHidden()
    {
        // Hiding is containment, not a refund — a hidden work still consumed the author's one
        // submission for that round (this lane's design brief: no expiry, no cooldown reset).
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        var (_, id, _) = await Submit(store, "main", user, "Kolton", "First Work", "first body", roundId: 5);
        Assert.That(await store.HideWorkAsync(id, "reported", Now()), Is.True);

        var (posted, _, rejection) = await Submit(store, "main", user, "Kolton", "Second Work", "second body", roundId: 5);

        Assert.That(posted, Is.False);
        Assert.That(rejection, Is.EqualTo(LibraryPostRejection.AuthorAlreadySubmittedThisRound));
    }

    [Test]
    public async Task DifferentAuthors_SameRound_BothAllowed()
    {
        var store = new SeasonLedgerStore(_dbPath);

        var (posted1, _, _) = await Submit(store, "main", Guid.NewGuid(), "A", "Title A", "body A", roundId: 5);
        var (posted2, _, _) = await Submit(store, "main", Guid.NewGuid(), "B", "Title B", "body B", roundId: 5);

        Assert.That(posted1, Is.True);
        Assert.That(posted2, Is.True);
    }

    [Test]
    public async Task NeverSubmittedBefore_HasNoQuotaBlock()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var (posted, _, _) = await Submit(store, "main", Guid.NewGuid(), "Fresh", "First Ever", "first ever body");
        Assert.That(posted, Is.True);
    }

    // --- NO expiry, NO cooldown (deliberate divergence from Noticeboard) ----------------------------

    [Test]
    public async Task SubmittedLongAgo_StillAppearsActive_NoExpiry()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var longAgo = DateTime.UtcNow.AddYears(-1).ToString("o");

        await Submit(store, "main", Guid.NewGuid(), "Old Author", "Old Work", "still here", roundId: 1, nowUtc: longAgo);

        Assert.That(await store.GetRecentWorksAsync("main", 10), Has.Count.EqualTo(1),
            "a library work must never expire — persistence is the point");
    }

    // --- PROVIDENCE seeding: once-EVER per archive, atomic against the seed target ------------------

    [Test]
    public async Task ProvidenceSeed_ExemptFromAuthorQuota()
    {
        var store = new SeasonLedgerStore(_dbPath);

        var (first, _, _) = await SubmitProvidence(store, "main", "PROVIDENCE", "Seed 1", "seed body 1", roundId: 1);
        var (second, _, _) = await SubmitProvidence(store, "main", "PROVIDENCE", "Seed 2", "seed body 2", roundId: 1);

        // Two PROVIDENCE submissions in the SAME round, both from Guid.Empty — no per-round quota
        // applies to PROVIDENCE (it is gated by the seed target instead).
        Assert.That(first, Is.True);
        Assert.That(second, Is.True);
    }

    [Test]
    public async Task ProvidenceSeed_RefusedOnceTargetReached()
    {
        var store = new SeasonLedgerStore(_dbPath);

        var (first, _, _) = await SubmitProvidence(store, "main", "PROVIDENCE", "Seed 1", "seed body 1", seedTarget: 2);
        var (second, _, _) = await SubmitProvidence(store, "main", "PROVIDENCE", "Seed 2", "seed body 2", seedTarget: 2);
        var (third, _, rejection) = await SubmitProvidence(store, "main", "PROVIDENCE", "Seed 3", "seed body 3", seedTarget: 2);

        Assert.That(first, Is.True);
        Assert.That(second, Is.True);
        Assert.That(third, Is.False);
        Assert.That(rejection, Is.EqualTo(LibraryPostRejection.ProvidenceSeedTargetReached));
    }

    [Test]
    public async Task GetProvidenceWorkCount_CountsOnlyProvidenceRows()
    {
        var store = new SeasonLedgerStore(_dbPath);
        await Submit(store, "main", Guid.NewGuid(), "Player", "Player Work", "player body");
        await SubmitProvidence(store, "main", "PROVIDENCE", "Seed", "seed body");

        Assert.That(await store.GetProvidenceWorkCountAsync("main"), Is.EqualTo(1));
    }

    // --- hide / unhide (containment) -----------------------------------------------------------------

    [Test]
    public async Task Hide_RemovesFromRecentList()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var (_, id, _) = await Submit(store, "main", Guid.NewGuid(), "Kolton", "Work", "body");

        var hidden = await store.HideWorkAsync(id, "reported", Now());

        Assert.That(hidden, Is.True);
        Assert.That(await store.GetRecentWorksAsync("main", 10), Is.Empty);
    }

    [Test]
    public async Task Hide_IsIdempotent()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var (_, id, _) = await Submit(store, "main", Guid.NewGuid(), "Kolton", "Work", "body");

        Assert.That(await store.HideWorkAsync(id, "reported", Now()), Is.True);
        Assert.That(await store.HideWorkAsync(id, "reported", Now()), Is.False, "second hide is a no-op, not an error");
    }

    [Test]
    public async Task Unhide_RestoresToRecentList()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var (_, id, _) = await Submit(store, "main", Guid.NewGuid(), "Kolton", "Work", "body");
        await store.HideWorkAsync(id, "reported", Now());

        var unhidden = await store.UnhideWorkAsync(id, Now());

        Assert.That(unhidden, Is.True);
        Assert.That(await store.GetRecentWorksAsync("main", 10), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Unhide_OnANeverHiddenWork_IsANoOp()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var (_, id, _) = await Submit(store, "main", Guid.NewGuid(), "Kolton", "Work", "body");

        Assert.That(await store.UnhideWorkAsync(id, Now()), Is.False);
    }

    [Test]
    public async Task Unhide_NeverBlockedByExpiry_UnlikeNoticeboard()
    {
        // The divergence pin: a library work has no expires_utc to re-check, so an unhide always
        // succeeds regardless of how long ago the work was submitted (contrast
        // NoticeboardStoreTests.Unhide_NeverRevivesANoteThatExpiredWhileHidden).
        var store = new SeasonLedgerStore(_dbPath);
        var longAgo = DateTime.UtcNow.AddYears(-2).ToString("o");
        var (_, id, _) = await Submit(store, "main", Guid.NewGuid(), "Old", "Old Work", "old body", nowUtc: longAgo);
        await store.HideWorkAsync(id, "reported", Now());

        Assert.That(await store.UnhideWorkAsync(id, Now()), Is.True);
        Assert.That(await store.GetRecentWorksAsync("main", 10), Has.Count.EqualTo(1));
    }
}
