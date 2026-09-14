#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.Corporate.Projects;
using Content.Server._Solreign.SeasonLedger;
using Content.Shared._Solreign.Corporate.Projects;
using NUnit.Framework;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     Integration tests for SR-W-030 (Player-run Corporate Projects).
///     Verifies bounded contributions, persistence across round shifts, anti-monopoly account caps, and corporate unlock milestones.
/// </summary>
[TestFixture]
public sealed class SolreignCorporateProjectIntegrationTest : GameTest
{
    private static readonly ProtoId<SolreignCorporateProjectPrototype> NaniteRefineryProject =
        "ProjectNaniteRefinery";

    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        DummyTicker = false,
    };

    // Each test gets its OWN ledger file. These tests exercise SeasonLedgerStore directly
    // and only need the server for prototype indexing — resolving the pooled pair's real
    // ledger path made every contribution accumulate across tests, so absolute assertions
    // failed by exactly the amounts earlier tests had contributed (50 became 100, 250
    // became 650) while each test passed in isolation. Persistence-across-reopen semantics
    // are unchanged: reopening the same temp file is the same act as reopening the real one.
    private string ResolveLedgerDbPath()
    {
        var path = Path.Combine(
            Path.GetTempPath(), $"solreign-corp-project-test-{Guid.NewGuid():N}.db");
        _tempDbPaths.Add(path);
        return path;
    }

    private readonly List<string> _tempDbPaths = new();

    [TearDown]
    public void CleanupTempLedgers()
    {
        foreach (var path in _tempDbPaths)
        {
            try
            {
                File.Delete(path);
                File.Delete(path + "-wal");
                File.Delete(path + "-shm");
            }
            catch (IOException)
            {
                // Best-effort; temp dir cleanup will get it eventually.
            }
        }
        _tempDbPaths.Clear();
    }

    [Test]
    public async Task TryContribute_BoundedContribution_ClampsSingleActionToMaxPerAction()
    {
        var server = Server;
        var dbPath = ResolveLedgerDbPath();

        await server.WaitPost(() => _ = server.System<SeasonLedgerSystem>());

        var protoManager = server.ResolveDependency<IPrototypeManager>();
        Assert.That(protoManager.TryIndex(NaniteRefineryProject, out var proto), Is.True,
            "Setup failed: ProjectNaniteRefinery prototype must exist.");

        var store = new SeasonLedgerStore(dbPath);
        var user = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow.ToString("o");

        // Attempt single contribution of 200 when MaxContributionPerAction is 50
        var result = await store.TryContributeToProjectAsync("ProjectNaniteRefinery", user, 200, nowUtc, proto!);

        Assert.That(result.Kind, Is.EqualTo(CorporateProjectContributionResultKind.Success));
        Assert.That(result.AmountGranted, Is.EqualTo(50), "Single contribution action must be bounded to MaxContributionPerAction (50).");
        Assert.That(result.NewTotalContribution, Is.EqualTo(50));
        Assert.That(result.NewAccountContribution, Is.EqualTo(50));
    }

    [Test]
    public async Task CorporateProjects_PersistAcrossRoundChanges()
    {
        var server = Server;
        var dbPath = ResolveLedgerDbPath();

        await server.WaitPost(() => _ = server.System<SeasonLedgerSystem>());

        var protoManager = server.ResolveDependency<IPrototypeManager>();
        Assert.That(protoManager.TryIndex(NaniteRefineryProject, out var proto), Is.True);

        var store = new SeasonLedgerStore(dbPath);
        var user = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow.ToString("o");

        // Round 1 contribution
        await store.TryContributeToProjectAsync("ProjectNaniteRefinery", user, 50, nowUtc, proto!);

        // Simulate new round / fresh store initialization against the same DB file
        var reloadedStore = new SeasonLedgerStore(dbPath);
        var record = await reloadedStore.GetCorporateProjectRecordAsync("ProjectNaniteRefinery");
        var accountContrib = await reloadedStore.GetAccountContributionAsync("ProjectNaniteRefinery", user);

        Assert.That(record, Is.Not.Null, "Corporate project state must survive round changes.");
        Assert.That(record!.TotalContribution, Is.EqualTo(50), "Project total contribution must persist across round boundary.");
        Assert.That(accountContrib, Is.EqualTo(50), "Account contribution record must persist across round boundary.");
    }

    [Test]
    public async Task TryContribute_AntiMonopolyCap_PreventsSingleAccountFromMonopolizingProject()
    {
        var server = Server;
        var dbPath = ResolveLedgerDbPath();

        await server.WaitPost(() => _ = server.System<SeasonLedgerSystem>());

        var protoManager = server.ResolveDependency<IPrototypeManager>();
        Assert.That(protoManager.TryIndex(NaniteRefineryProject, out var proto), Is.True);

        // ProjectNaniteRefinery target = 1000, maxAccountShareFraction = 0.35 -> maxAccountCap = 350
        int maxCap = proto!.GetMaxAccountContribution();
        Assert.That(maxCap, Is.EqualTo(350), "35% of 1000 target must equal 350 cap.");

        var store = new SeasonLedgerStore(dbPath);
        var user1 = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow.ToString("o");

        // Player 1 contributes 7 times 50 = 350 (reaches account cap)
        for (int i = 0; i < 7; i++)
        {
            var res = await store.TryContributeToProjectAsync("ProjectNaniteRefinery", user1, 50, nowUtc, proto);
            Assert.That(res.Kind, Is.EqualTo(CorporateProjectContributionResultKind.Success));
        }

        var check1 = await store.GetAccountContributionAsync("ProjectNaniteRefinery", user1);
        Assert.That(check1, Is.EqualTo(350), "Player 1 should be at max account cap of 350.");

        // Player 1 attempts an 8th contribution -> should be rejected with AccountCapReached
        var cappedResult = await store.TryContributeToProjectAsync("ProjectNaniteRefinery", user1, 50, nowUtc, proto);
        Assert.That(cappedResult.Kind, Is.EqualTo(CorporateProjectContributionResultKind.AccountCapReached),
            "Anti-monopoly rule must reject further contributions from an account at its cap.");
        Assert.That(cappedResult.AmountGranted, Is.EqualTo(0));

        // Player 2 can now contribute up to their own 350 cap
        var user2 = Guid.NewGuid();
        var user2Result = await store.TryContributeToProjectAsync("ProjectNaniteRefinery", user2, 50, nowUtc, proto);
        Assert.That(user2Result.Kind, Is.EqualTo(CorporateProjectContributionResultKind.Success));
        Assert.That(user2Result.AmountGranted, Is.EqualTo(50));
    }

    [Test]
    public async Task TryContribute_CrossingMilestoneThreshold_TriggersMilestoneUnlock()
    {
        var server = Server;
        var dbPath = ResolveLedgerDbPath();

        await server.WaitPost(() => _ = server.System<SeasonLedgerSystem>());

        var protoManager = server.ResolveDependency<IPrototypeManager>();
        Assert.That(protoManager.TryIndex(NaniteRefineryProject, out var proto), Is.True);

        var store = new SeasonLedgerStore(dbPath);
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow.ToString("o");

        // Milestone 1 threshold is 25% of 1000 = 250 points.
        // Player 1 contributes 200 points (4 actions of 50)
        for (int i = 0; i < 4; i++)
        {
            await store.TryContributeToProjectAsync("ProjectNaniteRefinery", user1, 50, nowUtc, proto!);
        }

        // Player 2 contributes 50 points (pushes total to 250 -> 25% milestone crossed!)
        var milestoneResult = await store.TryContributeToProjectAsync("ProjectNaniteRefinery", user2, 50, nowUtc, proto!);

        Assert.That(milestoneResult.Kind, Is.EqualTo(CorporateProjectContributionResultKind.Success));
        Assert.That(milestoneResult.NewTotalContribution, Is.EqualTo(250));
        Assert.That(milestoneResult.NewlyUnlockedMilestones, Contains.Item("nanite_phase_1"),
            "Crossing 250 points (25% threshold) must trigger unlock of nanite_phase_1 milestone.");

        var record = await store.GetCorporateProjectRecordAsync("ProjectNaniteRefinery");
        Assert.That(record!.UnlockedMilestones, Contains.Item("nanite_phase_1"));
    }
}
