namespace Content.Server._Solreign.MartialArts;

/// <summary>
///     Marks an apron as a certified kitchen-brigade instrument: it teaches the combo core's Chef
///     CQC moveset WHILE WORN. Same wear-conditioned grant idiom as
///     <see cref="SolreignJudoBeltComponent"/> (and the opposite of
///     <see cref="SolreignCarpScrollComponent"/>'s one-time permanent lesson) — buckle it on and
///     you're certified, take it off and you're not.
///
///     Purely a marker on the CLOTHING entity — <see cref="SolreignChefApronSystem"/> listens for
///     ClothingGotEquippedEvent/ClothingGotUnequippedEvent on this component and adds/removes
///     <see cref="SolreignChefApronWearerComponent"/> (the "currently trained" marker + all combo
///     tunables) on the wearer.
/// </summary>
[RegisterComponent, Access(typeof(SolreignChefApronSystem))]
public sealed partial class SolreignChefApronComponent : Component;
