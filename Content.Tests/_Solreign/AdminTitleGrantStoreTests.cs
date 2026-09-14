using System;
using System.IO;
using System.Threading.Tasks;
using Content.Server._Solreign.SeasonLedger;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Persistence contract for admin-granted titles (community rewards program): grants survive store
///     reopen (i.e. across rounds/server restarts), are season-scoped like earned titles, revoke cleanly,
///     and carry their who/when paper trail.
/// </summary>
[TestFixture]
[TestOf(typeof(SeasonLedgerStore))]
public sealed class AdminTitleGrantStoreTests
{
    private string _dbPath = default!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"solreign_admin_title_test_{Guid.NewGuid():N}.db");
    }

    [TearDown]
    public void TearDown()
    {
        // Best-effort cleanup of the temp DB + WAL/SHM sidecars.
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
    public async Task NoGrant_ReturnsNull()
    {
        var store = new SeasonLedgerStore(_dbPath);
        Assert.That(await store.GetAdminTitleAsync(Guid.NewGuid()), Is.Null);
    }

    [Test]
    public async Task Grant_ThenGet_ReturnsTitle()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.SetAdminTitleAsync(user, "Community Hero", "adminA");

        Assert.That(await store.GetAdminTitleAsync(user), Is.EqualTo("Community Hero"));
    }

    [Test]
    public async Task Grant_PersistsAcrossStoreReopen()
    {
        var user = Guid.NewGuid();
        await new SeasonLedgerStore(_dbPath).SetAdminTitleAsync(user, "Community Hero", "adminA");

        // A fresh store over the same file is the "next round / server restart" case.
        var reopened = new SeasonLedgerStore(_dbPath);
        Assert.That(await reopened.GetAdminTitleAsync(user), Is.EqualTo("Community Hero"));
    }

    [Test]
    public async Task SecondGrant_ReplacesFirst()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.SetAdminTitleAsync(user, "Community Hero", "adminA");
        await store.SetAdminTitleAsync(user, "Raffle Winner", "adminB");

        Assert.That(await store.GetAdminTitleAsync(user), Is.EqualTo("Raffle Winner"));
    }

    [Test]
    public async Task GrantsAreScopedPerAccount()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var granted = Guid.NewGuid();
        var bystander = Guid.NewGuid();

        await store.SetAdminTitleAsync(granted, "Community Hero", "adminA");

        Assert.That(await store.GetAdminTitleAsync(bystander), Is.Null);
    }

    [Test]
    public async Task Revoke_RemovesGrant_AndReportsWhetherOneExisted()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.SetAdminTitleAsync(user, "Community Hero", "adminA");

        Assert.That(await store.RevokeAdminTitleAsync(user), Is.True, "an existing grant revokes");
        Assert.That(await store.GetAdminTitleAsync(user), Is.Null, "the grant is gone after revoke");
        Assert.That(await store.RevokeAdminTitleAsync(user), Is.False, "a second revoke finds nothing");
    }

    [Test]
    public async Task RevokeWithNoGrant_ReturnsFalse()
    {
        var store = new SeasonLedgerStore(_dbPath);
        Assert.That(await store.RevokeAdminTitleAsync(Guid.NewGuid()), Is.False);
    }

    [Test]
    public async Task SeasonBump_RetiresGrant_LikeEarnedTitles()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.SetAdminTitleAsync(user, "Community Hero", "adminA");
        await store.BumpSeasonAsync();

        Assert.That(await store.GetAdminTitleAsync(user), Is.Null,
            "a season bump wipes the slate for admin grants exactly as it does for earned titles");

        // The retired season's row stays archived on record — only the CURRENT season is consulted.
        Assert.That(await CountGrantRowsAsync(user), Is.EqualTo(1));
    }

    [Test]
    public async Task GrantInNewSeason_WorksIndependentlyOfArchivedGrant()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.SetAdminTitleAsync(user, "Community Hero", "adminA");
        await store.BumpSeasonAsync();
        await store.SetAdminTitleAsync(user, "Returning Champion", "adminB");

        Assert.That(await store.GetAdminTitleAsync(user), Is.EqualTo("Returning Champion"));
        Assert.That(await CountGrantRowsAsync(user), Is.EqualTo(2), "old season's row remains archived");
    }

    [Test]
    public async Task Grant_RecordsWhoAndWhen_ForThePaperTrail()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.SetAdminTitleAsync(user, "Community Hero", "adminA");

        var (grantedBy, grantedUtc) = await ReadGrantAuditAsync(user);
        Assert.Multiple(() =>
        {
            Assert.That(grantedBy, Is.EqualTo("adminA"));
            Assert.That(DateTime.TryParse(grantedUtc, out _), Is.True, "granted_utc is a parseable timestamp");
        });
    }

    [Test]
    public void EmptyTitle_IsRejectedByTheStoreGuard()
    {
        var store = new SeasonLedgerStore(_dbPath);
        Assert.ThrowsAsync<ArgumentException>(() => store.SetAdminTitleAsync(Guid.NewGuid(), " ", "adminA"));
    }

    [Test]
    public void MarkupTitle_IsRejectedAtThePersistenceBoundary()
    {
        // The full TitleGrantRules policy holds at the store, not only in the console command — a future
        // Director/website caller must not be able to persist markup that reaches PushMarkup.
        var store = new SeasonLedgerStore(_dbPath);
        Assert.ThrowsAsync<ArgumentException>(
            () => store.SetAdminTitleAsync(Guid.NewGuid(), "[color=red]Admin[/color]", "adminA"));
    }

    [Test]
    public void OverLengthTitle_IsRejectedAtThePersistenceBoundary()
    {
        var store = new SeasonLedgerStore(_dbPath);
        Assert.ThrowsAsync<ArgumentException>(
            () => store.SetAdminTitleAsync(Guid.NewGuid(), new string('x', TitleGrantRules.MaxLength + 1), "adminA"));
    }

    [Test]
    public async Task StorePersistsTheNormalizedForm()
    {
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.SetAdminTitleAsync(user, "  Community \t Hero  ", "adminA");

        Assert.That(await store.GetAdminTitleAsync(user), Is.EqualTo("Community Hero"));
    }

    [Test]
    public void MissingGrantedBy_IsRejectedByTheStoreGuard()
    {
        var store = new SeasonLedgerStore(_dbPath);
        Assert.ThrowsAsync<ArgumentException>(() => store.SetAdminTitleAsync(Guid.NewGuid(), "Community Hero", ""));
    }

    [Test]
    public async Task AdminGrant_DoesNotTouchEarnedAnnouncementRecord()
    {
        // title_grants (earned-title ceremony dedup) and admin_title_grants are separate ledgers.
        var store = new SeasonLedgerStore(_dbPath);
        var user = Guid.NewGuid();

        await store.SetAnnouncedTitleAsync(user, "Chain Closer");
        await store.SetAdminTitleAsync(user, "Community Hero", "adminA");

        Assert.That(await store.GetAnnouncedTitleAsync(user), Is.EqualTo("Chain Closer"));

        await store.RevokeAdminTitleAsync(user);

        Assert.That(await store.GetAnnouncedTitleAsync(user), Is.EqualTo("Chain Closer"));
    }

    private async Task<long> CountGrantRowsAsync(Guid user)
    {
        await using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM admin_title_grants WHERE user_id = $uid;";
        cmd.Parameters.AddWithValue("$uid", user.ToString());
        return (long) (await cmd.ExecuteScalarAsync() ?? 0L);
    }

    private async Task<(string GrantedBy, string GrantedUtc)> ReadGrantAuditAsync(Guid user)
    {
        await using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT granted_by, granted_utc FROM admin_title_grants WHERE user_id = $uid;";
        cmd.Parameters.AddWithValue("$uid", user.ToString());
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True, "expected a grant row on record");
        return (reader.GetString(0), reader.GetString(1));
    }
}
