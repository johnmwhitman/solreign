using System.Numerics;

namespace Content.Server._Solreign.Zones;

/// <summary>
/// Marks the "ladder up" prop placed inside Resources/Maps/_Solreign/zone_hell.yml's Boardroom
/// (spec: docs/specs/2026-07-11-hell-heaven-zones.md §2, "THE LADDER UP — one-way flavor door/
/// passage; 'promotion' moment"). Activating it loads the standalone Heaven grid
/// (Resources/Maps/_Solreign/zone_heaven.yml) if needed and teleports the traveler there.
///
/// No access lock (spec: the way up, unlike the way in, is not gated) and — unlike
/// <see cref="SolreignHellZoneEntryComponent"/> — this does NOT touch
/// <see cref="SolreignZoneReturnComponent"/>: the traveler is mid-trip (Hell -> Heaven), not
/// entering fresh from the real station, so their original real-world return coordinates
/// (recorded at the Sublevel H gate) must survive the hop untouched. The Heaven-side exit gate
/// (<see cref="SolreignZoneReturnGateComponent"/>, placed inside zone_heaven.yml) sends them
/// all the way back to that original point, same as bailing out early from Hell would.
///
/// Closes the gap left by the zone_heaven.yml build pass, whose file header documents this
/// wiring as deliberately out of that lane's scope ("this file is not reachable in-round yet...
/// leave wiring to whoever picks this up next") — Wave 10 verifier fix, not a design change.
/// </summary>
[RegisterComponent]
public sealed partial class SolreignHeavenZoneLadderComponent : Component
{
    /// <summary>
    /// Local tile-center coordinate on the loaded Heaven grid (Resources/Maps/_Solreign/zone_heaven.yml)
    /// where arriving entities are placed. See SolreignZoneGateSystem for the verified-clear tile
    /// this was picked against (the atrium's entrance-row area, away from every placed prop).
    /// </summary>
    [DataField]
    public Vector2 SpawnOffset = new(-6.5f, -1.5f);
}
