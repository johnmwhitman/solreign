namespace Content.Shared._Solreign.HotPotato;

/// <summary>
///     Pure fuse/transfer math for the Mandatory Team-Building Exercise, kept free of ECS state so
///     it can be unit tested directly (Content.Tests/_Solreign/HotPotatoTests.cs).
/// </summary>
public static class HotPotatoFuseMath
{
    /// <summary>
    ///     Beep interval for the accelerating countdown: linearly interpolates from
    ///     <paramref name="max"/> (full fuse) down to <paramref name="min"/> (detonation).
    ///     Inputs outside the fuse window are clamped, and degenerate configurations
    ///     (non-positive fuse, min >= max) collapse to <paramref name="min"/>.
    /// </summary>
    public static TimeSpan BeepInterval(TimeSpan remaining, TimeSpan fuse, TimeSpan min, TimeSpan max)
    {
        if (fuse <= TimeSpan.Zero || min >= max)
            return min;

        var fraction = Math.Clamp(remaining / fuse, 0.0, 1.0);
        return min + (max - min) * fraction;
    }

    /// <summary>
    ///     Whether a collision hand-off is allowed at <paramref name="curTime"/>. The boundary is
    ///     inclusive: a transfer exactly at the cooldown's end is allowed.
    /// </summary>
    public static bool CanTransfer(TimeSpan curTime, TimeSpan nextTransferAllowed)
    {
        return curTime >= nextTransferAllowed;
    }

    /// <summary>
    ///     The earliest time the next hand-off may happen after a transfer at <paramref name="curTime"/>.
    /// </summary>
    public static TimeSpan NextTransferTime(TimeSpan curTime, TimeSpan cooldown)
    {
        return curTime + cooldown;
    }

    /// <summary>
    ///     Fuse time left, clamped to zero once <paramref name="detonateAt"/> has passed.
    /// </summary>
    public static TimeSpan Remaining(TimeSpan curTime, TimeSpan detonateAt)
    {
        return detonateAt > curTime ? detonateAt - curTime : TimeSpan.Zero;
    }
}
