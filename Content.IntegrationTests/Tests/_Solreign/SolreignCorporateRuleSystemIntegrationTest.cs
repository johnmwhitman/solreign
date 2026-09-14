#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.Corporate;
using Content.Server._Solreign.SeasonLedger;
using Content.Server.GameTicking;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using NUnit.Framework;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Project-audit gap ("tests cover pure helper functions, not live ECS Systems"): the Corporate
///     Ladder's pure math (<c>CorporateScoring</c>) has store-level unit coverage
///     (<c>Content.Tests/_Solreign/CorporateScoringTests.cs</c>), but nothing drove the live
///     <see cref="SolreignCorporateRuleSystem"/> -- a <c>GameRuleSystem</c> with its own per-round
///     account-standing state (<c>_standing</c>/<c>_names</c>, private, no store-level test can reach
///     them) -- through its real handlers. <c>grep SolreignCorporateRuleSystem
///     Content.IntegrationTests/</c> returned nothing before this file.
///
///     Drives the rule exactly as production does: <see cref="GameTicker.StartGameRule(string, out EntityUid)"/>
///     (the same call <c>SolreignCorporateLayerSystem.OnRoundStarting</c> makes) so <c>Started</c>
///     flips the scoring gate on, then the two real ways standing gets credited --
///     <see cref="SolreignCorporateRuleSystem.AwardStanding"/> (the public Contracts-payout entry
///     point, deliberately NOT gated on the rule being active) and a directed
///     <see cref="MobStateChangedEvent"/> (the kill-attribution broadcast handler, which IS gated) --
///     then closes the loop through the real round-end handoff
///     (<see cref="SolreignCorporateRuleSystem.AppendRoundEndText"/> calling
///     <see cref="SeasonLedgerSystem.SubmitRoundStandings"/>) into the on-disk Season Ledger, the same
///     "poll the real SQLite file" idiom <c>SeasonLedgerSystemIntegrationTest</c> established for
///     exactly this class of async-void, off-tick side effect -- but that file never starts the
///     Corporate rule or touches <c>Standing</c>, so this is a distinct gap, not a duplicate.
/// </summary>
[TestFixture]
public sealed class SolreignCorporateRuleSystemIntegrationTest : GameTest
{
    // Dirty: starts a real game rule (leaves ActiveGameRuleComponent/EndedGameRuleComponent on a rule
    // entity), raises synthetic MobStateChanged/RoundEndTextAppend/RoundEndMessage events on the real
    // bus, and writes directly to the on-disk ledger DB outside GameTest's entity-cleanup tracking --
    // same "Dirty=true" precedent as SeasonLedgerSystemIntegrationTest for exactly this class of side
    // effect, plus WerewolfPolymorphTriggerTest's precedent for leftover rule/mob entities.
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        // DummyTicker=false so the connected session has a real attached body with a real Mind
        // (required to resolve MobStateChangedEvent.Origin back to an account in OnMobStateChanged).
        DummyTicker = false,
    };

    private const string CorporateRuleId = "SolreignCorporate";

    /// <summary>
    ///     Resolves the ledger file through the SAME helper <see cref="SeasonLedgerSystem.Initialize"/>
    ///     uses, so the test opens exactly the file the live system writes. In integration tests that
    ///     is a unique per-server-instance temp file — <c>Content.IntegrationTests.Pair.TestPair.ServerOptions</c>
    ///     injects it via <c>CCVars.SolreignSeasonLedgerDbPath</c> — NOT the shared bin-directory
    ///     fallback path. (An earlier draft of this fixture re-derived the fallback path by hand and
    ///     silently diverged from the live store the moment that per-instance seam landed.)
    /// </summary>
    private string ResolveLedgerDbPath()
    {
        var server = Server;
        return SeasonLedgerDbPath.Resolve(
            server.ResolveDependency<IConfigurationManager>(),
            server.ResolveDependency<IResourceManager>());
    }

    /// <summary>
    ///     Fresh round id per test run — same discipline as SeasonLedgerSystemIntegrationTest: the
    ///     ledger's round-envelope replay protection treats a repeated (round id, account) pair as a
    ///     conflict, so hardcoded round ids break the moment the same db sees a second fold.
    /// </summary>
    private static int UniqueRoundId()
    {
        return Random.Shared.Next(100_000, int.MaxValue - 16);
    }

    private static async Task PollUntilAsync(
        RobustIntegrationTest.ServerIntegrationInstance server,
        Func<Task<bool>> predicate,
        string failureMessage,
        int maxAttempts = 150)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (await predicate())
                return;

            await server.WaitRunTicks(1);
        }

        Assert.Fail(failureMessage);
    }

    private static RoundEndMessageEvent.RoundEndPlayerInfo MakeConnectedPassenger(Guid userId, string oocName)
    {
        return new RoundEndMessageEvent.RoundEndPlayerInfo
        {
            PlayerOOCName = oocName,
            PlayerICName = oocName,
            PlayerGuid = new NetUserId(userId),
            Role = "Passenger",
            JobPrototypes = new[] { "Passenger" },
            AntagPrototypes = Array.Empty<string>(),
            PlayerNetEntity = null,
            Antag = false,
            Observer = false,
            Connected = true,
        };
    }

    /// <summary>
    ///     Fires the real round-end handoff chain: AppendRoundEndText (folds the rule's <c>_standing</c>
    ///     into the ledger's <c>_roundStanding</c>) then RoundEndMessageEvent (async-void OnRoundEnd,
    ///     folds <c>_roundStanding</c> into the persisted <c>standing_total</c> column).
    /// </summary>
    private static void FireRoundEnd(IEntityManager entMan, Guid userId, int roundId)
    {
        var textEv = new RoundEndTextAppendEvent();
        entMan.EventBus.RaiseEvent(EventSource.Local, textEv);

        var ev = new RoundEndMessageEvent(
            gamemodeTitle: "Wave25CorporateIntegration",
            roundEndText: textEv.Text,
            roundDuration: TimeSpan.FromMinutes(10),
            roundId: roundId,
            playerCount: 1,
            allPlayersEndInfo: new[] { MakeConnectedPassenger(userId, "CorporateFixture") },
            restartSound: null);

        entMan.EventBus.RaiseEvent(EventSource.Local, ev);
    }

    [Test]
    public async Task AwardStanding_PositiveDelta_FoldsIntoSeasonLedgerStandingTotal_AfterRoundEnd()
    {
        var server = Server;
        var entMan = server.EntMan;
        var dbPath = ResolveLedgerDbPath();

        await server.WaitPost(() => _ = server.System<SeasonLedgerSystem>());

        var guid = Guid.NewGuid();

        await server.WaitPost(() =>
        {
            var ticker = server.System<GameTicker>();
            Assert.That(ticker.StartGameRule(CorporateRuleId, out _), Is.True,
                "Setup failed: SolreignCorporate must start cleanly (no delay configured on the prototype).");

            var rule = server.System<SolreignCorporateRuleSystem>();
            rule.AwardStanding(new NetUserId(guid), 5, "Fixture Contract Payout");

            FireRoundEnd(entMan, guid, UniqueRoundId());
        });

        await PollUntilAsync(
            server,
            async () =>
            {
                if (!File.Exists(dbPath))
                    return false;

                var store = new SeasonLedgerStore(dbPath);
                var stats = await store.GetStatsAsync(guid);
                return stats.StandingTotal >= 5;
            },
            $"AwardStanding(5) must fold into standing_total for {guid:N} in {dbPath} via the real " +
            $"AppendRoundEndText -> SubmitRoundStandings -> OnRoundEnd chain.");

        var finalStore = new SeasonLedgerStore(dbPath);
        var finalStats = await finalStore.GetStatsAsync(guid);
        Assert.That(finalStats.StandingTotal, Is.EqualTo(5),
            "A single 5-point AwardStanding call, folded once, must land exactly 5 -- not scaled or duplicated.");
    }

    [Test]
    public async Task AwardStanding_NonPositiveDelta_IsIgnored_LeavesStandingTotalAtZero()
    {
        var server = Server;
        var entMan = server.EntMan;
        var dbPath = ResolveLedgerDbPath();

        await server.WaitPost(() => _ = server.System<SeasonLedgerSystem>());

        var guid = Guid.NewGuid();

        await server.WaitPost(() =>
        {
            var ticker = server.System<GameTicker>();
            Assert.That(ticker.StartGameRule(CorporateRuleId, out _), Is.True,
                "Setup failed: SolreignCorporate must start cleanly.");

            var rule = server.System<SolreignCorporateRuleSystem>();
            rule.AwardStanding(new NetUserId(guid), 0, "Fixture Zero Payout");
            rule.AwardStanding(new NetUserId(guid), -3, "Fixture Negative Payout");

            FireRoundEnd(entMan, guid, UniqueRoundId());
        });

        // Bounded, not sleep-and-pray: give the async write every chance the positive-delta test's
        // credit would have taken, then assert nothing landed.
        await PollUntilAsync(
            server,
            async () =>
            {
                if (!File.Exists(dbPath))
                    return false;

                var store = new SeasonLedgerStore(dbPath);
                var stats = await store.GetStatsAsync(guid);
                // A round record for this fresh guid must exist (tours >= 1) before we trust the
                // standing column is genuinely zero rather than "the write hasn't landed yet".
                return stats.Tours >= 1;
            },
            $"Round-end record for {guid:N} never landed in {dbPath}.");

        var finalStore = new SeasonLedgerStore(dbPath);
        var finalStats = await finalStore.GetStatsAsync(guid);
        Assert.That(finalStats.StandingTotal, Is.EqualTo(0),
            "AwardStanding must ignore non-positive deltas (anti-grief rule 9: standing only ever goes up).");
    }

    [Test]
    public async Task KillAttribution_ThroughRealMobStateChangedEvent_CreditsOriginAccount_WhenRuleIsActive()
    {
        var server = Server;
        var entMan = server.EntMan;
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var dbPath = ResolveLedgerDbPath();

        await server.WaitPost(() => _ = server.System<SeasonLedgerSystem>());

        EntityUid killer = default;
        Guid killerGuid = default;

        await server.WaitPost(() =>
        {
            var ticker = server.System<GameTicker>();
            Assert.That(ticker.StartGameRule(CorporateRuleId, out _), Is.True,
                "Setup failed: SolreignCorporate must start cleanly.");

            var session = playerMan.Sessions.First();
            killer = session.AttachedEntity
                     ?? throw new InvalidOperationException(
                         "Connected player has no AttachedEntity -- DummyTicker must be false.");
            killerGuid = session.UserId.UserId;

            var mind = server.System<SharedMindSystem>();
            Assert.That(mind.TryGetMind(killer, out _, out _), Is.True,
                "Setup failed: the connected player's spawned body must already have a real Mind " +
                "(attached during the normal DummyTicker=false spawn flow) for OnMobStateChanged's " +
                "killer-account resolution to have anything to resolve.");

            var victim = entMan.SpawnEntity(null, MapCoordinates.Nullspace);

            // Same call MobStateSystem.ChangeState makes on a real death -- directed + broadcast --
            // matching this fork's "raise the real subscription event directly" idiom
            // (SolreignZoneGateIntegrationTest, ContractClaimFlowIntegrationTest) instead of driving
            // full damage/death mechanics just to reach OnMobStateChanged.
            var ev = new MobStateChangedEvent(victim, null!, MobState.Alive, MobState.Dead, Origin: killer);
            entMan.EventBus.RaiseLocalEvent(victim, ev, true);

            FireRoundEnd(entMan, killerGuid, UniqueRoundId());
        });

        await PollUntilAsync(
            server,
            async () =>
            {
                if (!File.Exists(dbPath))
                    return false;

                var store = new SeasonLedgerStore(dbPath);
                var stats = await store.GetStatsAsync(killerGuid);
                return stats.StandingTotal >= 1;
            },
            $"A kill attributed via a real MobStateChangedEvent (origin={killer}) must credit " +
            $"{killerGuid:N}'s standing_total in {dbPath} through OnMobStateChanged -> " +
            $"AppendRoundEndText -> SubmitRoundStandings -> OnRoundEnd.");

        var finalStore = new SeasonLedgerStore(dbPath);
        var finalStats = await finalStore.GetStatsAsync(killerGuid);
        Assert.That(finalStats.StandingTotal, Is.EqualTo(1),
            "A single attributed kill must credit exactly +1 Corporate Standing.");
    }
}
