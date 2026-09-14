#nullable enable
using System;
using System.Collections.Generic;
using Content.Server._Solreign.PlayerDelight.Mark;
using Content.Server._Solreign.Records;
using Content.Server._Solreign.SeasonLedger;
using Content.Server._Solreign.Social;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Pure planning logic for the Personnel Records Terminal (wave-2 item einstein-016). Every
///     branch is about honesty: a field the ledger has no data for must plan an honest absence, never
///     a fabricated number or a guessed value over corrupt data — the record-rendering permutations
///     the wave-2 brief calls for (fresh account / decorated veteran / dead-once / marked).
/// </summary>
[TestFixture]
[TestOf(typeof(RecordsTerminalRenderer))]
public sealed class RecordsTerminalRendererTests
{
    private static readonly DateTime Now = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    private static RecordsTerminalLedgerSnapshot FreshData(
        IReadOnlyList<string>? socialFirsts = null,
        FirstDeathRecord? firstDeath = null,
        MarkRecord? mark = null,
        string title = TitleRules.DefaultTitle,
        int tours = 0,
        int rankIndex = 0)
    {
        return new RecordsTerminalLedgerSnapshot(
            title,
            tours,
            rankIndex,
            socialFirsts ?? Array.Empty<string>(),
            firstDeath,
            mark);
    }

    private static MarkRecord MarkRow(string kindLedger, string plantedUtc) => new(
        Guid.NewGuid(),
        kindLedger,
        PlantedRoundId: 1,
        PlantedUtc: plantedUtc,
        PlantedMap: "test-map",
        CharacterName: "Test Asset",
        ToursAtPlanting: 1,
        SlotIndex: 0,
        NudgeShown: false,
        LastVisitUtc: plantedUtc,
        LastVisitStage: 0);

    private static FirstDeathRecord DeathRow() => new(
        Guid.NewGuid(),
        RoundId: 1,
        CharacterName: "Test Asset",
        Cause: "VIOLENCE",
        ToursAtDeath: 1,
        TitleAtDeath: TitleRules.DefaultTitle,
        EpitaphId: "epitaph-1",
        DiedAtUtc: Now.ToString("o"),
        RehireShown: false,
        CryptReported: false);

    // --- Fresh account -----------------------------------------------------------------------

    [Test]
    public void FreshAccount_ProducesAllHonestEmptyStates()
    {
        var plan = RecordsTerminalRenderer.BuildPlan(FreshData(), Now);

        Assert.That(plan.DisplayTitle, Is.EqualTo(TitleRules.DefaultTitle));
        Assert.That(plan.Tours, Is.EqualTo(0));
        Assert.That(plan.RankIndex, Is.EqualTo(0));
        Assert.That(plan.CelebratedSocialFirstFlags, Is.Empty);
        Assert.That(plan.HasFirstDeath, Is.False);
        Assert.That(plan.HasMark, Is.False);
    }

    // --- Decorated veteran ---------------------------------------------------------------------

    [Test]
    public void DecoratedVeteran_EchoesTitleToursAndRank()
    {
        var data = FreshData(title: "Board's Favorite", tours: 12, rankIndex: 7);
        var plan = RecordsTerminalRenderer.BuildPlan(data, Now);

        Assert.That(plan.DisplayTitle, Is.EqualTo("Board's Favorite"));
        Assert.That(plan.Tours, Is.EqualTo(12));
        Assert.That(plan.RankIndex, Is.EqualTo(7));
    }

    [Test]
    public void AllThreeCelebratedFlags_AllAppearInFixedOrder()
    {
        // Deliberately supplied out of order — the plan must always re-sort to CelebratedFlagOrder.
        var data = FreshData(socialFirsts: new[]
        {
            SolreignSocialFirstFlags.ItemReceived,
            SolreignSocialFirstFlags.ChirpAnswered,
            SolreignSocialFirstFlags.HealedByAnother,
        });

        var plan = RecordsTerminalRenderer.BuildPlan(data, Now);

        Assert.That(plan.CelebratedSocialFirstFlags, Is.EqualTo(new[]
        {
            SolreignSocialFirstFlags.ChirpAnswered,
            SolreignSocialFirstFlags.HealedByAnother,
            SolreignSocialFirstFlags.ItemReceived,
        }));
    }

    [Test]
    public void WingmatePromptFlag_IsExcluded()
    {
        // WingmatePrompt is an internal once-ever nudge marker, not a celebrated milestone (no
        // reason/chat copy pair exists for it in SolreignSocialFirstsSystem's Copy table).
        var data = FreshData(socialFirsts: new[] { SolreignSocialFirstFlags.WingmatePrompt });
        var plan = RecordsTerminalRenderer.BuildPlan(data, Now);

        Assert.That(plan.CelebratedSocialFirstFlags, Is.Empty);
    }

    [Test]
    public void UnknownFlagId_IsIgnoredNotThrown()
    {
        var data = FreshData(socialFirsts: new[] { "some_future_flag_not_yet_in_the_closed_set" });
        Assert.DoesNotThrow(() => RecordsTerminalRenderer.BuildPlan(data, Now));

        var plan = RecordsTerminalRenderer.BuildPlan(data, Now);
        Assert.That(plan.CelebratedSocialFirstFlags, Is.Empty);
    }

