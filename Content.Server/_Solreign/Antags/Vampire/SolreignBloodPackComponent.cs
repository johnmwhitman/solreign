namespace Content.Server._Solreign.Antags.Vampire;

/// <summary>
///     Marks an item as a Medbay blood pack (spec §4.2: "blood packs or a willing donor" — no
///     attack-shaped feeding, ever, anti-grief rule 1). A Nocturnal Acquisitions Specialist uses one on
///     THEMSELVES (<c>AfterInteractEvent</c>, do-after) to reduce thirst; the pack is consumed on
///     success. Gated by <see cref="VampireThirstMath.CanFeed"/> exactly like the donor verb — garlic
///     and chapel block a pack sip too (spec §4.3: "regardless of thirst").
///
///     TODO(build): attach to an actual blood-pack item entity/prototype (sprite factory, task #9) —
///     Resources/Textures/Objects/Specific/Medical/medical.rsi already ships the art upstream. This
///     component only needs to exist on SOME solution-free consumable item for the interaction to work;
///     it deliberately does not depend on the real chemistry/solution-transfer machinery to reduce the
///     surface area this pass has to get right blind (no dotnet build available to verify).
/// </summary>
[RegisterComponent, Access(typeof(SolreignVampireSystem))]
public sealed partial class SolreignBloodPackComponent : Component
{
    /// <summary>Thirst removed on a successful sip. Defaults to the vampire's own DrinkAmount at use time if unset.</summary>
    [DataField]
    public double? DrinkOverride;
}
