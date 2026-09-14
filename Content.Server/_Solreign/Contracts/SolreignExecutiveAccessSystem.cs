using Content.Server.VendingMachines;
using Content.Shared._Solreign.SeasonLedger;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Content.Shared.VendingMachines.Components;

namespace Content.Server._Solreign.Contracts;

/// <summary>
///     Rank-gates the Executive Vendor's UI (spec M4, "the ladder buys things"): cancels
///     <see cref="ActivatableUIOpenAttemptEvent"/> for a sub-rank asset, the same shape as the upstream
///     access-gate template (<c>ActivatableUIRequiresAccessSystem</c>, studied before writing this) except
///     it checks career rank instead of an ID-card access tag.
///
///     Canceling the OPEN ATTEMPT — not per-item ejection — means a sub-rank asset never even sees the
///     vendor's shelf; there is no separate purchase check to bolt onto
///     <c>SharedVendingMachineSystem.AuthorizedVend</c>/<c>TryEjectVendorItem</c>, so this needed no edits
///     to upstream vending code and no per-item UI surgery. It also composes cleanly with upstream's own
///     Broken-state cancel on the same event (<c>SharedVendingMachineSystem.OnActivatableUIOpenAttempt</c>):
///     that subscribes <c>(VendingMachineComponent, ActivatableUIOpenAttemptEvent)</c>; this system
///     subscribes the DISTINCT pair <c>(SolreignExecutiveAccessComponent, ActivatableUIOpenAttemptEvent)</c>
///     on the same entity — grepped first, never subscribed before repo-wide (ANALYZER LAW: one
///     (component, event) subscription pair repo-wide).
///
///     Server-only by necessity: <see cref="SeasonTitleComponent"/> is deliberately NOT networked
///     (server-authoritative — see its own doc comment), so the rank check can only run here. This is not a
///     prediction gap: <c>ActivatableUISystem</c>'s client-side replay of the same open attempt has no
///     Solreign subscriber and so has no opinion either way, but <c>SharedUserInterfaceSystem</c>'s BUI open
///     state is server-authoritative/networked — the client never actually sees the vendor's window unless
///     the server's <see cref="Robust.Shared.GameObjects.EntitySystem"/> pass here lets it through.
/// </summary>
public sealed partial class SolreignExecutiveAccessSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private VendingMachineSystem _vending = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SolreignExecutiveAccessComponent, ActivatableUIOpenAttemptEvent>(OnUIOpenAttempt);
    }

    private void OnUIOpenAttempt(Entity<SolreignExecutiveAccessComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        var rankIndex = TryComp<SeasonTitleComponent>(args.User, out var title) ? title.RankIndex : 0;
        if (ContractRules.MeetsRankGate(rankIndex, ent.Comp.MinRankIndex))
            return;

        args.Cancel();

        if (args.Silent)
            return;

        _popup.PopupEntity(Loc.GetString(ent.Comp.PopupMessage), ent, args.User);

        // Cosmetic parity with a denied vend (buzz + deny sprite flash), reusing the upstream Deny idiom
        // rather than inventing a new one. No-op if this rank gate is ever put on a non-vending UI.
        if (TryComp<VendingMachineComponent>(ent.Owner, out var vending))
            _vending.Deny((ent.Owner, vending), args.User);
    }
}
