namespace Content.Server._Solreign.Wardrobe;

/// <summary>
///     Pure inventory-template policy for the "Solreign Wardrobe" admin verb
///     (<see cref="SolreignWardrobeSystem"/>). Kept free of engine types so it is unit-testable,
///     mirroring how <c>RankRules</c> backs <c>SeasonLedgerSystem</c>.
/// </summary>
public static class WardrobeRules
{
    /// <summary>
    ///     The full-visual ghost template defined in
    ///     <c>Resources/Prototypes/_Solreign/InventoryTemplates/wardrobe_inventory_template.yml</c>.
    /// </summary>
    public const string WardrobeTemplateId = "solreignWardrobe";

    /// <summary>
    ///     Inventory templates known to be sparse ghost templates that the wardrobe should widen.
    ///     The stock admin-ghost template only carries back/id/head/mask ("for drip reasons").
    /// </summary>
    private static readonly string[] UpgradableTemplates = { "aghost" };

    /// <summary>
    ///     Decides what template a ghost should be switched to before the outfit picker opens.
    /// </summary>
    /// <param name="currentTemplateId">The ghost's current inventory template prototype id.</param>
    /// <returns>
    ///     The template to switch to, or null when the current template must be left alone:
    ///     it is already the wardrobe, already a full template (e.g. <c>human</c>), or something
    ///     unrecognized that we refuse to clobber. Never downgrades.
    /// </returns>
    public static string? ResolveTemplateUpgrade(string currentTemplateId)
    {
        if (currentTemplateId == WardrobeTemplateId)
            return null;

        foreach (var upgradable in UpgradableTemplates)
        {
            if (currentTemplateId == upgradable)
                return WardrobeTemplateId;
        }

        return null;
    }
}
