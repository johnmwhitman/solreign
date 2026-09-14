using Content.Shared.Actions;
using Content.Shared.Ninja.Systems;

namespace Content.Shared._Solreign.Ninjitsu;

/// <summary>
///     Wires <see cref="NinjitsuGearComponent"/>'s Smoke Vanish action onto the ninja suit the exact
///     same way upstream <c>SharedNinjaSuitSystem</c> wires EMP/recall katana: ensure the action
///     entity on <see cref="MapInitEvent"/>, offer it via <see cref="GetItemActionsEvent"/> only to
///     an actual <see cref="SharedSpaceNinjaSystem.IsNinja"/> wearer. Kept in Shared so the action
///     grant is predicted on equip like the rest of the suit's actions; the ability effect itself
///     (Content.Server._Solreign.Ninjitsu.NinjitsuSystem) is server-authoritative only, matching
///     <c>SolreignMartialArtistComponent</c>'s "no networking needed" reasoning.
/// </summary>
public abstract partial class SharedNinjitsuGearSystem : EntitySystem
{
    [Dependency] private ActionContainerSystem _actionContainer = default!;
    [Dependency] private SharedSpaceNinjaSystem _ninja = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NinjitsuGearComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<NinjitsuGearComponent, GetItemActionsEvent>(OnGetItemActions);
    }

    private void OnMapInit(Entity<NinjitsuGearComponent> ent, ref MapInitEvent args)
    {
        var (uid, comp) = ent;
        _actionContainer.EnsureAction(uid, ref comp.SmokeVanishActionEntity, comp.SmokeVanishAction);
        Dirty(uid, comp);
    }

    private void OnGetItemActions(Entity<NinjitsuGearComponent> ent, ref GetItemActionsEvent args)
    {
        if (!_ninja.IsNinja(args.User))
            return;

        args.AddAction(ref ent.Comp.SmokeVanishActionEntity, ent.Comp.SmokeVanishAction);
    }
}
