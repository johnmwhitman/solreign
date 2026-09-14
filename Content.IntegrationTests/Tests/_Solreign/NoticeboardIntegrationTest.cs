#nullable enable
using System;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.Noticeboards;
using Content.Server._Solreign.SeasonLedger;
using Content.Shared._Solreign.Noticeboards;
using Content.Shared.CCVar;
using NUnit.Framework;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Crew Noticeboards ECS wiring (v14 wave-1 #2, docs/council/2026-07-17-player-text-safety.md
///     — the C2 posture memo). Pure rule mechanics (the atomic quota/capacity/cooldown engine,
///     hide/unhide, expiry sweep) are exhaustively unit-tested without a server in
///     Content.Tests/_Solreign/Noticeboard*Tests.cs — this file covers only what those tests
///     cannot reach: the CVar-gated system wiring itself.
///
///     SCOPE BOUNDARY (receipt-documented, not hidden): a full end-to-end "player posts → daemon
///     classifier approves → note lands on the board" round trip needs a live or in-process-faked
///     HTTP daemon answering DirectorChannel's signed request — out of this lane's foreground
///     budget. What IS proven end-to-end here without any daemon: dormancy is genuinely zero-behavior, a
///     post attempt with the Director channel unconfigured fails closed and writes nothing, and
///     the READ/PROJECT and REPORT/HIDE paths (which never touch the daemon at all) work against
///     rows written directly through the same atomic store API the real write path uses. Combined
///     with the daemon's own pytest suite (tests/test_noticeboard.py, solreign-director repo)
///     proving the classifier gate itself, every individual link in the chain is covered even
///     though no single automated test drives all links at once.
/// </summary>
[TestFixture]
public sealed class NoticeboardIntegrationTest : GameTest
{
    // Spawns entities, flips CVars, writes ledger rows directly — this server must never be
    // handed back to the pool (the MarkGardenIntegrationTest precedent).
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        DummyTicker = false,
    };

    private static string Now() => DateTime.UtcNow.ToString("o");

    /// <summary>
    ///     A fresh, per-test board id. All four tests in this fixture default the same
    ///     <c>"main"</c> BoardId and share ONE pooled server's Season Ledger SQLite file within a
    ///     test run — giving every test its own id keeps them independent regardless of NUnit's
    ///     execution/interleaving order, the same isolation discipline
    ///     <c>NoticeboardStoreTests.GetActiveNotes_ScopedToBoardId</c> already pins at the store
    ///     level.
    /// </summary>
    private static string FreshBoardId() => $"test-{Guid.NewGuid():N}";

    [Test]
    public async Task Dormant_ShipPosture_IsZeroBehavior()
    {
        var server = Server;
        var entMan = server.EntMan;
        var noticeboard = server.System<SolreignNoticeboardSystem>();
        var ui = server.System<UserInterfaceSystem>();
        var ledger = server.System<SeasonLedgerSystem>();

        // The SHIP posture is the tested posture.
        Assert.That(server.CfgMan.GetCVar(CCVars.SolreignNoticeboardsEnabled), Is.False,
            "solreign.noticeboards.enabled must ship FALSE (dormant by mission rail)");

        EntityUid mob = default;
        EntityUid board = default;
        await server.WaitPost(() =>
        {
            mob = ServerSession!.AttachedEntity!.Value;
            board = entMan.SpawnEntity("SolreignNoticeboard", entMan.GetComponent<TransformComponent>(mob).Coordinates);
            entMan.GetComponent<SolreignNoticeboardComponent>(board).BoardId = FreshBoardId();
        });
        await server.WaitRunTicks(20);

        // Even a spawned board must project nothing while off — MapInit Providence seeding is a
        // no-op, and a board-open read pushes no state at all.
        await server.WaitAssertion(() =>
        {
            Assert.That(ui.TryGetUiState<SolreignNoticeboardUiState>(board, SolreignNoticeboardUiKey.Key, out _), Is.False,
                "enabled=false must never push a board state — not even an empty one");
        });

        var boardId = entMan.GetComponent<SolreignNoticeboardComponent>(board).BoardId;
        Assert.That(await ledger.GetActiveNoticeboardNotesAsync(boardId, Now()), Is.Empty,
            "enabled=false must be a full kill switch: MapInit seeding may never touch a row");

        // A post attempt while off must also be a full no-op.
        await server.WaitPost(() => entMan.EventBus.RaiseLocalEvent(board,
            (object) new SolreignNoticeboardPostMessage("hello crew") { Actor = mob }, true));
        await server.WaitRunTicks(20);

        Assert.That(await ledger.GetActiveNoticeboardNotesAsync(boardId, Now()), Is.Empty,
            "enabled=false must be a full kill switch: the post path may never touch a row");
    }

    [Test]
    public async Task Enabled_DirectorChannelNotConfigured_PostAttempt_FailsClosed_NoRowWritten()
    {
        var server = Server;
        var entMan = server.EntMan;
        var ledger = server.System<SeasonLedgerSystem>();

        server.CfgMan.SetCVar(CCVars.SolreignNoticeboardsEnabled, true);
        // solreign.director.token stays at its default (empty) — the channel is unconfigured, the
        // same "daemon unreachable" shape as a genuinely offline daemon (DirectorChannel.TryGetReadyToken
        // fails identically either way).
        Assert.That(server.CfgMan.GetCVar(CCVars.SolreignDirectorToken), Is.Empty,
            "test precondition: the Director channel must be unconfigured");

        EntityUid mob = default;
        EntityUid board = default;
        await server.WaitPost(() =>
        {
            mob = ServerSession!.AttachedEntity!.Value;
            board = entMan.SpawnEntity("SolreignNoticeboard", entMan.GetComponent<TransformComponent>(mob).Coordinates);
            entMan.GetComponent<SolreignNoticeboardComponent>(board).BoardId = FreshBoardId();
        });
        await server.WaitRunTicks(20); // let MapInit Providence-seed attempt run to completion (and fail closed) too

        var boardId = entMan.GetComponent<SolreignNoticeboardComponent>(board).BoardId;
        Assert.That(await ledger.GetActiveNoticeboardNotesAsync(boardId, Now()), Is.Empty,
            "an unconfigured Director channel must mean Providence seeding writes nothing either");

        await server.WaitPost(() => entMan.EventBus.RaiseLocalEvent(board,
            (object) new SolreignNoticeboardPostMessage("hello crew") { Actor = mob }, true));
        await server.WaitRunTicks(20);

        Assert.That(await ledger.GetActiveNoticeboardNotesAsync(boardId, Now()), Is.Empty,
            "a post attempt with no configured Director channel must fail closed and write nothing " +
            "— this IS the 'pending forever' outcome the spec describes for a dead daemon: no row, ever");
    }

    [Test]
    public async Task BoardOpen_ProjectsActiveNotes_FromTheLedger()
    {
        // Bypasses the daemon entirely (the scope boundary noted in this file's header) by writing
        // an approved row directly through the SAME atomic store API the real write path commits
        // through post-approval — proving the READ/PROJECT half of the system in isolation.
        var server = Server;
        var entMan = server.EntMan;
        var noticeboard = server.System<SolreignNoticeboardSystem>();
        var ledger = server.System<SeasonLedgerSystem>();
        var ui = server.System<UserInterfaceSystem>();

        server.CfgMan.SetCVar(CCVars.SolreignNoticeboardsEnabled, true);

        EntityUid mob = default;
        EntityUid board = default;
        SolreignNoticeboardComponent comp = default!;
        await server.WaitPost(() =>
        {
            mob = ServerSession!.AttachedEntity!.Value;
            board = entMan.SpawnEntity("SolreignNoticeboard", entMan.GetComponent<TransformComponent>(mob).Coordinates);
            comp = entMan.GetComponent<SolreignNoticeboardComponent>(board);
            comp.BoardId = FreshBoardId();
        });
        await server.WaitRunTicks(5);

        var (posted, noteId, rejection) = await ledger.TryPostNoticeboardNoteAsync(
            comp.BoardId, Guid.NewGuid(), isProvidence: false, "Kolton", "Movie night 8pm.",
            Now(), expiryHours: 72, boardCapacity: 12, providenceCapacity: 2, cooldownHours: 24);
        Assert.That(posted, Is.True, rejection?.ToString());

        await server.WaitPost(() => noticeboard.OnBoardUiOpenedForTests(board, comp));
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(ui.TryGetUiState<SolreignNoticeboardUiState>(board, SolreignNoticeboardUiKey.Key, out var state), Is.True);
            Assert.That(state!.Offline, Is.False);
            Assert.That(state.Notes, Has.Count.EqualTo(1));
            Assert.That(state.Notes[0].Id, Is.EqualTo(noteId));
            Assert.That(state.Notes[0].Text, Is.EqualTo("Movie night 8pm."));
            Assert.That(state.Notes[0].AuthorDisplay, Is.EqualTo("Kolton"));
            Assert.That(state.Notes[0].IsProvidence, Is.False);
        });
    }

    [Test]
    public async Task Report_HidesTheNote_AndTheNextProjectionOmitsIt()
    {
        var server = Server;
        var entMan = server.EntMan;
        var noticeboard = server.System<SolreignNoticeboardSystem>();
        var ledger = server.System<SeasonLedgerSystem>();
        var ui = server.System<UserInterfaceSystem>();

        server.CfgMan.SetCVar(CCVars.SolreignNoticeboardsEnabled, true);

        EntityUid mob = default;
        EntityUid board = default;
        SolreignNoticeboardComponent comp = default!;
        await server.WaitPost(() =>
        {
            mob = ServerSession!.AttachedEntity!.Value;
            board = entMan.SpawnEntity("SolreignNoticeboard", entMan.GetComponent<TransformComponent>(mob).Coordinates);
            comp = entMan.GetComponent<SolreignNoticeboardComponent>(board);
            comp.BoardId = FreshBoardId();
        });
        await server.WaitRunTicks(5);

        var (_, noteId, _) = await ledger.TryPostNoticeboardNoteAsync(
            comp.BoardId, Guid.NewGuid(), isProvidence: false, "Kolton", "Free toolbox in the bar.",
            Now(), expiryHours: 72, boardCapacity: 12, providenceCapacity: 2, cooldownHours: 24);

        await server.WaitPost(() => entMan.EventBus.RaiseLocalEvent(board,
            (object) new SolreignNoticeboardReportMessage(noteId) { Actor = mob }, true));
        await server.WaitRunTicks(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(ui.TryGetUiState<SolreignNoticeboardUiState>(board, SolreignNoticeboardUiKey.Key, out var state), Is.True);
            Assert.That(state!.Notes, Is.Empty, "a reported note must vanish from the very next projection");
        });

        Assert.That(await ledger.GetActiveNoticeboardNotesAsync(comp.BoardId, Now()), Is.Empty);
    }
}
