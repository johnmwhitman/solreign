namespace Content.Server._Solreign.TrainingCombat;

/// <summary>
///     The three practice zones a compliance training baton can connect with. This SPIKE picks one
///     by weighted random on the server; the eventual full targeting program (roadmap Program 3)
///     replaces the random pick with the player's zone selection while keeping the same outcomes.
/// </summary>
public enum TrainingZone : byte
{
    None,
    Head,
    Legs,
    Hands,
}

/// <summary>
///     Pure zone-weighting and cooldown math for <see cref="SolreignTrainingBatonSystem"/>. Kept free
///     of IoC/engine types so it is directly unit-testable (Content.Tests/_Solreign/TrainingZoneTests.cs),
///     mirroring how <c>PeriodicEffectTiming</c> and <c>CorporateScoring</c> isolate pure logic
///     elsewhere in _Solreign.
/// </summary>
public static class ZoneRules
{
    /// <summary>
    ///     Picks a zone from three relative weights using <paramref name="roll"/> (a uniform sample in
    ///     [0, 1], e.g. <c>IRobustRandom.NextDouble()</c>).
    ///
    ///     Misconfiguration is sanitized rather than thrown, because weights come from YAML: negative
    ///     weights clamp to zero, out-of-range rolls clamp into [0, 1], and all-zero weights yield
    ///     <see cref="TrainingZone.None"/> (the baton whiffs politely). A roll that lands exactly on
    ///     the top edge never selects a zero-weight zone.
    /// </summary>
    public static TrainingZone PickZone(float headWeight, float legsWeight, float handsWeight, double roll)
    {
        var head = MathF.Max(0f, headWeight);
        var legs = MathF.Max(0f, legsWeight);
        var hands = MathF.Max(0f, handsWeight);

        var total = head + legs + hands;
        if (total <= 0f)
            return TrainingZone.None;

        var r = Math.Clamp(roll, 0d, 1d) * total;

        if (r < head)
            return TrainingZone.Head;

        if (r < head + legs)
            return TrainingZone.Legs;

        // r may equal total exactly (roll == 1.0) or ride a float edge; never hand the pick to a
        // zone whose weight is zero.
        if (hands > 0f)
            return TrainingZone.Hands;

        return legs > 0f ? TrainingZone.Legs : TrainingZone.Head;
    }

    /// <summary>
    ///     Whether the baton's zone effect is off cooldown at <paramref name="curTime"/>.
    /// </summary>
    public static bool CooldownReady(TimeSpan curTime, TimeSpan nextEffectTime)
    {
        return curTime >= nextEffectTime;
    }

    /// <summary>
    ///     Computes when the next zone effect is allowed. A negative cooldown (YAML misconfiguration)
    ///     clamps to zero rather than scheduling into the past.
    /// </summary>
    public static TimeSpan NextEffectTime(TimeSpan curTime, TimeSpan cooldown)
    {
        return curTime + (cooldown > TimeSpan.Zero ? cooldown : TimeSpan.Zero);
    }
}
