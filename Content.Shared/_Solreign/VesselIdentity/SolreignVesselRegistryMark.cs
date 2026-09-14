namespace Content.Shared._Solreign.VesselIdentity;

/// <summary>
///     Defines the cosmetic registry mark tier earned by a crew vessel based on mission and salvage milestones (SR-W-043).
/// </summary>
public enum SolreignVesselRegistryMark : byte
{
    /// <summary>
    ///     Base registry level. Unmarked standard vessel hull.
    /// </summary>
    Unmarked = 0,

    /// <summary>
    ///     Tier 1: Bronze stripe commendation (3+ missions or 10k+ salvage).
    /// </summary>
    BronzeStripe = 1,

    /// <summary>
    ///     Tier 2: Silver star escort mark (10+ missions or 50k+ salvage).
    /// </summary>
    SilverInsignia = 2,

    /// <summary>
    ///     Tier 3: Gold emblem flagship insignia (25+ missions or 150k+ salvage).
    /// </summary>
    GoldEmblem = 3,

    /// <summary>
    ///     Tier 4: Veteran fleet pennant mark (50+ missions or 500k+ salvage).
    /// </summary>
    VeteranPennant = 4,

    /// <summary>
    ///     Tier 5: Solreign prime corporate chevron mark (100+ missions or 1M+ salvage).
    /// </summary>
    CorporateChevrons = 5
}
