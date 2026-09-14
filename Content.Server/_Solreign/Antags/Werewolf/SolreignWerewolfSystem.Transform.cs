using Content.Server.Polymorph.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Prototypes;

namespace Content.Server._Solreign.Antags.Werewolf;

/// <summary>
///     Presentation + interaction layer for the fur-event cycle (ZombieSystem.Transform.cs idiom, spec
///     §2.1/§2.2): the polymorph body swap, the nonlethal maul, and Moon-Touched cosmetics. The decision
///     layer (when to transform/revert) stays in the pure <see cref="WerewolfStateMachine"/> and the
///     main system file; this partial only reacts to what that layer already decided.
/// </summary>
public sealed partial class SolreignWerewolfSystem
{
    [Dependency] private PolymorphSystem _polymorph = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    /// <summary>
    ///     → Stirring: warning phase. No body-swap yet — just fair notice (spec §3.2: "colleagues get a
    ///     fair chance to react").
    /// </summary>
    private void OnEnterStirring(EntityUid uid, SolreignWerewolfComponent wolf)
    {
        _popup.PopupEntity(Loc.GetString("solreign-werewolf-stirring-popup"), uid, uid, PopupType.Medium);
    }

    /// <summary>
    ///     → Transformed: hand the body-swap to upstream <see cref="PolymorphSystem"/> (spec §2.2: "we
    ///     will not write a bespoke body-swap"). Resolves the prototype defensively — <c>TryIndex</c>,
    ///     never <c>Index</c> — because the wolf-form entity/polymorph YAML is a separate sprite-factory
    ///     build-pass item (task #9) that may not have landed yet. Missing prototype or a declined
    ///     polymorph (repeated-morph guard, cooldown) both fail safe: the moon window is a cosmetic
    ///     no-op for this episode instead of a crash. Either way the state machine keeps running on the
    ///     clock alone (see the AllEntityQuery comment on <see cref="Update"/>).
    ///
    ///     Phase-2 Track A1 minimum bar (roadmap docs/plans/2026-07-11-ROADMAP-PHASE2.md, Miyamoto's
    ///     dissent §5: "silent failure is never acceptable, but the fix is a diegetic message, not new
    ///     systems"): the prototype miss below is now ALSO a loud, in-character popup, not just a log
    ///     line — a drafted employee who presses transform and gets nothing at least gets told why,
    ///     even if a future edit ever breaks <see cref="SolreignWerewolfComponent.WolfPolymorphPrototype"/>
    ///     again. The prototype itself is fixed this pass
    ///     (Resources/Prototypes/_Solreign/Polymorphs/werewolf_polymorph.yml), so this branch is a
    ///     defense-in-depth backstop, not the expected path.
    /// </summary>
    private void OnEnterTransformed(EntityUid uid, SolreignWerewolfComponent wolf)
    {
        if (!_proto.TryIndex(wolf.WolfPolymorphPrototype, out var polyProto))
        {
            Log.Warning($"Werewolf polymorph prototype '{wolf.WolfPolymorphPrototype}' not found; " +
                         $"{ToPrettyString(uid)} stays human for this Full Moon Window (spec §3.2 fail-safe).");
            _popup.PopupEntity(Loc.GetString("solreign-werewolf-transform-failed-popup"), uid, uid, PopupType.MediumCaution);
            return;
        }

        var child = _polymorph.PolymorphEntity(uid, polyProto.Configuration);
        if (child is not { } wolfForm)
            return;

        wolf.WolfForm = wolfForm;

        // Programmatic marker (NOT YAML) so the maul handler below can find its way from the wolf body
        // back to the human's SolreignWerewolfComponent — see SolreignWolfFormComponent's doc comment.
        var marker = EnsureComp<SolreignWolfFormComponent>(wolfForm);
        marker.HumanForm = uid;

        _popup.PopupEntity(Loc.GetString("solreign-werewolf-transform-popup"), wolfForm, wolfForm, PopupType.LargeCaution);
    }

