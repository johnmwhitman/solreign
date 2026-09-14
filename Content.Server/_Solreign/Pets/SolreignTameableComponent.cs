using Content.Shared.Whitelist;

namespace Content.Server._Solreign.Pets;

/// <summary>
///     Marks a critter as tameable (Solreign Pets v1 / SR-W-047): feeding it a whitelisted food item registers the
///     feeder as its owner and points its follow-AI at them. Supports follow/stay orders, release, grooming/cleaning,
///     and cosmetic Season Ledger stamps. Pure data — all decisions live in <see cref="TamingRules"/> and <see cref="SolreignTameableSystem"/>.
/// </summary>
[RegisterComponent, Access(typeof(SolreignTameableSystem))]
public sealed partial class SolreignTameableComponent : Component
{
    /// <summary>
    ///     What counts as "food" for taming purposes. A null whitelist never passes.
    /// </summary>
    [DataField("whitelist")]
    public EntityWhitelist? FoodWhitelist;

    /// <summary>
    ///     Whether the fed item is deleted (eaten) on a successful or already-bonded feed.
    /// </summary>
    [DataField]
    public bool ConsumeFood = true;

    /// <summary>
    ///     The critter's current owner, if any. Runtime state.
    /// </summary>
    [ViewVariables]
    public EntityUid? TamedBy;

    /// <summary>
    ///     Whether the pet is currently actively following its owner.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool IsFollowing = true;

    /// <summary>
    ///     Whether the pet requires grooming/cleaning.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool IsDirty = false;

    /// <summary>
    ///     Optional whitelist for cleaning/grooming items (e.g. soap, towel, rag).
    /// </summary>
    [DataField("cleanerWhitelist")]
    public EntityWhitelist? CleanerWhitelist;

    /// <summary>
    ///     Cosmetic ledger stamp IDs registered for this pet.
    /// </summary>
    [DataField]
    public HashSet<string> LedgerStamps = new();

    // ---- Loc string keys ----

    [DataField]
    public string TameSuccessPopup = "solreign-pet-tame-success";

    [DataField]
    public string RetamedPopup = "solreign-pet-tame-retamed";

    [DataField]
    public string AlreadyBondedPopup = "solreign-pet-tame-already-bonded";

    [DataField]
    public string ReleaseSuccessPopup = "solreign-pet-release-success";

    [DataField]
    public string ToggleFollowPopup = "solreign-pet-toggle-follow";

    [DataField]
    public string ToggleStayPopup = "solreign-pet-toggle-stay";

    [DataField]
    public string CleanupSuccessPopup = "solreign-pet-cleanup-success";

    [DataField]
    public string AlreadyCleanPopup = "solreign-pet-already-clean";

    [DataField]
    public string LedgerStampPopup = "solreign-pet-ledger-stamp";

    [DataField]
    public string ExamineUntamed = "solreign-pet-examine-untamed";

    [DataField]
    public string ExamineTamed = "solreign-pet-examine-tamed";

    [DataField]
    public string ExamineLedgerFlavor = "solreign-pet-examine-ledger-flavor";
}
