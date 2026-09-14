namespace Content.Shared._Solreign.Sprint;

/// <summary>
///     Pure sprint drain/cooldown math, kept free of ECS state so it can be unit tested
///     directly (Content.Tests/_Solreign/SprintMathTests.cs). Consumed by
///     <see cref="SolreignSprintSystem"/>, which owns the actual stamina damage application
///     (via SharedStaminaSystem.TakeStaminaDamage) and the movement speed refresh.
/// </summary>
public static class SprintMath
{
    /// <summary>
    ///     Stamina damage after draining at <paramref name="drainPerSecond"/> for
    ///     <paramref name="seconds"/>. Non-positive drain or elapsed time leaves the damage
    ///     unchanged. This only ever adds damage; regular stamina decay/regen is handled
    ///     separately by SharedStaminaSystem.
    /// </summary>
    public static float StaminaDamageAfterDrain(float currentDamage, float drainPerSecond, float seconds)
    {
        if (drainPerSecond <= 0f || seconds <= 0f)
            return currentDamage;

        return currentDamage + drainPerSecond * seconds;
    }

    /// <summary>
    ///     Stamina damage as a 0-1 fraction of the crit threshold. A degenerate (non-positive)
    ///     threshold reports fully-tired rather than dividing by zero, so callers fail closed
    ///     into "can't sprint" instead of "always can".
    /// </summary>
    public static float DamageFraction(float staminaDamage, float critThreshold)
    {
        if (critThreshold <= 0f)
            return 1f;

        return Math.Clamp(staminaDamage / critThreshold, 0f, 1f);
    }

    /// <summary>
    ///     True once <paramref name="damageFraction"/> reaches (or already exceeds) the
    ///     auto-drop line. An out-of-range <paramref name="autoDropFraction"/> is clamped into
    ///     0-1 first, so a misconfigured negative value fails closed (never allows sprint) and
    ///     a misconfigured &gt;1 value just means "ride it all the way to crit".
    /// </summary>
    public static bool IsAutoDropTriggered(float damageFraction, float autoDropFraction)
    {
        return damageFraction >= Math.Clamp(autoDropFraction, 0f, 1f);
    }

    /// <summary>
    ///     The timestamp at which a sprint cooldown started at <paramref name="now"/> expires.
    ///     A non-positive cooldown expires immediately (i.e. no lockout).
    /// </summary>
    public static TimeSpan CooldownEndTime(TimeSpan now, float cooldownSeconds)
    {
        if (cooldownSeconds <= 0f)
            return now;

        return now + TimeSpan.FromSeconds(cooldownSeconds);
    }

    /// <summary>
    ///     Whether a sprint cooldown that expires at <paramref name="cooldownEndTime"/> is still
    ///     locked out at <paramref name="now"/>.
    /// </summary>
    public static bool IsOnCooldown(TimeSpan now, TimeSpan cooldownEndTime)
    {
        return now < cooldownEndTime;
    }

    /// <summary>
    ///     The full decision of whether an entity should be actively sprinting right now: the
    ///     key must be held, sprint must not be on cooldown, the mob must not already be in
    ///     stamina crit, and its damage fraction must be below the auto-drop line.
    /// </summary>
    public static bool ShouldSprint(bool keyHeld, bool onCooldown, bool staminaCritical, float damageFraction, float autoDropFraction)
    {
        if (!keyHeld || onCooldown || staminaCritical)
            return false;

        return !IsAutoDropTriggered(damageFraction, autoDropFraction);
    }

    /// <summary>
    ///     The movement speed multiplier to apply this tick. 1 (no change) while not sprinting;
    ///     otherwise <paramref name="speedModifier"/>, floored at zero so a misconfigured
    ///     negative value can't produce negative movement speed.
    /// </summary>
    public static float EffectiveSpeedModifier(bool sprinting, float speedModifier)
    {
        if (!sprinting)
            return 1f;

        return MathF.Max(speedModifier, 0f);
    }
}
