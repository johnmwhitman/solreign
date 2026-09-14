namespace Content.Server._Solreign.Antags.Vampire;

/// <summary>
///     Marks an entity as chapel ground (spec §4.5 rule 4: "round-start-available counters needing zero
///     antag knowledge"). A Nocturnal Acquisitions Specialist within <see cref="Radius"/> of one is
///     "in the chapel": powers off, feeding blocked, pod regen disabled (<see cref="VampireThirstMath"/>
///     already encodes the resulting gates — this component only supplies the detection signal).
///
///     Same entity-based proximity idiom as <see cref="SolreignGarlicWardComponent"/>, deliberately —
///     the spec's own build-pass estimate calls out "chapel via area/tag lookup" as a coarse detection,
///     and this fork has no existing tile/room-area API to key off (verified: no prior "Chapel" tag or
///     area system anywhere upstream). TODO(build): place one of these on (or near) the chapel's altar
///     entity in the chapel map/prototype once the sprite factory reaches that room; until then it's
///     placed by an admin/mapper wherever the chapel's floor plan needs the marker.
/// </summary>
[RegisterComponent]
public sealed partial class SolreignChapelGroundComponent : Component
{
    /// <summary>Detection radius in tiles.</summary>
    [DataField]
    public float Radius = 6f;
}
