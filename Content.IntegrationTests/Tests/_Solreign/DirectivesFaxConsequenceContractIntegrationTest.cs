#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.DirectivesFax;
using Content.Server._Solreign.DirectivesFax.Components;
using Content.Server._Solreign.SeasonLedger;
using Content.Server._Solreign.StationDirective;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.GameTicking;
using Content.Shared.Cargo.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Paper;
using Content.Shared.Station.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     End-to-end consequence contract for Directives Fax. The older live-system fixture proves the
///     individual start, report, and query paths; this fixture proves that they compose into one
///     player-visible and durable outcome under a synthetic duplicate round-end signal.
///
///     The test uses the production five-minute presence gate and real Season Ledger system. It
///     intentionally does not change the feature CVar, default, rule-start policy, or mid-round
///     semantics.
/// </summary>
[TestFixture]
public sealed class DirectivesFaxConsequenceContractIntegrationTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        DummyTicker = false,
    };

    private const string DirectivesFaxRuleId = "SolreignDirectivesFax";

    [Test]
    public async Task SyntheticDuplicateRoundEnd_ProducesOneStampedReportAndOneLogicalLedgerOutcome()
    {
        var server = Server;
        var entMan = server.EntMan;
        var account = ServerSession?.UserId.UserId
                      ?? throw new InvalidOperationException("The consequence contract needs the connected test session.");
        string directiveDisplayName = default!;
        HashSet<EntityUid> baselinePaperUids = default!;

        await server.WaitPost(() =>
        {
            RemoveExistingRules(entMan, server.System<GameTicker>());
            server.System<SharedGodmodeSystem>().EnableGodmode(
                ServerSession?.AttachedEntity
                ?? throw new InvalidOperationException("The connected test session has no attached entity."));
            baselinePaperUids = SnapshotPaperUids(entMan);

            var station = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            entMan.EnsureComponent<StationDataComponent>(station);
            entMan.EnsureComponent<StationBankAccountComponent>(station);
            entMan.EnsureComponent<StationCargoOrderDatabaseComponent>(station);

            Assert.That(server.System<GameTicker>().StartGameRule(DirectivesFaxRuleId, out _), Is.True,
                "Setup failed: the production Directives Fax rule must start.");

            var ruleQuery = entMan.EntityQueryEnumerator<DirectivesFaxRuleComponent>();
            Assert.That(ruleQuery.MoveNext(out _, out var rule), Is.True,
                "Setup failed: the started rule has no Directives Fax component.");
            directiveDisplayName = DirectivesFaxClauseCatalog.GetDisplayName(
                rule!.DirectiveId ?? throw new InvalidOperationException("The started rule selected no directive."));

            Assert.That(CountStartFaxes(entMan, baselinePaperUids, directiveDisplayName), Is.EqualTo(1),
                "A controlled rule start must create exactly one directive fax.");
        });

        // Exercise the real eligibility contract rather than reaching through private tracker state.
        await Pair.RunSeconds((float) TimeSpan.FromMinutes(5).TotalSeconds + 1);

        var initialStreak = await server.System<SeasonLedgerSystem>().GetDirectivesFaxStreakAsync(account);
        var expectedCurrentStreak = initialStreak.CurrentStreak + 1;
        var expectedBestStreak = Math.Max(initialStreak.BestStreak, expectedCurrentStreak);

        RoundEndTextAppendEvent first = default!;
        RoundEndTextAppendEvent duplicate = default!;
        string header = default!;
        string stamp = default!;
        string metLine = default!;
        string unmetLine = default!;

        await server.WaitPost(() =>
        {
            var stationQuery = entMan.EntityQueryEnumerator<
                StationDataComponent,
                StationBankAccountComponent,
                StationCargoOrderDatabaseComponent>();
            Assert.That(stationQuery.MoveNext(out var station, out _, out var bank, out var orders), Is.True,
                "Setup failed: controlled station rig disappeared.");

            // Every current directive clause is satisfied by zero casualties, four orders, and a
            // 4,000-credit cargo delta. This keeps the printed MET outcome truthful regardless of
            // which deterministic directive the current round ID selected.
            Assert.That(server.System<CargoSystem>().TryAdjustBankAccount((station, bank), "Cargo", 4_000), Is.True);
            orders!.NumOrdersCreated += 4;

            header = Loc.GetString("solreign-directives-fax-round-end-header");
            stamp = Loc.GetString("solreign-directives-fax-report-stamp");
            metLine = Loc.GetString(
                "solreign-directives-fax-round-end-met",
                ("directive", directiveDisplayName));
            unmetLine = Loc.GetString(
                "solreign-directives-fax-round-end-unmet",
                ("directive", directiveDisplayName));

            first = new RoundEndTextAppendEvent();
            entMan.EventBus.RaiseEvent(EventSource.Local, first);

            duplicate = new RoundEndTextAppendEvent();
            entMan.EventBus.RaiseEvent(EventSource.Local, duplicate);
        });

        Assert.Multiple(() =>
        {
            Assert.That(first.Text, Does.Contain(header));
            Assert.That(CountOccurrences(first.Text, header), Is.EqualTo(1),
                "The first round-end signal must append exactly one Directives Fax summary block.");
            Assert.That(first.Text, Does.Contain(metLine),
                "The controlled shift satisfies every current directive clause and must report MET.");
            Assert.That(first.Text, Does.Not.Contain(unmetLine));
            Assert.That(first.Text, Does.Not.Contain(stamp),
                "The round-end UI is a summary; the physical report alone carries the stamp.");
            Assert.That(duplicate.Text, Does.Not.Contain(header),
                "A repeated round-end signal must not append a second Directives Fax summary; " +
                "other active rules may still contribute their own blocks to the shared event.");
        });

        await PollUntilAsync(
            server,
            async () =>
            {
                var (current, _) = await server.System<SeasonLedgerSystem>()
                    .GetDirectivesFaxStreakAsync(account);
                return current == expectedCurrentStreak;
            },
            "The eligible connected player never received the one durable Directives Fax outcome.");

        await server.WaitRunTicks(10);

        var finalStreak = await server.System<SeasonLedgerSystem>().GetDirectivesFaxStreakAsync(account);
        await server.WaitAssertion(() =>
        {
            var report = FindStampedReport(entMan, baselinePaperUids);
            var reportMeta = entMan.GetComponent<MetaDataComponent>(report.Uid);
            Assert.Multiple(() =>
            {
                Assert.That(CountStartFaxes(entMan, baselinePaperUids, directiveDisplayName), Is.EqualTo(1),
                    "The composed consequence must retain exactly one unstamped start fax.");
                Assert.That(CountStampedReports(entMan, baselinePaperUids), Is.EqualTo(1),
                    "A repeated round-end signal must not print a second stamped report.");
                Assert.That(report.Paper.Content,
                    Does.Contain(directiveDisplayName),
                    "The sole stamped report must name the same directive as the start fax and UI summary.");
                Assert.That(report.Paper.Content, Does.Contain(metLine),
                    "The stamped report must carry the same truthful MET outcome as the UI summary.");
                Assert.That(reportMeta.EntityPrototype?.ID, Is.EqualTo("SolreignPaperDirectivesFaxReport"),
                    "The stamped artifact must use the dedicated Directives Fax report prototype.");
                Assert.That(finalStreak.CurrentStreak, Is.EqualTo(expectedCurrentStreak),
                    "A repeated round-end signal must not fold the same logical outcome twice.");
                Assert.That(finalStreak.BestStreak, Is.EqualTo(expectedBestStreak));
            });
        });
    }

    private static void RemoveExistingRules(IEntityManager entMan, GameTicker ticker)
    {
        var rulesToDelete = new List<EntityUid>();
        var rules = entMan.EntityQueryEnumerator<DirectivesFaxRuleComponent>();
        while (rules.MoveNext(out var uid, out _))
            rulesToDelete.Add(uid);

        foreach (var uid in rulesToDelete)
        {
            ticker.EndGameRule(uid);
            entMan.DeleteEntity(uid);
        }
    }

    private static HashSet<EntityUid> SnapshotPaperUids(IEntityManager entMan)
    {
        var snapshot = new HashSet<EntityUid>();
        var papers = entMan.EntityQueryEnumerator<PaperComponent>();
        while (papers.MoveNext(out var uid, out _))
            snapshot.Add(uid);

        return snapshot;
    }

    private static int CountStartFaxes(
        IEntityManager entMan,
        IReadOnlySet<EntityUid> baseline,
        string directiveDisplayName)
    {
        var stamp = Loc.GetString("solreign-directives-fax-report-stamp");
        var count = 0;
        var papers = entMan.EntityQueryEnumerator<PaperComponent>();
        while (papers.MoveNext(out var uid, out var paper))
        {
            if (!baseline.Contains(uid) &&
                paper.Content.Contains(directiveDisplayName, StringComparison.Ordinal) &&
                !paper.Content.Contains(stamp, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountStampedReports(IEntityManager entMan, IReadOnlySet<EntityUid> baseline)
    {
        var stamp = Loc.GetString("solreign-directives-fax-report-stamp");
        var count = 0;
        var papers = entMan.EntityQueryEnumerator<PaperComponent>();
        while (papers.MoveNext(out var uid, out var paper))
        {
            if (!baseline.Contains(uid) && paper.Content.Contains(stamp, StringComparison.Ordinal))
                count++;
        }

        return count;
    }

    private static (EntityUid Uid, PaperComponent Paper) FindStampedReport(
        IEntityManager entMan,
        IReadOnlySet<EntityUid> baseline)
    {
        var stamp = Loc.GetString("solreign-directives-fax-report-stamp");
        var papers = entMan.EntityQueryEnumerator<PaperComponent>();
        while (papers.MoveNext(out var uid, out var paper))
        {
            if (!baseline.Contains(uid) && paper.Content.Contains(stamp, StringComparison.Ordinal))
                return (uid, paper);
        }

        throw new AssertionException("No new stamped Directives Fax report was found.");
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var start = 0;
        while ((start = text.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += value.Length;
        }

        return count;
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
}
