using Content.Server.Objectives.Systems;
using Content.Shared.Objectives.Components;

namespace Content.Server._Solreign.Changeling;

/// <summary>
///     Progress for <see cref="SolreignChangelingCollectIdentitiesConditionComponent"/> — the round
///     objective "collect N identities" (spec §6 round-integration follow-up). Reads live off the
///     changeling's own roster through <see cref="SolreignChangelingSystem"/>'s public accessor (the
///     component itself is [Access]-locked to that system) rather than tracking a separate counter —
///     simpler than upstream's own event-driven <c>ChangelingObjectiveSystem</c>/
///     <c>ChangelingDevouredEvent</c> pairing, and needs no new hook into
///     <c>SolreignChangelingSystem.Absorb.cs</c> (which stays untouched by this task).
/// </summary>
public sealed partial class SolreignChangelingObjectiveSystem : EntitySystem
{
    [Dependency] private NumberObjectiveSystem _number = default!;
    [Dependency] private SolreignChangelingSystem _changeling = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SolreignChangelingCollectIdentitiesConditionComponent, ObjectiveGetProgressEvent>(OnGetProgress);
    }

    private void OnGetProgress(Entity<SolreignChangelingCollectIdentitiesConditionComponent> ent, ref ObjectiveGetProgressEvent args)
    {
        if (args.Mind.OwnedEntity is not { } mob)
        {
            args.Progress = 0f;
            return;
        }

        // Clamp the rolled NumberObjective target against the changeling's own MaxKnownAliases cap
        // (spec §3 rule 4) — defends against a future min/max retune making the objective
        // permanently uncompletable.
        var rawTarget = _number.GetTarget(ent.Owner);
        var target = SolreignChangelingRoundRules.ClampObjectiveTarget(rawTarget, _changeling.GetMaxKnownAliases(mob));

        args.Progress = SolreignChangelingRoundRules.GetObjectiveProgress(_changeling.GetKnownAliasCount(mob), target);
    }
}
