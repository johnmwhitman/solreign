using System.Numerics;

namespace Content.Server._Solreign.Zones;

/// <summary>
/// Marks an entity as the Sublevel H entry gate (spec: docs/specs/2026-07-11-hell-heaven-zones.md §3,
/// Option A — deep-maintenance door/teleporter). Intended placement: a single instance in deep
/// maintenance on the live station grid, paired with a vanilla <c>AccessReader</c> component
/// requiring the <c>SolreignSublevelH</c> access level (see
/// Resources/Prototypes/_Solreign/Entities/Markers/hellzone_gates.yml).
///
/// NOT placed by this wave — Resources/Maps/_Solreign/solreign_oasis.yml is Wave 9's file and is
/// out of scope here. This component + its prototype + SolreignZoneGateSystem are the reusable
/// "register it" deliverable; hand-placing the physical gate into deep maintenance is a follow-up
/// mapping pass (Wave 9 or a dedicated mapper).
/// </summary>
[RegisterComponent]
public sealed partial class SolreignHellZoneEntryComponent : Component
{
    /// <summary>
    /// Local tile-center coordinate on the loaded Sublevel H grid (Resources/Maps/_Solreign/zone_hell.yml)
    /// where arriving entities are placed. Hardcoded against the hand-authored map rather than a
    /// runtime marker search — the map is ours, the coordinate is known, and it's one fewer moving part.
    /// </summary>
    [DataField]
    public Vector2 SpawnOffset = new(8.5f, 2.5f);
}
