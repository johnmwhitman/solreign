using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.Shared._Solreign.PlayerDelight.Mark;

/// <summary>
///     Marks the Continuity Garden fixture (docs/specs/MARK-SPEC-2026-07-17-DRAFT.md §4) — the
///     station keepsake bed every recorded mark projects into each round, and the alt-click surface
///     the once-per-account planting verbs hang off. Shared only because the component appears in
///     prototype YAML (<c>mark_garden.yml</c>); all behavior is server-side
///     (<c>SolreignMarkGardenSystem</c>) and nothing here is networked.
/// </summary>
[RegisterComponent]
public sealed partial class SolreignMarkGardenComponent : Component
{
    /// <summary>
    ///     The garden's slot grid: tile offsets (relative to the fixture's own tile) in slot order.
    ///     A mark's ledger <c>slot_index</c> indexes this list modulo its length
    ///     (<see cref="MarkGardenLayout.SlotOffset"/>) — fixed positions owned by the fixture, never
    ///     by players (anti-grief rail 5: no free-form placement, nothing can be arranged into a
    ///     shape).
    /// </summary>
    [DataField]
    public List<Vector2i> SlotOffsets = MarkGardenLayout.DefaultOffsets();

    /// <summary>
    ///     Echoes of the Departed (v14, EOD-spec §6): a second, disjoint slot grid for
    ///     PROVIDENCE-authored Echo projections of <c>first_death</c> rows — same shape and same
    ///     anti-grief rail as <see cref="SlotOffsets"/> (fixed positions owned by the fixture,
    ///     never by players), but growing the opposite direction so the two families never
    ///     visually compete for the same tiles.
    /// </summary>
    [DataField]
    public List<Vector2i> EchoSlotOffsets = MarkGardenLayout.DefaultEchoOffsets();
}
