#nullable enable
using System;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.PlayerDelight.Mark;
using Content.Server._Solreign.SeasonLedger;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     "The Mark" PROVIDENCE beat pack wiring (docs/specs/MARK-SPEC-2026-07-17-DRAFT.md §3.5, §6,
///     MG-W4): the first-return nudge (C7), the growth return beat (C2), the anti-fatigue cap (at
///     most 4 beats ever per account — the task brief's own rail), the C6 overflow payload
///     substitution (this wave's boundary decision), and the explicit master/sub-gate kill-switch
///     zero-behavior posture.
///
///     Pure mechanics (queue drain-once law, age thresholds, copy-key selection) are exhaustively
///     unit-tested without a server in Content.Tests/_Solreign/Mark{BeatQueue,AgeRules,Copy}Tests.cs
///     (MG-W1) — this file only covers the <see cref="MarkBeatsSystem"/> ECS glue those tests
///     cannot reach: the real spawn-complete hook, the real ledger stamps, and the real CVar gates.
///
///     Two isolation laws every test in this file follows (MARK-DORMANCY-FIX receipt,
///     docs/receipts/MARK-DORMANCY-FIX-2026-07-17.md):
///       * <see cref="SeasonLedgerSystem.ClearAllMarksForTests"/> is called before any claim.
///         Unlike <c>first_death</c>/<c>social_firsts</c> (isolated per test purely by drawing a
///         fresh account <see cref="Guid"/>), a mark's <c>slot_index</c> is claimed from a GLOBAL
///         <c>COUNT(*) FROM mark</c> row count — a pooled server (and its ledger SQLite file) is
///         eligible for reuse across every test method in this fixture, so without the clear, a
///         later test's "first claim in this table = slot 0" precondition silently inherits rows
///         planted by earlier tests sharing the same recycled file.
///       * Dormancy claims (<see cref="Dormant_MasterCVarOff_IsZeroBehaviorRegardlessOfSubGate"/>,
///         <see cref="Dormant_ReturnBeatSubGateOff_IsZeroBehaviorEvenWhileMarkIsLive"/>,
///         <see cref="Dormant_MasterCVarOff_NeverQueuesOrFires_AcrossPlantSpawnRoundAndStageSequence"/>)
///         raise the REAL <see cref="PlayerSpawnCompleteEvent"/> through the event bus (the
///         FirstDeathSceneIntegrationTest <c>MakeSpawnEvent</c> pattern) rather than calling
///         <see cref="MarkBeatsSystem.LoadMarkBeatsForTests"/> — that seam intentionally skips
///         <c>OnPlayerSpawnComplete</c>'s CVar gate (mechanic tests below set both CVars true
///         themselves and rely on it purely for round-id control), so calling it with a gate CVar
///         off does not exercise the gate at all. Only a real spawn event proves the production
///         gate itself holds.
/// </summary>
[TestFixture]
public sealed class MarkBeatsIntegrationTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        DummyTicker = false,
    };

    private IDisposable PreserveCVarState()
    {
        var enabled = Server.CfgMan.GetCVar(CCVars.SolreignMarkEnabled);
        var returnBeatEnabled = Server.CfgMan.GetCVar(CCVars.SolreignMarkReturnBeatEnabled);
        var slots = Server.CfgMan.GetCVar(CCVars.SolreignMarkSlots);
        return new RestoreScope(() =>
        {
            Server.CfgMan.SetCVar(CCVars.SolreignMarkEnabled, enabled);
            Server.CfgMan.SetCVar(CCVars.SolreignMarkReturnBeatEnabled, returnBeatEnabled);
            Server.CfgMan.SetCVar(CCVars.SolreignMarkSlots, slots);
        });
    }

    private sealed class RestoreScope : IDisposable
    {
        private readonly Action _restore;

        public RestoreScope(Action restore)
        {
            _restore = restore;
        }

        public void Dispose() => _restore();
    }

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
    public async Task NoMarkRow_SpawnCompleteIsANoOp()
    {
        using var cvars = PreserveCVarState();
        var server = Server;
        var beats = server.System<MarkBeatsSystem>();
        var ledger = server.System<SeasonLedgerSystem>();
        var mob = ServerSession!.AttachedEntity!.Value;
        var account = Guid.NewGuid(); // this account never planted anything.

        Assert.Multiple(() =>
        {
            Assert.That(server.CfgMan.GetCVar(CCVars.SolreignMarkEnabled), Is.True,
                "the activation pass intentionally ships the Mark master gate enabled");
            Assert.That(server.CfgMan.GetCVar(CCVars.SolreignMarkReturnBeatEnabled), Is.True,
                "the activation pass intentionally ships return beats enabled");
        });

        server.CfgMan.SetCVar(CCVars.SolreignMarkEnabled, true);
        server.CfgMan.SetCVar(CCVars.SolreignMarkReturnBeatEnabled, true);
        await ledger.ClearAllMarksForTests();

        await server.WaitPost(() =>
        {
            beats.ResetRoundStateForTests();
            beats.LoadMarkBeatsForTests(mob, account, 1);
        });
        await server.WaitRunTicks(10);

        Assert.That(beats.PendingBeatCountForTests, Is.Zero, "no mark row -> zero pending beats");
    }

    [Test]
    public async Task FirstReturnNudge_FiresOnceEver_OnlyOnALaterRound()
    {
        using var cvars = PreserveCVarState();
        var server = Server;
        var beats = server.System<MarkBeatsSystem>();
        var ledger = server.System<SeasonLedgerSystem>();
        var mob = ServerSession!.AttachedEntity!.Value;
        var account = Guid.NewGuid();

        server.CfgMan.SetCVar(CCVars.SolreignMarkEnabled, true);
        server.CfgMan.SetCVar(CCVars.SolreignMarkReturnBeatEnabled, true);
        await ledger.ClearAllMarksForTests();

        Assert.That((await ledger.TryClaimMarkAsync(account, MarkKinds.SaplingLedger, 1, "test-map", "Subject", 0)).Claimed);

        // Same round as planting: the planter already got C1 — no nudge due yet.
        await server.WaitPost(() =>
        {
            beats.ResetRoundStateForTests();
            beats.LoadMarkBeatsForTests(mob, account, 1);
        });
        await server.WaitRunTicks(5);
        Assert.That(beats.PendingBeatCountForTests, Is.Zero, "no nudge on the planting round itself");

        // A later round's spawn: nudge becomes due.
        await server.WaitPost(() => beats.LoadMarkBeatsForTests(mob, account, 2));
        await server.WaitRunTicks(5);
        Assert.That(beats.PendingBeatCountForTests, Is.EqualTo(1), "exactly one nudge on the first later-round spawn");

        var record = await ledger.GetMarkAsync(account);
        Assert.That(record!.NudgeShown, Is.True, "the stamp must be write-first (already true by the time we poll)");

        // A second later-round spawn: already stamped, never fires again.
        await server.WaitPost(() =>
        {
            beats.ResetRoundStateForTests();
            beats.LoadMarkBeatsForTests(mob, account, 3);
        });
        await server.WaitRunTicks(5);
        Assert.That(beats.PendingBeatCountForTests, Is.Zero, "the nudge fires once, ever");
    }

    [Test]
    public async Task GrowthBeat_FiresOnStageAdvanceOnly_WriteFirstStampBlocksRepeat()
    {
        using var cvars = PreserveCVarState();
        var server = Server;
        var beats = server.System<MarkBeatsSystem>();
        var ledger = server.System<SeasonLedgerSystem>();
        var mob = ServerSession!.AttachedEntity!.Value;
        var account = Guid.NewGuid();

        server.CfgMan.SetCVar(CCVars.SolreignMarkEnabled, true);
        server.CfgMan.SetCVar(CCVars.SolreignMarkReturnBeatEnabled, true);
        await ledger.ClearAllMarksForTests();

        Assert.That((await ledger.TryClaimMarkAsync(account, MarkKinds.LampLedger, 1, "test-map", "Subject", 0)).Claimed);
        await ledger.SetMarkPlantedUtcForTests(account, DateTime.UtcNow.AddDays(-3).ToString("o")); // stage 1

        await server.WaitPost(() =>
        {
            beats.ResetRoundStateForTests();
            beats.LoadMarkBeatsForTests(mob, account, 5); // a later round -> the nudge also fires
        });
        await server.WaitRunTicks(5);

        // Nudge (C7) + growth (C2): stage advanced 0 -> 1 AND it's a later round than planting.
        Assert.That(beats.PendingBeatCountForTests, Is.EqualTo(2));

        var record = await ledger.GetMarkAsync(account);
        Assert.That(record!.LastVisitStage, Is.EqualTo(1), "the visit stamp must be write-first");

        // Re-spawn at the SAME stage: nudge already stamped, stage hasn't advanced -> silence.
        await server.WaitPost(() =>
        {
            beats.ResetRoundStateForTests();
            beats.LoadMarkBeatsForTests(mob, account, 6);
        });
        await server.WaitRunTicks(5);
        Assert.That(beats.PendingBeatCountForTests, Is.Zero, "no repeat beat at an unchanged stage");
    }

    [Test]
    public async Task AntiFatigueCap_AtMostFourBeatsEverPerAccount()
    {
        using var cvars = PreserveCVarState();
        var server = Server;
        var beats = server.System<MarkBeatsSystem>();
        var ledger = server.System<SeasonLedgerSystem>();
        var mob = ServerSession!.AttachedEntity!.Value;
        var account = Guid.NewGuid();

        server.CfgMan.SetCVar(CCVars.SolreignMarkEnabled, true);
        server.CfgMan.SetCVar(CCVars.SolreignMarkReturnBeatEnabled, true);
        await ledger.ClearAllMarksForTests();

        Assert.That((await ledger.TryClaimMarkAsync(account, MarkKinds.SaplingLedger, 1, "test-map", "Subject", 0)).Claimed);

        var totalScheduled = 0;
        // Walk the account through every stage boundary (0 -> 1 -> 2 -> 3) across separate later
        // rounds/spawns, each spawn strictly older than the last -- the task brief's "at most 4
        // lines ever" rail is 1 nudge + 3 stage advances.
        var ages = new[] { 3, 10, 25, 40 }; // days -> stages 1, 2, 3, 3 (capped, no further advance)
        var round = 2;
        foreach (var days in ages)
        {
            await ledger.SetMarkPlantedUtcForTests(account, DateTime.UtcNow.AddDays(-days).ToString("o"));
            await server.WaitPost(() =>
            {
                beats.ResetRoundStateForTests();
                beats.LoadMarkBeatsForTests(mob, account, round);
            });
            await server.WaitRunTicks(5);
            totalScheduled += beats.PendingBeatCountForTests;
            round++;
        }

        Assert.That(totalScheduled, Is.EqualTo(4),
            "1 nudge + 3 stage advances (1/2/3) — the fourth aged spawn (already at stage 3) must add nothing");
    }

    [Test]
    public async Task Overflow_SubstitutesC6ForBothBeats_ButStillConsumesTheSameStamps()
    {
        using var cvars = PreserveCVarState();
        var server = Server;
        var beats = server.System<MarkBeatsSystem>();
        var ledger = server.System<SeasonLedgerSystem>();
        var mob = ServerSession!.AttachedEntity!.Value;
        var account = Guid.NewGuid();

        server.CfgMan.SetCVar(CCVars.SolreignMarkEnabled, true);
        server.CfgMan.SetCVar(CCVars.SolreignMarkReturnBeatEnabled, true);
        server.CfgMan.SetCVar(CCVars.SolreignMarkSlots, 1); // capacity 1 -> anything at slot >= 1 overflows
        await ledger.ClearAllMarksForTests();

        // First claim fills the only slot (slot 0); the SECOND account overflows (slot 1).
        var accountA = Guid.NewGuid();
        Assert.That((await ledger.TryClaimMarkAsync(accountA, MarkKinds.SaplingLedger, 1, "test-map", "A", 0)).Claimed);
        var (claimed, slot) = await ledger.TryClaimMarkAsync(account, MarkKinds.LampLedger, 1, "test-map", "B", 0);
        Assert.That(claimed, Is.True);
        Assert.That(slot, Is.EqualTo(1), "precondition: this account's row must be beyond the 1-slot cap");
        await ledger.SetMarkPlantedUtcForTests(account, DateTime.UtcNow.AddDays(-3).ToString("o")); // stage 1

        await server.WaitPost(() =>
        {
            beats.ResetRoundStateForTests();
            beats.LoadMarkBeatsForTests(mob, account, 4); // later round -> nudge + growth both due
        });
        await server.WaitRunTicks(5);

        // The overflow substitution still fires the SAME two beat slots (nudge + growth) — it
        // changes the TEXT, not the count — and both stamps still land (the anti-fatigue budget is
        // still consumed, per this wave's boundary decision).
        Assert.That(beats.PendingBeatCountForTests, Is.EqualTo(2));

        var record = await ledger.GetMarkAsync(account);
        Assert.Multiple(() =>
        {
            Assert.That(record!.NudgeShown, Is.True, "overflow must still consume the nudge stamp");
            Assert.That(record.LastVisitStage, Is.EqualTo(1), "overflow must still consume the visit stamp");
        });

    }

    [Test]
    public async Task Dormant_MasterCVarOff_IsZeroBehaviorRegardlessOfSubGate()
    {
        using var cvars = PreserveCVarState();
        var server = Server;
        var entMan = server.EntMan;
        var beats = server.System<MarkBeatsSystem>();
        var ledger = server.System<SeasonLedgerSystem>();
        var mob = ServerSession!.AttachedEntity!.Value;
        var session = ServerSession!;
        // The real spawn event resolves the account from the session's UserId (production identity
        // resolution) -- must match, or the ledger row we claim below is invisible to the gate.
        var account = session.UserId.UserId;

        Assert.That(server.CfgMan.GetCVar(CCVars.SolreignMarkEnabled), Is.True,
            "the activation pass intentionally ships the Mark enabled");
        server.CfgMan.SetCVar(CCVars.SolreignMarkEnabled, false);
        server.CfgMan.SetCVar(CCVars.SolreignMarkReturnBeatEnabled, true); // sub-gate on, master off
        await ledger.ClearAllMarksForTests();

        Assert.That((await ledger.TryClaimMarkAsync(account, MarkKinds.SaplingLedger, 1, "test-map", "Subject", 0)).Claimed);

        await server.WaitPost(() => beats.ResetRoundStateForTests());

        // Drive the REAL production hook (OnPlayerSpawnComplete), not the internal test seam --
        // LoadMarkBeatsForTests intentionally bypasses the CVar gate (see the class doc comment),
        // so only a genuine PlayerSpawnCompleteEvent actually exercises the master kill switch.
        await server.WaitPost(() =>
            entMan.EventBus.RaiseLocalEvent(mob, MakeSpawnEvent(mob, session), broadcast: true));
        await server.WaitRunTicks(10);

        Assert.That(beats.PendingBeatCountForTests, Is.Zero, "the master CVar is a full kill switch");
    }

    [Test]
    public async Task Dormant_ReturnBeatSubGateOff_IsZeroBehaviorEvenWhileMarkIsLive()
    {
        using var cvars = PreserveCVarState();
        var server = Server;
        var entMan = server.EntMan;
        var beats = server.System<MarkBeatsSystem>();
        var ledger = server.System<SeasonLedgerSystem>();
        var mob = ServerSession!.AttachedEntity!.Value;
        var session = ServerSession!;
        // The real spawn event resolves the account from the session's UserId -- must match.
        var account = session.UserId.UserId;

        server.CfgMan.SetCVar(CCVars.SolreignMarkEnabled, true);
        server.CfgMan.SetCVar(CCVars.SolreignMarkReturnBeatEnabled, false); // sub-gate off, master on
        await ledger.ClearAllMarksForTests();

        Assert.That((await ledger.TryClaimMarkAsync(account, MarkKinds.SaplingLedger, 1, "test-map", "Subject", 0)).Claimed);

        await server.WaitPost(() => beats.ResetRoundStateForTests());

        // Same rationale as above: only a real spawn event exercises OnPlayerSpawnComplete's gate.
        await server.WaitPost(() =>
            entMan.EventBus.RaiseLocalEvent(mob, MakeSpawnEvent(mob, session), broadcast: true));
        await server.WaitRunTicks(10);

        Assert.That(beats.PendingBeatCountForTests, Is.Zero,
            "the sub-gate stops only the beats — examine/garden/planting stay live per spec §3.7");

    }

    /// <summary>
    ///     Adversarial dormancy proof (MARK-DORMANCY-FIX receipt): with the master CVar off, NO beat
    ///     may ever be queued or fired under ANY spawn/round/planting/aging sequence — not a single
    ///     real spawn, not a later-round respawn, not an aged stage-advance spawn, not with the
    ///     sub-gate also off, and not by later force-draining the queue. It also proves dormancy is
    ///     achieved by never running the write path at all (the ledger stamps stay untouched), and
    ///     that the SAME account behaves correctly the moment the feature is switched on — dormancy
    ///     must never be achieved by silently breaking the enabled feature.
    /// </summary>
    [Test]
    public async Task Dormant_MasterCVarOff_NeverQueuesOrFires_AcrossPlantSpawnRoundAndStageSequence()
    {
        using var cvars = PreserveCVarState();
        var server = Server;
        var entMan = server.EntMan;
        var beats = server.System<MarkBeatsSystem>();
        var ledger = server.System<SeasonLedgerSystem>();
        var ticker = server.System<GameTicker>();
        var mob = ServerSession!.AttachedEntity!.Value;
        var session = ServerSession!;
        // The real spawn event resolves the account from the session's UserId -- must match.
        var account = session.UserId.UserId;

        await ledger.ClearAllMarksForTests();

        Assert.That(server.CfgMan.GetCVar(CCVars.SolreignMarkEnabled), Is.True,
            "precondition: the activation pass ships solreign.mark.enabled on");
        Assert.That(server.CfgMan.GetCVar(CCVars.SolreignMarkReturnBeatEnabled), Is.True,
            "precondition: the sub-gate ships on, so this proves the MASTER switch alone is the kill switch");
        server.CfgMan.SetCVar(CCVars.SolreignMarkEnabled, false);

        // Planted strictly before the live round -- a spawn below is guaranteed "a later round" so
        // the nudge condition would be due if anything leaked through the gate.
        var plantRoundId = ticker.RoundId - 1;
        Assert.That((await ledger.TryClaimMarkAsync(account, MarkKinds.SaplingLedger, plantRoundId, "test-map", "Subject", 0)).Claimed);

        await server.WaitPost(() => beats.ResetRoundStateForTests());

        // Spawn 1: fresh mark, same relative round -- must queue nothing.
        await server.WaitPost(() =>
            entMan.EventBus.RaiseLocalEvent(mob, MakeSpawnEvent(mob, session), broadcast: true));
        await server.WaitRunTicks(10);
        Assert.That(beats.PendingBeatCountForTests, Is.Zero, "spawn 1 (fresh plant) queued nothing while dormant");

        // Age the mark into stage 1 and spawn again -- this sequence would fire BOTH the nudge (C7)
        // and the growth beat (C2) if the master gate leaked even one beat.
        await ledger.SetMarkPlantedUtcForTests(account, DateTime.UtcNow.AddDays(-3).ToString("o"));
        await server.WaitPost(() =>
            entMan.EventBus.RaiseLocalEvent(mob, MakeSpawnEvent(mob, session), broadcast: true));
        await server.WaitRunTicks(10);
        Assert.That(beats.PendingBeatCountForTests, Is.Zero, "spawn 2 (aged, stage 1) queued nothing while dormant");

        // Spawn 3: the sub-gate ALSO off (the two-off-at-once corner) -- still nothing.
        server.CfgMan.SetCVar(CCVars.SolreignMarkReturnBeatEnabled, false);
        await server.WaitPost(() =>
            entMan.EventBus.RaiseLocalEvent(mob, MakeSpawnEvent(mob, session), broadcast: true));
        await server.WaitRunTicks(10);
        Assert.That(beats.PendingBeatCountForTests, Is.Zero, "spawn 3 (both gates off) queued nothing");
        server.CfgMan.SetCVar(CCVars.SolreignMarkReturnBeatEnabled, true); // restore for the enabled leg below

        // Force-draining the queue right now (or far in the future) must be a genuine no-op -- the
        // queue is provably empty, not merely "scheduled but never fired".
        await server.WaitPost(() =>
            Assert.DoesNotThrow(() => beats.FireDueBeatsForTests(server.Timing.CurTime + TimeSpan.FromDays(1))));
        Assert.That(beats.PendingBeatCountForTests, Is.Zero);

        // Ledger truth: dormancy means the write path itself never ran -- neither stamp was ever
        // attempted, not merely swallowed at delivery.
        var dormantRecord = await ledger.GetMarkAsync(account);
        Assert.Multiple(() =>
        {
            Assert.That(dormantRecord!.NudgeShown, Is.False, "dormant must never even attempt the nudge write");
            Assert.That(dormantRecord.LastVisitStage, Is.EqualTo(0), "dormant must never even attempt the visit write");
        });

        // Flip the feature ON and prove the SAME account still works correctly afterward: dormancy
        // must be achieved by killing the feature while off, never by quietly breaking it once
        // re-enabled.
        server.CfgMan.SetCVar(CCVars.SolreignMarkEnabled, true);
        await server.WaitPost(() => beats.ResetRoundStateForTests());
        await server.WaitPost(() =>
            entMan.EventBus.RaiseLocalEvent(mob, MakeSpawnEvent(mob, session), broadcast: true));
        await server.WaitRunTicks(10);

        Assert.That(beats.PendingBeatCountForTests, Is.EqualTo(2),
            "once enabled, the same aged account's nudge (C7) + growth (C2) beats fire normally");

        var enabledRecord = await ledger.GetMarkAsync(account);
        Assert.Multiple(() =>
        {
            Assert.That(enabledRecord!.NudgeShown, Is.True, "enabling the feature lets the nudge stamp land");
            Assert.That(enabledRecord.LastVisitStage, Is.EqualTo(1), "enabling the feature lets the visit stamp land");
        });

    }
}
