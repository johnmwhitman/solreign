using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Solreign.PlayerDelight.Vista;

/// <summary>
///     Marks one map-baked, invisible "composed vista" spot on a station's first-shift path (council
///     memo docs/council/2026-07-16-design-magnetism.md, item 5: "route the first-shift path past one
///     composed vista with a PROVIDENCE line"). Each of the seven rotation maps carries exactly one
///     (asserted by SolreignVistaMarkerMapPlacementIntegrationTest), placed at an existing
///     visually-composed spot — a window run onto space, a mood-lit office behind glass, an overgrown
///     cargo corner — that a player with an active First Shift assignment plausibly walks past.
///
///     The marker itself is pure data: an anchored MarkerBase child (invisible outside mapping mode,
///     see Content.Client.Markers.MarkerSystem) plus the map-specific PROVIDENCE line to deliver.
///     All behavior lives server-side in SolreignVistaBeatSystem.
/// </summary>
[RegisterComponent]
public sealed partial class SolreignVistaMarkerComponent : Component
{
    /// <summary>
    ///     Loc id of the map-specific PROVIDENCE line (vista-beat.ftl). Set per map in the map file —
    ///     each line references what the player is actually looking at from this marker's spot, so it
    ///     can never be shared between maps. Empty means the marker is inert (defensive: a mapper who
    ///     forgets the override gets silence, not a raw loc-id popup).
    /// </summary>
    [DataField]
    public string LineId = string.Empty;

    /// <summary>
    ///     Delivery radius in tiles (world units) around the marker. 3 covers the corridor width the
    ///     seven chosen spots sit on without leaking through neighboring rooms' walls.
    /// </summary>
    [DataField]
    public float Range = 3f;
}
