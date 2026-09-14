using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Administration.Systems;
using Content.Server.GameTicking;
using Content.Server._Solreign.Bounties;
using Content.Server._Solreign.Market;
using Content.Server._Solreign.Records;
using Content.Server._Solreign.StationIdentity;
using Content.Shared.CCVar;
using Content.Shared.Maps;
using Content.Shared.Station.Components;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
/// MAPSEED-AUDIT-2026-07-16: extends the SR-W-012 map-health scorecard's coverage into
/// content-seeding (the scorecard itself only asserts structural health -- station grid, spawns,
/// shuttle, APC, atmosphere -- never front-door/decor/ambience presence). Reuses
/// <see cref="SolreignMapTestCatalog"/>, the same shared seven-map source
/// <see cref="SolreignMapHealthScorecardIntegrationTest"/> and
/// <see cref="SolreignWingmateBeaconMapPlacementIntegrationTest"/> already consume, and the same
/// "largest station-member grid" resolution idiom both of those tests use.
///
/// Exists to close the recurrence risk the mapseed audit flagged: without an assertion here, a
/// future edit could silently drop a map's front door, its <c>SolreignStationMood</c> component
/// (as Nocturne and Verdant had, until the concurrent <c>feat/mood-rollout</c> merge fixed it), or
/// its cake-showcase decor, and nothing would fail CI.
///
/// LORE-TRAIL-2026-07-16 addendum: the mapseed audit's Gap 1 found the Season-1 "Ledger Wakes"
/// lore trail (8 Auditor's Memos + 20 Employee Handbook pages + the Vault Reward Crate) was
/// complete on Oasis only -- Meridian had handbook-only, Leviathan had 2/8 memos, and
/// Nocturne/Perihelion/Verdant/Terminus had none of it. The feat/lore-trail wave rolled the full
/// trail out to all 7 rotation maps; these extra assertions (exactly 8 distinct memos, all 20
/// distinct handbook pages, exactly 1 vault crate, all per map) close that recurrence risk the
/// same way the front-door/mood assertions above close theirs.
/// </summary>
[TestFixture]
public sealed class SolreignMapContentSeedIntegrationTest : GameTest
{
    /// <summary>The 20 canonical Employee Handbook page numbers (Season 1 narrative bible §4).</summary>
    private static readonly int[] HandbookPageNumbers =
    {
        3, 12, 27, 34, 45, 58, 62, 79, 83, 94, 101, 115, 128, 134, 147, 160, 172, 189, 195, 200,
    };

    public override PoolSettings PoolSettings => new()
    {
        // Loading a real production map mutates enough global engine state that this pair must
        // never be recycled -- same reasoning as the map-health scorecard and the beacon-placement
        // test.
        Dirty = true,
    };

