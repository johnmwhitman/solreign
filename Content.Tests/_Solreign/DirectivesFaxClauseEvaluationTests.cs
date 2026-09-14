using Content.Server._Solreign.DirectivesFax;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Pure unit coverage for clause evaluation + formatting — no ECS, no Loc. Exercises all three
///     closed-vocabulary clause kinds and the round's AND-not-majority outcome rule.
/// </summary>
[TestFixture]
[TestOf(typeof(DirectivesFaxClauseEvaluation))]
public sealed class DirectivesFaxClauseEvaluationTests
{
    // --- ZeroCasualties ---

    [Test]
    public void ZeroCasualties_NoDeaths_IsMet()
    {
        var clause = new DirectivesFaxClauseSpec("zero-casualties", DirectivesFaxClauseKind.ZeroCasualties);
        var state = new DirectivesFaxShiftState(CrewDeaths: 0, CargoRevenueDelta: 0, SupplyOrdersDelta: 0);

        Assert.That(DirectivesFaxClauseEvaluation.EvaluateClause(clause, state), Is.True);
    }

    [Test]
    public void ZeroCasualties_OneDeath_IsUnmet()
    {
        var clause = new DirectivesFaxClauseSpec("zero-casualties", DirectivesFaxClauseKind.ZeroCasualties);
        var state = new DirectivesFaxShiftState(CrewDeaths: 1, CargoRevenueDelta: 0, SupplyOrdersDelta: 0);

        Assert.That(DirectivesFaxClauseEvaluation.EvaluateClause(clause, state), Is.False);
    }

    // --- CargoRevenueMin ---

    [TestCase(1500, 1500, ExpectedResult = true)]
    [TestCase(1500, 1501, ExpectedResult = true)]
    [TestCase(1500, 1499, ExpectedResult = false)]
    [TestCase(1500, 0, ExpectedResult = false)]
    [TestCase(1500, -100, ExpectedResult = false)]
    public bool CargoRevenueMin_ComparesDeltaAgainstThreshold(int threshold, int cargoDelta)
    {
        var clause = new DirectivesFaxClauseSpec("cargo-revenue", DirectivesFaxClauseKind.CargoRevenueMin, threshold);
        var state = new DirectivesFaxShiftState(CrewDeaths: 0, CargoRevenueDelta: cargoDelta, SupplyOrdersDelta: 0);

        return DirectivesFaxClauseEvaluation.EvaluateClause(clause, state);
    }

    // --- SupplyOrdersMin ---

    [TestCase(3, 3, ExpectedResult = true)]
    [TestCase(3, 4, ExpectedResult = true)]
    [TestCase(3, 2, ExpectedResult = false)]
    [TestCase(3, 0, ExpectedResult = false)]
    public bool SupplyOrdersMin_ComparesDeltaAgainstThreshold(int threshold, int ordersDelta)
    {
        var clause = new DirectivesFaxClauseSpec("supply-orders", DirectivesFaxClauseKind.SupplyOrdersMin, threshold);
        var state = new DirectivesFaxShiftState(CrewDeaths: 0, CargoRevenueDelta: 0, SupplyOrdersDelta: ordersDelta);

        return DirectivesFaxClauseEvaluation.EvaluateClause(clause, state);
    }

    // --- EvaluateAll: AND semantics, not majority ---

    [Test]
    public void EvaluateAll_EmptyClauseList_IsVacuouslyTrue()
    {
        var state = new DirectivesFaxShiftState(CrewDeaths: 99, CargoRevenueDelta: -999, SupplyOrdersDelta: 0);

        Assert.That(DirectivesFaxClauseEvaluation.EvaluateAll(System.Array.Empty<DirectivesFaxClauseSpec>(), state), Is.True);
    }

    [Test]
    public void EvaluateAll_AllClausesMet_IsTrue()
    {
        var clauses = new[]
        {
            new DirectivesFaxClauseSpec("zero-casualties", DirectivesFaxClauseKind.ZeroCasualties),
            new DirectivesFaxClauseSpec("cargo-revenue", DirectivesFaxClauseKind.CargoRevenueMin, 1000),
        };
        var state = new DirectivesFaxShiftState(CrewDeaths: 0, CargoRevenueDelta: 1000, SupplyOrdersDelta: 0);

        Assert.That(DirectivesFaxClauseEvaluation.EvaluateAll(clauses, state), Is.True);
    }

    [Test]
    public void EvaluateAll_OneClauseUnmet_OutOfTwo_IsFalse_NotMajority()
    {
        var clauses = new[]
        {
            new DirectivesFaxClauseSpec("zero-casualties", DirectivesFaxClauseKind.ZeroCasualties),
            new DirectivesFaxClauseSpec("cargo-revenue", DirectivesFaxClauseKind.CargoRevenueMin, 1000),
        };
        // Zero deaths (met), but revenue short by 1 (unmet) -- a directive with two clauses that only
        // half-delivers must NOT count as complied with.
        var state = new DirectivesFaxShiftState(CrewDeaths: 0, CargoRevenueDelta: 999, SupplyOrdersDelta: 0);

        Assert.That(DirectivesFaxClauseEvaluation.EvaluateAll(clauses, state), Is.False);
    }

    [Test]
    public void EvaluateAll_AllClausesUnmet_IsFalse()
    {
        var clauses = new[]
        {
            new DirectivesFaxClauseSpec("zero-casualties", DirectivesFaxClauseKind.ZeroCasualties),
            new DirectivesFaxClauseSpec("supply-orders", DirectivesFaxClauseKind.SupplyOrdersMin, 3),
        };
        var state = new DirectivesFaxShiftState(CrewDeaths: 2, CargoRevenueDelta: 0, SupplyOrdersDelta: 0);

        Assert.That(DirectivesFaxClauseEvaluation.EvaluateAll(clauses, state), Is.False);
    }

    // --- DirectivesFaxClauseFormatting: pure loc-key + args selection ---

    [Test]
    public void Describe_ZeroCasualties_UsesTheZeroCasualtiesKey_WithNoArgs()
    {
        var clause = new DirectivesFaxClauseSpec("zero-casualties", DirectivesFaxClauseKind.ZeroCasualties);

        var (locKey, args) = DirectivesFaxClauseFormatting.Describe(clause);

        Assert.That(locKey, Is.EqualTo("solreign-directives-fax-clause-zero-casualties"));
        Assert.That(args, Is.Empty);
    }

    [Test]
    public void Describe_CargoRevenueMin_PassesTheThresholdAsAmount()
    {
        var clause = new DirectivesFaxClauseSpec("cargo-revenue", DirectivesFaxClauseKind.CargoRevenueMin, 2500);

        var (locKey, args) = DirectivesFaxClauseFormatting.Describe(clause);

        Assert.That(locKey, Is.EqualTo("solreign-directives-fax-clause-cargo-revenue-min"));
        Assert.That(args, Has.Length.EqualTo(1));
        Assert.That(args[0], Is.EqualTo(("amount", (object) 2500)));
    }

    [Test]
    public void Describe_SupplyOrdersMin_PassesTheThresholdAsCount()
    {
        var clause = new DirectivesFaxClauseSpec("supply-orders", DirectivesFaxClauseKind.SupplyOrdersMin, 4);

        var (locKey, args) = DirectivesFaxClauseFormatting.Describe(clause);

        Assert.That(locKey, Is.EqualTo("solreign-directives-fax-clause-supply-orders-min"));
        Assert.That(args, Has.Length.EqualTo(1));
        Assert.That(args[0], Is.EqualTo(("count", (object) 4)));
    }
}
