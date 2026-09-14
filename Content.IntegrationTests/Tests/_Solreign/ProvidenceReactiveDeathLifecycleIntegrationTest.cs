#nullable enable
using System;
using System.Linq;
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
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Qualifies the server-side death classification and dispatch-attempt path in
///     <see cref="ProvidenceEventReactiveSystem"/> against genuine ECS deaths. The existing fixture
///     covers only synthetic round-end transitions; these tests use real threshold damage so every
///     subscriber sees the same <see cref="MobStateChangedEvent"/> raised in production. Actual
///     client receipt, global audience delivery and audio playback remain outside this fixture.
///
///     Each test gets a fresh, destructive connected pair. First Death and Commiseration are disabled
///     to isolate reactive state and keep their independent Ledger, obituary and audio effects out of
///     the assertion surface. No external Director or Crypt channel is enabled.
/// </summary>
[TestFixture]
public sealed class ProvidenceReactiveDeathLifecycleIntegrationTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Fresh = true,
        Destructive = true,
        DummyTicker = false,
    };

    private static readonly ProtoId<DamageTypePrototype> BluntDamageTypeId = "Blunt";

    private sealed record CVarSnapshot(
        float ReactiveCooldownSeconds,
        bool FirstDeathEnabled,
        bool CommiserationEnabled);

    private async Task<(EntityUid Mob, Guid Account, string SanitizedName, CVarSnapshot Snapshot)> SetUpAsync(
        bool reactiveEnabled)
    {
        var server = Server;
        var reactive = server.System<ProvidenceEventReactiveSystem>();
        var ticker = server.System<GameTicker>();

        var snapshot = new CVarSnapshot(
            server.CfgMan.GetCVar(CCVars.SolreignProvidenceReactiveCooldownSeconds),
            server.CfgMan.GetCVar(CCVars.SolreignFirstDeathEnabled),
            server.CfgMan.GetCVar(CCVars.SolreignProvidenceCommiserationEnabled));

        server.CfgMan.SetCVar(CCVars.SolreignProvidenceReactiveEnabled, reactiveEnabled);
        server.CfgMan.SetCVar(CCVars.SolreignProvidenceReactiveCooldownSeconds, 90f);
        server.CfgMan.SetCVar(CCVars.SolreignFirstDeathEnabled, false);
        server.CfgMan.SetCVar(CCVars.SolreignProvidenceCommiserationEnabled, false);
        await server.WaitRunTicks(1);
        await server.WaitPost(reactive.ResetRoundStateForTests);

        EntityUid mob = default;
        Guid account = default;
        string sanitizedName = default!;
        await server.WaitPost(() =>
        {
            ICommonSession session = ServerSession
                ?? throw new InvalidOperationException("Connected pair has no server session.");
            mob = session.AttachedEntity
                ?? throw new InvalidOperationException("Connected player has no body; DummyTicker must be false.");
            account = session.UserId.UserId;
            sanitizedName = ProvidenceNameSanitizer.Sanitize(Identity.Name(mob, SEntMan))
                ?? throw new InvalidOperationException("Fixture character name did not pass the production sanitizer.");
        });

        await server.WaitAssertion(() =>
            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound),
                "The reactive death handler intentionally rejects deaths outside an active round."));

        return (mob, account, sanitizedName, snapshot);
    }

    private async Task QuiesceAsync(CVarSnapshot snapshot)
    {
        var server = Server;

        // This pair is Destructive, so leave Reactive OFF until disposal. Its Ledger continuation is
        // async-void; restoring the usual true default after an arbitrary tick delay could let a late
        // continuation reach a dispatch attempt during teardown. This prevents late dispatch; it does
        // not claim every continuation has completed.
        server.CfgMan.SetCVar(CCVars.SolreignProvidenceReactiveEnabled, false);
        await server.WaitRunTicks(2);

        server.CfgMan.SetCVar(CCVars.SolreignProvidenceReactiveCooldownSeconds, snapshot.ReactiveCooldownSeconds);
        server.CfgMan.SetCVar(CCVars.SolreignFirstDeathEnabled, snapshot.FirstDeathEnabled);
        server.CfgMan.SetCVar(CCVars.SolreignProvidenceCommiserationEnabled, snapshot.CommiserationEnabled);
        await server.WaitRunTicks(1);
    }

    private async Task KillForRealAsync(EntityUid mob)
    {
        var server = Server;
        var damageable = server.System<DamageableSystem>();
        var thresholds = server.System<MobThresholdSystem>();

        await server.WaitPost(() =>
        {
            var damageableComponent = SEntMan.GetComponent<DamageableComponent>(mob);
            var deadThreshold = thresholds.GetThresholdForState(mob, MobState.Dead);
            var damage = new DamageSpecifier(SProtoMan.Index(BluntDamageTypeId), deadThreshold);
            damageable.SetDamage((mob, damageableComponent), damage);
        });
        await server.WaitRunTicks(6);

        await server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<MobStateComponent>(mob).CurrentState, Is.EqualTo(MobState.Dead),
                "Setup failed: real threshold damage did not produce a genuine death."));
    }

    private async Task ReviveForRealAsync(EntityUid mob)
    {
        var server = Server;
        var rejuvenate = server.System<RejuvenateSystem>();

        await server.WaitPost(() => rejuvenate.PerformRejuvenate(mob));
        await server.WaitRunTicks(6);
        await server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<MobStateComponent>(mob).CurrentState, Is.EqualTo(MobState.Alive),
                "Setup failed: rejuvenation did not restore the fixture body."));
    }

    private async Task<bool> PollAsync(Func<bool> condition, int maxTicks = 50)
    {
        var met = false;
        for (var i = 0; i < maxTicks && !met; i++)
        {
            await Server.WaitRunTicks(1);
            await Server.WaitPost(() => met = condition());
        }

        return met;
    }

    [Test]
    public async Task Disabled_RealDeathLeavesReactiveStateSilent()
    {
        var reactive = Server.System<ProvidenceEventReactiveSystem>();
        var (mob, account, _, snapshot) = await SetUpAsync(reactiveEnabled: false);

        try
        {
            await KillForRealAsync(mob);
            await Server.WaitRunTicks(30);

            await Server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(reactive.DeathCountThisRoundForTests(account), Is.EqualTo(0));
                    Assert.That(reactive.ReactiveDispatchCountForTests, Is.EqualTo(0));
                    Assert.That(reactive.LastDeathLineClassForTests, Is.Null);
                    Assert.That(reactive.LastDispatchTextForTests, Is.Null);
                });
            });
        }
        finally
        {
            await QuiesceAsync(snapshot);
        }
    }

    [Test]
    public async Task Enabled_FreshAccountRealDeathsAttemptDispatchOnceAndCooldownSuppressesBurst()
    {
        var reactive = Server.System<ProvidenceEventReactiveSystem>();
        var ledger = Server.System<SeasonLedgerSystem>();
        var (mob, account, name, snapshot) = await SetUpAsync(reactiveEnabled: true);

        try
        {
            Assert.That(await ledger.GetFirstDeathAsync(account), Is.Null,
                "Fresh-pair precondition: this account must not already carry a Ledger memory.");

            await KillForRealAsync(mob);
            Assert.That(await PollAsync(() => reactive.ReactiveDispatchCountForTests == 1), Is.True,
                "A fresh account's real death never reached the reactive dispatch-attempt boundary.");

            await Server.WaitAssertion(() =>
            {
                var expectedLines = ProvidenceReactiveCopy.DeathGenericKeys
                    .Select(key => Loc.GetString(key, ("name", name)))
                    .ToArray();

                var text = reactive.LastDispatchTextForTests;
                var isCuratedGeneric = expectedLines.Contains(text);
                var containsRawAccountId = text?.Contains(account.ToString(), StringComparison.Ordinal) == true;

                Assert.Multiple(() =>
                {
                    Assert.That(reactive.DeathCountThisRoundForTests(account), Is.EqualTo(1));
                    Assert.That(reactive.LastDeathLineClassForTests, Is.EqualTo(ProvidenceReactiveDeathLineClass.Generic));
                    Assert.That(isCuratedGeneric, Is.True,
                        "The fresh-account dispatch attempt must use one curated Generic template.");
                    Assert.That(containsRawAccountId, Is.False,
                        "The player-facing announcement composition must not contain a raw account identifier.");
                });
            });

            await ReviveForRealAsync(mob);
            await KillForRealAsync(mob);
            Assert.That(
                await PollAsync(() => reactive.DeathContinuationCompletionCountForTests == 2),
                Is.True,
                "The second real-death Ledger continuation never reached a terminal path.");

            await Server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(reactive.DeathCountThisRoundForTests(account), Is.EqualTo(2),
                        "The cooldown suppresses a dispatch attempt, not truthful round-local counting.");
                    Assert.That(reactive.ReactiveDispatchCountForTests, Is.EqualTo(1),
                        "A rapid second death must be dropped by the shared cooldown.");
                });
            });
        }
        finally
        {
            await QuiesceAsync(snapshot);
        }
    }

    [Test]
    public async Task Enabled_CurrentRoundLedgerRecordStillUsesGenericClass()
    {
        const string currentRoundTitle = "Current Round Canary";

        var server = Server;
        var reactive = server.System<ProvidenceEventReactiveSystem>();
        var ledger = server.System<SeasonLedgerSystem>();
        var ticker = server.System<GameTicker>();
        var (mob, account, name, snapshot) = await SetUpAsync(reactiveEnabled: true);

        try
        {
            Assert.That(
                await ledger.TryClaimFirstDeathAsync(
                    account,
                    ticker.RoundId,
                    characterName: "Current Round Historical Name",
                    cause: "VIOLENCE",
                    toursAtDeath: 1,
                    titleAtDeath: currentRoundTitle,
                    epitaphId: "current-round-private-epitaph"),
                Is.True,
                "Fresh-pair setup failed to seed one same-round record.");

            await KillForRealAsync(mob);
            Assert.That(await PollAsync(() => reactive.ReactiveDispatchCountForTests == 1), Is.True,
                "A real death with same-round history never reached the dispatch-attempt boundary.");

            await Server.WaitAssertion(() =>
            {
                var expectedLines = ProvidenceReactiveCopy.DeathGenericKeys
                    .Select(key => Loc.GetString(key, ("name", name)))
                    .ToArray();

                var text = reactive.LastDispatchTextForTests;
                var isCuratedGeneric = expectedLines.Contains(text);
                var containsCurrentRoundTitle = text?.Contains(currentRoundTitle, StringComparison.Ordinal) == true;
                var containsRawAccountId = text?.Contains(account.ToString(), StringComparison.Ordinal) == true;

                Assert.Multiple(() =>
                {
                    Assert.That(reactive.LastDeathLineClassForTests, Is.EqualTo(ProvidenceReactiveDeathLineClass.Generic),
                        "A record from this same death round is present context, never historical memory.");
                    Assert.That(isCuratedGeneric, Is.True,
                        "A same-round record must still compose one curated Generic template.");
                    Assert.That(containsCurrentRoundTitle, Is.False,
                        "Same-round title data must not enter the player-facing announcement composition.");
                    Assert.That(containsRawAccountId, Is.False,
                        "The player-facing announcement composition must not contain a raw account identifier.");
                });
            });
        }
        finally
        {
            await QuiesceAsync(snapshot);
        }
    }

    [Test]
    public async Task Enabled_DifferentRoundIdentityRecordUsesCuratedMemoryClass()
    {
        const string historicalName = "Archived Fixture Name";
        const string historicalTitle = "Chain Closer";
        const string privateEpitaph = "private-epitaph-canary";
        var server = Server;
        var reactive = server.System<ProvidenceEventReactiveSystem>();
        var ledger = server.System<SeasonLedgerSystem>();
        var ticker = server.System<GameTicker>();
        var (mob, account, name, snapshot) = await SetUpAsync(reactiveEnabled: true);

        try
        {
            var differentRoundId = ticker.RoundId == int.MaxValue
                ? ticker.RoundId - 1
                : ticker.RoundId + 1;
            Assert.That(
                await ledger.TryClaimFirstDeathAsync(
                    account,
                    differentRoundId,
                    historicalName,
                    "VACUUM",
                    toursAtDeath: 3,
                    titleAtDeath: historicalTitle,
                    epitaphId: privateEpitaph),
                Is.True,
                "Fresh-pair setup failed to seed one pre-existing, different-round-identity memory.");

            await KillForRealAsync(mob);
            Assert.That(await PollAsync(() => reactive.ReactiveDispatchCountForTests == 1), Is.True,
                "A real death with different-round-identity history never reached the dispatch-attempt boundary.");

            await Server.WaitAssertion(() =>
            {
                var closedCauseLabel = FirstDeathCopy.CauseLabelFor(FirstDeathCause.Vacuum);
                var expectedLines = ProvidenceReactiveCopy.DeathMemoryKeys
                    .Select(key => Loc.GetString(
                        key,
                        ("name", name),
                        ("cause", closedCauseLabel),
                        ("title", historicalTitle)))
                    .ToArray();

                var text = reactive.LastDispatchTextForTests;
                var isCuratedMemory = expectedLines.Contains(text);
                var containsHistoricalName = text?.Contains(historicalName, StringComparison.Ordinal) == true;
                var containsPrivateEpitaph = text?.Contains(privateEpitaph, StringComparison.Ordinal) == true;
                var containsRawCause = text?.Contains("VACUUM", StringComparison.Ordinal) == true;
                var containsRawAccountId = text?.Contains(account.ToString(), StringComparison.Ordinal) == true;

                Assert.Multiple(() =>
                {
                    Assert.That(reactive.DeathCountThisRoundForTests(account), Is.EqualTo(1));
                    Assert.That(reactive.LastDeathLineClassForTests, Is.EqualTo(ProvidenceReactiveDeathLineClass.Memory));
                    Assert.That(isCuratedMemory, Is.True,
                        "The dispatch attempt must be one curated Memory template rendered from allowlisted fields.");
                    Assert.That(containsHistoricalName, Is.False,
                        "The historical character name must not enter player-facing announcement composition.");
                    Assert.That(containsPrivateEpitaph, Is.False,
                        "The private epitaph identifier must not enter player-facing announcement composition.");
                    Assert.That(containsRawCause, Is.False,
                        "The raw Ledger cause code must not enter player-facing announcement composition.");
                    Assert.That(containsRawAccountId, Is.False,
                        "The raw account identifier must not enter player-facing announcement composition.");
                });
            });
        }
        finally
        {
            await QuiesceAsync(snapshot);
        }
    }
}
