#nullable enable
using System;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.Providence;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Ghost.Components;
using Content.Shared.Preferences;
using NUnit.Framework;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Wow-wiring wave (docs/receipts/wow-wiring/WOW-WIRING-2026-07-16.md), Feature 1: "Providence
///     welcomes you personally" — the delayed, personally-addressed follow-up beat additive to the
///     existing <c>ProvidenceWelcomeSystem</c> immediate welcome (see that class's own extensive doc
///     comment for the full design). This file drives the real spawn hook end-to-end and exercises the
///     failure modes the mission explicitly called out:
///       * fires once per player, not per round-join (queue only ever holds one pending entry per
///         eligible spawn; the pre-existing <c>ProvidenceWelcomeGate</c> — already covered by
///         <c>ProvidenceWelcomeGateTests</c> — prevents a second schedule on respawn/reconnect)
///       * no fire for ghosts/observers (guard checked at fire time, since the player can die/ghost in
///         the few seconds between scheduling and firing)
///       * CVar-off is fully inert (nothing schedules; a mid-flight flip drops anything already queued)
///
///     Pure timing/queue mechanics (drain-once, never-double-fire, round-boundary clear) are exhaustively
///     unit-tested without any engine dependency in
///     Content.Tests/_Solreign/ProvidenceFirstShiftPersonalQueueTests.cs — this file only covers the
///     ECS-level wiring those pure tests cannot reach (session re-resolution, ghost component check,
///     real CVar plumbing).
///
///     Every test sets its CVars, then calls <see cref="ProvidenceWelcomeSystem.ResetRoundStateForTests"/>
///     — in that order, so a test that flips the personal-layer CVar off can never race a reset that
///     runs before the flip lands — before firing any synthetic spawn. Investigated and required: a
///     pool-connected session (<c>PoolSettings.Connected = true, DummyTicker = false</c>) already goes
///     through a REAL spawn during pool setup, before any test body runs — with this feature's CVars at
///     their production default (on), that real spawn already schedules a pending personal beat the
///     test never asked for, and the pre-existing per-round anti-fatigue gate then silently blocks the
///     test's OWN synthetic spawn from scheduling a second one. Without the reset, a test could pass or
///     fail for the wrong reason (residue from pool setup, not from the scenario under test) — this was
///     caught by an initial run of <c>FirstShiftWelcomeCvar_Off_NeverSchedulesAnything</c> failing with
///     "Expected: 0, But was: 1" even with the CVar correctly read as off at spawn time.
/// </summary>
[TestFixture]
public sealed class ProvidenceFirstShiftWelcomeIntegrationTest : GameTest
{
    // Dirty: flips CCVars directly via server.CfgMan (precedented NukeOpsTest/ProvidenceVoiceSystem
    // idiom) and fires a synthetic PlayerSpawnCompleteEvent against the real connected session's mob —
    // this server must never be handed back to the pool.
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        // Real attached body required (HotPotato/SeasonLedger precedent) — DummyTicker=false.
        DummyTicker = false,
    };

    private static PlayerSpawnCompleteEvent MakeSpawnEvent(EntityUid mob, ICommonSession session, bool silent = false)
    {
        return new PlayerSpawnCompleteEvent(
            mob,
            session,
            jobId: "Passenger",
            lateJoin: false,
            silent: silent,
            joinOrder: 1,
            station: EntityUid.Invalid,
            profile: new HumanoidCharacterProfile());
    }

    [Test]
    public async Task FreshAccount_Spawn_SchedulesExactlyOnePendingPersonalBeat()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var welcome = server.System<ProvidenceWelcomeSystem>();

        server.CfgMan.SetCVar(CCVars.SolreignProvidenceWelcomeEnabled, true);
        server.CfgMan.SetCVar(CCVars.SolreignProvidenceFirstShiftWelcome, true);
        await server.WaitPost(welcome.ResetRoundStateForTests);
        await server.WaitRunTicks(1);

        EntityUid mob = default;
        ICommonSession session = default!;

        await server.WaitPost(() =>
        {
            session = playerMan.Sessions[0];
            mob = session.AttachedEntity
                  ?? throw new InvalidOperationException("Connected player has no AttachedEntity -- DummyTicker must be false.");

            // The pool-connected test account has no round-end records in the ledger DB (fresh per-test
            // temp SQLite file, see CCVars.SolreignSeasonLedgerDbPath) -> the real GetCareerStatsAsync
            // read resolves Tours == 0, same "brand-new account" signal production relies on.
            entMan.EventBus.RaiseLocalEvent(mob, MakeSpawnEvent(mob, session), broadcast: true);
        });

        // LoadWelcome is async void (awaits the real ledger read) -> poll briefly for the schedule to land.
        var scheduled = false;
        for (var attempt = 0; attempt < 50 && !scheduled; attempt++)
        {
            await server.WaitRunTicks(1);
            await server.WaitPost(() => scheduled = welcome.PersonalQueueCountForTests == 1);
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(scheduled, Is.True,
                "A brand-new account's first eligible spawn must schedule exactly one delayed personal beat.");
        });
    }

    [Test]
    public async Task FirstShiftWelcomeCvar_Off_NeverSchedulesAnything()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var welcome = server.System<ProvidenceWelcomeSystem>();

        server.CfgMan.SetCVar(CCVars.SolreignProvidenceWelcomeEnabled, true);
        server.CfgMan.SetCVar(CCVars.SolreignProvidenceFirstShiftWelcome, false);
        await server.WaitPost(welcome.ResetRoundStateForTests);
        await server.WaitRunTicks(1);

        EntityUid mob = default;
        ICommonSession session = default!;

        await server.WaitPost(() =>
        {
            session = playerMan.Sessions[0];
            mob = session.AttachedEntity!.Value;
            entMan.EventBus.RaiseLocalEvent(mob, MakeSpawnEvent(mob, session), broadcast: true);
        });

        // Give LoadWelcome's async ledger read plenty of ticks to resolve -- the CVar must keep the
        // queue at zero throughout, not just at the instant checked.
        for (var i = 0; i < 20; i++)
            await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(welcome.PersonalQueueCountForTests, Is.EqualTo(0),
                "solreign.providence.first_shift_welcome=false must be a full kill switch: the immediate " +
                "welcome may still fire, but the delayed personal layer must never schedule.");
        });

        // Restore for pool hygiene even though this fixture is Dirty (defensive, cheap).
        server.CfgMan.SetCVar(CCVars.SolreignProvidenceFirstShiftWelcome, true);
    }

    [Test]
    public async Task FiringAgainstAGhost_IsASilentNoOp_NeverThrows()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var welcome = server.System<ProvidenceWelcomeSystem>();

        server.CfgMan.SetCVar(CCVars.SolreignProvidenceWelcomeEnabled, true);
        server.CfgMan.SetCVar(CCVars.SolreignProvidenceFirstShiftWelcome, true);
        await server.WaitPost(welcome.ResetRoundStateForTests);
        await server.WaitRunTicks(1);

        EntityUid mob = default;
        ICommonSession session = default!;

        await server.WaitPost(() =>
        {
            session = playerMan.Sessions[0];
            mob = session.AttachedEntity!.Value;
            entMan.EventBus.RaiseLocalEvent(mob, MakeSpawnEvent(mob, session), broadcast: true);
        });

        var scheduled = false;
        for (var attempt = 0; attempt < 50 && !scheduled; attempt++)
        {
            await server.WaitRunTicks(1);
            await server.WaitPost(() => scheduled = welcome.PersonalQueueCountForTests == 1);
        }

        Assert.That(scheduled, Is.True, "Setup failed: pending personal beat never scheduled.");

        // Simulate the player having died and ghosted in the few seconds between scheduling and firing:
        // the session's attached entity is now a ghost. GhostComponent is the guard FireFirstShiftPersonal
        // checks before doing anything player-facing.
        await server.WaitPost(() =>
        {
            entMan.EnsureComponent<GhostComponent>(mob);
        });

        await server.WaitAssertion(() =>
        {
            Assert.DoesNotThrow(() => welcome.FireDuePersonalForTests(server.Timing.CurTime + TimeSpan.FromDays(1)),
                "Firing a due personal beat against an entity that has since become a ghost must be a " +
                "silent no-op -- it must never throw, and it must never pop up Providence's onboarding " +
                "address over a ghost's view.");

            Assert.That(welcome.PersonalQueueCountForTests, Is.EqualTo(0),
                "The pending entry must be consumed (drained) even when the guard chain no-ops -- it must " +
                "never remain queued to retry, and it must never double-fire on a later Update tick.");
        });
    }

    [Test]
    public async Task SilentSpawn_NeverSchedulesAPersonalBeat()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var welcome = server.System<ProvidenceWelcomeSystem>();

        server.CfgMan.SetCVar(CCVars.SolreignProvidenceWelcomeEnabled, true);
        server.CfgMan.SetCVar(CCVars.SolreignProvidenceFirstShiftWelcome, true);
        await server.WaitPost(welcome.ResetRoundStateForTests);
        await server.WaitRunTicks(1);

        EntityUid mob = default;
        ICommonSession session = default!;

        await server.WaitPost(() =>
        {
            session = playerMan.Sessions[0];
            mob = session.AttachedEntity!.Value;
            // Silent=true mirrors a cryo-storage return -- same flag the vanilla join greeting and the
            // existing immediate welcome both respect.
            entMan.EventBus.RaiseLocalEvent(mob, MakeSpawnEvent(mob, session, silent: true), broadcast: true);
        });

        for (var i = 0; i < 20; i++)
            await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(welcome.PersonalQueueCountForTests, Is.EqualTo(0),
                "A silent spawn must never reach LoadWelcome at all, so it must never schedule a personal beat.");
        });
    }
}
