namespace Content.Server._Solreign.MiniBoss;

/// <summary>
///     Pure elapsed-vs-warning-window math for <c>SolreignSpecimenZeroRule</c>'s 60-second telegraph
///     (spawn only fires once the warning has fully played out — no surprise instant spawns). Kept free
///     of IoC/engine types so it is directly unit-testable
///     (Content.Tests/_Solreign/MiniBossGateTests.cs), same rationale as <see cref="MiniBossPopulationGate"/>.
/// </summary>
public static class MiniBossTelegraph
{
    /// <summary>
    ///     True once <paramref name="elapsedSeconds"/> has reached the (sanitized, non-negative)
    ///     <paramref name="warningSeconds"/> window. A misconfigured negative window clamps to zero
    ///     (spawn immediately) rather than never firing.
    /// </summary>
    public static bool WarningElapsed(float elapsedSeconds, float warningSeconds)
    {
        var window = warningSeconds < 0f ? 0f : warningSeconds;
        return elapsedSeconds >= window;
    }
}
