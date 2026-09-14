using System.Collections.Generic;
using System.Linq;
using Content.Server._Solreign.StationAudits;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Pure-unit coverage for <see cref="StationAuditInspectionSelection"/> (v14 gap-closure pass) — no
///     ECS, no I/O. Exercises round-robin determinism, the no-immediate-repeat property, the
///     always-eligible curation rule, and every degenerate input the spec calls out.
/// </summary>
[TestFixture]
[TestOf(typeof(StationAuditInspectionSelection))]
public sealed class StationAuditInspectionSelectionTests
{
    // Mirrors the real seed catalog's shape (6 entries, indices 0-2 always eligible) without coupling
    // the test to StationAuditCriterionCatalog's exact contents.
    private static readonly IReadOnlySet<int> AlwaysEligible = new HashSet<int> { 0, 1, 2 };

    [Test]
    public void SelectAssigned_IsDeterministic_ForTheSameInputs()
    {
        var first = StationAuditInspectionSelection.SelectAssigned(42, 6, 2, AlwaysEligible);
        var second = StationAuditInspectionSelection.SelectAssigned(42, 6, 2, AlwaysEligible);

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void SelectAssigned_ReturnsRequestedCount_WhenWithinCatalogBounds()
    {
        var picked = StationAuditInspectionSelection.SelectAssigned(0, 6, 2, AlwaysEligible);
        Assert.That(picked, Has.Count.EqualTo(2));
    }

    [Test]
    public void SelectAssigned_EveryPickedIndex_IsWithinCatalogBounds()
    {
        for (var roundId = 0; roundId < 20; roundId++)
        {
            var picked = StationAuditInspectionSelection.SelectAssigned(roundId, 6, 2, AlwaysEligible);
            Assert.That(picked, Is.All.Matches<int>(i => i is >= 0 and <= 5), $"round {roundId} produced an out-of-range index");
        }
    }

    [Test]
    public void SelectAssigned_ConsecutiveRoundIds_NeverProduceTheIdenticalSet()
    {
        // Round-robin-by-round-id property: a window that shifts by one index per round id cannot
        // reproduce the exact same set on the very next round id (for a window smaller than the
        // catalog).
        for (var roundId = 0; roundId < 10; roundId++)
        {
            var a = StationAuditInspectionSelection.SelectAssigned(roundId, 6, 2, AlwaysEligible);
            var b = StationAuditInspectionSelection.SelectAssigned(roundId + 1, 6, 2, AlwaysEligible);

            Assert.That(a.SequenceEqual(b), Is.False, $"round {roundId} and {roundId + 1} assigned the identical set");
        }
    }

    [Test]
    public void SelectAssigned_CuratesAtLeastOneAlwaysEligibleCriterion()
    {
        // A window of size 2 starting at index 3 (round id 3) with catalog size 6 and always-eligible
        // = {0,1,2} would naturally land on {3,4} — zero always-eligible entries — without the
        // curation rule.
        var picked = StationAuditInspectionSelection.SelectAssigned(3, 6, 2, AlwaysEligible);

        Assert.That(picked.Any(AlwaysEligible.Contains), Is.True,
            "assignment must guarantee at least one always-eligible criterion so it never reads as a guaranteed no-op");
    }

    [Test]
    public void SelectAssigned_WindowAlreadyContainingAnAlwaysEligibleCriterion_IsUnchangedByCuration()
    {
        // Round id 0 with a 2-wide window naturally picks {0, 1} — both already always-eligible — so
        // curation must not perturb it.
        var picked = StationAuditInspectionSelection.SelectAssigned(0, 6, 2, AlwaysEligible);

        Assert.That(picked, Is.EqualTo(new[] { 0, 1 }));
    }

    // --- Degenerate inputs — must never throw -------------------------------------------------------

    [Test]
    public void SelectAssigned_ZeroAssignCount_ReturnsEmpty()
    {
        Assert.That(StationAuditInspectionSelection.SelectAssigned(0, 6, 0, AlwaysEligible), Is.Empty);
    }

    [Test]
    public void SelectAssigned_NegativeAssignCount_ReturnsEmpty()
    {
        Assert.That(StationAuditInspectionSelection.SelectAssigned(0, 6, -3, AlwaysEligible), Is.Empty);
    }

    [Test]
    public void SelectAssigned_ZeroCatalogCount_ReturnsEmpty()
    {
        Assert.That(StationAuditInspectionSelection.SelectAssigned(0, 0, 2, AlwaysEligible), Is.Empty);
    }

    [Test]
    public void SelectAssigned_AssignCountExceedsCatalogCount_ClampsToWholeCatalog()
    {
        var picked = StationAuditInspectionSelection.SelectAssigned(0, 6, 999, AlwaysEligible);
        Assert.That(picked, Has.Count.EqualTo(6));
        Assert.That(picked.Distinct().Count(), Is.EqualTo(6), "clamped full-catalog assignment must not duplicate an index");
    }

    [Test]
    public void SelectAssigned_NegativeRoundId_NeverThrows_AndProducesValidIndices()
    {
        Assert.DoesNotThrow(() =>
        {
            var picked = StationAuditInspectionSelection.SelectAssigned(-7, 6, 2, AlwaysEligible);
            Assert.That(picked, Is.All.Matches<int>(i => i is >= 0 and <= 5));
        });
    }

    [Test]
    public void SelectAssigned_EmptyAlwaysEligibleSet_NeverThrows_AndSkipsCuration()
    {
        Assert.DoesNotThrow(() =>
        {
            var picked = StationAuditInspectionSelection.SelectAssigned(3, 6, 2, new HashSet<int>());
            Assert.That(picked, Has.Count.EqualTo(2));
        });
    }
}
