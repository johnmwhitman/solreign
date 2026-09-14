namespace Content.Shared._Solreign.Medical;

/// <summary>
///     Pure math for the Solreign "topicals apply faster" polish pass (docs/BETA-FEEDBACK-01.md:
///     "Topicals apply 10% faster to self AND others"). See
///     <see cref="SolreignTopicalSpeedSystem"/> for the ECS wiring that applies this once, at
///     startup, to every upstream <c>Content.Shared.Medical.Healing.HealingComponent</c> - ointment,
///     gauze, bicaridine patches, and any other topical, since they all share that one component.
///     Kept ECS-free so it's unit-testable without spinning up the server (see
///     Content.Tests/_Solreign/TopicalTimingTests.cs).
///
///     Scaling the shared <c>HealingComponent.Delay</c> field once, instead of forking or
///     RaiseLocalEvent-hooking upstream's own doAfter math
///     (Content.Shared.Medical.Healing.HealingSystem.TryHeal), means both branches read the
///     already-faster base delay for free: the other-heal branch uses it directly, and the self-heal
///     branch multiplies it by <c>HealingComponent.SelfHealPenaltyMultiplier</c> same as before - so
///     "self AND others" falls out of the one change instead of needing two.
/// </summary>
public static class TopicalTiming
{
    /// <summary>10% faster.</summary>
    public const float DelayMultiplier = 0.9f;

    /// <summary>
    ///     The scaled doAfter delay for a topical whose unscaled delay is <paramref name="baseDelay"/>.
    ///     A non-positive input is returned unchanged (fails closed to "no change" rather than
    ///     producing a negative or zero doAfter delay some other system might mishandle).
    /// </summary>
    public static TimeSpan ScaledDelay(TimeSpan baseDelay)
    {
        if (baseDelay <= TimeSpan.Zero)
            return baseDelay;

        return baseDelay * DelayMultiplier;
    }
}
