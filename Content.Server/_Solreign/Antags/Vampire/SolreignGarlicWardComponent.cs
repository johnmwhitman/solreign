namespace Content.Server._Solreign.Antags.Vampire;

/// <summary>
///     Marks an item (garlic bulb, garlic garland, garlic bread if the chef is feeling strategic) as
///     a ward: within <see cref="Radius"/> of one, a Nocturnal Acquisitions Specialist cannot feed,
///     accrues thirst 50% faster, and sneezes. Comedy, not damage — the kitchen grows the counter
///     round-start (anti-grief rule 4, spec §4.5).
///
///     TODO(build): apply to grown garlic produce via prototype; proximity check runs on the vampire
///     tick in <see cref="SolreignVampireSystem"/> (range query, coarse interval).
/// </summary>
[RegisterComponent]
public sealed partial class SolreignGarlicWardComponent : Component
{
    /// <summary>Ward radius in tiles.</summary>
    [DataField]
    public float Radius = 2f;
}
