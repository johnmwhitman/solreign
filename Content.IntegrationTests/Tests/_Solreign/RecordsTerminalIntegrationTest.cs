#nullable enable
using System;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.Records;
using Content.Server._Solreign.SeasonLedger;
using Content.Shared._Solreign.Records;
using Content.Shared.CCVar;
using NUnit.Framework;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Personnel Records Terminal ECS wiring (wave-2 item einstein-016): drives the real
///     <see cref="SolreignRecordsTerminalSystem"/> against the connected pool session — the explicit
///     runtime-off kill switch (opening the console force-closes the window and never reaches the
///     ledger) and the enabled path staying open (proving the async read completes without throwing
///     against a real, freshly-connected account). The feature now ships enabled, so every test
///     arranges its state explicitly and restores the state it inherited.
///
///     Pure content-planning logic (fresh/decorated/dead-once/marked permutations, honest empty
///     states, corrupt-row fallbacks) is exhaustively unit-tested without a server in
///     <c>Content.Tests/_Solreign/RecordsTerminalRendererTests.cs</c> — this file only covers the ECS
///     wiring those tests cannot reach.
///
///     "Own record only" is a STRUCTURAL guarantee, not a runtime branch this fixture can toggle:
///     every message type in <c>RecordsTerminalUiMessages.cs</c> derives its account solely from
///     <c>BoundUIOpenedEvent.Actor</c> / <c>RecordsTerminalRefreshMessage</c>'s sender — there is no
///     message anywhere in the feature that carries a target account, so "browse another player's
///     file" isn't a code path that exists to test against. See
///     <see cref="SolreignRecordsTerminalSystem"/>'s doc comment.
/// </summary>
[TestFixture]
public sealed class RecordsTerminalIntegrationTest : GameTest
{
    // Dirty: flips CCVars directly and drives the connected session's own account through the real
    // ledger — the MarkGardenIntegrationTest / SeasonLedgerSystemIntegrationTest precedent.
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        DummyTicker = false,
    };

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
    public async Task Disabled_AtRuntime_OpeningForceClosesAndNeverReadsTheLedger()
    {
        var server = Server;
        var entMan = server.EntMan;
        var ui = server.System<UserInterfaceSystem>();
        var originalEnabled = server.CfgMan.GetCVar(CCVars.SolreignRecordsTerminalEnabled);
        EntityUid terminal = default;

        try
        {
            server.CfgMan.SetCVar(CCVars.SolreignRecordsTerminalEnabled, false);
            await server.WaitRunTicks(1);

            EntityUid mob = default;
            await server.WaitPost(() =>
            {
                mob = ServerSession!.AttachedEntity!.Value;
                terminal = entMan.SpawnEntity("SolreignRecordsTerminal", entMan.GetComponent<TransformComponent>(mob).Coordinates);
            });
            await server.WaitRunTicks(2);

            await server.WaitPost(() =>
            {
                ui.TryOpenUi(terminal, RecordsTerminalUiKey.Key, mob);
            });
            await server.WaitRunTicks(5);

            await server.WaitAssertion(() =>
            {
                Assert.That(ui.IsUiOpen(terminal, RecordsTerminalUiKey.Key, mob), Is.False,
                    "enabled=false must force-close any opened terminal window — full kill switch");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                if (entMan.EntityExists(terminal))
                    entMan.DeleteEntity(terminal);
                server.CfgMan.SetCVar(CCVars.SolreignRecordsTerminalEnabled, originalEnabled);
            });
        }
    }

    [Test]
    public async Task Enabled_OpeningStaysOpen_AsyncReadDoesNotThrowForARealAccount()
    {
        var server = Server;
        var entMan = server.EntMan;
        var ui = server.System<UserInterfaceSystem>();
        var ledger = server.System<SeasonLedgerSystem>();
        var originalEnabled = server.CfgMan.GetCVar(CCVars.SolreignRecordsTerminalEnabled);
        EntityUid terminal = default;

        try
        {
            server.CfgMan.SetCVar(CCVars.SolreignRecordsTerminalEnabled, true);

            EntityUid mob = default;
            await server.WaitPost(() =>
            {
                mob = ServerSession!.AttachedEntity!.Value;
                terminal = entMan.SpawnEntity("SolreignRecordsTerminal", entMan.GetComponent<TransformComponent>(mob).Coordinates);
            });
            await server.WaitRunTicks(2);

            await server.WaitPost(() =>
            {
                ui.TryOpenUi(terminal, RecordsTerminalUiKey.Key, mob);
            });

            // Give the async ledger read (real SQLite round-trip through SeasonLedgerSystem, the same
            // path OnPlayerSpawnComplete's title load takes) time to land on the main thread.
            var stillOpen = await PollAsync(() => ui.IsUiOpen(terminal, RecordsTerminalUiKey.Key, mob));

            await server.WaitAssertion(() =>
            {
                Assert.That(stillOpen, Is.True,
                    "enabled=true must never self-close the window — a successful read leaves it open");
            });

            // The account behind this session is real (the connected pool's fresh account) — confirm the
            // exact read the system performed completes cleanly and returns the pre-anything-happened
            // fresh-account shape, proving the wiring reached the real ledger and not a stub.
            var account = ServerSession!.UserId.UserId;
            var data = await ledger.GetRecordsTerminalDataAsync(account);
            Assert.That(data.Tours, Is.GreaterThanOrEqualTo(0));
            Assert.That(data.SocialFirstFlags, Is.Not.Null);
        }
        finally
        {
            await server.WaitPost(() =>
            {
                if (entMan.EntityExists(terminal))
                    entMan.DeleteEntity(terminal);
                server.CfgMan.SetCVar(CCVars.SolreignRecordsTerminalEnabled, originalEnabled);
            });
        }
    }
}
