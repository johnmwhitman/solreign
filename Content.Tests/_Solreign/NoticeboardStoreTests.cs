#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using Content.Server._Solreign.SeasonLedger;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Crew Noticeboards persistence tests (v14 wave-1 #2, docs/council/2026-07-17-player-text-
///     safety.md — the C2 posture memo, LAW for these numbers). The <c>SeasonLedgerStoreTests</c>
///     temp-file idiom: every quota/capacity/cooldown rule from the memo is exercised directly
///     against <see cref="SeasonLedgerStore.TryPostNoteAsync"/>'s atomic transaction — no ECS, no
///     daemon, no HTTP, just the store's own rule engine.
/// </summary>
[TestFixture]
[TestOf(typeof(SeasonLedgerStore))]
public sealed class NoticeboardStoreTests
{
    private string _dbPath = default!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"solreign_noticeboard_test_{Guid.NewGuid():N}.db");
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
    private static string HoursAgo(int hours) => DateTime.UtcNow.AddHours(-hours).ToString("o");

    private async Task<(bool Posted, int Id, NoticeboardPostRejection? Rejection)> Post(
        SeasonLedgerStore store, string board, Guid user, bool providence, string author, string body,
        string? nowUtc = null, int expiryHours = 72, int capacity = 12, int providenceCap = 2, int cooldownHours = 24)
    {
        return await store.TryPostNoteAsync(board, user, providence, author, body, nowUtc ?? Now(),
            expiryHours, capacity, providenceCap, cooldownHours);
    }

    // --- basic post + read --------------------------------------------------------------------------

    [Test]
    public async Task Post_ThenRead_AppearsActive()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        var (posted, id, rejection) = await Post(store, "main", user, false, "Kolton", "Movie night 8pm.");

        Assert.That(posted, Is.True);
        Assert.That(rejection, Is.Null);
        Assert.That(id, Is.GreaterThan(0));

        var active = await store.GetActiveNotesAsync("main", Now());
        Assert.That(active, Has.Count.EqualTo(1));
        Assert.That(active[0].Body, Is.EqualTo("Movie night 8pm."));
        Assert.That(active[0].AuthorDisplay, Is.EqualTo("Kolton"));
        Assert.That(active[0].IsProvidence, Is.False);
    }

    [Test]
    public async Task GetActiveNotes_ScopedToBoardId()
    {
        var store = new SeasonLedgerStore(_dbPath);

        await Post(store, "main", Guid.NewGuid(), false, "A", "note on main");
        await Post(store, "other", Guid.NewGuid(), false, "B", "note on other");

        Assert.That(await store.GetActiveNotesAsync("main", Now()), Has.Count.EqualTo(1));
        Assert.That(await store.GetActiveNotesAsync("other", Now()), Has.Count.EqualTo(1));
    }

    // --- one active note per author (spec rule 2) ----------------------------------------------------

    [Test]
    public async Task SecondPost_WhileFirstStillActive_Refused()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await Post(store, "main", user, false, "Kolton", "first note");
        var (posted, _, rejection) = await Post(store, "main", user, false, "Kolton", "second note");

        Assert.That(posted, Is.False);
        Assert.That(rejection, Is.EqualTo(NoticeboardPostRejection.AuthorHasActiveNote));
        Assert.That(await store.GetActiveNotesAsync("main", Now()), Has.Count.EqualTo(1), "the refused post must not land");
    }

    [Test]
    public async Task DifferentAuthors_BothActive_Independently()
    {
        var store = new SeasonLedgerStore(_dbPath);

        var (posted1, _, _) = await Post(store, "main", Guid.NewGuid(), false, "A", "note A");
        var (posted2, _, _) = await Post(store, "main", Guid.NewGuid(), false, "B", "note B");

        Assert.That(posted1, Is.True);
        Assert.That(posted2, Is.True);
        Assert.That(await store.GetActiveNotesAsync("main", Now()), Has.Count.EqualTo(2));
    }

    // --- 24h cooldown, independent of whether the old note is still active (spec rule 2) -----------

    [Test]
    public async Task CooldownStillApplies_AfterTheOldNoteWasHiddenAway()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        var (_, id, _) = await Post(store, "main", user, false, "Kolton", "first note", nowUtc: HoursAgo(1));
        Assert.That(await store.HideNoteAsync(id, "reported", Now()), Is.True);

        // The author no longer has an ACTIVE note (it's hidden) — but the cooldown clock started
        // 1h ago, well inside the 24h window, so a second post must still be refused.
        var (posted, _, rejection) = await Post(store, "main", user, false, "Kolton", "second note");

        Assert.That(posted, Is.False);
        Assert.That(rejection, Is.EqualTo(NoticeboardPostRejection.AuthorOnCooldown));
    }

    [Test]
    public async Task CooldownElapsed_AndOldNoteGone_AllowsANewPost()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        var (_, id, _) = await Post(store, "main", user, false, "Kolton", "first note", nowUtc: HoursAgo(30));
        Assert.That(await store.HideNoteAsync(id, "reported", Now()), Is.True);

        var (posted, _, rejection) = await Post(store, "main", user, false, "Kolton", "second note");

        Assert.That(posted, Is.True, rejection?.ToString());
    }

    [Test]
    public async Task NeverPostedBefore_HasNoCooldown()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var (posted, _, _) = await Post(store, "main", Guid.NewGuid(), false, "Fresh", "first ever note");
        Assert.That(posted, Is.True);
    }

    // --- board capacity, shared across players and PROVIDENCE (spec rule 2) -------------------------

    [Test]
    public async Task BoardCapacity_TwelveActive_ThirteenthRefused()
    {
        var store = new SeasonLedgerStore(_dbPath);

        for (var i = 0; i < 12; i++)
        {
            var (posted, _, _) = await Post(store, "main", Guid.NewGuid(), false, $"P{i}", $"note {i}", capacity: 12);
            Assert.That(posted, Is.True, $"post {i} should have succeeded");
        }

        var (thirteenth, _, rejection) = await Post(store, "main", Guid.NewGuid(), false, "P13", "note 13", capacity: 12);

        Assert.That(thirteenth, Is.False);
        Assert.That(rejection, Is.EqualTo(NoticeboardPostRejection.BoardFull));
        Assert.That(await store.GetActiveNotesAsync("main", Now()), Has.Count.EqualTo(12));
    }

    // --- PROVIDENCE's own quota, inside the shared cap (spec rule 5) --------------------------------

    [Test]
    public async Task ProvidenceQuota_TwoActive_ThirdRefused()
    {
        var store = new SeasonLedgerStore(_dbPath);

        var (first, _, _) = await Post(store, "main", Guid.Empty, true, "PROVIDENCE", "seed 1", providenceCap: 2);
        var (second, _, _) = await Post(store, "main", Guid.Empty, true, "PROVIDENCE", "seed 2", providenceCap: 2);
        var (third, _, rejection) = await Post(store, "main", Guid.Empty, true, "PROVIDENCE", "seed 3", providenceCap: 2);

        Assert.That(first, Is.True);
        Assert.That(second, Is.True);
        Assert.That(third, Is.False);
        Assert.That(rejection, Is.EqualTo(NoticeboardPostRejection.ProvidenceQuotaFull));
    }

    [Test]
    public async Task Providence_HasNoCooldownOrActiveNoteCap()
    {
        var store = new SeasonLedgerStore(_dbPath);

        var (first, _, _) = await Post(store, "main", Guid.Empty, true, "PROVIDENCE", "seed 1");
        var (second, _, _) = await Post(store, "main", Guid.Empty, true, "PROVIDENCE", "seed 2");

        // Two PROVIDENCE posts back to back, no cooldown gap — quota-gated only, per spec rule 5.
        Assert.That(first, Is.True);
        Assert.That(second, Is.True);
    }

    // --- hide / unhide (spec rule 3) -----------------------------------------------------------------

    [Test]
    public async Task Hide_RemovesFromActiveList()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var (_, id, _) = await Post(store, "main", Guid.NewGuid(), false, "Kolton", "note");

        var hidden = await store.HideNoteAsync(id, "reported", Now());

        Assert.That(hidden, Is.True);
        Assert.That(await store.GetActiveNotesAsync("main", Now()), Is.Empty);
    }

    [Test]
    public async Task Hide_IsIdempotent()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var (_, id, _) = await Post(store, "main", Guid.NewGuid(), false, "Kolton", "note");

        Assert.That(await store.HideNoteAsync(id, "reported", Now()), Is.True);
        Assert.That(await store.HideNoteAsync(id, "reported", Now()), Is.False, "second hide is a no-op, not an error");
    }

    [Test]
    public async Task Unhide_RestoresToActiveList()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var (_, id, _) = await Post(store, "main", Guid.NewGuid(), false, "Kolton", "note");
        await store.HideNoteAsync(id, "reported", Now());

        var unhidden = await store.UnhideNoteAsync(id, Now());

        Assert.That(unhidden, Is.True);
        Assert.That(await store.GetActiveNotesAsync("main", Now()), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Unhide_NeverRevivesANoteThatExpiredWhileHidden()
    {
        // Spec rule 3: "silence always resolves to the safe outcome — the note is gone, never
        // quietly restored to view."
        var store = new SeasonLedgerStore(_dbPath);
        var (_, id, _) = await Post(store, "main", Guid.NewGuid(), false, "Kolton", "note", nowUtc: HoursAgo(80), expiryHours: 72);
        await store.HideNoteAsync(id, "reported", Now());

        var unhidden = await store.UnhideNoteAsync(id, Now());

        Assert.That(unhidden, Is.False);
        Assert.That(await store.GetActiveNotesAsync("main", Now()), Is.Empty);
    }

    [Test]
    public async Task Unhide_OnANeverHiddenNote_IsANoOp()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var (_, id, _) = await Post(store, "main", Guid.NewGuid(), false, "Kolton", "note");

        Assert.That(await store.UnhideNoteAsync(id, Now()), Is.False);
    }

    // --- expiry sweep (spec rule 4) -------------------------------------------------------------------

    [Test]
    public async Task ExpiredNote_NeverAppearsActive_EvenBeforeTheSweepRuns()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var (_, id, _) = await Post(store, "main", Guid.NewGuid(), false, "Kolton", "note", nowUtc: HoursAgo(80), expiryHours: 72);

        // Correctness never depends on the physical sweep having run yet.
        Assert.That(await store.GetActiveNotesAsync("main", Now()), Is.Empty);
        Assert.That(id, Is.GreaterThan(0));
    }

    [Test]
    public async Task Sweep_PhysicallyDeletesExpiredRows_LeavesActiveOnesAlone()
    {
        var store = new SeasonLedgerStore(_dbPath);
        await Post(store, "main", Guid.NewGuid(), false, "Old", "expired note", nowUtc: HoursAgo(80), expiryHours: 72);
        await Post(store, "main", Guid.NewGuid(), false, "New", "fresh note");

        var removed = await store.SweepExpiredNotesAsync(Now());

        Assert.That(removed, Is.EqualTo(1));
        Assert.That(await store.GetActiveNotesAsync("main", Now()), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Sweep_NeverTouchesTheAuthorCooldownClock()
    {
        // The whole point of the separate noticeboard_authors table: the cooldown must survive
        // the note row's own physical deletion. Isolate the exact scenario: posted 2h ago with a
        // 1h expiry (so the note is already EXPIRED and sweepable) while the 24h cooldown clock
        // (which started at that same post time) still has 22h left to run.
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();
        await Post(store, "main", user, false, "Kolton", "note", nowUtc: HoursAgo(2), expiryHours: 1, cooldownHours: 24);

        var removed = await store.SweepExpiredNotesAsync(Now());
        Assert.That(removed, Is.EqualTo(1), "the note should have been expired and swept");

        var (posted, _, rejection) = await Post(store, "main", user, false, "Kolton", "second note", cooldownHours: 24);

        Assert.That(posted, Is.False, "the cooldown clock must survive the swept note's deletion");
        Assert.That(rejection, Is.EqualTo(NoticeboardPostRejection.AuthorOnCooldown));
    }

    // --- providence count read (used by the seeding pre-check) --------------------------------------

    [Test]
    public async Task GetActiveProvidenceCount_CountsOnlyProvidenceRows()
    {
        var store = new SeasonLedgerStore(_dbPath);
        await Post(store, "main", Guid.NewGuid(), false, "Player", "player note");
        await Post(store, "main", Guid.Empty, true, "PROVIDENCE", "seed 1");

        Assert.That(await store.GetActiveProvidenceCountAsync("main", Now()), Is.EqualTo(1));
    }
}
