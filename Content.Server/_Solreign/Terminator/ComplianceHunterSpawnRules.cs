namespace Content.Server._Solreign.Terminator;

/// <summary>
///     Pure spawn-gating math for <see cref="SolreignComplianceHunterRule"/>. Keeps concurrent
///     Compliance Retrieval Units from stacking when the event re-fires via the Compliance Hunt
///     scheduler or an admin re-triggers <c>addgamerule SolreignComplianceHunterRule</c>.
///     No IoC, no engine types — unit-tested in Content.Tests/_Solreign/ComplianceHunterSpawnRulesTests.cs
///     (same isolation pattern as <see cref="FixationRules"/> and MiniBossPopulationGate).
/// </summary>
public static class ComplianceHunterSpawnRules
{
    /// <summary>Default cap: one live Compliance Auditor on the station at a time.</summary>
    public const int DefaultMaxConcurrent = 1;

    /// <summary>Default players needed for an additional hunter.</summary>
    public const int DefaultPlayersPerHunter = 15;

    /// <summary>
    ///     Whether another hunter may be spawned given <paramref name="existingCount"/> live units
    ///     already on the map and a designer-supplied <paramref name="maxConcurrent"/> cap.
    ///     Misconfiguration is sanitized rather than thrown (YAML-sourced values): negative existing
    ///     counts clamp to zero, and a negative max clamps to zero (always reject) rather than
    ///     treating negatives as uncapped — an uncapped designer intent is expressed with a large
    ///     positive max, not a typo.
    /// </summary>
    public static bool MaySpawn(int existingCount, int maxConcurrent = DefaultMaxConcurrent)
    {
        var existing = existingCount < 0 ? 0 : existingCount;
        var max = maxConcurrent < 0 ? 0 : maxConcurrent;
        return existing < max;
    }

    /// <summary>
    ///     Calculates the maximum allowed concurrent hunters based on the current population.
    ///     Provides <paramref name="baseMax"/> hunters plus one extra for every <paramref name="playersPerHunter"/>
    ///     players alive on the station.
    /// </summary>
    public static int CalculateMaxConcurrent(int population, int playersPerHunter = DefaultPlayersPerHunter, int baseMax = DefaultMaxConcurrent)
    {
        var pop = population < 0 ? 0 : population;
        var pph = playersPerHunter <= 0 ? 0 : playersPerHunter;
        var bMax = baseMax < 0 ? 0 : baseMax;

        if (pph == 0)
            return bMax;

        return bMax + (pop / pph);
    }
}
