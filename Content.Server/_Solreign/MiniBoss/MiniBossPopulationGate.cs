namespace Content.Server._Solreign.MiniBoss;

/// <summary>
///     Pure min/max population-bound math shared by both mini-boss rules (<c>SolreignAuditorPrimeRule</c>,
///     <c>SolreignSpecimenZeroRule</c>). Kept free of IoC/engine types so it is directly unit-testable
///     (Content.Tests/_Solreign/MiniBossGateTests.cs), mirroring how <c>PeriodicEffectTiming</c> isolates
///     pure math elsewhere in the Solreign effects engine.
///
///     Why a hand-rolled gate instead of relying on upstream <c>GameRuleComponent.MinPlayers</c>: that
///     field is only consulted once, at <c>RoundStartAttemptEvent</c> (Content.Server/GameTicking/
///     GameTicker.GameRule.cs) — an admin/Director firing a mini-boss mid-round via
///     <c>addgamerule</c>/the Game Rules panel never passes through that check, so a rule spawned at 3
///     alive crew would go entirely ungated without this. Both rules call
///     <see cref="InBounds"/> for themselves in <c>Started</c> against a live alive-crew headcount.
/// </summary>
public static class MiniBossPopulationGate
{
    /// <summary>
    ///     True if <paramref name="aliveCount"/> falls within [<paramref name="minPopulation"/>,
    ///     <paramref name="maxPopulation"/>]. Misconfiguration is sanitized rather than thrown (these
    ///     values come from YAML): a negative minimum clamps to zero, and
    ///     <paramref name="maxPopulation"/> &lt;= 0 means "uncapped" (no upper bound) rather than
    ///     "always fails" — matching how a designer would naturally leave the field unset in YAML.
    /// </summary>
    public static bool InBounds(int aliveCount, int minPopulation, int maxPopulation)
    {
        var min = minPopulation < 0 ? 0 : minPopulation;

        if (aliveCount < min)
            return false;

        if (maxPopulation > 0 && aliveCount > maxPopulation)
            return false;

        return true;
    }
}
