using Content.Shared.Examine;
using Content.Shared.Hands;
using Content.Shared.Popups;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Shared._Solreign.HotPotato;

/// <summary>
///     Shared (predicted) half of the Mandatory Team-Building Exercise: arms the fuse on first
///     pickup and cancels every attempt to remove an armed exercise from its container outside a
///     sanctioned collision hand-off. The server half (Content.Server._Solreign.HotPotato) owns the
///     fuse clock, the beeps, collision transfers and the detonation.
///
///     Patterned on upstream SharedHotPotatoSystem (no-drop via ContainerGettingRemovedAttemptEvent
///     + a transient CanTransfer gate) and TriggerSystem.Timer (fuse bookkeeping).
/// </summary>
public abstract partial class SharedSolreignHotPotatoSystem : EntitySystem
{
    [Dependency] protected IGameTiming Timing = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SolreignHotPotatoComponent, GotEquippedHandEvent>(OnGotEquippedHand);
        SubscribeLocalEvent<SolreignHotPotatoComponent, ContainerGettingRemovedAttemptEvent>(OnRemoveAttempt);
        SubscribeLocalEvent<SolreignHotPotatoComponent, ExaminedEvent>(OnExamined);
    }

    /// <summary>
    /// First pickup arms the exercise. The fuse is fixed and never resets — hand-offs merely
    /// change who is holding the responsibility when it matures.
    /// </summary>
    private void OnGotEquippedHand(Entity<SolreignHotPotatoComponent> ent, ref GotEquippedHandEvent args)
    {
        // Server hook for holder bookkeeping. Runs on EVERY equip (including hand-offs);
        // it lives on this subscription because a system may only subscribe once per
        // (component, event) pair and the server half shares this instance.
        OnPotatoEquippedHand(ent, args.User);

        if (ent.Comp.Armed)
            return;

        ent.Comp.Armed = true;
        ent.Comp.DetonateAt = Timing.CurTime + ent.Comp.FuseDuration;
        ent.Comp.NextBeep = Timing.CurTime;
        ent.Comp.NextTransferAllowed = Timing.CurTime;
        Dirty(ent);

        _popup.PopupPredicted(Loc.GetString("solreign-hot-potato-armed"), ent.Owner, args.User, PopupType.LargeCaution);

        // Server-only voice hook (empty here, same virtual-hook idiom as OnPotatoEquippedHand just
        // above): playing a station-wide Providence line from Shared would double-fire under client
        // prediction, so the real work happens in the server override.
        OnPotatoArmed(ent.Owner);
    }

    /// <summary>
    /// Called whenever the exercise lands in someone's hand. The server override tracks the
    /// current holder for collision hand-offs.
    /// </summary>
    protected virtual void OnPotatoEquippedHand(Entity<SolreignHotPotatoComponent> ent, EntityUid user)
    {
    }

    /// <summary>
    /// Called exactly once, the moment an exercise transitions from unarmed to armed (guarded by the
    /// early-return above, never on hand-offs). Server override plays the Providence hot_potato line.
    /// </summary>
    protected virtual void OnPotatoArmed(EntityUid potato)
    {
    }

    /// <summary>
    /// An armed exercise cannot be dropped, thrown, stripped or stored. The only way out is the
    /// server-side collision hand-off, which briefly flips <see cref="SolreignHotPotatoComponent.CanTransfer"/>.
    /// </summary>
    private void OnRemoveAttempt(Entity<SolreignHotPotatoComponent> ent, ref ContainerGettingRemovedAttemptEvent args)
    {
        if (!Timing.ApplyingState && ent.Comp.Armed && !ent.Comp.CanTransfer)
            args.Cancel();
    }

    private void OnExamined(Entity<SolreignHotPotatoComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (!ent.Comp.Armed)
        {
            args.PushText(Loc.GetString("solreign-hot-potato-examine-idle"));
            return;
        }

        var remaining = HotPotatoFuseMath.Remaining(Timing.CurTime, ent.Comp.DetonateAt);
        args.PushText(Loc.GetString("solreign-hot-potato-examine-armed", ("seconds", (int)remaining.TotalSeconds)));
    }
}
