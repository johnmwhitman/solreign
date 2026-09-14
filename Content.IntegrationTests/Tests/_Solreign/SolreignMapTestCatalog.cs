using System.Collections.Immutable;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
/// Shared test-only catalog of the seven reviewed Solreign map-pool members established by SR-W-011
/// (see <see cref="SolreignMapPoolIntegrationTest"/>). This is the single source of Solreign map IDs
/// for test fixtures -- SR-W-012's map-health scorecard (<see cref="SolreignMapHealthScorecardIntegrationTest"/>)
/// reuses it instead of maintaining a second seven-map list, per
/// docs/research/SR-W-012-MAP-HEALTH-GAP-AUDIT-2026-07-15.md.
/// </summary>
/// <remarks>
/// Population bands are deliberately not duplicated here. Each consumer reads
/// <c>GameMapPrototype.MinPlayers</c>/<c>MaxPlayers</c> directly off the indexed prototype, and
/// <see cref="SolreignMapPoolIntegrationTest.ProductionPoolHasSevenUniqueResolvableMaps"/> is what
/// cross-checks <see cref="MapIds"/> against the indexed <c>SolreignMapPool</c> (<see cref="MapPoolId"/>)
/// prototype.
/// </remarks>
internal static class SolreignMapTestCatalog
{
    /// <summary>
    /// The <c>GameMapPoolPrototype</c> ID that <see cref="MapIds"/> is reviewed against.
    /// </summary>
    internal const string MapPoolId = "SolreignMapPool";

    /// <summary>
    /// The seven reviewed Solreign map-pool members, in the same content SR-W-011 established.
    /// Immutable so no consumer can mutate the shared reviewed list.
    /// </summary>
    internal static readonly ImmutableArray<string> MapIds = ImmutableArray.Create(
        "SolreignLeviathan",
        "SolreignMeridian",
        "SolreignNocturne",
        "SolreignOasis",
        "SolreignPerihelion",
        "SolreignTerminus",
        "SolreignVerdant");
}
