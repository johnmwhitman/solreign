#nullable enable
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.Providence;
using Content.Server._Solreign.SeasonLedger;
using Content.Server.GameTicking;
using Content.Shared.Administration.Systems;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Preferences;
using NUnit.Framework;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     "The Authored First Death" scene wiring (docs/specs/FIRST-DEATH-SPEC-2026-07-16-DRAFT.md §7,
///     FD-W2): drives <c>ProvidenceFirstDeathSystem</c> end-to-end against the real connected pool
///     session — the once-per-ACCOUNT guarantee (two deaths → one scene), enabled=false → zero
///     behavior, the rehire beat's exactly-once next-spawn delivery (Silent spawns skipped), and
///     coexistence with the pre-existing death beats (commiseration + EarlyDeath + the directed
///     telemetry pair all subscribe the same death moment; additive-only proof).
///
///     Deaths are REAL: damage to exactly the MobThresholdSystem-reported Dead threshold (the
///     DefibrillatorTest/Changeling-cycle technique — never a hand-set MobStateComponent, and never
///     a synthetic MobStateChangedEvent, which downstream alert systems reject with error logs that
///     fail the pair). Revivals between deaths use <see cref="RejuvenateSystem.PerformRejuvenate"/>.
///     Blunt-only damage with no attacker also pins the classifier's real snapshot path:
///     Blunt-dominant without Asphyxiation must classify MISADVENTURE (the resolved barotrauma rule's
///     complement).
///
///     Pure mechanics (claim atomicity, classifier table, picker determinism, queue drain-once,
///     round guard) are exhaustively unit-tested without a server in Content.Tests/_Solreign/
///     FirstDeath*Tests.cs — this file only covers the ECS wiring those tests cannot reach.
///
///     Every test sets CVars, then calls <c>ResetRoundStateForTests</c>, in that order (the
///     ProvidenceFirstShiftWelcomeIntegrationTest rationale, verbatim): the pool-connected session
///     already goes through a REAL spawn during setup, which — with this feature's CVars at their
///     production default (on) — already ran a real rehire lookup the test never asked for.
/// </summary>
[TestFixture]
public sealed class FirstDeathSceneIntegrationTest : GameTest
{
    // Dirty: flips CCVars directly, kills and rejuvenates the real connected session's mob — this
    // server must never be handed back to the pool.
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        // Real attached body required (HotPotato/SeasonLedger/Welcome precedent) — DummyTicker=false.
        DummyTicker = false,
    };

    private static readonly ProtoId<DamageTypePrototype> BluntDamageTypeId = "Blunt";

    /// <summary>Per-test temp path the FD-W4 obituary JSONL is redirected to (see
    /// <see cref="SetUpCleanSlateAsync"/>); cleaned up after each test.</summary>
    private string? _obituaryJsonlPath;

    [TearDown]
    public void DeleteObituaryJsonl()
    {
        if (_obituaryJsonlPath is { } path && File.Exists(path))
            File.Delete(path);
        _obituaryJsonlPath = null;
    }

    private string[] ReadObituaryJsonlLines()
    {
        return _obituaryJsonlPath is { } path && File.Exists(path)
            ? File.ReadAllLines(path)
            : Array.Empty<string>();
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

    /// <summary>Kills the mob for real: Blunt damage to exactly the Dead threshold, letting
    /// MobThresholdSystem raise the genuine MobStateChangedEvent every death system consumes.</summary>
    private async Task KillForRealAsync(EntityUid mob)
    {
        var server = Server;
        var entMan = server.EntMan;
        var damageable = server.System<DamageableSystem>();
        var mobThresholds = server.System<MobThresholdSystem>();

        await server.WaitPost(() =>
        {
            var comp = entMan.GetComponent<DamageableComponent>(mob);
            var deadThreshold = mobThresholds.GetThresholdForState(mob, MobState.Dead);
            var deadDamage = new DamageSpecifier(SProtoMan.Index(BluntDamageTypeId), deadThreshold);
            damageable.SetDamage((mob, comp), deadDamage);
        });

        await server.WaitRunTicks(6); // let MobThresholdSystem process the damage into a state change

        await server.WaitAssertion(() =>
        {
            var mobState = entMan.GetComponent<MobStateComponent>(mob);
            Assert.That(mobState.CurrentState, Is.EqualTo(MobState.Dead),
                "Setup failed: the real damage path never produced an actual death.");
        });
    }

    /// <summary>Brings a really-dead mob all the way back (admin rejuvenate) so it can die again or
    /// receive the rehire beat's alive-body delivery.</summary>
    private async Task ReviveForRealAsync(EntityUid mob)
    {
        var server = Server;
        var entMan = server.EntMan;
        var rejuvenate = server.System<RejuvenateSystem>();

        await server.WaitPost(() => rejuvenate.PerformRejuvenate(mob));
        await server.WaitRunTicks(6);

        await server.WaitAssertion(() =>
        {
            var mobState = entMan.GetComponent<MobStateComponent>(mob);
            Assert.That(mobState.CurrentState, Is.EqualTo(MobState.Alive),
                "Setup failed: rejuvenate never restored the mob to Alive.");
        });
    }

    private async Task<(EntityUid Mob, ICommonSession Session, Guid Account)> SetUpCleanSlateAsync(
        ProvidenceFirstDeathSystem firstDeath,
        bool enabled = true)
    {
        var server = Server;
        var playerMan = server.ResolveDependency<IPlayerManager>();

        server.CfgMan.SetCVar(CCVars.SolreignFirstDeathEnabled, enabled);

        // FD-W4: every claimed first death also appends to the obituary JSONL ledger — redirect it
        // to a per-test temp file (the SeasonLedgerDbPath law: tests never write the real server
        // data location) and zero the egress counter so every scenario starts provably egress-free.
        var obituarySystem = server.System<FirstDeathObituarySystem>();
        _obituaryJsonlPath = Path.Combine(Path.GetTempPath(), $"first_deaths_test_{Guid.NewGuid():N}.jsonl");
        await server.WaitPost(() =>
        {
            obituarySystem.SetJsonlPathForTests(_obituaryJsonlPath);
            obituarySystem.SetSendOverrideForTests(null);
            obituarySystem.ResetSendAttemptsForTests();
        });

        await server.WaitPost(firstDeath.ResetRoundStateForTests);
        await server.WaitRunTicks(1);

        EntityUid mob = default;
        ICommonSession session = default!;
        await server.WaitPost(() =>
        {
            session = playerMan.Sessions[0];
            mob = session.AttachedEntity
                  ?? throw new InvalidOperationException("Connected player has no AttachedEntity -- DummyTicker must be false.");
        });

        return (mob, session, session.UserId.UserId);
    }

    private async Task<bool> PollAsync(Func<bool> condition, int maxTicks = 50)
    {
        var server = Server;
        var satisfied = false;
        for (var attempt = 0; attempt < maxTicks && !satisfied; attempt++)
        {
            await server.WaitRunTicks(1);
            await server.WaitPost(() => satisfied = condition());
        }

        return satisfied;
    }

    [Test]
    public async Task FreshAccount_Dies_ExactlyOneSceneAndOneClaim_LaterDeathsNothing()
    {
        var server = Server;
        var firstDeath = server.System<ProvidenceFirstDeathSystem>();
        var ledger = server.System<SeasonLedgerSystem>();
        var ticker = server.System<GameTicker>();

        var (mob, _, account) = await SetUpCleanSlateAsync(firstDeath);

        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound),
                "Precondition: the scene's fire-time guard requires an active round.");
            Assert.That(firstDeath.PendingBeatCountForTests, Is.EqualTo(0), "clean slate");
        });

        // FIRST death: the async claim must land exactly one pending death-scene beat.
        await KillForRealAsync(mob);

        var scheduled = await PollAsync(
            () => firstDeath.PendingBeatCountOfKindForTests(FirstDeathBeatKind.DeathScene) == 1);
        Assert.That(scheduled, Is.True,
            "A fresh account's first death must schedule exactly one authored death-scene beat.");

        // FD-W3: the claimed first death composed exactly one extended crypt report. Composition
        // is counted BEFORE SolreignCryptSystem's own fail-closed DirectorChannel gate — which, at
        // this fixture's production-default CVars (crypt ON, Director master OFF, token empty),
        // swallows the report without attempting any outbound HTTP (the offline law,
        // DirectorChannel.TryGetReadyToken's short-circuit).
        await server.WaitAssertion(() =>
        {
            Assert.That(server.CfgMan.GetCVar(CCVars.SolreignCryptEnabled), Is.True,
                "precondition: the crypt feature ships activated");
            Assert.That(server.CfgMan.GetCVar(CCVars.SolreignDirectorEnabled), Is.False,
                "precondition: the Director master gate keeps the fixture offline");
            Assert.That(server.CfgMan.GetCVar(CCVars.SolreignDirectorToken), Is.Empty,
                "precondition: no signed Director egress is possible without a token");
            Assert.That(firstDeath.CryptReportsComposedForTests, Is.EqualTo(1),
                "a claimed first death must compose exactly one extended crypt report");
        });

        // The claim row is durably present, snapshotted at the death tick.
        var record = await ledger.GetFirstDeathAsync(account);
        Assert.That(record, Is.Not.Null, "the atomic claim row must exist once the scene scheduled");
        Assert.Multiple(() =>
        {
            Assert.That(record!.CharacterName, Is.Not.Empty, "the eulogy needs the name as eulogized");
            Assert.That(record.EpitaphId, Is.Not.Empty, "the plaque is persisted by plate id");
            Assert.That(record.Cause, Is.EqualTo("MISADVENTURE"),
                "a real Blunt-only, no-attacker, no-asphyxiation death must classify MISADVENTURE " +
                "(the resolved barotrauma rule's complement)");
            Assert.That(record.RehireShown, Is.False, "the rehire beat has not fired yet");
        });

        // Firing the due beat must deliver (announcement + sting + private line) without throwing
        // and must drain the queue — a fired beat can never re-fire.
        await server.WaitAssertion(() =>
        {
            Assert.DoesNotThrow(() => firstDeath.FireDueBeatsForTests(server.Timing.CurTime + TimeSpan.FromDays(1)));
            Assert.That(firstDeath.PendingBeatCountForTests, Is.EqualTo(0));
        });

        // SECOND death, same round: the per-round in-flight guard stops the dispatch outright.
        await ReviveForRealAsync(mob);
        await KillForRealAsync(mob);
        for (var i = 0; i < 20; i++)
            await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(firstDeath.PendingBeatCountOfKindForTests(FirstDeathBeatKind.DeathScene), Is.EqualTo(0),
                "a second death in the same round must never schedule a second scene");
            Assert.That(firstDeath.CryptReportsComposedForTests, Is.EqualTo(1),
                "an unclaimed (repeat) death must never compose another crypt report");
        });

        // THIRD death, next round (simulated boundary — clears the in-flight guard, exactly what a
        // real round start does): the guard passes but the persistent claim row must refuse.
        await server.WaitPost(firstDeath.ResetRoundStateForTests);
        await ReviveForRealAsync(mob);
        await KillForRealAsync(mob);
        for (var i = 0; i < 30; i++)
            await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(firstDeath.PendingBeatCountOfKindForTests(FirstDeathBeatKind.DeathScene), Is.EqualTo(0),
                "the once-per-account-EVER claim must hold across round boundaries: many deaths -> one scene");
            // The simulated round boundary reset the composition counter with the round state; the
            // refused claim must leave it at zero — the crypt report is downstream of the CLAIM,
            // never of the death (FD-W3: the payload fires only for claimed first deaths).
            Assert.That(firstDeath.CryptReportsComposedForTests, Is.EqualTo(0),
                "a claim-refused death must never compose a crypt report");
        });
    }

    [Test]
    public async Task EnabledFalse_IsZeroBehavior_NoBeat_NoClaim()
    {
        var server = Server;
        var firstDeath = server.System<ProvidenceFirstDeathSystem>();
        var ledger = server.System<SeasonLedgerSystem>();

        var (mob, _, account) = await SetUpCleanSlateAsync(firstDeath, enabled: false);

        await KillForRealAsync(mob);

        // Give any (wrongly) dispatched async claim ample ticks to land.
        for (var i = 0; i < 30; i++)
            await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(firstDeath.PendingBeatCountForTests, Is.EqualTo(0),
                "solreign.first_death.enabled=false must be a full kill switch: no beat may schedule");
            Assert.That(firstDeath.CryptReportsComposedForTests, Is.EqualTo(0),
                "disabled means ZERO behavior — no extended crypt report may compose either (FD-W3)");
        });

        Assert.That(await ledger.GetFirstDeathAsync(account), Is.Null,
            "disabled means ZERO behavior — not even the claim row may be written");

        // Restore for pool hygiene even though this fixture is Dirty (defensive, cheap).
        server.CfgMan.SetCVar(CCVars.SolreignFirstDeathEnabled, true);
    }

    [Test]
    public async Task Rehire_FiresOnNextSpawn_ExactlyOnce_AndSilentSpawnsAreSkipped()
    {
        var server = Server;
        var entMan = server.EntMan;
        var firstDeath = server.System<ProvidenceFirstDeathSystem>();
        var ledger = server.System<SeasonLedgerSystem>();

        var (mob, session, account) = await SetUpCleanSlateAsync(firstDeath);

        // Establish the claim (the death scene itself is covered above), then bring the body back
        // so the rehire beat's fire-time alive-body guard can pass.
        await KillForRealAsync(mob);
        var claimed = await PollAsync(
            () => firstDeath.PendingBeatCountOfKindForTests(FirstDeathBeatKind.DeathScene) == 1);
        Assert.That(claimed, Is.True, "Setup failed: the first death never scheduled its scene.");
        await server.WaitPost(() => firstDeath.FireDueBeatsForTests(server.Timing.CurTime + TimeSpan.FromDays(1)));
        await ReviveForRealAsync(mob);

        // A SILENT spawn (cryo-return semantics) must not trigger the rehire beat at all.
        await server.WaitPost(() =>
            entMan.EventBus.RaiseLocalEvent(mob, MakeSpawnEvent(mob, session, silent: true), broadcast: true));
        for (var i = 0; i < 20; i++)
            await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(firstDeath.PendingBeatCountOfKindForTests(FirstDeathBeatKind.Rehire), Is.EqualTo(0),
                "a Silent spawn must never reach the rehire lookup");
        });
        Assert.That((await ledger.GetFirstDeathAsync(account))!.RehireShown, Is.False,
            "a Silent spawn must not consume the once-ever rehire stamp");

        // The NEXT eligible spawn: rehire_shown is stamped write-first, then the private beat queues.
        await server.WaitPost(() =>
            entMan.EventBus.RaiseLocalEvent(mob, MakeSpawnEvent(mob, session), broadcast: true));

        var rehireQueued = await PollAsync(
            () => firstDeath.PendingBeatCountOfKindForTests(FirstDeathBeatKind.Rehire) == 1);
        Assert.That(rehireQueued, Is.True,
            "the first eligible spawn after a claimed first death must schedule exactly one rehire beat");
        Assert.That((await ledger.GetFirstDeathAsync(account))!.RehireShown, Is.True,
            "the stamp must be written BEFORE the beat delivers (write-before-dispatch)");

        // Delivery: popup + private chat line against the live, alive body — must not throw, must drain.
        await server.WaitAssertion(() =>
        {
            Assert.DoesNotThrow(() => firstDeath.FireDueBeatsForTests(server.Timing.CurTime + TimeSpan.FromDays(1)));
            Assert.That(firstDeath.PendingBeatCountForTests, Is.EqualTo(0));
        });

        // A SECOND eligible spawn: the conditional-update stamp must refuse a repeat, forever.
        await server.WaitPost(() =>
            entMan.EventBus.RaiseLocalEvent(mob, MakeSpawnEvent(mob, session), broadcast: true));
        for (var i = 0; i < 30; i++)
            await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(firstDeath.PendingBeatCountOfKindForTests(FirstDeathBeatKind.Rehire), Is.EqualTo(0),
                "the rehire beat fires ONCE ever — a later spawn must never re-deliver it");
        });
    }

    [Test]
    public async Task Coexistence_TheSameDeathStillFeedsEveryPreExistingDeathBeat()
    {
        // Additive-only proof (spec §3.6/§7): this system's broadcast MobStateChangedEvent
        // subscription coexists with ProvidenceCommiserationSystem + SeasonLedgerSystem.EarlyDeath
        // (broadcast) and SolreignDeathTelemetrySystem (the directed (ActorComponent, ...) pair) on
        // ONE real dispatched death — the server booted with all four subscribed (the event bus
        // would have thrown at Initialize on a directed collision), and the real death must complete
        // with every default-on system live. The commiseration double-beat this can produce is
        // ACCEPTED for v1 (open question 3 unresolved — deliberately no suppressor in this lane).
        var server = Server;
        var firstDeath = server.System<ProvidenceFirstDeathSystem>();
        var ledger = server.System<SeasonLedgerSystem>();

        var (mob, _, account) = await SetUpCleanSlateAsync(firstDeath);

        // Commiseration at its production default (on) — the coexistence under test.
        server.CfgMan.SetCVar(CCVars.SolreignProvidenceCommiserationEnabled, true);

        await KillForRealAsync(mob);

        var scheduled = await PollAsync(
            () => firstDeath.PendingBeatCountOfKindForTests(FirstDeathBeatKind.DeathScene) == 1);

        Assert.That(scheduled, Is.True,
            "the authored scene must still claim and schedule with every coexisting death beat live");
        Assert.That(await ledger.GetFirstDeathAsync(account), Is.Not.Null);
    }

    [Test]
    public async Task PostRoundDeath_DoesNotClaim_TheSceneSurvivesForARealRound()
    {
        // Review F1 (cdx round 1, CONFIRMED P0): the claim gate must hold at CLAIM time, not just
        // fire time. A first death during PostRound — the classic post-round brawl — must NOT
        // consume the once-per-ACCOUNT claim, because FireBeat's own InRound guard would drop the
        // beat and the player's authored first death would be permanently lost undelivered.
        var server = Server;
        var firstDeath = server.System<ProvidenceFirstDeathSystem>();
        var ledger = server.System<SeasonLedgerSystem>();
        var ticker = server.System<GameTicker>();

        var (mob, _, account) = await SetUpCleanSlateAsync(firstDeath);

        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound), "precondition");
            ticker.EndRound();
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PostRound),
                "EndRound must land PostRound before the death under test");
        });

        // The post-round brawl death: the mob still exists and takes real damage in PostRound.
        await KillForRealAsync(mob);

        // Give the (would-be) async claim generous time to land, then assert it never did.
        var claimed = await PollAsync(
            () => firstDeath.PendingBeatCountOfKindForTests(FirstDeathBeatKind.DeathScene) > 0,
            maxTicks: 30);
        Assert.That(claimed, Is.False,
            "a PostRound death must not schedule a death-scene beat (it could never fire)");
        Assert.That(await ledger.GetFirstDeathAsync(account), Is.Null,
            "a PostRound death must not burn the once-per-account claim row");

        // Un-poison the fixture for any test that runs after this one: restart into a live round
        // (the test pool runs lobby-disabled, so RestartRound re-enters InRound and respawns the
        // connected session).
        await server.WaitPost(() => ticker.RestartRound());
        var backInRound = await PollAsync(() => ticker.RunLevel == GameRunLevel.InRound, maxTicks: 120);
        Assert.That(backInRound, Is.True, "fixture cleanup: a fresh round must start after RestartRound");

        // This is a connected fixture. RestartRound flushes the old entity tree and sends the
        // replacement round to the client; leaving that traffic queued makes generic teardown the
        // first client tick after the reset, where any sync fault is hidden by its bare Assert.Fail.
        // Match the repository's connected RestartRoundTest contract and prove the new round can
        // synchronize here, while this test still owns the operation and can report a real stack.
        await Pair.RunUntilSynced();
    }

    // --- FD-W4: the Discord obituary leg (spec §6 — designed, GATED, ships inert) -------------------

    /// <summary>Fake but parseable webhook URL for the armed scenarios — no test ever contacts
    /// it: the send path is always replaced by the mock seam before the webhook can matter.</summary>
    private const string FakeWebhookUrl = "https://discord.com/api/webhooks/1234567890/not-a-real-token";

    [Test]
    public async Task Obituary_DefaultConfig_JsonlOnly_ZeroEgress_OncePerClaim()
    {
        // THE inert-by-design proof (spec §6.2 webhook gate / §9 Q1): at production defaults the
        // webhook CVar is EMPTY, so a claimed first death lands exactly one manual-paste JSONL
        // line and the send path is never entered — zero egress with default config.
        var server = Server;
        var firstDeath = server.System<ProvidenceFirstDeathSystem>();
        var obituary = server.System<FirstDeathObituarySystem>();

        var (mob, _, _) = await SetUpCleanSlateAsync(firstDeath);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.CfgMan.GetCVar(CCVars.SolreignFirstDeathWebhook), Is.Empty,
                "precondition: the webhook CVar SHIPS empty — this test runs the shipping defaults");
            Assert.That(obituary.WebhookConfiguredForTests, Is.False,
                "an empty webhook CVar must never produce a usable webhook identifier");
        });

        await KillForRealAsync(mob);
        var scheduled = await PollAsync(
            () => firstDeath.PendingBeatCountOfKindForTests(FirstDeathBeatKind.DeathScene) == 1);
        Assert.That(scheduled, Is.True, "Setup failed: the first death never claimed.");

        var lines = ReadObituaryJsonlLines();
        Assert.That(lines, Has.Length.EqualTo(1),
            "a claimed first death appends exactly ONE ready-to-paste obituary line");

        using (var doc = JsonDocument.Parse(lines[0]))
        {
            Assert.Multiple(() =>
            {
                Assert.That(doc.RootElement.GetProperty("dispatch").GetString(), Is.EqualTo("webhook-empty"),
                    "the gate decision is recorded in the line (write-before-dispatch)");
                Assert.That(doc.RootElement.GetProperty("paste").GetString(), Does.Contain("In Memoriam:"),
                    "the paste block is the ready-to-hand §8D text");
            });
        }

        Assert.That(obituary.DiscordSendAttemptsForTests, Is.EqualTo(0),
            "ZERO egress with default config — the send path must never even be entered");

        // Once-per-claim idempotence: a repeat death (same round, then a fresh-round claim refusal)
        // must never append a second line.
        await ReviveForRealAsync(mob);
        await KillForRealAsync(mob);
        for (var i = 0; i < 20; i++)
            await server.WaitRunTicks(1);

        await server.WaitPost(firstDeath.ResetRoundStateForTests);
        await ReviveForRealAsync(mob);
        await KillForRealAsync(mob);
        for (var i = 0; i < 30; i++)
            await server.WaitRunTicks(1);

        Assert.Multiple(() =>
        {
            Assert.That(ReadObituaryJsonlLines(), Has.Length.EqualTo(1),
                "the JSONL rides the once-per-account-EVER claim: many deaths -> one line, ever");
            Assert.That(obituary.DiscordSendAttemptsForTests, Is.EqualTo(0), "still zero egress");
        });
    }

    [Test]
    public async Task Obituary_WebhookSetButBelowPlayerGate_JsonlOnly_NoSend()
    {
        // The recap-law analogue (spec §6.2 player gate): with the webhook ARMED but connected
        // players below min_players at death time, the advertisement surface stays silent —
        // "Below the gate, the obituary goes to the JSONL file only."
        var server = Server;
        var firstDeath = server.System<ProvidenceFirstDeathSystem>();
        var obituary = server.System<FirstDeathObituarySystem>();

        var (mob, _, _) = await SetUpCleanSlateAsync(firstDeath);

        server.CfgMan.SetCVar(CCVars.SolreignFirstDeathWebhook, FakeWebhookUrl);
        server.CfgMan.SetCVar(CCVars.SolreignFirstDeathMinPlayers, 99);
        var mockSends = 0;
        await server.WaitPost(() =>
            obituary.SetSendOverrideForTests(_ =>
            {
                mockSends++;
                return Task.CompletedTask;
            }));

        await server.WaitAssertion(() =>
            Assert.That(obituary.WebhookConfiguredForTests, Is.True,
                "precondition: the fake webhook URL must parse — this scenario tests the PLAYER gate"));

        await KillForRealAsync(mob);
        var scheduled = await PollAsync(
            () => firstDeath.PendingBeatCountOfKindForTests(FirstDeathBeatKind.DeathScene) == 1);
        Assert.That(scheduled, Is.True, "Setup failed: the first death never claimed.");

        var lines = ReadObituaryJsonlLines();
        Assert.That(lines, Has.Length.EqualTo(1), "below the player gate the JSONL line still lands");
        using (var doc = JsonDocument.Parse(lines[0]))
        {
            Assert.That(doc.RootElement.GetProperty("dispatch").GetString(), Is.EqualTo("below-player-gate"));
        }

        Assert.Multiple(() =>
        {
            Assert.That(obituary.DiscordSendAttemptsForTests, Is.EqualTo(0),
                "an under-witnessed death must never reach the send path");
            Assert.That(mockSends, Is.EqualTo(0), "and the mock confirms it: zero invocations");
        });

        // Disarm for pool hygiene even though this fixture is Dirty (defensive, cheap).
        server.CfgMan.SetCVar(CCVars.SolreignFirstDeathWebhook, string.Empty);
        await server.WaitPost(() => obituary.SetSendOverrideForTests(null));
    }

    [Test]
    public async Task Obituary_BothGatesMet_SendPathInvokedExactlyOnce_OnTheMock()
    {
        // The armed path, fully mocked (no test ever egresses): webhook parseable + player gate
        // met -> the send path is entered exactly ONCE with the §8D payload, and the JSONL line
        // still lands FIRST (write-before-dispatch) recording the dispatch decision.
        var server = Server;
        var firstDeath = server.System<ProvidenceFirstDeathSystem>();
        var obituary = server.System<FirstDeathObituarySystem>();

        var (mob, _, _) = await SetUpCleanSlateAsync(firstDeath);

        server.CfgMan.SetCVar(CCVars.SolreignFirstDeathWebhook, FakeWebhookUrl);
        // The pooled fixture has exactly one connected player; arm the gate for it.
        server.CfgMan.SetCVar(CCVars.SolreignFirstDeathMinPlayers, 1);

        var mockSends = 0;
        var mockTitle = "";
        await server.WaitPost(() =>
            obituary.SetSendOverrideForTests(payload =>
            {
                mockSends++;
                if (payload.Embeds is { Count: > 0 } embeds)
                    mockTitle = embeds[0].Title;
                return Task.CompletedTask;
            }));

        await KillForRealAsync(mob);
        var scheduled = await PollAsync(
            () => firstDeath.PendingBeatCountOfKindForTests(FirstDeathBeatKind.DeathScene) == 1);
        Assert.That(scheduled, Is.True, "Setup failed: the first death never claimed.");

        var sent = await PollAsync(() => obituary.DiscordSendAttemptsForTests == 1);
        Assert.That(sent, Is.True, "with both gates met the send path must be invoked");

        Assert.Multiple(() =>
        {
            Assert.That(mockSends, Is.EqualTo(1), "exactly ONE send per claimed first death");
            Assert.That(mockTitle, Does.StartWith("In Memoriam: ").And.EndWith(" · First Departure"),
                "the mock received the §8D embed");
        });

        var lines = ReadObituaryJsonlLines();
        Assert.That(lines, Has.Length.EqualTo(1), "the JSONL line lands even when the webhook fires");
        using (var doc = JsonDocument.Parse(lines[0]))
        {
            Assert.That(doc.RootElement.GetProperty("dispatch").GetString(), Is.EqualTo("dispatched"));
        }

        // A repeat death must not send again (the claim is the linchpin; no claim, no obituary).
        await ReviveForRealAsync(mob);
        await KillForRealAsync(mob);
        for (var i = 0; i < 20; i++)
            await server.WaitRunTicks(1);

        Assert.Multiple(() =>
        {
            Assert.That(obituary.DiscordSendAttemptsForTests, Is.EqualTo(1),
                "a repeat death never re-enters the send path");
            Assert.That(ReadObituaryJsonlLines(), Has.Length.EqualTo(1), "and never re-appends");
        });

        // Disarm for pool hygiene even though this fixture is Dirty (defensive, cheap).
        server.CfgMan.SetCVar(CCVars.SolreignFirstDeathWebhook, string.Empty);
        await server.WaitPost(() => obituary.SetSendOverrideForTests(null));
    }
}
