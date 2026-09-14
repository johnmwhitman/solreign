namespace Content.Server._Solreign.Zones;

/// <summary>
/// Marks the Sublevel H exit ("the ladder up out of the Annex") placed inside
/// Resources/Maps/_Solreign/zone_hell.yml itself. Activating it sends the traveler back to
/// wherever they entered from (see <see cref="SolreignZoneReturnComponent"/>), matching the
/// spec's "the trip out is quick" design rule (§2). No access lock — the way out is always free.
/// </summary>
[RegisterComponent]
public sealed partial class SolreignZoneReturnGateComponent : Component
{
}
