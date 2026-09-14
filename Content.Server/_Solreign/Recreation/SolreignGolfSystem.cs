using Content.Shared.Popups;
using Content.Shared.Trigger;
using Content.Shared.Weapons.Melee.Components;

namespace Content.Server._Solreign.Recreation;

/// <summary>
/// Mini golf's one genuine addition on top of the zero-C#-gap primitives shipped in
/// docs/specs/2026-07-11-recreation-spec.md: a per-ball stroke counter and a "sunk it in N
/// strokes" popup. The swing itself (club → ball impulse) is entirely existing upstream physics —
/// <c>MeleeWeaponComponent</c> (0-damage, PG) + <c>MeleeThrowOnHitComponent</c> — and the hole
/// detection is entirely existing upstream Trigger primitives (<c>TriggerOnCollide</c> +
/// <c>DeleteOnTrigger</c> + <c>EmitSoundOnTrigger</c> + <c>PopupOnTrigger</c>, all still shipped
/// as-is on <c>SolreignGolfHole</c>). This system only adds the stroke bookkeeping around them.
/// </summary>
public sealed partial class SolreignGolfSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        // MeleeThrowOnHitStartEvent is raised at the TARGET (the ball) by
        // MeleeThrowOnHitSystem.ThrowOnHitHelper right before the throw is applied — the exact hook
        // point for "a swing connected", independent of which club (putter/driver) did it.
        SubscribeLocalEvent<SolreignGolfBallProgressComponent, MeleeThrowOnHitStartEvent>(OnBallWhacked);

        // TriggerEvent is raised at the hole (ent.Owner) with args.User = the entity that collided
        // with it — for SolreignGolfHole's cup fixture, that's always the ball (see
        // XOnTriggerSystem<T> / TriggerOnCollideSystem: "the user is the entity collided with").
        SubscribeLocalEvent<SolreignGolfHoleFeedbackComponent, TriggerEvent>(OnHoleTrigger);
    }

    private void OnBallWhacked(Entity<SolreignGolfBallProgressComponent> ball, ref MeleeThrowOnHitStartEvent args)
    {
        ball.Comp.Strokes = SolreignGolfScoreMath.RecordStroke(ball.Comp.Strokes);

        if (args.User is not { } golfer)
            return;

        _popup.PopupEntity(
            Loc.GetString("solreign-golf-stroke-count", ("count", ball.Comp.Strokes)),
            ball.Owner,
            golfer,
            PopupType.Small);
    }

    private void OnHoleTrigger(Entity<SolreignGolfHoleFeedbackComponent> hole, ref TriggerEvent args)
    {
        if (args.User is not { } ball || !TryComp<SolreignGolfBallProgressComponent>(ball, out var progress))
            return;

        // SOLREIGN LEDGER INTEGRATION POINT (comment only — SeasonLedgerSystem is owned by the
        // Ledger team; do NOT wire from this file without their sign-off):
        //   A best-strokes-per-hole leaderboard entry could land here, keyed the same way as
        //   round-end results (mirrors SolreignHotPotatoSystem.Detonate's identical hookup note).
        //   Stretch goal per the spec doc's "Best-time persistence" note — not built in this pass.

        _popup.PopupEntity(
            Loc.GetString("solreign-golf-hole-sunk-strokes", ("count", progress.Strokes)),
            hole.Owner,
            args.User,
            PopupType.Medium);
    }
}
