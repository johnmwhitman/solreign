using Content.Shared.Examine;
using Robust.Shared.Random;

namespace Content.Server._Solreign.EasterEggs;

/// <summary>
///     Delight-eggs batch (feat/delight-eggs): drives <see cref="SolreignCuriosityExamineComponent"/>.
///     Increments a per-examiner examine count on <see cref="ExaminedEvent"/>, shows the deepest tier
///     unlocked so far, and — only once the examiner has reached the top tier — has a small chance of
///     also appending one rare aside line. All math lives in the pure
///     <see cref="SolreignCuriosityExamineRules"/>; this is thin ECS glue over it, same shape as
///     <c>SolreignWristOrganizerSystem</c> over <c>SolreignWristOrganizerRules</c>.
///
///     Deliberately ungated by any CVar (see the component's doc comment) — pure examine-text flavor,
///     no gameplay effect, no broadcast.
/// </summary>
public sealed partial class SolreignCuriosityExamineSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SolreignCuriosityExamineComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<SolreignCuriosityExamineComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var comp = ent.Comp;
        if (comp.TierLocKeys.Count == 0 || comp.TierThresholds.Count == 0)
            return;

        comp.ExamineCounts.TryGetValue(args.Examiner, out var count);
        count++;
        comp.ExamineCounts[args.Examiner] = count;

        var tier = SolreignCuriosityExamineRules.TierIndexForCount(count, comp.TierThresholds);
        if (tier < 0)
            return;

        // A mismatched threshold/text list length degrades to the shorter list rather than throwing.
        var textCount = Math.Min(comp.TierLocKeys.Count, comp.TierThresholds.Count);
        if (tier >= textCount)
            tier = textCount - 1;

        args.PushMarkup(Loc.GetString(comp.TierLocKeys[tier]));

        if (comp.RareAsideLocKey is not { } asideKey)
            return;

        var topTier = textCount - 1;
        if (SolreignCuriosityExamineRules.ShouldShowRareAside(tier, topTier, _random.NextDouble(), comp.RareAsideChance))
            args.PushMarkup(Loc.GetString(asideKey));
    }
}
