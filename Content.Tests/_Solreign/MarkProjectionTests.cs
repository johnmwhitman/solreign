#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._Solreign.PlayerDelight.Mark;
using Content.Shared._Solreign.PlayerDelight.Mark;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests._Solreign;

/// <summary>
///     Pure projection law for the Mark garden wave (MARK-SPEC §3.3/§4.1/§4.3, MG-W2):
///       * <see cref="MarkGardenLayout"/> — the default 24-slot bed is complete, collision-free,
///         never overlaps the fixture's own tile, and slot→offset resolution is deterministic and
///         modulo-stable for ANY slot index (a mark on a smaller custom bed still resolves);
///       * <see cref="MarkProjectionRules"/> — the (kind, stage) → prototype-id table is the
///         closed 3x4 set, nothing else, and rejects out-of-table stages.
///     The ids' spawnability against the real YAML is integration-tested in
///     Content.IntegrationTests/Tests/_Solreign/MarkGardenIntegrationTest.cs.
/// </summary>
[TestFixture]
[TestOf(typeof(MarkProjectionRules))]
public sealed class MarkProjectionTests
{
    // --- Slot layout ------------------------------------------------------------------------------

    [Test]
    public void DefaultBed_IsTwentyFourUniqueOffsets_MatchingTheSlotsCVarDefault()
    {
        var offsets = MarkGardenLayout.DefaultOffsets();
        Assert.Multiple(() =>
        {
            Assert.That(offsets, Has.Count.EqualTo(24),
                "the default bed matches solreign.mark.slots' default capacity");
            Assert.That(offsets, Is.Unique, "two slots may never share a tile");
            Assert.That(offsets, Does.Not.Contain(Vector2i.Zero),
                "no slot may overlap the garden fixture's own tile");
        });
    }

    [Test]
    public void DefaultBed_IsDeterministic_AndRowMajorFromTheFixture()
    {
        var first = MarkGardenLayout.DefaultOffsets();
        var second = MarkGardenLayout.DefaultOffsets();
        Assert.Multiple(() =>
        {
            Assert.That(second, Is.EqualTo(first),
                "the bed must be identical on every call — slot positions are forever");
            Assert.That(MarkGardenLayout.DefaultOffsetFor(0), Is.EqualTo(new Vector2i(0, -1)),
                "slot 0 sits immediately adjacent to the fixture");
            Assert.That(MarkGardenLayout.DefaultOffsetFor(MarkGardenLayout.Columns),
                Is.EqualTo(new Vector2i(0, -2)),
                "the bed wraps row-major, growing away from the fixture");
        });
    }

    [Test]
    public void DefaultOffsetFor_NegativeSlot_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MarkGardenLayout.DefaultOffsetFor(-1));
    }

    [Test]
    public void SlotOffset_IsIdentity_InsideTheBed()
    {
        var offsets = MarkGardenLayout.DefaultOffsets();
        for (var slot = 0; slot < offsets.Count; slot++)
        {
            Assert.That(MarkGardenLayout.SlotOffset(slot, offsets), Is.EqualTo(offsets[slot]),
                $"slot {slot} must resolve its own offset");
        }
    }

    [Test]
    public void SlotOffset_WrapsModulo_ForAnySlotIndex()
    {
        // Spec §4.3: slot_index indexes the CURRENT map's offset list modulo its length — a ledger
        // slot beyond a smaller custom bed still resolves deterministically, never throws.
        var smallBed = new List<Vector2i> { new(0, -1), new(1, -1), new(2, -1) };
        Assert.Multiple(() =>
        {
            Assert.That(MarkGardenLayout.SlotOffset(3, smallBed), Is.EqualTo(smallBed[0]));
            Assert.That(MarkGardenLayout.SlotOffset(7, smallBed), Is.EqualTo(smallBed[1]));
            Assert.That(MarkGardenLayout.SlotOffset(int.MaxValue, smallBed),
                Is.EqualTo(smallBed[int.MaxValue % 3]));
            // Defensive: a corrupt negative index still lands in range rather than throwing.
            Assert.That(smallBed, Does.Contain(MarkGardenLayout.SlotOffset(-1, smallBed)));
        });
    }

    [Test]
    public void SlotOffset_EmptyBed_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            MarkGardenLayout.SlotOffset(0, new List<Vector2i>()));
    }

    // --- Prototype mapping ------------------------------------------------------------------------

    [Test]
    public void PrototypeTable_IsExactlyTheClosedTwelve()
    {
        var expected = new[]
        {
            "SolreignMarkSapling0", "SolreignMarkSapling1", "SolreignMarkSapling2", "SolreignMarkSapling3",
            "SolreignMarkLamp0", "SolreignMarkLamp1", "SolreignMarkLamp2", "SolreignMarkLamp3",
            "SolreignMarkPlate0", "SolreignMarkPlate1", "SolreignMarkPlate2", "SolreignMarkPlate3",
        };

        var actual = MarkKinds.All
            .SelectMany(kind => Enumerable.Range(0, MarkAgeRules.MaxStage + 1)
                .Select(stage => MarkProjectionRules.PrototypeFor(kind, stage)))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.EquivalentTo(expected),
                "the projection table is the closed 3x4 mark_garden.yml set, nothing else");
            Assert.That(actual, Is.Unique, "no two (kind, stage) cells may share a prototype");
        });
    }

    [Test]
    public void PrototypeMapping_IsDeterministic()
    {
        foreach (var kind in MarkKinds.All)
        {
            for (var stage = 0; stage <= MarkAgeRules.MaxStage; stage++)
            {
                var first = MarkProjectionRules.PrototypeFor(kind, stage);
                var second = MarkProjectionRules.PrototypeFor(kind, stage);
                Assert.That(second, Is.EqualTo(first), "same row, same prototype, forever");
            }
        }
    }

    [TestCase(-1)]
    [TestCase(4)]
    [TestCase(int.MaxValue)]
    public void PrototypeFor_OutsideTheClosedTable_Throws(int stage)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MarkProjectionRules.PrototypeFor(MarkKind.Sapling, stage));
    }

    [Test]
    public void ProjectionOfAgedFixture_LandsTheStageMatchedPrototype()
    {
        // The §3.3 pipeline in miniature: ledger timestamp → StageAt → PrototypeFor. Pins the
        // composition the integration test exercises against the live YAML.
        var planted = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        Assert.Multiple(() =>
        {
            Assert.That(
                MarkProjectionRules.PrototypeFor(MarkKind.Sapling, MarkAgeRules.StageAt(planted, planted.AddHours(1))),
                Is.EqualTo("SolreignMarkSapling0"));
            Assert.That(
                MarkProjectionRules.PrototypeFor(MarkKind.Lamp, MarkAgeRules.StageAt(planted, planted.AddDays(3))),
                Is.EqualTo("SolreignMarkLamp1"));
            Assert.That(
                MarkProjectionRules.PrototypeFor(MarkKind.NamePlate, MarkAgeRules.StageAt(planted, planted.AddDays(10))),
                Is.EqualTo("SolreignMarkPlate2"));
            Assert.That(
                MarkProjectionRules.PrototypeFor(MarkKind.Sapling, MarkAgeRules.StageAt(planted, planted.AddDays(400))),
                Is.EqualTo("SolreignMarkSapling3"));
        });
    }
}
