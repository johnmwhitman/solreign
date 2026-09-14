namespace Content.Server._Solreign.Antags.Vampire;

/// <summary>
///     Marks a container entity as an Executive Recharge Pod (coffin, walnut veneer, barcode).
///     A resting Nocturnal Acquisitions Specialist recovers thirst and heals faster inside — but the
///     pod is a normal, findable, weldable structure: the vampire's power has an address, and
///     removing it is the crew's legitimate nonlethal counter (anti-grief rule 3, spec §4.5).
///
///     TODO(build): attach to a Solreign coffin entity prototype (upstream EntityStorage base) and
///     detect occupancy via container insert/remove events in <see cref="SolreignVampireSystem"/>.
/// </summary>
[RegisterComponent]
public sealed partial class SolreignCoffinComponent : Component
{
    /// <summary>Multiplier applied to the occupant's CoffinRecoveryPerMinute (pod quality tiers later).</summary>
    [DataField]
    public double RecoveryMultiplier = 1.0d;
}