    [Test]
    [TestCaseSource(typeof(SolreignMapTestCatalog), nameof(SolreignMapTestCatalog.MapIds))]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.GridFill), false)]
    public async Task ProductionMapCarriesExpectedFrontDoorsAndDecor(string mapProtoId)
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var ticker = entMan.System<GameTicker>();
        var mapSystem = entMan.System<SharedMapSystem>();

        var loadedMapId = MapId.Nullspace;
        var stationGrids = new HashSet<EntityUid>();
        EntityUid? station = null;

        var directiveTerminalCount = 0;
        var marketConsoleCount = 0;
        var bountyBoardCount = 0;
        var cakeShowcaseFilledCount = 0;
        var recordsTerminalCount = 0;
        var hasStationMood = false;

        // LORE-TRAIL-2026-07-16: per-prototype-id counts for the Season-1 trail, scoped to
        // station-owned grids the same way everything else in this test is scoped.
        var memoCounts = new Dictionary<string, int>();
        for (var i = 1; i <= 8; i++)
            memoCounts[$"SolreignPaperAuditorMemo{i}"] = 0;

        var handbookCounts = new Dictionary<string, int>();
        foreach (var page in HandbookPageNumbers)
            handbookCounts[$"SolreignPaperHandbookPage{page}"] = 0;

        var vaultCrateCount = 0;

        await server.WaitPost(() =>
        {
            Assert.That(protoMan.TryIndex<GameMapPrototype>(mapProtoId, out var mapProto),
                $"{mapProtoId} must resolve to one GameMapPrototype.");

            var opts = DeserializationOptions.Default with { InitializeMaps = true };
            ticker.LoadGameMap(mapProto!, out loadedMapId, opts);

            // Same "largest station-member grid" resolution SolreignMapHealthScorecardIntegrationTest
            // and SolreignWingmateBeaconMapPlacementIntegrationTest both already use.
            var memberQuery = entMan.GetEntityQuery<StationMemberComponent>();
            var grids = mapSystem.GetAllGrids(loadedMapId).ToList();
            Assert.That(grids, Is.Not.Empty, $"{mapProtoId}: must resolve at least one grid.");

            var targetGrid = grids[0].Owner;
            var largest = 0f;
            foreach (var grid in grids)
            {
                if (!memberQuery.HasComponent(grid.Owner))
                    continue;

                var area = grid.Comp.LocalAABB.Width * grid.Comp.LocalAABB.Height;
                if (area > largest)
                {
                    largest = area;
                    targetGrid = grid.Owner;
                }
            }

            if (entMan.TryGetComponent<StationMemberComponent>(targetGrid, out var memberComp))
                station = memberComp.Station;

            if (station is { } st && entMan.TryGetComponent<StationDataComponent>(st, out var stationData))
                stationGrids.UnionWith(stationData.Grids);

            Assert.That(stationGrids, Is.Not.Empty, $"{mapProtoId}: must resolve at least one station-owned grid.");

            // Front doors: each carries its own marker/logic component, scoped to station-owned grids
            // the same way the beacon-placement test scopes WingmateBeaconComponent.
            var terminalQuery = entMan.AllEntityQueryEnumerator<SolreignOracleComponent, TransformComponent>();
            while (terminalQuery.MoveNext(out _, out _, out var xform))
                if (xform.GridUid is { } g && stationGrids.Contains(g))
                    directiveTerminalCount++;

            var marketQuery = entMan.AllEntityQueryEnumerator<SolreignMarketConsoleComponent, TransformComponent>();
            while (marketQuery.MoveNext(out _, out _, out var xform))
                if (xform.GridUid is { } g && stationGrids.Contains(g))
                    marketConsoleCount++;

            var bountyQuery = entMan.AllEntityQueryEnumerator<SolreignBountyBoardComponent, TransformComponent>();
            while (bountyQuery.MoveNext(out _, out _, out var xform))
                if (xform.GridUid is { } g && stationGrids.Contains(g))
                    bountyBoardCount++;

            var recordsQuery = entMan.AllEntityQueryEnumerator<SolreignRecordsTerminalComponent, TransformComponent>();
            while (recordsQuery.MoveNext(out _, out _, out var xform))
                if (xform.GridUid is { } g && stationGrids.Contains(g))
                    recordsTerminalCount++;

            // Cake showcase: no dedicated marker component exists, so this matches by prototype id
            // (MetaDataComponent.EntityPrototype), the same way map-content is identified in the
            // mapseed audit's own YAML-side parser -- just resolved against the live loaded entities
            // instead of the raw map file.
            var metaQuery = entMan.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (metaQuery.MoveNext(out _, out var meta, out var xform))
            {
                if (xform.GridUid is not { } g || !stationGrids.Contains(g))
                    continue;
                if (meta.EntityPrototype?.ID == "SolreignCakeShowcaseFilled")
                    cakeShowcaseFilledCount++;

                var protoId = meta.EntityPrototype?.ID;
                if (protoId is null)
                    continue;

                // Only count entities placed directly on the grid, not ones nested inside a
                // container -- the Vault Reward Crate's own EntityTableContainerFill spawns a
                // *second*, bonus copy of SolreignPaperAuditorMemo8 inside itself (see
                // lore_papers.yml), which would otherwise double-count memo 8 here even though
                // exactly one *findable-in-the-world* copy is what this assertion cares about.
                if (xform.ParentUid != g)
                    continue;

                if (memoCounts.ContainsKey(protoId))
                    memoCounts[protoId]++;
                else if (handbookCounts.ContainsKey(protoId))
                    handbookCounts[protoId]++;
                else if (protoId == "SolreignVaultRewardCrate")
                    vaultCrateCount++;
            }

            // SolreignStationMood lives on the STATION entity itself (added via the gameMap
            // prototype's `stations:` component block), not a placed grid entity -- check the
            // station, not a grid query.
            if (station is { } stUid)
                hasStationMood = entMan.HasComponent<SolreignStationMoodComponent>(stUid);

            if (loadedMapId != MapId.Nullspace)
                mapSystem.DeleteMap(loadedMapId);
        });

        Assert.Multiple(() =>
        {
            Assert.That(directiveTerminalCount, Is.EqualTo(1),
                $"{mapProtoId}: expected exactly one Directive Terminal (SolreignOracleComponent) on a station-owned grid, found {directiveTerminalCount}.");
            // P2.2 (2026-08-08) + R1 doctrine: market/bounty boards stay dark and off rotation maps
            // (solreign.market.enabled / solreign.bounties.enabled default false; orphan-allowlisted).
            // Assert absence so a future map_patch re-seed fails closed instead of shipping economy
            // front doors into arrivals again.
            Assert.That(marketConsoleCount, Is.EqualTo(0),
                $"{mapProtoId}: expected zero market consoles on rotation maps (economy dark); found {marketConsoleCount}.");
            Assert.That(bountyBoardCount, Is.EqualTo(0),
                $"{mapProtoId}: expected zero bounty/liability boards on rotation maps (economy dark); found {bountyBoardCount}.");
            Assert.That(cakeShowcaseFilledCount, Is.EqualTo(1),
                $"{mapProtoId}: expected exactly one filled holographic-cake showcase, found {cakeShowcaseFilledCount}.");
            Assert.That(hasStationMood, Is.True,
                $"{mapProtoId}: expected the station entity to carry SolreignStationMoodComponent.");
            Assert.That(recordsTerminalCount, Is.EqualTo(1),
                $"{mapProtoId}: expected exactly one Personnel Records terminal, found {recordsTerminalCount}.");

            // LORE-TRAIL-2026-07-16: every rotation map now carries the full Season-1 trail --
            // exactly one of each of the 8 Auditor's Memos, exactly one of each of the 20 Employee
            // Handbook pages, and exactly one Vault Reward Crate. A future edit that drops any
            // single fragment (the same way Meridian/Leviathan/4-others were silently missing most
            // or all of this before the mapseed audit) will fail here instead of drifting quietly.
            foreach (var (protoId, count) in memoCounts)
                Assert.That(count, Is.EqualTo(1),
                    $"{mapProtoId}: expected exactly one {protoId}, found {count}.");

            foreach (var (protoId, count) in handbookCounts)
                Assert.That(count, Is.EqualTo(1),
                    $"{mapProtoId}: expected exactly one {protoId}, found {count}.");

            Assert.That(vaultCrateCount, Is.EqualTo(1),
                $"{mapProtoId}: expected exactly one SolreignVaultRewardCrate, found {vaultCrateCount}.");
        });
    }
}
