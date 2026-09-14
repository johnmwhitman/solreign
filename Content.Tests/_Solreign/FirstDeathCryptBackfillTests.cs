#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Content.Server._Solreign.Providence;
using Content.Server._Solreign.SeasonLedger;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Unit coverage for the FD-W3.5 plaque backfill's pure/storage pieces:
///       * the <c>crypt_reported</c> stamp (SeasonLedgerStore.FirstDeath.cs) — conditional-UPDATE
///         semantics (true exactly once), independence from <c>rehire_shown</c>, and the
///         unreported-rows query (unstamped only, oldest death first);
///       * the v13.3 legacy-schema migration — a DB whose <c>first_death</c> table predates the
///         column (the LIVE shape when the wire gap opened) must come forward with every banked
///         row defaulting to "memorial not yet minted";
///       * <c>ProvidenceFirstDeathSystem.BuildBackfillReport</c> — the persisted claim row must
///         re-compose to EXACTLY the wire payload the claim-time path would have sent;
///       * <see cref="FirstDeathCryptBackfillQueue"/> — FIFO order and the one-per-spacing-window
///         pacing that keeps backfill POSTs inside the crypt_first_death rate limit.
///     Same per-test temp-DB harness as <see cref="FirstDeathStoreTests"/>.
/// </summary>
[TestFixture]
public sealed class FirstDeathCryptBackfillTests
{
    private string _dbPath = default!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"solreign_crypt_backfill_test_{Guid.NewGuid():N}.db");
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

    // --- crypt_reported stamp semantics --------------------------------------------------------------

    [Test]
    public async Task FreshClaim_IsNotCryptReported_AndListsAsUnreported()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        Assert.That(await ClaimAsync(store, user), Is.True);

        Assert.That((await store.GetFirstDeathAsync(user))!.CryptReported, Is.False,
            "a fresh claim must not be pre-stamped as crypt-reported");

        var unreported = await store.GetCryptUnreportedFirstDeathsAsync();
        Assert.That(unreported.Select(r => r.User), Is.EqualTo(new[] { user }),
            "an unstamped claim is exactly what the backfill scan must find");
    }

    [Test]
    public async Task MarkCryptReported_TrueExactlyOnce_ThenFalseForever()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();
        await ClaimAsync(store, user);

        Assert.That(await store.TryMarkCryptReportedAsync(user), Is.True,
            "the first stamp after a claim must win");
        Assert.That(await store.TryMarkCryptReportedAsync(user), Is.False,
            "the conditional UPDATE's rowcount guard must reject a second stamp — the daemon " +
            "mints a plaque per POST, so a second win here would mean a duplicate plaque");
        Assert.That((await store.GetFirstDeathAsync(user))!.CryptReported, Is.True);
    }

    [Test]
    public async Task MarkCryptReported_WithoutAClaimRow_ReturnsFalse()
    {
        var store = new SeasonLedgerStore(_dbPath);
        Assert.That(await store.TryMarkCryptReportedAsync(Guid.NewGuid()), Is.False,
            "an account that never claimed must have nothing to stamp");
    }

    [Test]
    public async Task RacingCryptStamps_HandExactlyOneWin()
    {
        // Two store instances against the same file (each has its own process-local lock), so the
        // win must come from the conditional UPDATE itself — the FirstDeathStoreTests idiom.
        var storeA = new SeasonLedgerStore(_dbPath);
        var storeB = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();
        await ClaimAsync(storeA, user);

        var results = await Task.WhenAll(
            storeA.TryMarkCryptReportedAsync(user),
            storeB.TryMarkCryptReportedAsync(user));

        Assert.That(results.Count(marked => marked), Is.EqualTo(1),
            "racing claim-time/backfill stamps must hand the win to exactly one caller");
    }

    [Test]
    public async Task CryptStamp_And_RehireStamp_AreIndependent()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();
        await ClaimAsync(store, user);

        Assert.That(await store.TryMarkRehireShownAsync(user), Is.True);
        var record = await store.GetFirstDeathAsync(user);
        Assert.That(record!.CryptReported, Is.False,
            "the rehire beat's stamp must never consume the memorial's");

        Assert.That(await store.TryMarkCryptReportedAsync(user), Is.True);
        record = await store.GetFirstDeathAsync(user);
        Assert.Multiple(() =>
        {
            Assert.That(record!.RehireShown, Is.True);
            Assert.That(record.CryptReported, Is.True);
        });
    }

    // --- unreported-rows query -----------------------------------------------------------------------

    [Test]
    public async Task UnreportedQuery_ExcludesStampedRows_AndOrdersOldestDeathFirst()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();
        var stamped = Guid.NewGuid();

        // Claim through the real path (schema + defaults), then pin died_at_utc raw so the
        // ordering under test is deterministic rather than riding sub-tick claim timestamps.
        await ClaimAsync(store, older);
        await ClaimAsync(store, newer, roundId: 2);
        await ClaimAsync(store, stamped, roundId: 3);
        await store.TryMarkCryptReportedAsync(stamped);

        await using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE first_death SET died_at_utc = '2026-07-01T00:00:00.0000000Z' WHERE user_id = $older;
                UPDATE first_death SET died_at_utc = '2026-07-15T00:00:00.0000000Z' WHERE user_id = $newer;
                """;
            cmd.Parameters.AddWithValue("$older", older.ToString());
            cmd.Parameters.AddWithValue("$newer", newer.ToString());
            await cmd.ExecuteNonQueryAsync();
        }

        var unreported = await store.GetCryptUnreportedFirstDeathsAsync();

        Assert.That(unreported.Select(r => r.User), Is.EqualTo(new[] { older, newer }),
            "the scan must return only unstamped rows, earliest lost memorial first");
    }

    // --- v13.3 legacy schema migration ---------------------------------------------------------------

    [Test]
    public async Task LegacyV133Table_ComesForward_WithBankedRowsUnreported()
    {
        // Recreate the LIVE v13.3 shape by hand: the first_death table WITHOUT crypt_reported —
        // exactly the DB of a player whose once-ever first death fired before the wire shipped.
        await using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE first_death (
                    user_id        TEXT PRIMARY KEY,
                    round_id       INTEGER NOT NULL,
                    character_name TEXT NOT NULL,
                    cause          TEXT NOT NULL,
                    tours_at_death INTEGER NOT NULL,
                    title_at_death TEXT NOT NULL,
                    epitaph_id     TEXT NOT NULL,
                    died_at_utc    TEXT NOT NULL,
                    rehire_shown   INTEGER NOT NULL DEFAULT 0
                );
                INSERT INTO first_death
                    (user_id, round_id, character_name, cause, tours_at_death, title_at_death, epitaph_id, died_at_utc, rehire_shown)
                VALUES
                    ('00000000-0000-0000-0000-00000000abcd', 7, 'Kolton', 'MISADVENTURE', 0, 'Probationary Asset', '01', '2026-07-17T00:00:00.0000000Z', 1);
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        // Opening the store runs the schema battery: EnsureColumnAsync must append crypt_reported
        // with DEFAULT 0 — the banked row surfaces as an unreported memorial, nothing else changes.
        var store = new SeasonLedgerStore(_dbPath);
        var unreported = await store.GetCryptUnreportedFirstDeathsAsync();

        Assert.That(unreported, Has.Count.EqualTo(1),
            "a pre-wire claim row must come forward as a not-yet-minted memorial");
        var record = unreported[0];
        Assert.Multiple(() =>
        {
            Assert.That(record.User, Is.EqualTo(Guid.Parse("00000000-0000-0000-0000-00000000abcd")));
            Assert.That(record.CharacterName, Is.EqualTo("Kolton"));
            Assert.That(record.RehireShown, Is.True, "migration must not disturb the rehire stamp");
            Assert.That(record.CryptReported, Is.False);
        });

        // And the stamp machinery works on the migrated row.
        Assert.That(await store.TryMarkCryptReportedAsync(record.User), Is.True);
        Assert.That(await store.GetCryptUnreportedFirstDeathsAsync(), Is.Empty,
            "a stamped memorial must never re-report");
    }

    // --- BuildBackfillReport -------------------------------------------------------------------------

    private static FirstDeathRecord MakeRecord(
        Guid user,
        string cause = "VIOLENCE",
        int tours = 7,
        string title = "Quarterly Standout",
        string epitaphId = "09",
        string name = "Juno Pike")
    {
        return new FirstDeathRecord(
            user, 42, name, cause, tours, title, epitaphId, "2026-07-17T00:00:00.0000000Z",
            RehireShown: false, CryptReported: false);
    }

    [Test]
    public void BackfillReport_MatchesTheClaimTimeWirePayload_ByteForByte()
    {
        // The invariant that makes the backfill trustworthy: for the same claim, the re-composed
        // report serializes IDENTICALLY to what the claim-time path would have sent — the daemon
        // cannot tell a backfilled memorial from a live one.
        var user = Guid.NewGuid();
        var cause = FirstDeathCause.Burn;
        const int tours = 12;
        const string title = "Chain Closer";
        const string name = "Vex Marlow";

        var epitaph = FirstDeathEpitaphPicker.Pick(tours, cause, title);
        var claimTime = FirstDeathCryptReport.Build(user, name, epitaph.Text, cause, tours, title);

        var record = MakeRecord(user, cause: "BURN", tours: tours, title: title, epitaphId: epitaph.Id, name: name);
        var backfill = ProvidenceFirstDeathSystem.BuildBackfillReport(record);

        Assert.That(JsonSerializer.Serialize(backfill), Is.EqualTo(JsonSerializer.Serialize(claimTime)));
    }

    [Test]
    public void BackfillReport_RendersTitleIntoTemplatePlates()
    {
        var record = MakeRecord(Guid.NewGuid(), cause: "BURN", tours: 2, title: "Chain Closer", epitaphId: "04");
        var report = ProvidenceFirstDeathSystem.BuildBackfillReport(record);

        Assert.That(report.Epitaph, Is.EqualTo("Chain Closer. Briefly employed. Deeply filed."),
            "the persisted plate id must re-render with the RECORDED title substituted");
    }

    [Test]
    public void BackfillReport_ResolvesShortVariantPlateIds()
    {
        var record = MakeRecord(Guid.NewGuid(), epitaphId: "04s", tours: 2, cause: "BURN");
        var report = ProvidenceFirstDeathSystem.BuildBackfillReport(record);

        Assert.That(report.Epitaph, Is.EqualTo("Briefly employed. Deeply filed."),
            "short-variant ids (04s/05s/07s) are legal persisted ids and must resolve");
    }

    [Test]
    public void BackfillReport_UnknownPlateId_DegradesToTheDeterministicRePick()
    {
        var record = MakeRecord(Guid.NewGuid(), cause: "VACUUM", tours: 6, title: "Colleague", epitaphId: "99");
        var report = ProvidenceFirstDeathSystem.BuildBackfillReport(record);

        var expected = FirstDeathEpitaphPicker.Pick(6, FirstDeathCause.Vacuum, "Colleague").Text;
        Assert.That(report.Epitaph, Is.EqualTo(expected),
            "a retired plate id must fall back to the deterministic re-pick over the recorded triple");
    }

    [Test]
    public void BackfillReport_UnparseableCause_DegradesToUnknown()
    {
        var record = MakeRecord(Guid.NewGuid(), cause: "GLITTER", epitaphId: "01");
        var report = ProvidenceFirstDeathSystem.BuildBackfillReport(record);

        Assert.That(report.CauseLabel, Is.EqualTo(FirstDeathCopy.CauseLabelFor(FirstDeathCause.Unknown)),
            "an unparseable cause string (never expected — closed vocabulary) must degrade to Unknown");
    }

    [Test]
    public void BackfillReport_CarriesEveryWireField_AndTheRedactionLaw()
    {
        var user = Guid.NewGuid();
        var record = MakeRecord(user);
        var report = ProvidenceFirstDeathSystem.BuildBackfillReport(record);

        Assert.Multiple(() =>
        {
            Assert.That(report.VictimGuid, Is.EqualTo(user.ToString()));
            Assert.That(report.FirstDeath, Is.True);
            Assert.That(report.CharacterName, Is.EqualTo("Juno Pike"));
            Assert.That(report.CauseLabel, Is.EqualTo(FirstDeathCopy.CauseLabelFor(FirstDeathCause.Violence)));
            Assert.That(report.Tours, Is.EqualTo(7));
            Assert.That(report.Title, Is.EqualTo("Quarterly Standout"));
            Assert.That(report.AttackerGuid, Is.Empty,
                "redaction law §3.3: the first-death surface never carries attacker data");
        });
    }

    // --- FirstDeathCryptBackfillQueue ----------------------------------------------------------------

    private static FirstDeathCryptBackfillItem MakeItem(Guid user)
    {
        return new FirstDeathCryptBackfillItem(user, ProvidenceFirstDeathSystem.BuildBackfillReport(MakeRecord(user)));
    }

    [Test]
    public void Queue_ReleasesInFifoOrder_OnePerSpacingWindow()
    {
        var queue = new FirstDeathCryptBackfillQueue();
        var spacing = TimeSpan.FromSeconds(1.5);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        queue.Enqueue(MakeItem(first));
        queue.Enqueue(MakeItem(second));

        Assert.Multiple(() =>
        {
            Assert.That(queue.TryDequeueDue(TimeSpan.FromSeconds(10), spacing, out var a), Is.True);
            Assert.That(a.AccountId, Is.EqualTo(first), "oldest banked memorial dispatches first");

            Assert.That(queue.TryDequeueDue(TimeSpan.FromSeconds(10.5), spacing, out _), Is.False,
                "inside the spacing window nothing may release — the rate limiter would drop it");
            Assert.That(queue.Count, Is.EqualTo(1), "a paced-out item must stay queued, not burn");

            Assert.That(queue.TryDequeueDue(TimeSpan.FromSeconds(11.5), spacing, out var b), Is.True,
                "the window reopens exactly at now + spacing");
            Assert.That(b.AccountId, Is.EqualTo(second));

            Assert.That(queue.TryDequeueDue(TimeSpan.FromSeconds(20), spacing, out _), Is.False,
                "an empty queue releases nothing");
        });
    }

    [Test]
    public void Queue_EmptyPolls_DoNotConsumeThePacingWindow()
    {
        var queue = new FirstDeathCryptBackfillQueue();
        var spacing = TimeSpan.FromSeconds(1.5);

        Assert.That(queue.TryDequeueDue(TimeSpan.FromSeconds(5), spacing, out _), Is.False);

        queue.Enqueue(MakeItem(Guid.NewGuid()));
        Assert.That(queue.TryDequeueDue(TimeSpan.FromSeconds(5), spacing, out _), Is.True,
            "an empty poll must not have started a pacing window");
    }

    [Test]
    public void Queue_Clear_DropsItemsAndReopensTheWindow()
    {
        var queue = new FirstDeathCryptBackfillQueue();
        var spacing = TimeSpan.FromSeconds(1.5);
        queue.Enqueue(MakeItem(Guid.NewGuid()));
        Assert.That(queue.TryDequeueDue(TimeSpan.FromSeconds(1), spacing, out _), Is.True);

        queue.Enqueue(MakeItem(Guid.NewGuid()));
        queue.Clear();

        Assert.That(queue.Count, Is.Zero, "round boundaries drop pending items (the scan reloads them)");

        queue.Enqueue(MakeItem(Guid.NewGuid()));
        Assert.That(queue.TryDequeueDue(TimeSpan.Zero, spacing, out _), Is.True,
            "Clear must also reset pacing — a fresh round starts with an open window");
    }
}
