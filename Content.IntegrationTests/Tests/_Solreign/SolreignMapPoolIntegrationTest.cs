using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Maps;
using Content.Shared.CCVar;
using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Solreign;

[TestFixture]
public sealed class SolreignMapPoolIntegrationTest : GameTest
{
    // Single source of the seven reviewed map IDs is SolreignMapTestCatalog (SR-W-012 reuses it too;
    // do not reintroduce a second independent seven-map list here).
    private const string PoolId = SolreignMapTestCatalog.MapPoolId;

    private static readonly System.Collections.Immutable.ImmutableArray<string> ExpectedMapIds =
        SolreignMapTestCatalog.MapIds;

    private static readonly object[] PopulationCases =
    {
        new object[] { 0, new[] { "SolreignNocturne", "SolreignOasis", "SolreignPerihelion", "SolreignVerdant" } },
        new object[] { 15, new[] { "SolreignNocturne", "SolreignOasis", "SolreignPerihelion", "SolreignVerdant" } },
        new object[] { 16, new[] { "SolreignOasis", "SolreignPerihelion", "SolreignVerdant" } },
        new object[] { 30, new[] { "SolreignLeviathan", "SolreignOasis", "SolreignPerihelion", "SolreignVerdant" } },
        new object[] { 34, new[] { "SolreignLeviathan", "SolreignOasis", "SolreignPerihelion", "SolreignVerdant" } },
        new object[] { 35, new[] { "SolreignLeviathan", "SolreignMeridian", "SolreignOasis", "SolreignPerihelion", "SolreignVerdant" } },
        new object[] { 36, new[] { "SolreignLeviathan", "SolreignMeridian" } },
        new object[] { 69, new[] { "SolreignLeviathan", "SolreignMeridian" } },
        new object[] { 70, new[] { "SolreignLeviathan", "SolreignMeridian", "SolreignTerminus" } },
        new object[] { 71, new[] { "SolreignLeviathan", "SolreignTerminus" } },
        new object[] { 90, new[] { "SolreignLeviathan", "SolreignTerminus" } },
        new object[] { 91, new[] { "SolreignTerminus" } },
        new object[] { 100, new[] { "SolreignTerminus" } },
    };

    [Test]
    public async Task ProductionPoolHasSevenUniqueResolvableMaps()
    {
        await Server.WaitAssertion(() =>
        {
            var pool = SProtoMan.Index<GameMapPoolPrototype>(PoolId);

            Assert.That(pool.Maps, Has.Count.EqualTo(ExpectedMapIds.Length));
            Assert.That(pool.Maps, Is.EquivalentTo(ExpectedMapIds));
            Assert.That(pool.Maps.All(id => SProtoMan.HasIndex<GameMapPrototype>(id)), Is.True);
        });
    }

    [TestCaseSource(nameof(PopulationCases))]
    public async Task ProductionPoolCoversPopulationBand(int population, string[] expectedIds)
    {
        await Server.WaitAssertion(() =>
        {
            var pool = SProtoMan.Index<GameMapPoolPrototype>(PoolId);
            var eligible = pool.Maps
                .Select(id => SProtoMan.Index<GameMapPrototype>(id))
                .Where(map => GameMapSelectionPolicy.IsPopulationEligible(map, population))
                .Select(map => map.ID)
                .Order()
                .ToArray();

            Assert.That(eligible, Is.EqualTo(expectedIds),
                $"Population {population} must have the reviewed Solreign map band.");
            Assert.That(eligible, Is.Not.Empty,
                $"Population {population} must never leave rotation without a compatible Solreign map.");
        });
    }

    [Test]
    public async Task PopulationPolicyRejectsInvalidNegativeCounts()
    {
        await Server.WaitAssertion(() =>
        {
            var pool = SProtoMan.Index<GameMapPoolPrototype>(PoolId);

            Assert.That(pool.Maps
                .Select(id => SProtoMan.Index<GameMapPrototype>(id))
                .Any(map => GameMapSelectionPolicy.IsPopulationEligible(map, -1)), Is.False);
        });
    }

    [Test]
    public async Task RotationAvoidsImmediateRepeatWhenAlternativesExist()
    {
        await OverrideCVar(Side.Server, CCVars.GameMapPool, PoolId);
        await OverrideCVar(Side.Server, CCVars.GameMap, "");
        await OverrideCVar(Side.Server, CCVars.GameMapRotation, true);
        await OverrideCVar(Side.Server, CCVars.GameMapMemoryDepth, 1);
        var manager = Server.ResolveDependency<IGameMapManager>();

        await Server.WaitAssertion(() =>
        {
            manager.ClearSelectedMap();
            manager.SelectMapFromRotationQueue(markAsPlayed: true);
            var first = manager.GetSelectedMap()?.ID;

            manager.SelectMapFromRotationQueue(markAsPlayed: true);
            var second = manager.GetSelectedMap()?.ID;

            Assert.That(first, Is.Not.Null.And.Not.Empty);
            Assert.That(second, Is.Not.EqualTo(first));
        });
    }

    [Test]
    public async Task EmergencyOverrideWinsOutsidePopulationBandAndInvalidIdFailsSafely()
    {
        await OverrideCVar(Side.Server, CCVars.GameMapPool, PoolId);
        await OverrideCVar(Side.Server, CCVars.GameMap, "SolreignTerminus");
        var manager = Server.ResolveDependency<IGameMapManager>();

        await Server.WaitAssertion(() =>
        {
            Assert.That(manager.GetSelectedMap()?.ID, Is.EqualTo("SolreignTerminus"),
                "The explicit emergency map must win even when the connected test population is below its band.");
        });

        await OverrideCVar(Side.Server, CCVars.GameMap, "");

        await Server.WaitAssertion(() =>
        {
            manager.ClearSelectedMap();
            Assert.That(manager.TrySelectMapIfEligible("SolreignOasis"), Is.True);
            Assert.That(manager.GetSelectedMap()?.ID, Is.EqualTo("SolreignOasis"));

            var changed = manager.TrySelectMapIfEligible("DefinitelyNotASolreignMap");

            Assert.That(changed, Is.False);
            Assert.That(manager.GetSelectedMap()?.ID, Is.EqualTo("SolreignOasis"),
                "An invalid selection must not replace the last valid runtime-selected map.");
        });
    }
}
