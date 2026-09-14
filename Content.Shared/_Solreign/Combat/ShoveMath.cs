namespace Content.Shared._Solreign.Combat;

/// <summary>
///     Pure math for the Solreign shove extension layered on top of upstream's disarm/shove
///     (Content.Shared.Weapons.Melee.SharedMeleeWeaponSystem.DoDisarm raises
///     <c>Content.Shared.CombatMode.DisarmedEvent</c> on the target; handled by
///     <c>Content.Server.Hands.Systems.HandsSystem.OnDisarmed</c> for the item-in-hand branch and
///     <c>Content.Shared.Damage.Systems.SharedStaminaSystem.OnDisarmed</c> for the stamina-shove
///     branch). Upstream already applies stamina damage and, on a stumble, a stun; this layer adds a
///     physical push-away (via the same knockback primitive
///     Content.Shared.Weapons.Melee.MeleeThrowOnHitSystem uses - <c>ThrowingSystem.TryThrow</c>) plus
///     a small bonus of stamina damage (via SharedStaminaSystem's own public
///     <c>TakeStaminaDamage</c>), both scaled by how clean the disarm landed
///     (<c>DisarmedEvent.PushProbability</c>: 1 - the fail chance, so higher means a more decisive
///     disarm). Kept ECS-free so it's unit-testable without spinning up the server (see
///     Content.Tests/_Solreign/ShoveMathTests.cs). Consumed by
///     Content.Server._Solreign.Combat.SolreignShoveSystem.
/// </summary>
public static class ShoveMath
{
    /// <summary>Push distance (tiles) for the weakest possible successful shove.</summary>
    public const float MinPushDistance = 0.5f;

    /// <summary>Push distance (tiles) for the cleanest possible successful shove.</summary>
    public const float MaxPushDistance = 2.5f;

    /// <summary>Push (throw) speed for the weakest possible successful shove.</summary>
    public const float MinPushSpeed = 3f;

    /// <summary>Push (throw) speed for the cleanest possible successful shove.</summary>
    public const float MaxPushSpeed = 6f;

    /// <summary>
    ///     Extra stamina damage layered on top of whatever upstream's own shove handling already
    ///     applied (or, for the item-in-hand branch, didn't apply at all), capped here so even a
    ///     maximally clean disarm can never one-shot a healthy target into stamina crit on the bonus
    ///     alone.
    /// </summary>
    public const float MaxBonusStaminaDamage = 6f;

    /// <summary>
    ///     How far (tiles) to knock the target back for a shove with the given
    ///     <paramref name="pushProbability"/> (0-1; out-of-range values are clamped so a
    ///     misconfigured caller fails closed to the nearest sane bound instead of throwing an entity
    ///     an unbounded distance or not moving it at all).
    /// </summary>
    public static float PushDistance(float pushProbability)
    {
        var clamped = Math.Clamp(pushProbability, 0f, 1f);
        return MinPushDistance + clamped * (MaxPushDistance - MinPushDistance);
    }

    /// <summary>The throw speed (see <see cref="PushDistance"/>'s clamping) for the knockback.</summary>
    public static float PushSpeed(float pushProbability)
    {
        var clamped = Math.Clamp(pushProbability, 0f, 1f);
        return MinPushSpeed + clamped * (MaxPushSpeed - MinPushSpeed);
    }

    /// <summary>The bonus stamina damage (see <see cref="MaxBonusStaminaDamage"/>) for the shove.</summary>
    public static float BonusStaminaDamage(float pushProbability)
    {
        return Math.Clamp(pushProbability, 0f, 1f) * MaxBonusStaminaDamage;
    }
}
