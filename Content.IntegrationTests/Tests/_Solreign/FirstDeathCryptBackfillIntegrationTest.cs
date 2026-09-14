#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.Director;
using Content.Server._Solreign.Providence;
using Content.Server._Solreign.SeasonLedger;
using Content.Server.Administration.Systems;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using NUnit.Framework;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     FD-W3.5 plaque backfill wiring (ProvidenceFirstDeathSystem.CryptBackfill.cs), end-to-end
///     against the real connected pool session:
///       * a banked claim row (the v13.3 wire-gap scenario — claim exists, memorial never minted)
///         is found by the round-start scan, re-reported through the REAL
///         <c>SolreignCryptSystem.ReportFirstDeath</c> wire exactly once, stamped, and never
///         re-reported by a later scan;
///       * a closed crypt channel banks rows instead of burning them — no scan, no stamp, no send;
///       * the claim-time path stamps its own row on a successful hand-off (so the backfill finds
///         nothing to do) and leaves it UNSTAMPED when the channel is closed (so the backfill can
///         recover the memorial later — the row banked in the live wire gap is exactly this shape).
///
///     Gate-direction law (see <c>SolreignCryptSystem.FirstDeathGateOverrideForTests</c>): the
///     CLOSED-gate tests ride the real fail-closed DirectorChannel chain at production-default
///     CVars (feature enabled, Director master disabled, token empty: the pool's offline law — zero
///     outbound HTTP). The OPEN-gate tests drive the same
///     code path through the paired gate+post override seams, because flipping the real Director
///     master CVar would arm the Oracle poll loop's genuine HTTP. The rate limiter is always real
///     (static, wall-clock): dispatch-expecting tests first wait out
///     <see cref="DirectorChannel.MinRequestInterval"/>.
///
///     Pure pieces (stamp semantics, unreported query, legacy-schema migration, report
///     re-composition, queue pacing) are unit-tested in
///     Content.Tests/_Solreign/FirstDeathCryptBackfillTests.cs — this file covers only the ECS
///     wiring those tests cannot reach.
/// </summary>
[TestFixture]
public sealed class FirstDeathCryptBackfillIntegrationTest : GameTest
{
    // Fresh + Destructive (stronger than the FirstDeathScene fixture's Dirty): the backfill SCANS
    // the pair's whole ledger DB, and the per-pair SQLite file survives pool recycling — a
    // recycled pair carrying another test's unstamped first_death rows (its own claim-refusal
    // would also break the death-driven tests here) makes every "the backfill finds N rows"
    // assertion flaky. Fresh = never inherit a pair; Destructive = never donate one.
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Fresh = true,
        Destructive = true,
        DummyTicker = false,
    };

    private static readonly ProtoId<DamageTypePrototype> BluntDamageTypeId = "Blunt";

    /// <summary>The real static rate limiter must have an open crypt_first_death window before a
    /// dispatch-expecting phase — earlier tests (and earlier phases) consume it.</summary>
    private static Task OpenRateWindowAsync()
    {
        return Task.Delay(DirectorChannel.MinRequestInterval + TimeSpan.FromMilliseconds(250));
    }

    /// <summary>Kills the mob for real (the FirstDeathSceneIntegrationTest technique): Blunt
    /// damage to exactly the Dead threshold, so MobThresholdSystem raises the genuine
    /// MobStateChangedEvent every death system consumes.</summary>
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

        await server.WaitRunTicks(6);

        await server.WaitAssertion(() =>
        {
            var mobState = entMan.GetComponent<MobStateComponent>(mob);
            Assert.That(mobState.CurrentState, Is.EqualTo(MobState.Dead),
                "Setup failed: the real damage path never produced an actual death.");
        });
    }

    private async Task<(EntityUid Mob, Guid Account)> SetUpCleanSlateAsync(
        ProvidenceFirstDeathSystem firstDeath)
    {
        var server = Server;
        var playerMan = server.ResolveDependency<IPlayerManager>();

        server.CfgMan.SetCVar(CCVars.SolreignFirstDeathEnabled, true);
        await server.WaitPost(firstDeath.ResetRoundStateForTests);
        await server.WaitRunTicks(1);

        EntityUid mob = default;
        Guid account = default;
        await server.WaitPost(() =>
        {
            var session = playerMan.Sessions[0];
            mob = session.AttachedEntity
                  ?? throw new InvalidOperationException("Connected player has no AttachedEntity -- DummyTicker must be false.");
            account = session.UserId.UserId;
        });

        return (mob, account);
    }

    /// <summary>Installs the paired open-gate seams; returns the captured-payload list.</summary>
    private async Task<List<string>> OpenCryptChannelForTestsAsync(SolreignCryptSystem crypt)
    {
        var payloads = new List<string>();
        await Server.WaitPost(() =>
        {
            crypt.FirstDeathGateOverrideForTests = () => true;
            crypt.FirstDeathPostOverrideForTests = payloads.Add;
        });
        return payloads;
    }

    /// <summary>Runs ticks until the condition — evaluated on the TEST thread, so it may await
    /// ledger reads without blocking the game loop — reports satisfied.</summary>
    private async Task<bool> PollAsync(Func<Task<bool>> condition, int maxTicks = 50)
    {
        var server = Server;
        for (var attempt = 0; attempt < maxTicks; attempt++)
        {
            await server.WaitRunTicks(1);
            if (await condition())
                return true;
        }

        return false;
    }

    [Test]
    public async Task BankedRow_ChannelReady_BackfillsExactlyOnce_StampsIt_NeverAgain()
    {
        var server = Server;
        var firstDeath = server.System<ProvidenceFirstDeathSystem>();
        var crypt = server.System<SolreignCryptSystem>();
        var ledger = server.System<SeasonLedgerSystem>();

        await SetUpCleanSlateAsync(firstDeath);
        var payloads = await OpenCryptChannelForTestsAsync(crypt);

        // Seed the wire-gap shape: a claim banked WITHOUT ever reaching the crypt wire —
        // exactly what every v13.3-era first death (the owner's son's included) looks like.
        var user = Guid.NewGuid();
        Assert.That(await ledger.TryClaimFirstDeathAsync(
            user, 5, "Kolton Vale", "VACUUM", 3, "Chain Closer", "10"), Is.True);
        Assert.That((await ledger.GetFirstDeathAsync(user))!.CryptReported, Is.False,
            "precondition: the banked row has no minted memorial");

        await OpenRateWindowAsync();

        // Round-start scan; the REAL Update pump then dispatches the queued item on its own
        // (the queue is transiently 1 for at most a tick — outcomes, not intermediates, are
        // asserted). Exactly one hand-off through the real ReportFirstDeath wire, then the stamp.
        await server.WaitPost(firstDeath.StartCryptBackfillForTests);

        var handed = await PollAsync(() => Task.FromResult(firstDeath.CryptBackfillHandedForTests == 1));
        Assert.That(handed, Is.True, "the banked memorial must hand off exactly once");

        await server.WaitAssertion(() =>
        {
            Assert.That(payloads, Has.Count.EqualTo(1));
            Assert.That(payloads[0], Does.Contain(user.ToString()).And.Contain("\"first_death\":true")
                .And.Contain("Out of scope for life support."),
                "the backfilled payload must be the recomposed FD-W3 wire report (plate 10, VACUUM)");
        });

        // The stamp lands after the hand-off (the async half of stamp-on-hand-off).
        var stamped = await PollAsync(async () =>
            (await ledger.GetFirstDeathAsync(user)) is { CryptReported: true });
        Assert.That(stamped, Is.True, "a handed memorial must stamp crypt_reported");

        // Double-round idempotence: the next round's scan finds a stamped row — nothing queues,
        // nothing dispatches, no second plaque.
        await server.WaitPost(firstDeath.ResetRoundStateForTests);
        await server.WaitPost(firstDeath.StartCryptBackfillForTests);
        await server.WaitRunTicks(15);
        await server.WaitAssertion(() =>
        {
            Assert.That(firstDeath.CryptBackfillQueuedForTests, Is.EqualTo(0),
                "a stamped memorial must never re-queue: the daemon mints per POST, so a " +
                "re-report would be a duplicate plaque");
            Assert.That(firstDeath.CryptBackfillHandedForTests, Is.EqualTo(0),
                "nothing may hand off on the second round");
            Assert.That(payloads, Has.Count.EqualTo(1), "exactly one plaque, ever, for one victim");
        });
    }

    [Test]
    public async Task BankedRow_ChannelClosed_StaysBanked_NeverBurned()
    {
        var server = Server;
        var firstDeath = server.System<ProvidenceFirstDeathSystem>();
        var ledger = server.System<SeasonLedgerSystem>();

        await SetUpCleanSlateAsync(firstDeath);

        // NO seams: the REAL fail-closed DirectorChannel chain at production-default CVars.
        // The feature itself is activated; the Director master and empty token close egress.
        await server.WaitAssertion(() =>
        {
            Assert.That(server.CfgMan.GetCVar(CCVars.SolreignCryptEnabled), Is.True,
                "precondition: the crypt feature ships activated");
            Assert.That(server.CfgMan.GetCVar(CCVars.SolreignDirectorEnabled), Is.False,
                "precondition: the Director master gate keeps the fixture offline");
            Assert.That(server.CfgMan.GetCVar(CCVars.SolreignDirectorToken), Is.Empty,
                "precondition: no signed Director egress is possible without a token");
        });

        var user = Guid.NewGuid();
        Assert.That(await ledger.TryClaimFirstDeathAsync(
            user, 5, "Kolton Vale", "VACUUM", 3, "Chain Closer", "10"), Is.True);

        await server.WaitPost(firstDeath.StartCryptBackfillForTests);
        await server.WaitRunTicks(15);

        await server.WaitAssertion(() =>
        {
            Assert.That(firstDeath.CryptBackfillQueuedForTests, Is.EqualTo(0),
                "a closed channel must skip the scan entirely — rows are banked, not queued");
            Assert.That(firstDeath.CryptBackfillHandedForTests, Is.EqualTo(0));
        });
        Assert.That((await ledger.GetFirstDeathAsync(user))!.CryptReported, Is.False,
            "a closed channel must never consume the once-ever memorial — banked, not burned");
    }

    [Test]
    public async Task ClaimTime_ChannelReady_StampsTheRow_AndTheBackfillFindsNothing()
    {
        var server = Server;
        var firstDeath = server.System<ProvidenceFirstDeathSystem>();
        var crypt = server.System<SolreignCryptSystem>();
        var ledger = server.System<SeasonLedgerSystem>();

        var (mob, account) = await SetUpCleanSlateAsync(firstDeath);
        var payloads = await OpenCryptChannelForTestsAsync(crypt);

        await OpenRateWindowAsync();

        // A REAL first death with the channel open: the claim-time FD-W3 wire hands off and
        // must stamp its own row (write-after-successful-hand-off).
        await KillForRealAsync(mob);

        var composed = await PollAsync(() => Task.FromResult(firstDeath.CryptReportsComposedForTests == 1));
        Assert.That(composed, Is.True, "the claimed first death must compose its crypt report");
        await server.WaitAssertion(() =>
        {
            Assert.That(payloads, Has.Count.EqualTo(1), "the claim-time report must hand off");
        });

        var stamped = await PollAsync(async () =>
            (await ledger.GetFirstDeathAsync(account)) is { CryptReported: true });
        Assert.That(stamped, Is.True,
            "a successful claim-time hand-off must stamp crypt_reported — the sole dedupe " +
            "against the backfill");

        // The backfill has nothing to do for this account: claim-time and backfill can never
        // both report one row.
        await server.WaitPost(firstDeath.ResetRoundStateForTests);
        await server.WaitPost(firstDeath.StartCryptBackfillForTests);
        await server.WaitRunTicks(15);
        await server.WaitAssertion(() =>
        {
            Assert.That(firstDeath.CryptBackfillQueuedForTests, Is.EqualTo(0),
                "the stamped claim-time row must never re-queue for backfill");
            Assert.That(firstDeath.CryptBackfillHandedForTests, Is.EqualTo(0));
            Assert.That(payloads, Has.Count.EqualTo(1), "exactly one plaque, ever, for one victim");
        });
    }

    [Test]
    public async Task ClaimTime_ChannelClosed_LeavesTheRowBanked_ForALaterBackfill()
    {
        var server = Server;
        var firstDeath = server.System<ProvidenceFirstDeathSystem>();
        var ledger = server.System<SeasonLedgerSystem>();

        var (mob, account) = await SetUpCleanSlateAsync(firstDeath);

        // NO seams: the real chain, gates closed (production defaults) — the exact shape of every
        // live-wire-gap first death: the scene plays, the claim banks, the plaque waits.
        await KillForRealAsync(mob);

        var composed = await PollAsync(() => Task.FromResult(firstDeath.CryptReportsComposedForTests == 1));
        Assert.That(composed, Is.True, "the authored scene still composes (offline law: no HTTP)");

        var record = await ledger.GetFirstDeathAsync(account);
        Assert.That(record, Is.Not.Null, "the claim must bank");
        Assert.That(record!.CryptReported, Is.False,
            "a refused hand-off must NOT stamp — the row stays recoverable by the backfill in a " +
            "round where the daemon is reachable (pre-W3.5 this memorial was lost forever)");
    }
}
