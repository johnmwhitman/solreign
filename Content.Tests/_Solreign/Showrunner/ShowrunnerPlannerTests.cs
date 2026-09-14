#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._Solreign.Showrunner;
using NUnit.Framework;

namespace Content.Tests._Solreign.Showrunner;

[TestFixture]
[TestOf(typeof(ShowrunnerPlanner))]
public sealed class ShowrunnerPlannerTests
{
    private const string DigestA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string DigestB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Test]
    public void BuildPlan_WithOneEligibleBeatPerRole_ReturnsCompletePlan()
    {
        var result = ShowrunnerPlanner.BuildPlan(Context(), CompleteCatalog());

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Reasons, Is.Empty);
            Assert.That(result.Plan, Is.Not.Null);
            Assert.That(result.Plan!.Opening.Role, Is.EqualTo(ShowrunnerBeatRole.Opening));
            Assert.That(result.Plan.Escalation.Role, Is.EqualTo(ShowrunnerBeatRole.Escalation));
            Assert.That(result.Plan.Finale.Role, Is.EqualTo(ShowrunnerBeatRole.Finale));
            Assert.That(result.Plan.PlanFingerprint, Does.Match("^[0-9a-f]{64}$"));
        });
    }

    [Test]
    public void BuildPlan_SameNormalizedInputs_ReturnsSamePlanAndFingerprint()
    {
        var first = ShowrunnerPlanner.BuildPlan(Context(seed: 123), CompleteCatalog());
        var second = ShowrunnerPlanner.BuildPlan(Context(seed: 123), CompleteCatalog());

        Assert.Multiple(() =>
        {
            Assert.That(SelectedBeatIds(second), Is.EqualTo(SelectedBeatIds(first)));
            Assert.That(second.Plan!.PlanFingerprint, Is.EqualTo(first.Plan!.PlanFingerprint));
        });
    }

    [Test]
    public void BuildPlan_V0FingerprintMatchesIndependentCanonicalVector()
    {
        var result = ShowrunnerPlanner.BuildPlan(Context(), CompleteCatalog());

        // Independently derived from the documented v0 length-prefixed canonical form.
        Assert.That(
            result.Plan!.PlanFingerprint,
            Is.EqualTo("2c298f7dcc14ddb7d3d436551df2e15b3b5992f2d79c8756a1d277407cfbd9a3"));
    }

    [Test]
    public void BuildPlan_InputOrderDoesNotAffectSelection()
    {
        var catalog = CompleteCatalog(withAlternates: true);
        var reversed = catalog.Reverse().ToArray();

        var first = ShowrunnerPlanner.BuildPlan(Context(seed: 987), catalog);
        var second = ShowrunnerPlanner.BuildPlan(Context(seed: 987), reversed);

        Assert.Multiple(() =>
        {
            Assert.That(SelectedBeatIds(second), Is.EqualTo(SelectedBeatIds(first)));
            Assert.That(second.Plan!.PlanFingerprint, Is.EqualTo(first.Plan!.PlanFingerprint));
        });
    }

    [Test]
    public void BuildPlan_SnapshotsMutableDescriptorCollections()
    {
        var allowedMaps = new List<string> { "Amber" };
        var catalog = CompleteCatalog();
        catalog[0] = catalog[0] with { AllowedMaps = allowedMaps };

        var result = ShowrunnerPlanner.BuildPlan(Context(), catalog);
        allowedMaps.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Plan!.Opening.AllowedMaps, Is.EqualTo(new[] { "Amber" }));
            Assert.That(result.Plan.PlanFingerprint, Does.Match("^[0-9a-f]{64}$"));
        });
    }

    [Test]
    public void BuildPlan_SetOrderAndDuplicatesDoNotChangePlanFingerprint()
    {
        var first = ShowrunnerPlanner.BuildPlan(
            Context(
                coolingDownBeatIds: new[] { "unused-b", "unused-a" },
                activeRuleIds: new[] { "UnusedRuleB", "UnusedRuleA" }),
            CompleteCatalog());
        var second = ShowrunnerPlanner.BuildPlan(
            Context(
                coolingDownBeatIds: new[] { "unused-a", "unused-b", "unused-a" },
                activeRuleIds: new[] { "UnusedRuleA", "UnusedRuleB", "UnusedRuleA" }),
            CompleteCatalog());

        Assert.Multiple(() =>
        {
            Assert.That(SelectedBeatIds(second), Is.EqualTo(SelectedBeatIds(first)));
            Assert.That(second.Plan!.PlanFingerprint, Is.EqualTo(first.Plan!.PlanFingerprint));
        });
    }

    [Test]
    public void BuildPlan_EmergencyStopDominatesMalformedCatalog()
    {
        var malformed = new[]
        {
            Beat("", "", ShowrunnerBeatRole.Opening, capabilityDigest: "not-a-digest"),
        };

        var result = ShowrunnerPlanner.BuildPlan(Context(emergencyStop: true), malformed);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Plan, Is.Null);
            Assert.That(result.Reasons, Is.EqualTo(new[] { ShowrunnerReasonCode.EmergencyStop }));
        });
    }

    [TestCase(ShowrunnerCapabilityState.Unknown)]
    [TestCase(ShowrunnerCapabilityState.PreviewOnly)]
    [TestCase(ShowrunnerCapabilityState.Ineligible)]
    public void BuildPlan_NonEligibleCapability_FailsClosed(ShowrunnerCapabilityState state)
    {
        var catalog = CompleteCatalog();
        catalog[0] = Beat("opening-a", "RuleOpeningA", ShowrunnerBeatRole.Opening, capabilityState: state);

        var result = ShowrunnerPlanner.BuildPlan(Context(), catalog);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reasons, Does.Contain(ShowrunnerReasonCode.CapabilityNotEligible));
            Assert.That(result.Reasons, Does.Contain(ShowrunnerReasonCode.MissingRoleCandidate));
            Assert.That(result.Reasons, Does.Contain(ShowrunnerReasonCode.NoCompletePlan));
        });
    }

    [TestCase("")]
    [TestCase("ABCDEF")]
    [TestCase("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaag")]
    public void BuildPlan_EligibleBeatWithoutValidEvidenceDigest_RejectsCatalog(string digest)
    {
        var catalog = CompleteCatalog();
        catalog[0] = Beat("opening-a", "RuleOpeningA", ShowrunnerBeatRole.Opening, capabilityDigest: digest);

        var result = ShowrunnerPlanner.BuildPlan(Context(), catalog);

        Assert.That(result.Reasons, Is.EqualTo(new[] { ShowrunnerReasonCode.InvalidDescriptor }));
    }

    [Test]
    public void BuildPlan_DuplicateBeatId_RejectsEntireCatalog()
    {
        var catalog = CompleteCatalog();
        catalog[1] = catalog[1] with { BeatId = catalog[0].BeatId };

        var result = ShowrunnerPlanner.BuildPlan(Context(), catalog);

        Assert.That(result.Reasons, Is.EqualTo(new[] { ShowrunnerReasonCode.DuplicateBeatId }));
    }

    [Test]
    public void BuildPlan_DuplicateRuleId_RejectsEntireCatalog()
    {
        var catalog = CompleteCatalog();
        catalog[1] = catalog[1] with { RuleId = catalog[0].RuleId };

        var result = ShowrunnerPlanner.BuildPlan(Context(), catalog);

        Assert.That(result.Reasons, Is.EqualTo(new[] { ShowrunnerReasonCode.DuplicateRuleId }));
    }

    [Test]
    public void BuildPlan_CooldownExcludesBeat()
    {
        var result = ShowrunnerPlanner.BuildPlan(
            Context(coolingDownBeatIds: new[] { "opening-a" }),
            CompleteCatalog());

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reasons, Does.Contain(ShowrunnerReasonCode.CooldownActive));
            Assert.That(result.Reasons, Does.Contain(ShowrunnerReasonCode.NoCompletePlan));
        });
    }

    [Test]
    public void BuildPlan_ActiveRuleExcludesBeat()
    {
        var result = ShowrunnerPlanner.BuildPlan(
            Context(activeRuleIds: new[] { "RuleOpeningA" }),
            CompleteCatalog());

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reasons, Does.Contain(ShowrunnerReasonCode.ActiveRuleConflict));
            Assert.That(result.Reasons, Does.Contain(ShowrunnerReasonCode.NoCompletePlan));
        });
    }

    [Test]
    public void BuildPlan_MapAndPopulationFiltersFailClosed()
    {
        var catalog = CompleteCatalog();
        catalog[0] = catalog[0] with { AllowedMaps = new[] { "OtherMap" } };
        catalog[1] = catalog[1] with { MinPlayers = 9 };

        var result = ShowrunnerPlanner.BuildPlan(Context(population: 8, mapId: "Amber"), catalog);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reasons, Does.Contain(ShowrunnerReasonCode.MapUnsupported));
            Assert.That(result.Reasons, Does.Contain(ShowrunnerReasonCode.PopulationOutOfRange));
            Assert.That(result.Reasons, Does.Contain(ShowrunnerReasonCode.NoCompletePlan));
        });
    }

    [Test]
    public void BuildPlan_ChoosesCombinationWithAtMostOneHeadliner()
    {
        var catalog = CompleteCatalog(withAlternates: true);
        catalog[0] = catalog[0] with { IsHeadliner = true };
        catalog[1] = catalog[1] with { IsHeadliner = true };

        var result = ShowrunnerPlanner.BuildPlan(Context(seed: 55), catalog);

        var selected = new[] { result.Plan!.Opening, result.Plan.Escalation, result.Plan.Finale };
        Assert.That(selected.Count(beat => beat.IsHeadliner), Is.LessThanOrEqualTo(1));
    }

    [Test]
    public void BuildPlan_WhenEveryCombinationHasTwoHeadliners_FailsClosed()
    {
        var catalog = CompleteCatalog();
        catalog[0] = catalog[0] with { IsHeadliner = true };
        catalog[1] = catalog[1] with { IsHeadliner = true };

        var result = ShowrunnerPlanner.BuildPlan(Context(), catalog);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reasons, Does.Contain(ShowrunnerReasonCode.HeadlinerLimit));
            Assert.That(result.Reasons, Does.Contain(ShowrunnerReasonCode.NoCompletePlan));
        });
    }

    [Test]
    public void BuildPlan_InvalidContextFailsBeforePlanning()
    {
        var result = ShowrunnerPlanner.BuildPlan(Context(population: -1, mapId: ""), CompleteCatalog());

        Assert.That(result.Reasons, Is.EqualTo(new[] { ShowrunnerReasonCode.InvalidContext }));
    }

    [Test]
    public void BuildPlan_DescriptorLimitIsHardGate()
    {
        var catalog = Enumerable.Range(0, ShowrunnerPlanner.MaximumDescriptors + 1)
            .Select(index => Beat($"opening-{index}", $"RuleOpening{index}", ShowrunnerBeatRole.Opening))
            .ToArray();

        var result = ShowrunnerPlanner.BuildPlan(Context(), catalog);

        Assert.That(result.Reasons, Is.EqualTo(new[] { ShowrunnerReasonCode.TooManyDescriptors }));
    }

    [Test]
    public void BuildPlan_StopsEnumeratingAtDescriptorLimit()
    {
        var result = ShowrunnerPlanner.BuildPlan(Context(), DescriptorStreamThatMustBeBounded());

        Assert.That(result.Reasons, Is.EqualTo(new[] { ShowrunnerReasonCode.TooManyDescriptors }));
    }

    [Test]
    public void BuildPlan_NonEligibleNullDigestFailsClosedInsteadOfThrowing()
    {
        var catalog = CompleteCatalog().ToList();
        catalog.Add(Beat(
                "preview-only",
                "RulePreviewOnly",
                ShowrunnerBeatRole.Opening,
                ShowrunnerCapabilityState.PreviewOnly)
            with
            {
                CapabilityDigest = null!,
            });

        var result = ShowrunnerPlanner.BuildPlan(Context(), catalog);

        Assert.That(result.Reasons, Is.EqualTo(new[] { ShowrunnerReasonCode.InvalidDescriptor }));
    }

    [Test]
    public void BuildPlan_ReasonsAreSortedAndUnique()
    {
        var catalog = CompleteCatalog();
        catalog[0] = catalog[0] with { AllowedMaps = new[] { "OtherMap" } };
        catalog[1] = catalog[1] with { MinPlayers = 50 };
        catalog[2] = catalog[2] with { CapabilityState = ShowrunnerCapabilityState.Unknown };

        var result = ShowrunnerPlanner.BuildPlan(Context(population: 8), catalog);

        Assert.Multiple(() =>
        {
            Assert.That(result.Reasons, Is.Ordered);
            Assert.That(result.Reasons.Distinct().Count(), Is.EqualTo(result.Reasons.Count));
        });
    }

    [Test]
    public void BuildPlan_RefusalReasonsAreReadOnlySnapshots()
    {
        var result = ShowrunnerPlanner.BuildPlan(Context(emergencyStop: true), CompleteCatalog());
        var mutableView = (IList<ShowrunnerReasonCode>) result.Reasons;

        Assert.That(
            () => mutableView[0] = ShowrunnerReasonCode.InvalidContext,
            Throws.InstanceOf<NotSupportedException>());
        Assert.That(result.Reasons, Is.EqualTo(new[] { ShowrunnerReasonCode.EmergencyStop }));
    }

    private static string[] SelectedBeatIds(ShowrunnerPlanningResult result)
    {
        return
        [
            result.Plan!.Opening.BeatId,
            result.Plan.Escalation.BeatId,
            result.Plan.Finale.BeatId,
        ];
    }

    private static ShowrunnerPlanningContext Context(
        ulong seed = 1,
        int population = 8,
        string mapId = "Amber",
        bool emergencyStop = false,
        IReadOnlyCollection<string>? coolingDownBeatIds = null,
        IReadOnlyCollection<string>? activeRuleIds = null)
    {
        return new ShowrunnerPlanningContext(
            seed,
            population,
            mapId,
            emergencyStop,
            coolingDownBeatIds ?? Array.Empty<string>(),
            activeRuleIds ?? Array.Empty<string>());
    }

    private static ShowrunnerBeatDescriptor[] CompleteCatalog(bool withAlternates = false)
    {
        var beats = new List<ShowrunnerBeatDescriptor>
        {
            Beat("opening-a", "RuleOpeningA", ShowrunnerBeatRole.Opening),
            Beat("escalation-a", "RuleEscalationA", ShowrunnerBeatRole.Escalation),
            Beat("finale-a", "RuleFinaleA", ShowrunnerBeatRole.Finale),
        };

        if (withAlternates)
        {
            beats.Add(Beat("opening-b", "RuleOpeningB", ShowrunnerBeatRole.Opening, capabilityDigest: DigestB));
            beats.Add(Beat("escalation-b", "RuleEscalationB", ShowrunnerBeatRole.Escalation, capabilityDigest: DigestB));
            beats.Add(Beat("finale-b", "RuleFinaleB", ShowrunnerBeatRole.Finale, capabilityDigest: DigestB));
        }

        return beats.ToArray();
    }

    private static ShowrunnerBeatDescriptor Beat(
        string beatId,
        string ruleId,
        ShowrunnerBeatRole role,
        ShowrunnerCapabilityState capabilityState = ShowrunnerCapabilityState.Eligible,
        string capabilityDigest = DigestA)
    {
        return new ShowrunnerBeatDescriptor(
            beatId,
            ruleId,
            role,
            1,
            15,
            new[] { "Amber", "Bagel" },
            false,
            capabilityState,
            capabilityDigest);
    }

    private static IEnumerable<ShowrunnerBeatDescriptor> DescriptorStreamThatMustBeBounded()
    {
        for (var index = 0; index <= ShowrunnerPlanner.MaximumDescriptors; index++)
        {
            yield return Beat(
                $"opening-{index}",
                $"RuleOpening{index}",
                ShowrunnerBeatRole.Opening);
        }

        throw new InvalidOperationException("Planner enumerated beyond its advertised hard limit.");
    }
}
