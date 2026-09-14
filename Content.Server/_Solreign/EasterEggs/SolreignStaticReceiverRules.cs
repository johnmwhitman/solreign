namespace Content.Server._Solreign.EasterEggs;

/// <summary>
///     Pure signal-model math for <see cref="SolreignStaticReceiverSystem"/>, redesigned per the
///     orchestrator's round-integrity review (2026-07-16): the first draft was a deterministic,
///     binary antag detector — functionally a wallhack against hidden-identity rounds once the
///     community discovered it. This model kills the intel value while keeping the flavor true:
///
///       1. AMBIGUOUS SOURCES — the system scans for several "supernatural anchor" component types
///          (xenoartifacts, consecrated-ground markers, recharge-pod props, moon-touched crew), not
///          just antagonists, so a crackle never uniquely implies "antag nearby".
///       2. NOISY SIGNAL — probabilistic, not binary: near ANY source the receiver crackles only
///          ~60% of scans, and with nothing in range it still false-crackles ~12% of scans
///          (<see cref="ShouldCrackle"/>).
///       3. NOT SAVE-SCUMMABLE — the roll is a deterministic hash of (item entity, round id, time
///          bucket) via <see cref="Roll"/>/<see cref="TimeBucket"/>, so re-using the item within
///          the same window re-yields the SAME answer instead of re-rolling the dice. The bucket
///          length is the scan cooldown, so every fresh scan-slot gets exactly one fresh roll.
///          Deliberately NOT <c>HashCode.Combine</c>, whose seed is randomized per process — the
///          mix below (splitmix64 finalizer) is stable, which also keeps the unit tests exact.
///       4. TIERED TEXT — even a positive crackle is atmosphere, not radar: the text varies across
///          common/rare variants (<see cref="CrackleVariant"/>), with the rare "almost-words" line
///          showing up only occasionally.
///
///     Kept free of IoC/engine types so it is directly unit-testable
///     (Content.Tests/_Solreign/SolreignStaticReceiverRulesTests.cs), same split as
///     <c>SolreignWristOrganizerRules</c> and <c>SolreignCuriosityExamineRules</c>.
/// </summary>
public static class SolreignStaticReceiverRules
{
    /// <summary>
    ///     Coarse time bucket for the deterministic roll: the current game time divided into
    ///     <paramref name="bucketLength"/>-sized slots. Scans inside the same slot share a roll.
    ///     A non-positive bucket length (YAML misconfiguration) falls back to one minute rather
    ///     than dividing by zero.
    /// </summary>
    public static long TimeBucket(TimeSpan curTime, TimeSpan bucketLength)
    {
        var lengthTicks = bucketLength > TimeSpan.Zero ? bucketLength.Ticks : TimeSpan.TicksPerMinute;
        return curTime.Ticks / lengthTicks;
    }

    /// <summary>
    ///     Deterministic pseudo-random sample in [0, 1) from (entity id, round id, time bucket,
    ///     salt). Same inputs always yield the same output — across frames AND across processes
    ///     (splitmix64 finalizer, no randomized-seed framework hashing). <paramref name="salt"/>
    ///     lets one scan derive several independent rolls (crackle decision vs. text pick).
    /// </summary>
    public static double Roll(int entityId, int roundId, long timeBucket, uint salt)
    {
        var z = unchecked((ulong) (uint) entityId * 0x9E3779B97F4A7C15UL
                          ^ (ulong) (uint) roundId * 0xBF58476D1CE4E5B9UL
                          ^ (ulong) timeBucket * 0x94D049BB133111EBUL
                          ^ salt);

        unchecked
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
        }

        // Top 53 bits -> a uniform double in [0, 1), the standard 64-bit-PRNG-to-double idiom.
        return (z >> 11) * (1.0 / (1UL << 53));
    }

    /// <summary>
    ///     The noisy-signal gate: near a source the receiver crackles when <paramref name="roll"/>
    ///     falls under <paramref name="nearbyChance"/> (~60%); with nothing in range it still
    ///     false-crackles under <paramref name="falseChance"/> (~12%). Both chances are clamped
    ///     into [0, 1] so a misconfigured value can't invert the odds or throw — same defensive
    ///     idiom as <c>ProvidenceCommiserationGate.ShouldFire</c>.
    /// </summary>
    public static bool ShouldCrackle(bool sourceNearby, double roll, float nearbyChance, float falseChance)
    {
        var chance = Math.Clamp(sourceNearby ? nearbyChance : falseChance, 0f, 1f);
        return roll < chance;
    }

    /// <summary>
    ///     Picks which crackle line shows, from an independent roll: index 2 (the rare
    ///     "almost-words" line) under <paramref name="rareChance"/>, otherwise an even split
    ///     between the two common variants 0 ("faint static") and 1 ("a low hum").
    ///     <paramref name="rareChance"/> is clamped into [0, 1].
    /// </summary>
    public static int CrackleVariant(double roll, float rareChance)
    {
        var rare = Math.Clamp(rareChance, 0f, 1f);
        if (roll < rare)
            return 2;

        // Split the remaining probability mass evenly between the common variants.
        return roll < rare + (1d - rare) / 2d ? 0 : 1;
    }
}
