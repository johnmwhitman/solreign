using Content.Shared.Examine;
using Content.Shared.Inventory.Events;
using Content.Shared.NightVision;
using Content.Shared.Overlays;
using Content.Shared.Popups;
using Content.Shared.Power.EntitySystems;
using Content.Shared.PowerCell;
using Content.Shared.PowerCell.Components;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._Solreign.NightVision;

/// <summary>
///     Shared half of the after-hours compliance lenses: bridges the upstream toggleable
///     <see cref="NightVisionComponent"/> (relayed overlay + <c>ToggleNightVisionEvent</c> action)
///     to the upstream power-cell idiom (<c>PowerCellSlot</c> + <c>PowerCellDraw</c>).
///
///     Wiring follows upstream <c>ToggleCellDrawSystem</c> (Content.Shared/PowerCell), which does
///     the same bridge for ItemToggle items: react to <see cref="PowerCellSlotEmptyEvent"/> for the
///     auto-off, and keep <c>PowerCellDrawComponent.Enabled</c> in lockstep with the toggle.
///     The server half owns the periodic sync; the client half owns overlay cosmetics.
/// </summary>
public abstract partial class SharedSolreignNightVisionSystem : EntitySystem
{
    [Dependency] protected IGameTiming Timing = default!;
    [Dependency] protected INetManager Net = default!;
    [Dependency] protected SharedNightVisionSystem NightVision = default!;
    [Dependency] protected PowerCellSystem PowerCell = default!;
    [Dependency] protected SharedBatterySystem Battery = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SolreignPoweredNightVisionComponent, PowerCellSlotEmptyEvent>(OnCellEmpty);
        SubscribeLocalEvent<SolreignPoweredNightVisionComponent, GotUnequippedEvent>(OnGotUnequipped);
        SubscribeLocalEvent<SolreignPoweredNightVisionComponent, ExaminedEvent>(OnExamined);
    }

    /// <summary>
    ///     Auto-off the instant the cell runs dry or is yanked out of the slot
    ///     (upstream raises <see cref="PowerCellSlotEmptyEvent"/> for both).
    /// </summary>
    private void OnCellEmpty(Entity<SolreignPoweredNightVisionComponent> ent, ref PowerCellSlotEmptyEvent args)
    {
        if (Timing.ApplyingState)
            return;

        ShutDown(ent.Owner, popup: true);
    }

    /// <summary>
    ///     Taking the lenses off switches them off, so a pocketed pair never drains its cell.
    /// </summary>
    private void OnGotUnequipped(Entity<SolreignPoweredNightVisionComponent> ent, ref GotUnequippedEvent args)
    {
        if (Timing.ApplyingState || !ent.Comp.DisableWhenRemoved)
            return;

        NightVision.SetEnabled(ent.Owner, false, args.EquipTarget);
        PowerCell.SetDrawEnabled(ent.Owner, false);
    }

    /// <summary>
    ///     Examine reports the estimated remaining runtime from the drain math, alongside the
    ///     charge percentage upstream <c>PowerCellSlot</c> examine already provides.
    /// </summary>
    private void OnExamined(Entity<SolreignPoweredNightVisionComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || !TryComp<PowerCellDrawComponent>(ent, out var draw))
            return;

        if (!PowerCell.TryGetBatteryFromSlot(ent.Owner, out var battery))
        {
            args.PushMarkup(Loc.GetString("solreign-nvg-examine-no-cell"));
            return;
        }

        var charge = Battery.GetCharge(battery.Value.AsNullable());
        var seconds = NvgPowerMath.RuntimeSeconds(charge, draw.DrawRate);
        if (float.IsPositiveInfinity(seconds))
            return;

        var minutes = (int) MathF.Ceiling(seconds / 60f);
        args.PushMarkup(Loc.GetString("solreign-nvg-examine-runtime", ("minutes", minutes)));
    }

    /// <summary>
    ///     Turns the lenses off and stops the cell draw. The wearer (container owner) is passed
    ///     as the viewer so their overlay refreshes; the popup only fires server-side.
    /// </summary>
    protected void ShutDown(Entity<NightVisionComponent?> ent, bool popup)
    {
        if (!Resolve(ent, ref ent.Comp, false) || !ent.Comp.Enabled)
            return;

        NightVision.SetEnabled(ent, false, FindWearer(ent.Owner));
        PowerCell.SetDrawEnabled(ent.Owner, false);

        if (popup && Net.IsServer)
            _popup.PopupEntity(Loc.GetString("solreign-nvg-cell-dead"), ent.Owner, PopupType.MediumCaution);
    }

    /// <summary>
    ///     Whoever's inventory the lenses currently sit in, if anyone's.
    /// </summary>
    protected EntityUid? FindWearer(EntityUid lenses)
    {
        return _container.TryGetContainingContainer((lenses, null, null), out var container)
            ? container.Owner
            : null;
    }
}
