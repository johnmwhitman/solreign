using Robust.Shared.Map;

namespace Content.Server._Solreign.Zones;

/// <summary>
/// Transient bookkeeping stamped on a traveler the moment they step through a
/// <see cref="SolreignHellZoneEntryComponent"/> gate, recording where to send them back to. Consumed
/// (and removed) by <see cref="SolreignZoneReturnGateComponent"/> on the far side. Runtime-only —
/// never set via prototype/YAML, so it carries no DataFields.
/// </summary>
[RegisterComponent]
public sealed partial class SolreignZoneReturnComponent : Component
{
    public EntityCoordinates ReturnCoordinates;
}
