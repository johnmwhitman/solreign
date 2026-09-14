using System.Numerics;
using Content.Server.Hands.Systems;
using Content.Shared._Solreign.Combat;
using Content.Shared.CombatMode;
using Content.Shared.Damage.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Throwing;

namespace Content.Server._Solreign.Combat;

/// <summary>
///     Extends upstream's disarm/shove with a physical push-away, on top of whatever upstream
///     already did (item-in-hand throw via <c>HandsSystem</c>, or stamina damage + stun via
///     <c>SharedStaminaSystem</c> - see <c>DisarmedEvent</c>'s doc trail). Uses the same knockback
///     primitive as <c>Content.Shared.Weapons.Melee.MeleeThrowOnHitSystem</c>
///     (<c>ThrowingSystem.TryThrow</c>) plus a small bonus of stamina damage via
///     <c>SharedStaminaSystem</c>'s own public API. All the distance/speed/damage math is pure and
///     lives in <see cref="ShoveMath"/> so it's unit-testable.
///
///     DisarmedEvent hook: <c>MobStateComponent</c>
///     (Content.Server._Solreign.MartialArts.SolreignMartialArtsSystem, "Carp") and
///     <c>HumanoidProfileComponent</c> (Content.Server._Solreign.MartialArts.SolreignJudoBeltSystem,
///     "Judo Belt") already own those (component, event) pairs repo-wide (grepped), so this hooks
///     <see cref="MovementSpeedModifierComponent"/> instead - a distinct pair, and still ubiquitous
///     on every shovable mob (Resources/Prototypes/Entities/Mobs/base.yml puts it on MobBase itself,
///     alongside Physics, which the knockback needs anyway). Ordered AFTER both upstream handlers,
///     same discipline as the martial arts systems, so this only reacts to a shove that actually
///     landed (<c>args.Handled</c>) and never races HandsSystem's own item throw.
/// </summary>
public sealed partial class SolreignShoveSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private SharedStaminaSystem _stamina = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MovementSpeedModifierComponent, DisarmedEvent>(OnAnyDisarmed,
            after: new[] { typeof(HandsSystem), typeof(SharedStaminaSystem) });
    }

    private void OnAnyDisarmed(Entity<MovementSpeedModifierComponent> target, ref DisarmedEvent args)
    {
        // Unhandled means upstream aborted the shove entirely (target resisted, is incapacitated,
        // etc) - nothing landed, so there's nothing to push.
        if (!args.Handled)
            return;

        var sourcePos = _transform.GetMapCoordinates(args.Source).Position;
        var targetPos = _transform.GetMapCoordinates(target.Owner).Position;
        var direction = targetPos - sourcePos;

        // TryThrow itself no-ops on a Vector2.Zero direction (and on a target with no
        // PhysicsComponent), but source and target sharing exact coordinates is degenerate enough
        // to guard explicitly rather than rely on that fallback.
        if (direction != Vector2.Zero)
        {
            _throwing.TryThrow(
                target.Owner,
                direction.Normalized() * ShoveMath.PushDistance(args.PushProbability),
                ShoveMath.PushSpeed(args.PushProbability),
                user: args.Source);
        }

        _stamina.TakeStaminaDamage(target.Owner, ShoveMath.BonusStaminaDamage(args.PushProbability), source: args.Source);
    }
}
