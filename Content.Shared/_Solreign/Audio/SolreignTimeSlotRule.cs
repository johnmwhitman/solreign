using Content.Shared.EntityConditions;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._Solreign.Audio;

public sealed partial class SolreignTimeSlotConditionSystem
    : EntityConditionSystem<TransformComponent, SolreignTimeSlotCondition>
{
    [Dependency] private IGameTiming _timing = default!;

    protected override void Condition(Entity<TransformComponent> entity, ref EntityConditionEvent<SolreignTimeSlotCondition> args)
    {
        var currentSlot = SolreignTimeSlotRuleMath.CurrentSlot(
            _timing.CurTime, args.Condition.SlotCount, args.Condition.SlotDuration);
        args.Result = currentSlot == args.Condition.Slot;
    }
}

/// <summary>
///     True only during its own SLOT of a repeating time cycle.
///     Ported from the deleted RulesRule base to EntityCondition after upstream #44696.
/// </summary>
public sealed partial class SolreignTimeSlotCondition : EntityConditionBase<SolreignTimeSlotCondition>
{
    [DataField(required: true)]
    public int Slot;

    [DataField]
    public int SlotCount = 3;

    [DataField]
    public TimeSpan SlotDuration = TimeSpan.FromSeconds(45);

    public override string EntityConditionGuidebookText(IPrototypeManager prototype) => string.Empty;
}
