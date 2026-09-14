#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.Library;
using Content.Server._Solreign.SeasonLedger;
using Content.Server.Station.Systems;
using Content.Shared._Solreign.Library;
using Content.Shared.CCVar;
using Content.Shared.Paper;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     STATION-LIBRARY ECS wiring: drives <see cref="SolreignLibrarySystem"/> end-to-end against
///     the real connected pool session — dormancy (enabled=false is the SHIP posture and must be
///     zero-behavior), a submission attempt with the Director channel unconfigured failing closed
///     (the "pending forever" outcome for a dead daemon — nothing is EVER written before the
///     daemon approves), round-start projection materializing ledger rows as book items inserted
///     into the Annex's own storage, and report/hide removing a work from the next projection.
///
///     Pure mechanics (the atomic quota/seed-target rule engine, hide/unhide, length caps, the seed
///     corpus) are exhaustively unit-tested without a server in
///     Content.Tests/_Solreign/Library*Tests.cs — this file covers only what those tests cannot
///     reach: the CVar-gated system wiring itself.
///
///     SCOPE BOUNDARY (receipt-documented, not hidden — the NoticeboardIntegrationTest precedent):
///     a full end-to-end "player submits -&gt; daemon classifier approves -&gt; work lands on the
///     shelf" round trip needs a live or in-process-faked HTTP daemon answering
///     <c>DirectorChannel</c>'s signed request — out of this lane's foreground budget, and doubly
///     so here since the "library" daemon surface has not landed on <c>solreign-director</c>'s
///     <c>main</c> yet (see this lane's receipt). What IS proven end to end, without any daemon: a
///     submission with the channel unconfigured fails closed and writes nothing, and the
///     PROJECT/REPORT paths (which never touch the daemon) work against rows written through the
///     exact same atomic store API the real post-approval commit uses.
/// </summary>
[TestFixture]
public sealed class LibraryIntegrationTest : GameTest
{
    // Spawns entities, flips CVars, writes ledger rows directly — this server must never be
    // handed back to the pool (the MarkGardenIntegrationTest/NoticeboardIntegrationTest precedent).
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        DummyTicker = false,
    };

    private static string Now() => DateTime.UtcNow.ToString("o");

    /// <summary>A fresh, per-test archive id — the NoticeboardIntegrationTest.FreshBoardId isolation idiom.</summary>
    private static string FreshArchiveId() => $"test-{Guid.NewGuid():N}";

    private static List<(EntityUid Uid, SolreignLibraryBookComponent Comp)> CollectBooks(IEntityManager entMan)
    {
        var books = new List<(EntityUid, SolreignLibraryBookComponent)>();
        var query = entMan.AllEntityQueryEnumerator<SolreignLibraryBookComponent>();
        while (query.MoveNext(out var uid, out var comp))
            books.Add((uid, comp));
        return books;
    }

    private async Task<bool> PollAsync(Func<bool> condition, int maxTicks = 60)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            var hit = false;
            await Server.WaitPost(() => hit = condition());
            if (hit)
                return true;
            await Server.WaitRunTicks(1);
        }

        return false;
    }

    [Test]
    public async Task Dormant_ShipPosture_IsZeroBehavior()
    {
        var server = Server;
        var entMan = server.EntMan;
        var library = server.System<SolreignLibrarySystem>();
        var ledger = server.System<SeasonLedgerSystem>();
        var stations = server.System<StationSystem>();

        // The SHIP posture is the tested posture.
        Assert.That(server.CfgMan.GetCVar(CCVars.SolreignLibraryEnabled), Is.False,
            "solreign.library.enabled must ship FALSE (dormant by mission rail)");

        EntityUid mob = default;
        EntityUid station = default;
        EntityUid annex = default;
        var archiveId = FreshArchiveId();
        await server.WaitPost(() =>
        {
            mob = ServerSession!.AttachedEntity!.Value;
            station = stations.GetOwningStation(mob)
                      ?? throw new InvalidOperationException("Connected mob has no owning station.");
            annex = entMan.SpawnEntity("SolreignLibraryAnnex", entMan.GetComponent<TransformComponent>(mob).Coordinates);
            entMan.GetComponent<SolreignLibraryAnnexComponent>(annex).ArchiveId = archiveId;
            library.ResetRoundStateForTests();
            library.MaterializeAndProjectForTests(station);
        });
        await server.WaitRunTicks(20);

        await server.WaitAssertion(() =>
        {
            Assert.That(CollectBooks(entMan), Is.Empty,
                "enabled=false must never materialize a book — not even from a spawned Annex");
        });

        Assert.That(await ledger.GetRecentLibraryWorksAsync(archiveId, 10), Is.Empty,
            "enabled=false must be a full kill switch: MapInit seeding may never touch a row");

        // A submit attempt while off must also be a full no-op.
        await server.WaitPost(() => library.TrySubmitForTests(annex, mob, "My Book", "My words."));
        await server.WaitRunTicks(20);

        Assert.That(await ledger.GetRecentLibraryWorksAsync(archiveId, 10), Is.Empty,
            "enabled=false must be a full kill switch: the submit path may never touch a row");
    }

    [Test]
    public async Task Enabled_DirectorChannelNotConfigured_SubmitAttempt_FailsClosed_NoRowWritten()
    {
        var server = Server;
        var entMan = server.EntMan;
        var library = server.System<SolreignLibrarySystem>();
        var ledger = server.System<SeasonLedgerSystem>();

        server.CfgMan.SetCVar(CCVars.SolreignLibraryEnabled, true);
        // solreign.director.token stays at its default (empty) — the channel is unconfigured, the
        // same "daemon unreachable" shape as a genuinely offline daemon.
        Assert.That(server.CfgMan.GetCVar(CCVars.SolreignDirectorToken), Is.Empty,
            "test precondition: the Director channel must be unconfigured");

        EntityUid mob = default;
        EntityUid annex = default;
        var archiveId = FreshArchiveId();
        await server.WaitPost(() =>
        {
            library.ResetRoundStateForTests();
            mob = ServerSession!.AttachedEntity!.Value;
            annex = entMan.SpawnEntity("SolreignLibraryAnnex", entMan.GetComponent<TransformComponent>(mob).Coordinates);
            entMan.GetComponent<SolreignLibraryAnnexComponent>(annex).ArchiveId = archiveId;
        });
        await server.WaitRunTicks(20); // let MapInit Providence-seed attempt run to completion (and fail closed) too

        Assert.That(await ledger.GetRecentLibraryWorksAsync(archiveId, 10), Is.Empty,
            "an unconfigured Director channel must mean Providence seeding writes nothing either");

        await server.WaitPost(() => library.TrySubmitForTests(annex, mob, "My Book", "My words."));
        await server.WaitRunTicks(20);

        Assert.That(await ledger.GetRecentLibraryWorksAsync(archiveId, 10), Is.Empty,
            "a submit attempt with no configured Director channel must fail closed and write nothing " +
            "— this IS the 'pending forever' outcome for a dead daemon: no row, ever");
    }

    [Test]
    public async Task Projection_MaterializesRecentWorksAsBooksInsertedIntoTheAnnexShelf()
    {
        // Bypasses the daemon entirely (the scope boundary noted in this file's header) by writing
        // an approved row directly through the SAME atomic store API the real write path commits
        // through post-approval — proving the round-start PROJECT half of the system in isolation.
        var server = Server;
        var entMan = server.EntMan;
        var library = server.System<SolreignLibrarySystem>();
        var ledger = server.System<SeasonLedgerSystem>();
        var stations = server.System<StationSystem>();

        server.CfgMan.SetCVar(CCVars.SolreignLibraryEnabled, true);

        var archiveId = FreshArchiveId();
        var (posted, workId, rejection) = await ledger.TryPostLibraryWorkAsync(
            archiveId, Guid.NewGuid(), false, "Kolton", "Notes From The Bar", "It was a dark and stormy shift.", 1, Now());
        Assert.That(posted, Is.True, rejection?.ToString());

        EntityUid mob = default;
        EntityUid station = default;
        EntityUid annex = default;
        await server.WaitPost(() =>
        {
            mob = ServerSession!.AttachedEntity!.Value;
            station = stations.GetOwningStation(mob)
                      ?? throw new InvalidOperationException("Connected mob has no owning station.");
            annex = entMan.SpawnEntity("SolreignLibraryAnnex", entMan.GetComponent<TransformComponent>(mob).Coordinates);
            entMan.GetComponent<SolreignLibraryAnnexComponent>(annex).ArchiveId = archiveId;
            library.ResetRoundStateForTests();
            library.MaterializeAndProjectForTests(station);
        });

        var projected = await PollAsync(() => CollectBooks(entMan).Count == 1);
        Assert.That(projected, Is.True, "round-start projection must seat exactly one book for the one ledger row");

        await server.WaitAssertion(() =>
        {
            var books = CollectBooks(entMan);
            Assert.That(books, Has.Count.EqualTo(1));
            var (uid, comp) = books[0];

            Assert.Multiple(() =>
            {
                Assert.That(comp.WorkId, Is.EqualTo(workId));
                Assert.That(entMan.GetComponent<MetaDataComponent>(uid).EntityName, Is.EqualTo("Notes From The Bar"),
                    "the spawned book's name must be stamped from the ledger row's title");
                Assert.That(entMan.GetComponent<PaperComponent>(uid).Content, Is.EqualTo("It was a dark and stormy shift."),
                    "the spawned book's Paper content must be stamped from the ledger row's body — reading it reuses the existing Paper UI");
                Assert.That(entMan.GetComponent<TransformComponent>(uid).ParentUid, Is.EqualTo(annex),
                    "the book must be physically inserted into the Annex's own storage container");
            });
        });
    }

    [Test]
    public async Task Report_HidesTheWork_AndTheNextProjectionOmitsIt()
    {
        var server = Server;
        var entMan = server.EntMan;
        var library = server.System<SolreignLibrarySystem>();
        var ledger = server.System<SeasonLedgerSystem>();
        var stations = server.System<StationSystem>();

        server.CfgMan.SetCVar(CCVars.SolreignLibraryEnabled, true);

        var archiveId = FreshArchiveId();
        var (_, workId, _) = await ledger.TryPostLibraryWorkAsync(
            archiveId, Guid.NewGuid(), false, "Kolton", "Notes From The Bar", "Free toolbox in the bar.", 1, Now());

        EntityUid mob = default;
        EntityUid station = default;
        EntityUid annex = default;
        await server.WaitPost(() =>
        {
            mob = ServerSession!.AttachedEntity!.Value;
            station = stations.GetOwningStation(mob)
                      ?? throw new InvalidOperationException("Connected mob has no owning station.");
            annex = entMan.SpawnEntity("SolreignLibraryAnnex", entMan.GetComponent<TransformComponent>(mob).Coordinates);
            entMan.GetComponent<SolreignLibraryAnnexComponent>(annex).ArchiveId = archiveId;
            library.ResetRoundStateForTests();
            library.MaterializeAndProjectForTests(station);
        });

        var projected = await PollAsync(() => CollectBooks(entMan).Count == 1);
        Assert.That(projected, Is.True, "setup failed: work never projected");
        var book = CollectBooks(entMan)[0].Uid;

        await server.WaitPost(() => library.TryReportForTests(book, mob, workId));
        await server.WaitRunTicks(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(book), Is.True, "a reported work's physical copy must come off the shelf immediately");
        });

        Assert.That(await ledger.GetRecentLibraryWorksAsync(archiveId, 10), Is.Empty,
            "a hidden work must vanish from the very next projection read");

        // A fresh projection pass (a new round) must not re-materialize the hidden work.
        await server.WaitPost(() =>
        {
            library.ResetRoundStateForTests();
            library.MaterializeAndProjectForTests(station);
        });
        await server.WaitRunTicks(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(CollectBooks(entMan), Is.Empty, "a hidden work must never re-project");
        });
    }
}
