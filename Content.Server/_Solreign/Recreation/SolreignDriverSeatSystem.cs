using Content.Shared.Buckle.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;

namespace Content.Server._Solreign.Recreation;

/// <summary>
/// Wires <see cref="SolreignDriverSeatComponent"/> to <c>SharedMoverController.SetRelay</c> —
/// gap #1 in docs/specs/2026-07-11-recreation-spec.md. Almost line-for-line
/// <c>CardboardBoxSystem.OnEntInserted</c>/<c>OnEntRemoved</c>, just triggered by buckling instead
/// of container insertion (same shape as <c>IgniteOnBuckleSystem</c>'s Strapped/Unstrapped hookup).
///
/// Server-only by design, matching this repo's existing buckle-effect precedent
/// (<c>IgniteOnBuckleSystem</c>, also Content.Server-only) rather than the fully-Shared/predicted
/// pattern <c>PilotedClothingSystem</c> uses for mech-style piloting. That means kart steering is
/// server-authoritative, not client-predicted — acceptable for a recreation prop, but a candidate
/// to move to Shared in a follow-up PR if kart input ever feels laggy in practice.
///
/// Kart-deleted-while-occupied cleanup: verified (by reading
/// <c>SharedMoverController.OnTargetRelayShutdown</c>, subscribed to
/// <c>MovementRelayTargetComponent, ComponentShutdown</c>) that the relay target's own shutdown
/// already zeroes the driver's move input and strips their stale <c>RelayInputMoverComponent</c>
/// generically — no bespoke relay-teardown logic is required for that case. This system still adds
/// a defensive shutdown handler purely to null out its own cached <see cref="SolreignDriverSeatComponent.Driver"/>
/// bookkeeping (belt-and-braces, mirrors CardboardBoxSystem.OnEntRemoved's same-entity guard).
/// </summary>
public sealed partial class SolreignDriverSeatSystem : EntitySystem
{
    [Dependency] private SharedMoverController _mover = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SolreignDriverSeatComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<SolreignDriverSeatComponent, UnstrappedEvent>(OnUnstrapped);
        SubscribeLocalEvent<SolreignDriverSeatComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStrapped(Entity<SolreignDriverSeatComponent> seat, ref StrappedEvent args)
    {
        // A driver seat only has one buckle slot in practice (SolreignGoKart's Strap has no
        // MaxBuckled override beyond the default single occupant), but guard anyway: don't clobber
        // an existing relay if something unexpected buckles a second entity in.
        if (seat.Comp.Driver != null)
            return;

        _mover.SetRelay(args.Buckle.Owner, seat.Owner);
        seat.Comp.Driver = args.Buckle.Owner;
    }

    private void OnUnstrapped(Entity<SolreignDriverSeatComponent> seat, ref UnstrappedEvent args)
    {
        if (args.Buckle.Owner != seat.Comp.Driver)
            return;

        RemCompDeferred<RelayInputMoverComponent>(seat.Comp.Driver.Value);
        seat.Comp.Driver = null;
    }

    private void OnShutdown(Entity<SolreignDriverSeatComponent> seat, ref ComponentShutdown args)
    {
        // Defensive only — see class remarks. SharedMoverController's own relay-target shutdown
        // handling already tears down the driver's relay component generically when the vehicle is
        // deleted; this just keeps our cached Driver reference from dangling in the meantime.
        seat.Comp.Driver = null;
    }
}
