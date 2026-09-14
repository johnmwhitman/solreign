using Content.Shared.Medical.Healing;

namespace Content.Shared._Solreign.Medical;

/// <summary>
///     Applies the Solreign "topicals 10% faster" polish pass (see <see cref="TopicalTiming"/> for
///     the math) to every upstream <c>HealingComponent</c> - ointment, gauze, bicaridine patches,
///     etc - the moment one spawns, rather than touching
///     Content.Shared.Medical.Healing.HealingSystem itself. No (component, event) subscription
///     collides with this: HealingSystem never subscribes <c>ComponentStartup</c> for its own
///     component (grepped repo-wide), it only handles <c>UseInHandEvent</c>/<c>AfterInteractEvent</c>
///     and its doAfter.
///
///     Shared (not Server-only) and deliberately does nothing but pure, deterministic math on data
///     both sides already loaded identically from the same prototype YAML, so client and server
///     compute the same scaled <see cref="HealingComponent.Delay"/> independently - no desync risk,
///     no need to mark the (non-autonetworked-on-this-field-anyway) component dirty.
/// </summary>
public sealed partial class SolreignTopicalSpeedSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HealingComponent, ComponentStartup>(OnHealingStartup);
    }

    private void OnHealingStartup(Entity<HealingComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.Delay = TopicalTiming.ScaledDelay(ent.Comp.Delay);
    }
}
