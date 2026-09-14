using Content.Server.Actions;
using Content.Shared._Solreign.Changeling;
using Content.Shared.Popups;

namespace Content.Server._Solreign.Changeling;

/// <summary>
///     Transform / Revert — "Assume Persona" (spec §2.2). Two self-targeted <c>InstantAction</c>s,
///     granted at <c>ComponentStartup</c> (main file). Deliberately does NOT use <c>PolymorphSystem</c>
///     — see the spec's §2.1 note: a changeling stays the same entity across a transform, unlike
///     Werewolf's wolf-form body swap.
/// </summary>
public sealed partial class SolreignChangelingSystem
{
    [Dependency] private ActionsSystem _actions = default!;

    private void InitializeTransform()
    {
        SubscribeLocalEvent<SolreignChangelingComponent, SolreignChangelingTransformActionEvent>(OnTransformAction);
        SubscribeLocalEvent<SolreignChangelingComponent, SolreignChangelingRevertActionEvent>(OnRevertAction);
    }

    /// <summary>Grants all three ability actions (Transform/Revert here, Arm Blade in its own partial).</summary>
    private void GrantAbilityActions(Entity<SolreignChangelingComponent> ent)
    {
        _actions.AddAction(ent.Owner, ref ent.Comp.TransformActionEntity, ent.Comp.TransformActionId);
        _actions.AddAction(ent.Owner, ref ent.Comp.RevertActionEntity, ent.Comp.RevertActionId);
        GrantArmBladeAction(ent);
    }

    private void OnTransformAction(Entity<SolreignChangelingComponent> ent, ref SolreignChangelingTransformActionEvent args)
    {
        if (args.Handled)
            return;

        var changeling = ent.Comp;
        if (!ChangelingIdentityRules.CanTransform(changeling.KnownAliases.Count))
        {
            _popup.PopupEntity(Loc.GetString("solreign-changeling-transform-none"), ent.Owner, ent.Owner, PopupType.SmallCaution);
            return;
        }

        var index = ChangelingIdentityRules.MostRecentAliasIndex(changeling.KnownAliases.Count);
        var alias = changeling.KnownAliases[index];

        ApplySnapshot(ent.Owner, alias.Snapshot);
        changeling.Transformed = true;

        _popup.PopupEntity(Loc.GetString("solreign-changeling-transform-popup", ("name", alias.DisplayName)),
            ent.Owner, ent.Owner, PopupType.Medium);

        args.Handled = true;
    }

    private void OnRevertAction(Entity<SolreignChangelingComponent> ent, ref SolreignChangelingRevertActionEvent args)
    {
        if (args.Handled)
            return;

        var changeling = ent.Comp;
        if (changeling.TrueForm is not { } trueForm)
            return; // shouldn't happen — captured unconditionally at ComponentStartup

        ApplySnapshot(ent.Owner, trueForm);
        changeling.Transformed = false;

        _popup.PopupEntity(Loc.GetString("solreign-changeling-revert-popup"), ent.Owner, ent.Owner, PopupType.Medium);

        args.Handled = true;
    }
}
