namespace Content.Shared._Solreign.NightVision;

/// <summary>
///     Pure drain math for the after-hours compliance lenses, kept free of ECS state so it can
///     be unit tested directly (Content.Tests/_Solreign/NvgPowerMathTests.cs).
///
///     Units follow upstream battery conventions: charge in joules, draw in watts
///     (see <c>BatteryComponent.MaxCharge</c> and <c>PowerCellDrawComponent.DrawRate</c>).
/// </summary>
public static class NvgPowerMath
{
    /// <summary>
    ///     How long a cell with <paramref name="chargeJoules"/> lasts at
    ///     <paramref name="drawRateWatts"/>. No charge means no runtime; a non-positive draw
    ///     never drains and yields <see cref="float.PositiveInfinity"/>.
    /// </summary>
    public static float RuntimeSeconds(float chargeJoules, float drawRateWatts)
    {
        if (chargeJoules <= 0f)
            return 0f;

        if (drawRateWatts <= 0f)
            return float.PositiveInfinity;

        return chargeJoules / drawRateWatts;
    }

    /// <summary>
    ///     Remaining charge after drawing <paramref name="drawRateWatts"/> for
    ///     <paramref name="seconds"/>. Never goes below zero; non-positive draw or elapsed
    ///     time leaves the (floored-at-zero) charge unchanged.
    /// </summary>
    public static float ChargeAfter(float chargeJoules, float drawRateWatts, float seconds)
    {
        var charge = MathF.Max(chargeJoules, 0f);

        if (drawRateWatts <= 0f || seconds <= 0f)
            return charge;

        return MathF.Max(charge - drawRateWatts * seconds, 0f);
    }

    /// <summary>
    ///     Charge as a 0–1 fraction of capacity. Degenerate capacities report empty.
    /// </summary>
    public static float ChargeFraction(float chargeJoules, float maxChargeJoules)
    {
        if (maxChargeJoules <= 0f)
            return 0f;

        return Math.Clamp(chargeJoules / maxChargeJoules, 0f, 1f);
    }

    /// <summary>
    ///     Low-battery static ramp: 0 while the charge fraction is at or above
    ///     <paramref name="lowChargeFraction"/>, rising linearly to 1 as the cell hits empty.
    ///     A non-positive threshold disables the ramp entirely.
    /// </summary>
    public static float FlickerStrength(float chargeFraction, float lowChargeFraction)
    {
        if (lowChargeFraction <= 0f)
            return 0f;

        var fraction = Math.Clamp(chargeFraction, 0f, 1f);
        if (fraction >= lowChargeFraction)
            return 0f;

        return 1f - fraction / lowChargeFraction;
    }
}
