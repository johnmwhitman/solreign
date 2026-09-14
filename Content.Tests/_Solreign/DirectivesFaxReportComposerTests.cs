using System.Linq;
using Content.Server._Solreign.DirectivesFax;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Pure-unit coverage for <see cref="DirectivesFaxReportComposer"/> — no ECS, no I/O, no
///     localization. Mirrors <c>StationAuditComposerTests</c>' idiom: exercise the composed data
///     directly, never render Loc strings here (that half lives only in the integration test, where a
///     real <c>ILocalizationManager</c> exists).
/// </summary>
[TestFixture]
[TestOf(typeof(DirectivesFaxReportComposer))]
public sealed class DirectivesFaxReportComposerTests
{
    private static readonly DirectivesFaxClauseSpec ZeroCasualties =
        new("zero-casualties", DirectivesFaxClauseKind.ZeroCasualties);

    private static readonly DirectivesFaxClauseSpec CargoRevenue1000 =
        new("cargo-revenue", DirectivesFaxClauseKind.CargoRevenueMin, 1000);

    [Test]
    public void AllClausesMet_ReportsMetTrue_AndEveryClausePassed()
    {
        var clauses = new[] { ZeroCasualties, CargoRevenue1000 };
        var state = new DirectivesFaxShiftState(CrewDeaths: 0, CargoRevenueDelta: 1000, SupplyOrdersDelta: 0);

        var report = DirectivesFaxReportComposer.Compose("productivity-mandate", clauses, state, eligibleCrewCount: 4);

        Assert.Multiple(() =>
        {
            Assert.That(report.Met, Is.True);
            Assert.That(report.Clauses, Has.Count.EqualTo(2));
            Assert.That(report.Clauses.All(c => c.Passed), Is.True);
        });
    }

    [Test]
    public void OneClauseUnmet_ReportsMetFalse_ButPerClauseResultsStayHonest_NotMajority()
    {
        var clauses = new[] { ZeroCasualties, CargoRevenue1000 };
        // Zero deaths (passes), revenue short by 1 (fails) -- overall Met must be false (AND, not
        // majority), but the per-clause breakdown must still show the true/false split, not collapse
        // to "everything failed" just because the overall verdict is unmet.
        var state = new DirectivesFaxShiftState(CrewDeaths: 0, CargoRevenueDelta: 999, SupplyOrdersDelta: 0);

        var report = DirectivesFaxReportComposer.Compose("productivity-mandate", clauses, state, eligibleCrewCount: 4);

        Assert.Multiple(() =>
        {
            Assert.That(report.Met, Is.False);
            Assert.That(report.Clauses.Single(c => c.Clause.Kind == DirectivesFaxClauseKind.ZeroCasualties).Passed, Is.True);
            Assert.That(report.Clauses.Single(c => c.Clause.Kind == DirectivesFaxClauseKind.CargoRevenueMin).Passed, Is.False);
        });
    }

    // --- Station-wide tally: met ? eligible : 0, vs eligible -- never a fabricated per-account split ---

    [TestCase(false, 5, 5)]
    [TestCase(true, 5, 0)]
    [TestCase(false, 0, 0)]
    [TestCase(true, 0, 0)]
    public void Tally_IsTheSingleStationWideVerdictBroadcastAcrossEligibleCount(bool crewDied, int eligibleCrewCount, int expectedCrewMetCount)
    {
        var clauses = new[] { ZeroCasualties };
        var state = new DirectivesFaxShiftState(CrewDeaths: crewDied ? 1 : 0, CargoRevenueDelta: 0, SupplyOrdersDelta: 0);

        var report = DirectivesFaxReportComposer.Compose("q3-incident-quota", clauses, state, eligibleCrewCount);

        Assert.Multiple(() =>
        {
            Assert.That(report.Met, Is.EqualTo(!crewDied));
            Assert.That(report.EligibleCrewCount, Is.EqualTo(eligibleCrewCount));
            Assert.That(report.CrewMetCount, Is.EqualTo(expectedCrewMetCount));
        });
    }

    [Test]
    public void NegativeEligibleCount_IsDefensivelyClampedToZero_NeverFabricatesANegativeHeadcount()
    {
        var clauses = new[] { ZeroCasualties };
        var state = new DirectivesFaxShiftState(CrewDeaths: 0, CargoRevenueDelta: 0, SupplyOrdersDelta: 0);

        var report = DirectivesFaxReportComposer.Compose("q3-incident-quota", clauses, state, eligibleCrewCount: -1);

        Assert.Multiple(() =>
        {
            Assert.That(report.EligibleCrewCount, Is.EqualTo(0));
            Assert.That(report.CrewMetCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void EmptyClauseList_IsVacuouslyMet_AndProducesNoClauseResults()
    {
        var state = new DirectivesFaxShiftState(CrewDeaths: 99, CargoRevenueDelta: -999, SupplyOrdersDelta: 0);

        var report = DirectivesFaxReportComposer.Compose("unrecognized-directive", System.Array.Empty<DirectivesFaxClauseSpec>(), state, eligibleCrewCount: 3);

        Assert.Multiple(() =>
        {
            Assert.That(report.Met, Is.True, "matches DirectivesFaxClauseEvaluation.EvaluateAll's own vacuous-true rule for an empty clause list.");
            Assert.That(report.Clauses, Is.Empty);
            Assert.That(report.CrewMetCount, Is.EqualTo(3));
        });
    }

    [Test]
    public void DirectiveId_IsCarriedThroughUnchanged()
    {
        var state = new DirectivesFaxShiftState(CrewDeaths: 0, CargoRevenueDelta: 0, SupplyOrdersDelta: 0);

        var report = DirectivesFaxReportComposer.Compose("safety-inspection", new[] { ZeroCasualties }, state, eligibleCrewCount: 1);

        Assert.That(report.DirectiveId, Is.EqualTo("safety-inspection"));
    }
}
