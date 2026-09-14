#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._Solreign.SeasonLedger;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Unit coverage for the <c>mark</c> table (SeasonLedgerStore.Mark.cs) — the exactly-once
///     linchpin of the Mark keepsake (MARK-SPEC §3.1/§7):
///       * <c>TryClaimMarkAsync</c> claims exactly once per account, including across racing
///         concurrent callers on separate store instances against the same file, and hands out
///         DENSE, planting-ordered slot indices atomically with the claim (the spec's
///         fold-the-count-into-the-INSERT verification, resolved with a BEGIN-IMMEDIATE tx);
///       * a season bump does NOT uproot anyone (career-scoped table);
///       * <c>TryMarkNudgeShownAsync</c> conditional once-ever semantics;
///       * <c>TryRecordVisitAsync</c> fires only on a stage ADVANCE (the anti-fatigue guard).
///     Same per-test temp-DB harness as <see cref="FirstDeathStoreTests"/> — never a bin path,
///     never a shared file, per-instance unique temp paths (the resolved §7 fixture verification:
///     the first-death store tests use per-test temp FILE DBs, not :memory:, and so do these).
/// </summary>
[TestFixture]
[TestOf(typeof(SeasonLedgerStore))]
public sealed class MarkStoreTests
{
    private string _dbPath = default!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"solreign_mark_test_{Guid.NewGuid():N}.db");
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

    private static Task<(bool Claimed, int Slot)> ClaimAsync(
        SeasonLedgerStore store,
        Guid user,
        int roundId = 1,
        string kind = "SAPLING")
    {
        return store.TryClaimMarkAsync(user, kind, roundId, "SolreignOasis", "Juno Pike", 0);
    }

    // --- Claim ------------------------------------------------------------------------------------

    [Test]
    public async Task FirstClaim_Succeeds_AndRoundTripsEveryColumn()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        var (claimed, slot) = await store.TryClaimMarkAsync(
            user, "LAMP", 42, "SolreignNocturne", "Juno Pike", 7);

        Assert.Multiple(() =>
        {
            Assert.That(claimed, Is.True);
            Assert.That(slot, Is.EqualTo(0), "the first mark ever must take slot 0");
        });

        var record = await store.GetMarkAsync(user);
        Assert.That(record, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(record!.User, Is.EqualTo(user));
            Assert.That(record.Kind, Is.EqualTo("LAMP"));
            Assert.That(record.PlantedRoundId, Is.EqualTo(42));
            Assert.That(record.PlantedUtc, Is.Not.Empty);
            Assert.That(record.PlantedMap, Is.EqualTo("SolreignNocturne"));
            Assert.That(record.CharacterName, Is.EqualTo("Juno Pike"));
            Assert.That(record.ToursAtPlanting, Is.EqualTo(7));
            Assert.That(record.SlotIndex, Is.EqualTo(0));
            Assert.That(record.NudgeShown, Is.False, "a fresh claim must not be pre-stamped as nudged");
            Assert.That(record.LastVisitUtc, Is.EqualTo(record.PlantedUtc),
                "last_visit_utc must initialize to planted_utc (spec §3.1)");
            Assert.That(record.LastVisitStage, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task SecondClaim_ReturnsFalse_AndNeverMutatesTheOriginalRow()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        Assert.That((await ClaimAsync(store, user)).Claimed, Is.True);

        var (claimed, slot) = await store.TryClaimMarkAsync(
            user, "NAMEPLATE", 2, "SolreignVerdant", "Second Name", 3);
        Assert.Multiple(() =>
        {
            Assert.That(claimed, Is.False, "a second plant attempt must never claim — one mark per account, EVER");
            Assert.That(slot, Is.EqualTo(-1));
        });

        var record = await store.GetMarkAsync(user);
        Assert.Multiple(() =>
        {
            Assert.That(record!.Kind, Is.EqualTo("SAPLING"), "the losing claim must not overwrite the winner");
            Assert.That(record.PlantedRoundId, Is.EqualTo(1));
            Assert.That(record.CharacterName, Is.EqualTo("Juno Pike"));
        });
    }

    [Test]
    public async Task RacingClaims_SameAccount_AcrossStoreInstances_HandExactlyOneWin()
    {
        // Two independent store instances against the SAME file — harder than in-process racing
        // (each instance has its own _lock), so the win must come from SQLite itself.
        var storeA = new SeasonLedgerStore(_dbPath);
        var storeB = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        var results = await Task.WhenAll(ClaimAsync(storeA, user), ClaimAsync(storeB, user));

        Assert.Multiple(() =>
        {
            Assert.That(results.Count(r => r.Claimed), Is.EqualTo(1),
                "racing claims must hand the win to exactly one caller");
            Assert.That(results.Single(r => r.Claimed).Slot, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task RacingClaims_DistinctAccounts_AcrossStoreInstances_GetDistinctDenseSlots()
    {
        // The §3.1 slot-atomicity resolution under fire: two DIFFERENT accounts racing from separate
        // store instances must both win and must never share a slot (the BEGIN-IMMEDIATE claim
        // serializes the COUNT+INSERT, so no torn read of the slot counter is possible).
        var storeA = new SeasonLedgerStore(_dbPath);
        var storeB = new SeasonLedgerStore(_dbPath);

        var results = await Task.WhenAll(
            ClaimAsync(storeA, Guid.NewGuid()),
            ClaimAsync(storeB, Guid.NewGuid()));

        Assert.Multiple(() =>
        {
            Assert.That(results.Count(r => r.Claimed), Is.EqualTo(2),
                "distinct accounts must claim independently");
            Assert.That(results.Select(r => r.Slot), Is.EquivalentTo(new[] { 0, 1 }),
                "racing distinct-account claims must take dense, non-colliding slots");
        });
    }

    [Test]
    public async Task Slots_AreDense_AndFollowPlantingOrder()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var users = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        for (var i = 0; i < users.Length; i++)
        {
            var (claimed, slot) = await ClaimAsync(store, users[i], roundId: i + 1);
            Assert.Multiple(() =>
            {
                Assert.That(claimed, Is.True);
                Assert.That(slot, Is.EqualTo(i), "slot order is planting order, forever");
            });
        }

        // Losing re-claims must not disturb the dense sequence.
        Assert.That((await ClaimAsync(store, users[0], roundId: 99)).Claimed, Is.False);
        var (claimedLate, slotLate) = await ClaimAsync(store, Guid.NewGuid(), roundId: 100);
        Assert.Multiple(() =>
        {
            Assert.That(claimedLate, Is.True);
            Assert.That(slotLate, Is.EqualTo(3), "a failed re-claim must not burn a slot index");
        });
    }

    [Test]
    public async Task SeasonBump_DoesNotUprootAnyone()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        Assert.That((await ClaimAsync(store, user)).Claimed, Is.True);

        await store.BumpSeasonAsync();

        Assert.That((await ClaimAsync(store, user, roundId: 99)).Claimed, Is.False,
            "a mark is a career event — a season bump must never reset the claim");
        Assert.That((await store.GetMarkAsync(user))!.PlantedRoundId, Is.EqualTo(1));
    }

    [Test]
    public async Task GetMark_UnknownAccount_ReturnsNull()
    {
        var store = new SeasonLedgerStore(_dbPath);
        Assert.That(await store.GetMarkAsync(Guid.NewGuid()), Is.Null);
    }

    // --- Projection read --------------------------------------------------------------------------

    [Test]
    public async Task GetAllMarks_ReturnsSlotOrder_AndHonorsTheCapacityLimit()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var users = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        foreach (var user in users)
            await ClaimAsync(store, user);

        var all = await store.GetAllMarksAsync(10);
        Assert.Multiple(() =>
        {
            Assert.That(all.Select(m => m.SlotIndex), Is.EqualTo(new[] { 0, 1, 2 }),
                "the projection read must come back in slot order");
            Assert.That(all.Select(m => m.User), Is.EqualTo(users),
                "slot order is planting order");
        });

        var capped = await store.GetAllMarksAsync(2);
        Assert.That(capped.Select(m => m.SlotIndex), Is.EqualTo(new[] { 0, 1 }),
            "over-capacity records must not come back — seniority (earliest planted) projects");
    }

    [Test]
    public async Task GetAllMarks_NonPositiveLimit_ReturnsEmpty()
    {
        var store = new SeasonLedgerStore(_dbPath);
        await ClaimAsync(store, Guid.NewGuid());

        Assert.That(await store.GetAllMarksAsync(0), Is.Empty);
        Assert.That(await store.GetAllMarksAsync(-5), Is.Empty);
    }

    // --- Nudge stamp ------------------------------------------------------------------------------

    [Test]
    public async Task MarkNudgeShown_TrueExactlyOnce_ThenFalseForever()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();
        await ClaimAsync(store, user);

        Assert.That(await store.TryMarkNudgeShownAsync(user), Is.True,
            "the first nudge stamp after a claim must win");
        Assert.That(await store.TryMarkNudgeShownAsync(user), Is.False,
            "the conditional UPDATE's rowcount guard must reject a second stamp");
        Assert.That((await store.GetMarkAsync(user))!.NudgeShown, Is.True);
    }

    [Test]
    public async Task MarkNudgeShown_WithoutAClaimRow_ReturnsFalse()
    {
        var store = new SeasonLedgerStore(_dbPath);
        Assert.That(await store.TryMarkNudgeShownAsync(Guid.NewGuid()), Is.False,
            "an account that never planted must have nothing to stamp");
    }

    [Test]
    public async Task RacingNudgeStamps_HandExactlyOneWin()
    {
        var storeA = new SeasonLedgerStore(_dbPath);
        var storeB = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();
        await ClaimAsync(storeA, user);

        var results = await Task.WhenAll(
            storeA.TryMarkNudgeShownAsync(user),
            storeB.TryMarkNudgeShownAsync(user));

        Assert.That(results.Count(marked => marked), Is.EqualTo(1),
            "a racing double-spawn must deliver the nudge exactly once");
    }

    // --- Visit stamp (the anti-fatigue guard) -----------------------------------------------------

    [Test]
    public async Task RecordVisit_FiresOnlyOnAStageAdvance()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();
        await ClaimAsync(store, user);

        var visitUtc = DateTime.UtcNow.ToString("o");
        Assert.That(await store.TryRecordVisitAsync(user, 1, visitUtc), Is.True,
            "the first visit at an advanced stage must stamp");

        var record = await store.GetMarkAsync(user);
        Assert.Multiple(() =>
        {
            Assert.That(record!.LastVisitStage, Is.EqualTo(1));
            Assert.That(record.LastVisitUtc, Is.EqualTo(visitUtc),
                "the stamp must record the caller's exact visit time");
        });

        Assert.That(await store.TryRecordVisitAsync(user, 1, DateTime.UtcNow.ToString("o")), Is.False,
            "a re-visit at the SAME stage must stay silent (anti-fatigue: last_visit_stage < stage)");
        Assert.That(await store.TryRecordVisitAsync(user, 0, DateTime.UtcNow.ToString("o")), Is.False,
            "a stage can never regress");
        Assert.That(await store.TryRecordVisitAsync(user, 3, DateTime.UtcNow.ToString("o")), Is.True,
            "a multi-stage skip (2/7/21-day absence) still stamps once");
        Assert.That((await store.GetMarkAsync(user))!.LastVisitStage, Is.EqualTo(3));
    }

    [Test]
    public async Task RecordVisit_WithoutAClaimRow_ReturnsFalse()
    {
        var store = new SeasonLedgerStore(_dbPath);
        Assert.That(
            await store.TryRecordVisitAsync(Guid.NewGuid(), 1, DateTime.UtcNow.ToString("o")),
            Is.False);
    }

    [Test]
    public async Task RacingVisitStamps_AtTheSameStage_HandExactlyOneWin()
    {
        var storeA = new SeasonLedgerStore(_dbPath);
        var storeB = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();
        await ClaimAsync(storeA, user);

        var utc = DateTime.UtcNow.ToString("o");
        var results = await Task.WhenAll(
            storeA.TryRecordVisitAsync(user, 2, utc),
            storeB.TryRecordVisitAsync(user, 2, utc));

        Assert.That(results.Count(stamped => stamped), Is.EqualTo(1),
            "a racing double-spawn must deliver at most one growth line per stage advance");
    }
}
