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
///     Pure projection law for Echoes of the Departed (v14, EOD-spec §6/§11) — mirrors
///     <see cref="MarkProjectionTests"/> exactly:
///       * <see cref="MarkGardenLayout"/>'s Echo bed — a disjoint, complete, collision-free 10-slot
///         grid, growing the OPPOSITE direction from the Mark bed so the two families never
///         compete for the same tiles;
///       * <see cref="EchoProjectionRules"/> — the closed 4-stage table (no kind axis, unlike
///         Mark's 3x4), and rejects out-of-table stages.
///     The ids' spawnability against the real YAML is integration-tested in
///     Content.IntegrationTests/Tests/_Solreign/EchoGardenIntegrationTest.cs.
/// </summary>
[TestFixture]
[TestOf(typeof(EchoProjectionRules))]
public sealed class EchoProjectionTests
{
    // --- Slot layout (Echo bed) --------------------------------------------------------------------

    [Test]
    public void EchoBed_IsTenUniqueOffsets_MatchingTheSlotsCVarDefault()
    {
        var offsets = MarkGardenLayout.DefaultEchoOffsets();
        Assert.Multiple(() =>
        {
            Assert.That(offsets, Has.Count.EqualTo(10),
                "the default Echo bed matches solreign.echo.slots' default capacity");
            Assert.That(offsets, Is.Unique, "two Echo slots may never share a tile");
            Assert.That(offsets, Does.Not.Contain(Vector2i.Zero),
                "no Echo slot may overlap the garden fixture's own tile");
        });
    }

    [Test]
    public void EchoBed_NeverOverlapsTheMarkBed()
    {
        // EOD-spec §6: Echoes grow the opposite direction from Marks (upward vs downward) so the
        // two families never visually compete for the same tiles.
        var markOffsets = MarkGardenLayout.DefaultOffsets();
        var echoOffsets = MarkGardenLayout.DefaultEchoOffsets();

        Assert.That(markOffsets.Intersect(echoOffsets), Is.Empty,
            "the Mark bed and the Echo bed must be disjoint sets of tiles");
    }

    [Test]
    public void EchoBed_IsDeterministic_AndRowMajorFromTheFixture_GrowingUpward()
    {
        var first = MarkGardenLayout.DefaultEchoOffsets();
        var second = MarkGardenLayout.DefaultEchoOffsets();
        Assert.Multiple(() =>
        {
            Assert.That(second, Is.EqualTo(first),
                "the Echo bed must be identical on every call — slot positions are forever");
            Assert.That(MarkGardenLayout.DefaultEchoOffsetFor(0), Is.EqualTo(new Vector2i(0, 1)),
                "Echo slot 0 sits immediately adjacent to the fixture, one row ABOVE it");
            Assert.That(MarkGardenLayout.DefaultEchoOffsetFor(MarkGardenLayout.EchoColumns),
                Is.EqualTo(new Vector2i(0, 2)),
                "the Echo bed wraps row-major, growing UPWARD away from the fixture");
        });
    }

    [Test]
    public void DefaultEchoOffsetFor_NegativeSlot_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MarkGardenLayout.DefaultEchoOffsetFor(-1));
    }

    [Test]
    public void SlotOffset_IsIdentity_InsideTheEchoBed()
    {
        var offsets = MarkGardenLayout.DefaultEchoOffsets();
        for (var slot = 0; slot < offsets.Count; slot++)
        {
            Assert.That(MarkGardenLayout.SlotOffset(slot, offsets), Is.EqualTo(offsets[slot]),
                $"Echo slot {slot} must resolve its own offset");
        }
    }

    // --- Prototype mapping ------------------------------------------------------------------------

    [Test]
    public void PrototypeTable_IsExactlyTheClosedFour()
    {
        var expected = new[] { "SolreignEcho0", "SolreignEcho1", "SolreignEcho2", "SolreignEcho3" };

        var actual = Enumerable.Range(0, MarkAgeRules.MaxStage + 1)
            .Select(EchoProjectionRules.PrototypeFor)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.EquivalentTo(expected),
                "the Echo projection table is the closed 4-stage echo_garden.yml set, nothing else");
            Assert.That(actual, Is.Unique, "no two stages may share a prototype");
        });
    }

    [Test]
    public void PrototypeMapping_IsDeterministic()
    {
        for (var stage = 0; stage <= MarkAgeRules.MaxStage; stage++)
        {
            var first = EchoProjectionRules.PrototypeFor(stage);
            var second = EchoProjectionRules.PrototypeFor(stage);
            Assert.That(second, Is.EqualTo(first), "same stage, same prototype, forever");
        }
    }

    [TestCase(-1)]
    [TestCase(4)]
    [TestCase(int.MaxValue)]
    public void PrototypeFor_OutsideTheClosedTable_Throws(int stage)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EchoProjectionRules.PrototypeFor(stage));
    }

    [Test]
    public void ProjectionOfAgedFixture_LandsTheStageMatchedPrototype()
    {
        // The §4 pipeline in miniature: ledger died_at_utc → StageAt → PrototypeFor. Pins the
        // composition the integration test exercises against the live YAML.
        var died = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        Assert.Multiple(() =>
        {
            Assert.That(
                EchoProjectionRules.PrototypeFor(MarkAgeRules.StageAt(died, died.AddHours(1))),
                Is.EqualTo("SolreignEcho0"));
            Assert.That(
                EchoProjectionRules.PrototypeFor(MarkAgeRules.StageAt(died, died.AddDays(3))),
                Is.EqualTo("SolreignEcho1"));
            Assert.That(
                EchoProjectionRules.PrototypeFor(MarkAgeRules.StageAt(died, died.AddDays(10))),
                Is.EqualTo("SolreignEcho2"));
            Assert.That(
                EchoProjectionRules.PrototypeFor(MarkAgeRules.StageAt(died, died.AddDays(400))),
                Is.EqualTo("SolreignEcho3"));
        });
    }
}
