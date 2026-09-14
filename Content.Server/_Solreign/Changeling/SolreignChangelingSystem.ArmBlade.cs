using Content.Shared._Solreign.Changeling;
using Content.Shared._Solreign.FX;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee;
using Robust.Server.Player;

namespace Content.Server._Solreign.Changeling;

/// <summary>
///     Augmented Arm Blade — "Deploy Retention Tool" (spec §2.3). Overrides the changeling's OWN
///     <see cref="MeleeWeaponComponent"/> (every humanoid has one for unarmed punches,
///     Resources/Prototypes/Entities/Mobs/base.yml) — same "the mob's body is the weapon" idiom
///     <c>SolreignWerewolfSystem.Transform.cs</c>'s maul handler documents for
///     <c>SolreignWolfFormComponent</c>. Snapshot-then-restore so retracting is always exact.
///
///     Also FX Language v1's first real consumer (spec docs/specs/FX-LANGUAGE-V1-SPEC-2026-07-16.md
///     §6): Extend/Retract each raise a <c>transformation</c> <see cref="SolreignFxCueV1"/> alongside
///     the pre-existing <see cref="SharedAppearanceSystem.SetData"/> call. Entirely dormant behind
///     <c>solreign.fx.cue_v1</c> (default false, checked inside
///     <see cref="ISolreignFxCueRaiser.RaiseSecretRoleCue"/> itself) — this file adds no gate of its
///     own. The arm-blade sprite-layer swap (the literal bug fix) is driven by the
///     <see cref="SharedAppearanceSystem.SetData"/> call below and is NOT part of the FX kill switch
///     (spec §4.0/§6.2: appearance data is public gameplay state, never confidential FX, and the
///     blade must stay visible with the FX cue off).
/// </summary>
public sealed partial class SolreignChangelingSystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private ISolreignFxCueRaiser _fxRaiser = default!;
    [Dependency] private IPlayerManager _players = default!;

    /// <summary>
    ///     The one allowlisted <c>transformation</c> id (spec §2.2). It is
    ///     <c>DetailOnly</c>/<c>requiresRedactedBroadcastVariant: true</c> in
    ///     <c>Resources/Prototypes/_Solreign/FX/effects.yml</c>, so <see cref="ISolreignFxCueRaiser.RaiseSecretRoleCue"/>
    ///     is the ONLY legal raise path for it — <c>RaiseCue</c> hard-refuses any DetailOnly id
    ///     (spec §5.2, grk #2A). Confidentiality here is API-enforced, not caller discipline: this
    ///     file cannot accidentally broadcast the real "changeling transformed" cue to bystanders even
    ///     if it tried.
    /// </summary>
    private const string ArmBladeTransformationEffectId = "transformation";

    /// <summary>
    ///     Raises the <c>transformation</c> FX cue for an Extend/Retract beat (spec §6.2 item 2).
    ///     Bystanders within PVS range receive only the redacted <c>transformation_generic</c>
    ///     broadcast cue with every numeric field scrubbed to that prototype's own fixed defaults
    ///     (spec §5.2 item 1) — never this call's real <paramref name="extending"/>-specific
    ///     intensity/scale/duration/palette/phase, and never the real <c>transformation</c> EffectId.
    ///     Only <paramref name="ent"/>'s own client receives the detail cue with the real values. A
    ///     missing player session (admin-spawned/NPC changeling with no attached client) is a safe
    ///     no-op — the FX cue is decorative juice, never the sole carrier of gameplay-critical state
    ///     (spec §4.0), so skipping it here changes nothing about the already-applied damage/appearance
    ///     change above.
    /// </summary>
    private void RaiseArmBladeTransformationCue(Entity<SolreignChangelingComponent> ent, bool extending)
    {
        if (!_players.TryGetSessionByEntity(ent.Owner, out var session))
            return;

        var anchor = GetNetEntity(ent.Owner);

        // Extend is the dramatic "reveal" beat (near-max intensity/scale); Retract is the lesser
        // undo beat. Both values are well within effects.yml's `transformation` bounds
        // (intensity/scale/duration and palette/phase get clamped+validated again inside
        // SolreignFxCueV1.TryCreate regardless of what's passed here — spec §1.3a).
        if (extending)
            _fxRaiser.RaiseSecretRoleCue(ArmBladeTransformationEffectId, null, anchor,
                intensity: 0.85f, scale: 1.1f, duration: 1.2f, paletteIndex: 1, phase: 0, ent.Owner, session);
        else
            _fxRaiser.RaiseSecretRoleCue(ArmBladeTransformationEffectId, null, anchor,
                intensity: 0.5f, scale: 0.9f, duration: 0.6f, paletteIndex: 2, phase: 1, ent.Owner, session);
    }

    private void InitializeArmBlade()
    {
        SubscribeLocalEvent<SolreignChangelingComponent, SolreignChangelingArmBladeToggleActionEvent>(OnArmBladeToggle);
    }

    private void GrantArmBladeAction(Entity<SolreignChangelingComponent> ent)
    {
        _actions.AddAction(ent.Owner, ref ent.Comp.ArmBladeActionEntity, ent.Comp.ArmBladeActionId);
    }

    private void OnArmBladeToggle(Entity<SolreignChangelingComponent> ent, ref SolreignChangelingArmBladeToggleActionEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<MeleeWeaponComponent>(ent.Owner, out var melee))
            return; // no unarmed-attack component to override — shouldn't happen for a humanoid mob

        var changeling = ent.Comp;

        if (changeling.ArmBladeExtended)
            RetractArmBlade(ent, melee);
        else
            ExtendArmBlade(ent, melee);

        args.Handled = true;
    }

    private void ExtendArmBlade(Entity<SolreignChangelingComponent> ent, MeleeWeaponComponent melee)
    {
        var changeling = ent.Comp;

        // Snapshot the ORIGINAL fists stats so retracting is an exact restore, not a hardcoded fallback.
        changeling.OriginalMeleeDamage = melee.Damage;
        changeling.OriginalMeleeHidden = melee.Hidden;

        melee.Damage = changeling.ArmBladeDamage;
        melee.Hidden = false; // a blade's damage should be examinable, unlike bare fists
        Dirty(ent.Owner, melee);

        changeling.ArmBladeExtended = true;
        _appearance.SetData(ent.Owner, SolreignChangelingVisuals.ArmBladeExtended, true);
        RaiseArmBladeTransformationCue(ent, extending: true);

        _popup.PopupEntity(Loc.GetString("solreign-changeling-armblade-extend-popup"), ent.Owner, ent.Owner, PopupType.Medium);
    }

    private void RetractArmBlade(Entity<SolreignChangelingComponent> ent, MeleeWeaponComponent melee)
    {
        var changeling = ent.Comp;

        if (changeling.OriginalMeleeDamage is { } originalDamage)
            melee.Damage = originalDamage;
        melee.Hidden = changeling.OriginalMeleeHidden;
        Dirty(ent.Owner, melee);

        changeling.OriginalMeleeDamage = null;
        changeling.ArmBladeExtended = false;
        _appearance.SetData(ent.Owner, SolreignChangelingVisuals.ArmBladeExtended, false);
        RaiseArmBladeTransformationCue(ent, extending: false);

        _popup.PopupEntity(Loc.GetString("solreign-changeling-armblade-retract-popup"), ent.Owner, ent.Owner, PopupType.Medium);
    }
}
