namespace Content.Server._Solreign.MartialArts;

/// <summary>
///     Marks a belt as a certified de-escalation instrument: it teaches the combo core's judo
///     moveset WHILE WORN. This is deliberately the opposite grant idiom from
///     <see cref="SolreignCarpScrollComponent"/> — the Carp Scroll is used once and the lesson is
///     permanent (<see cref="SolreignMartialArtistComponent"/> never goes away); this belt grants
///     for as long as it stays buckled on and nothing more. Take it off (or get stripped) and the
///     moveset goes with it.
///
///     Purely a marker on the CLOTHING entity — <see cref="SolreignJudoBeltSystem"/> listens for
///     ClothingGotEquippedEvent/ClothingGotUnequippedEvent on this component and adds/removes
///     <see cref="SolreignJudoBeltWearerComponent"/> (the "currently trained" marker + all combo
///     tunables) on the wearer, the same equip-driven grant shape as upstream's
///     AntiGravityClothingComponent / MagbootsComponent.
/// </summary>
[RegisterComponent, Access(typeof(SolreignJudoBeltSystem))]
public sealed partial class SolreignJudoBeltComponent : Component;