    [Test]
    public void PartialSocialFirsts_OnlyClaimedOnesAppear()
    {
        var data = FreshData(socialFirsts: new[] { SolreignSocialFirstFlags.HealedByAnother });
        var plan = RecordsTerminalRenderer.BuildPlan(data, Now);

        Assert.That(plan.CelebratedSocialFirstFlags, Is.EqualTo(new[] { SolreignSocialFirstFlags.HealedByAnother }));
    }

    // --- Dead-once -------------------------------------------------------------------------------

    [Test]
    public void FirstDeathClaimed_HasFirstDeathTrue()
    {
        var data = FreshData(firstDeath: DeathRow());
        var plan = RecordsTerminalRenderer.BuildPlan(data, Now);

        Assert.That(plan.HasFirstDeath, Is.True);
    }

    [Test]
    public void NoFirstDeathClaim_HasFirstDeathFalse()
    {
        var plan = RecordsTerminalRenderer.BuildPlan(FreshData(), Now);
        Assert.That(plan.HasFirstDeath, Is.False);
    }

    // --- Marked ----------------------------------------------------------------------------------

    [Test]
    public void FreshlyPlantedMark_Stage0()
    {
        var plantedUtc = Now.ToString("o");
        var data = FreshData(mark: MarkRow(MarkKinds.SaplingLedger, plantedUtc));
        var plan = RecordsTerminalRenderer.BuildPlan(data, Now);

        Assert.That(plan.HasMark, Is.True);
        Assert.That(plan.MarkKind, Is.EqualTo(MarkKind.Sapling));
        Assert.That(plan.MarkStage, Is.EqualTo(0));
    }

    [Test]
    public void MarkPlantedTenDaysAgo_Stage2()
    {
        var plantedUtc = Now.AddDays(-10).ToString("o");
        var data = FreshData(mark: MarkRow(MarkKinds.LampLedger, plantedUtc));
        var plan = RecordsTerminalRenderer.BuildPlan(data, Now);

        Assert.That(plan.HasMark, Is.True);
        Assert.That(plan.MarkKind, Is.EqualTo(MarkKind.Lamp));
        Assert.That(plan.MarkStage, Is.EqualTo(2));
    }

    [Test]
    public void MarkPlantedThirtyDaysAgo_TerminalStage3()
    {
        var plantedUtc = Now.AddDays(-30).ToString("o");
        var data = FreshData(mark: MarkRow(MarkKinds.NamePlateLedger, plantedUtc));
        var plan = RecordsTerminalRenderer.BuildPlan(data, Now);

        Assert.That(plan.HasMark, Is.True);
        Assert.That(plan.MarkKind, Is.EqualTo(MarkKind.NamePlate));
        Assert.That(plan.MarkStage, Is.EqualTo(3));
    }

    [Test]
    public void NoMarkRow_HasMarkFalse()
    {
        var plan = RecordsTerminalRenderer.BuildPlan(FreshData(), Now);
        Assert.That(plan.HasMark, Is.False);
    }

    [Test]
    public void CorruptMarkKind_FallsBackToHonestNoMark()
    {
        // Never guess a kind over garbage data — an unparseable row must plan as "no mark", not a
        // silently-wrong Sapling default.
        var data = FreshData(mark: MarkRow("NOT_A_REAL_KIND", Now.ToString("o")));
        var plan = RecordsTerminalRenderer.BuildPlan(data, Now);

        Assert.That(plan.HasMark, Is.False);
    }

    [Test]
    public void CorruptMarkTimestamp_FallsBackToHonestNoMark()
    {
        var data = FreshData(mark: MarkRow(MarkKinds.SaplingLedger, "not-a-timestamp"));
        var plan = RecordsTerminalRenderer.BuildPlan(data, Now);

        Assert.That(plan.HasMark, Is.False);
    }

    // --- Combined: a fully decorated account rendering every section at once ---------------------

    [Test]
    public void FullyDecoratedAccount_AllSectionsPresentSimultaneously()
    {
        var data = FreshData(
            title: "Chain Closer",
            tours: 20,
            rankIndex: 5,
            socialFirsts: new[]
            {
                SolreignSocialFirstFlags.ChirpAnswered,
                SolreignSocialFirstFlags.HealedByAnother,
                SolreignSocialFirstFlags.ItemReceived,
                SolreignSocialFirstFlags.WingmatePrompt,
            },
            firstDeath: DeathRow(),
            mark: MarkRow(MarkKinds.LampLedger, Now.AddDays(-3).ToString("o")));

        var plan = RecordsTerminalRenderer.BuildPlan(data, Now);

        Assert.That(plan.DisplayTitle, Is.EqualTo("Chain Closer"));
        Assert.That(plan.Tours, Is.EqualTo(20));
        Assert.That(plan.RankIndex, Is.EqualTo(5));
        Assert.That(plan.CelebratedSocialFirstFlags.Count, Is.EqualTo(3));
        Assert.That(plan.HasFirstDeath, Is.True);
        Assert.That(plan.HasMark, Is.True);
        Assert.That(plan.MarkKind, Is.EqualTo(MarkKind.Lamp));
        Assert.That(plan.MarkStage, Is.EqualTo(1));
    }
}