    /// <summary>
    ///     → Waning: the visible, vulnerable revert window (spec §3.2). Upstream Polymorph has no
    ///     "gradual" revert — the swap itself is instantaneous — so the crawling/vulnerable FEEL of
    ///     Waning is produced here, on the just-restored human body, as a knockdown for the whole
    ///     Waning duration. Reached whether the episode is curing (invariant: cure never skips this
    ///     window, spec §3.5) or not; either way the wolf form reverts right now.
    /// </summary>
    private void OnEnterWaning(EntityUid uid, SolreignWerewolfComponent wolf)
    {
        if (wolf.WolfForm is { } wolfForm)
        {
            _polymorph.Revert(wolfForm);
            wolf.WolfForm = null;
        }

        _stun.TryKnockdown(uid, TimeSpan.FromSeconds(wolf.WaningSeconds), refresh: true, autoStand: true);
    }

    /// <summary>
    ///     → Dormant: reached either as a near-miss (Stirring→Dormant, the moon closed before
    ///     transforming — <see cref="SolreignWerewolfComponent.WolfForm"/> was never set) or after
    ///     Waning already reverted the polymorph above. Nothing left to clean up in either case.
    /// </summary>
    private void OnEnterDormant(EntityUid uid, SolreignWerewolfComponent wolf)
    {
    }

    /// <summary>
    ///     The maul (spec §3.3). Directed on the WOLF body (upstream convention: <c>MeleeHitEvent</c> is
    ///     raised on the melee weapon entity, which for a natural attack like this is the mob itself —
    ///     same idiom as <c>ZombieSystem.OnMeleeHit</c>). Deals NO damage of its own (anti-grief rule 1,
    ///     spec §3.5) — whatever harmless base damage the wolf-form prototype configures passes through
    ///     untouched; this handler only ever ADDS the knockdown + Moon-Touched mark on top.
    /// </summary>
    private void OnMeleeHit(Entity<SolreignWolfFormComponent> wolf, ref MeleeHitEvent args)
    {
        if (!args.IsHit)
            return;

        if (!TryComp(wolf.Comp.HumanForm, out SolreignWerewolfComponent? humanWolf))
            return;

        var now = _timing.CurTime;

        foreach (var hitUid in args.HitEntities)
        {
            if (hitUid == args.User)
                continue;

            if (!TryComp<MobStateComponent>(hitUid, out var mobState) || !_mobState.IsAlive(hitUid, mobState))
                continue;

            var alreadyTouched = HasComp<SolreignMoonTouchedComponent>(hitUid);
            if (!WerewolfMaulRules.CanMaul(now, humanWolf.NextMaulAllowed, alreadyTouched))
                continue;

            _stun.TryKnockdown(hitUid, TimeSpan.FromSeconds(humanWolf.MaulKnockdownSeconds), refresh: true, autoStand: true);
            ApplyMoonTouched(hitUid, humanWolf, now);

            // Gates the WHOLE swing, not just this target (anti-grief rule 3: no corridor bowling) —
            // a wolf that clips two colleagues in one wide swing still only banks one knockdown's
            // worth of cooldown before it can maul again.
            humanWolf.NextMaulAllowed = WerewolfMaulRules.NextMaulAllowedAt(now, humanWolf.MaulCooldownSeconds);
        }
    }

    /// <summary>Applies (or refreshes) the Moon-Touched mark — infection-lite, spec §3.3.</summary>
    private void ApplyMoonTouched(EntityUid target, SolreignWerewolfComponent humanWolf, TimeSpan now)
    {
        var mark = EnsureComp<SolreignMoonTouchedComponent>(target);
        mark.ExpiresAt = now + TimeSpan.FromSeconds(humanWolf.MoonTouchedSeconds);
        _movementSpeed.RefreshMovementSpeedModifiers(target);
        _popup.PopupEntity(Loc.GetString("solreign-werewolf-moon-touched-popup"), target, target, PopupType.MediumCaution);
    }

    /// <summary>Moon-Touched cosmetic slow (spec §3.3: "~5-10%"). Same idiom as SharedZombieSystem.OnRefreshSpeed.</summary>
    private void OnMoonTouchedRefreshSpeed(Entity<SolreignMoonTouchedComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(ent.Comp.SlowMultiplier, ent.Comp.SlowMultiplier);
    }
}
