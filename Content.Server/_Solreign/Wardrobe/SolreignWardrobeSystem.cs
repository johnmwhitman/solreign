using Content.Server.Administration.UI;
using Content.Server.EUI;
using Content.Shared.Database;
using Content.Shared.Ghost.Components;
using Content.Shared.Inventory;
using Content.Shared.Verbs;
using Robust.Server.Console;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._Solreign.Wardrobe;

/// <summary>
///     "Solreign Wardrobe": ghost dress-up (LJ's direct request), as an admin verb.
///
///     The verb appears on any ghost that has an inventory — the stock <c>AdminObserver</c>,
///     <c>SolreignAdminGhost</c> and <c>SolreignGuestGhost</c> — and is gated behind the same
///     permission as the <c>setoutfit</c> command it fronts, mirroring the upstream
///     "Set Outfit" debug verb in <c>AdminVerbSystem</c>. On use it widens sparse ghost
///     inventory templates to the full-visual <c>solreignWardrobe</c> template (policy in
///     <see cref="WardrobeRules"/>), then opens the stock <see cref="SetOutfitEui"/> so admins
///     pick from the existing startingGear prototypes. Clothing then renders through upstream
///     <c>ClientClothingSystem</c> — no custom sprite code.
///
///     Plain observers (no inventory) never see the verb: the client-side clothing visuals
///     require an <c>InventorySlots</c> component that cannot be added server-side at runtime,
///     so dressable guests must be spawned as <c>SolreignGuestGhost</c> instead.
/// </summary>
public sealed partial class SolreignWardrobeSystem : EntitySystem
{
    [Dependency] private IConGroupController _groupController = default!;
    [Dependency] private EuiManager _euiManager = default!;
    [Dependency] private InventorySystem _inventory = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GhostComponent, GetVerbsEvent<Verb>>(OnGetGhostVerbs);
    }

    private void OnGetGhostVerbs(Entity<GhostComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!TryComp(args.User, out ActorComponent? actor))
            return;

        var player = actor.PlayerSession;

        // Same gate as the upstream "Set Outfit" debug verb: this verb fronts setoutfit,
        // so it matches that command's permissions exactly.
        if (!_groupController.CanCommand(player, "setoutfit"))
            return;

        // Ghosts without an inventory (plain observers) cannot wear anything; see class remarks.
        if (!TryComp(ent.Owner, out InventoryComponent? inventory))
            return;

        var target = ent.Owner;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("solreign-wardrobe-verb-text"),
            Message = Loc.GetString("solreign-wardrobe-verb-message"),
            Category = VerbCategory.Admin,
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/outfit.svg.192dpi.png")),
            Act = () =>
            {
                if (TerminatingOrDeleted(target))
                    return;

                // Widen sparse ghost templates (the stock aghost one: back/id/head/mask)
                // to the full drip set before offering outfits.
                if (WardrobeRules.ResolveTemplateUpgrade(inventory.TemplateId.Id) is { } upgrade)
                    _inventory.SetTemplateId((target, inventory), upgrade);

                _euiManager.OpenEui(new SetOutfitEui(GetNetEntity(target)), player);
            },
            Impact = LogImpact.Medium,
        });
    }
}
