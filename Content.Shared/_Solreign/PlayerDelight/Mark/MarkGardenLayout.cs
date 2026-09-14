using System.Collections.Generic;
using Robust.Shared.Maths;

namespace Content.Shared._Solreign.PlayerDelight.Mark;

/// <summary>
///     Pure slot-layout law for the Continuity Garden (docs/specs/MARK-SPEC-2026-07-17-DRAFT.md
///     §4.3): the garden owns a fixed grid of tile offsets; a mark's ledger <c>slot_index</c>
///     indexes that grid modulo its length on the CURRENT map — deterministic per map, stable
///     across visits, same relative planting order everywhere. Zero I/O, zero ECS; unit-tested in
///     Content.Tests/_Solreign/MarkProjectionTests.cs.
///
///     The default bed is 6 columns x 4 rows (24 slots — the <c>solreign.mark.slots</c> default),
///     laid out row-by-row starting on the tile row BELOW the garden fixture so slot 0 sits
///     immediately adjacent and the bed grows away from the fixture in planting order.
/// </summary>
public static class MarkGardenLayout
{
    /// <summary>Columns in the default bed.</summary>
    public const int Columns = 6;

    /// <summary>Rows in the default bed.</summary>
    public const int Rows = 4;

    /// <summary>The default 24-offset bed, in slot order. Fresh list per call (callers may own it).</summary>
    public static List<Vector2i> DefaultOffsets()
    {
        var offsets = new List<Vector2i>(Columns * Rows);
        for (var i = 0; i < Columns * Rows; i++)
            offsets.Add(DefaultOffsetFor(i));
        return offsets;
    }

    /// <summary>The default-bed offset for a slot: row-major, one row below the fixture, growing downward.</summary>
    public static Vector2i DefaultOffsetFor(int slotIndex)
    {
        if (slotIndex < 0)
            throw new System.ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, "Slot indices are non-negative");

        return new Vector2i(slotIndex % Columns, -1 - slotIndex / Columns);
    }

    /// <summary>
    ///     Maps a ledger slot index onto a garden's offset list — modulo the list length (spec §4.3's
    ///     "slot_index indexes that list modulo its length"), non-negative for any input, so a mark
    ///     always resolves a deterministic offset even on a garden with a smaller custom bed.
    /// </summary>
    public static Vector2i SlotOffset(int slotIndex, IReadOnlyList<Vector2i> offsets)
    {
        if (offsets.Count == 0)
            throw new System.ArgumentException("Offset list must be nonempty.", nameof(offsets));

        var count = offsets.Count;
        return offsets[(slotIndex % count + count) % count];
    }

    /// <summary>Columns in the default Echo bed (Echoes of the Departed, v14, EOD-spec §6).</summary>
    public const int EchoColumns = 5;

    /// <summary>Rows in the default Echo bed — 10 slots, matching <c>solreign.echo.slots</c>' default.</summary>
    public const int EchoRows = 2;

    /// <summary>
    ///     The default 10-offset Echo bed, in slot order. A disjoint bed from
    ///     <see cref="DefaultOffsets"/> — Marks grow downward from the fixture; Echoes grow
    ///     upward, so the two families never visually compete for the same tiles (EOD-spec §6).
    ///     Fresh list per call (callers may own it).
    /// </summary>
    public static List<Vector2i> DefaultEchoOffsets()
    {
        var offsets = new List<Vector2i>(EchoColumns * EchoRows);
        for (var i = 0; i < EchoColumns * EchoRows; i++)
            offsets.Add(DefaultEchoOffsetFor(i));
        return offsets;
    }

    /// <summary>The default Echo-bed offset for a slot: row-major, one row above the fixture, growing upward.</summary>
    public static Vector2i DefaultEchoOffsetFor(int slotIndex)
    {
        if (slotIndex < 0)
            throw new System.ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, "Slot indices are non-negative");

        return new Vector2i(slotIndex % EchoColumns, 1 + slotIndex / EchoColumns);
    }
}
