#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.GameTicking;
using Content.Server.Station.Systems;
using Content.Server._Solreign.PlayerDelight.Mark;
using Content.Server._Solreign.SeasonLedger;
using Content.Shared.CCVar;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Content.Shared._Solreign.PlayerDelight.Mark;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     "The Mark" garden wave wiring (docs/specs/MARK-SPEC-2026-07-17-DRAFT.md §7, MG-W2): drives
///     <c>SolreignMarkGardenSystem</c> end-to-end against the real connected pool session —
///     runtime rollback (enabled=false must add no garden or projection and must permit no
///     planting row), the once-per-account-EVER plant flow (one row + one stage-0 entity;
///     the second attempt changes nothing), round-start projection of seeded aged rows onto their
///     deterministic slots with stage-matched prototypes, the owner-only examine surface (rail 5:
///     the planted character name renders ONLY to its owner), and the §4.3 fallback placement
///     beside the wingmate beacon.
///
///     Pure mechanics (claim atomicity, age thresholds, slot/prototype tables, copy vocabulary)
///     are exhaustively unit-tested without a server in Content.Tests/_Solreign/Mark*Tests.cs —
///     this file only covers the ECS wiring those tests cannot reach.
///
///     Aged fixtures use the store's <c>SetMarkPlantedUtcForTests</c> seam (rewinds the planting
///     clock on a genuinely-claimed row) rather than hand-inserted rows, so every projected record
///     went through the real atomic claim. Every test sets CVars, then calls
///     <c>ResetRoundStateForTests</c>, in that order (the ProvidenceFirstShiftWelcome rationale).
/// </summary>
[TestFixture]
public sealed class MarkGardenIntegrationTest : GameTest
{
    // Dirty: flips CCVars directly, spawns fixtures onto the station grid, and rewinds ledger
    // clocks — this server must never be handed back to the pool.
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        // Real attached body required (HotPotato/SeasonLedger/Welcome precedent) — DummyTicker=false.
        DummyTicker = false,
    };

    /// <summary>
    ///     The pool's default map is tiny — the tiles a garden bed (or the beacon fallback ring)
    ///     needs mostly don't exist on it. Lay real floor around <paramref name="anchor"/> before
    ///     exercising placement (the map-health scorecard's lay-a-new-tile idiom) so the scenarios
    ///     test the SYSTEM's law, not the fixture map's floor plan. Server main thread only.
    /// </summary>
    private void PaintFloorAround(EntityUid anchor, IEnumerable<Vector2i> offsets)
    {
        var entMan = Server.EntMan;
        var mapSystem = Server.System<SharedMapSystem>();
        var tileDefMan = Server.ResolveDependency<Robust.Shared.Map.ITileDefinitionManager>();

        var xform = entMan.GetComponent<TransformComponent>(anchor);
        var gridUid = xform.GridUid!.Value;
        var grid = entMan.GetComponent<Robust.Shared.Map.Components.MapGridComponent>(gridUid);
        var origin = mapSystem.TileIndicesFor(gridUid, grid, xform.Coordinates);
        var floor = new Robust.Shared.Map.Tile(tileDefMan["FloorSteel"].TileId);

        foreach (var offset in offsets)
            mapSystem.SetTile((gridUid, grid), origin + offset, floor);
    }

    private static IEnumerable<Vector2i> Ring(int radius)
    {
        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
                yield return new Vector2i(x, y);
        }
    }

    private async Task<bool> PollAsync(Func<bool> condition, int maxTicks = 60)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            var hit = false;
            await Server.WaitPost(() => hit = condition());
            if (hit)
                return true;
            await Server.WaitRunTicks(1);
        }

        return false;
    }

    private List<(EntityUid Uid, SolreignMarkComponent Comp)> CollectMarks(IEntityManager entMan)
    {
        var marks = new List<(EntityUid, SolreignMarkComponent)>();
        var query = entMan.AllEntityQueryEnumerator<SolreignMarkComponent>();
        while (query.MoveNext(out var uid, out var comp))
            marks.Add((uid, comp));
        return marks;
    }

    private List<EntityUid> CollectGardens(IEntityManager entMan)
    {
        var gardens = new List<EntityUid>();
        var query = entMan.AllEntityQueryEnumerator<SolreignMarkGardenComponent>();
        while (query.MoveNext(out var uid, out _))
            gardens.Add(uid);
        return gardens;
    }

    private void DeleteGardensForStation(IEntityManager entMan, StationSystem stations, EntityUid station)
    {
        foreach (var garden in CollectGardens(entMan))
        {
            if (stations.GetOwningStation(garden) == station)
                entMan.DeleteEntity(garden);
        }
    }

    [Test]
    public async Task RuntimeKillSwitchOff_IsZeroBehavior()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mark = server.System<SolreignMarkGardenSystem>();
        var ledger = server.System<SeasonLedgerSystem>();
        var stations = server.System<StationSystem>();

        // The feature now ships enabled. Exercise the actual operator rollback posture explicitly.
        server.CfgMan.SetCVar(CCVars.SolreignMarkEnabled, false);
        await server.WaitRunTicks(10);
        Assert.That(server.CfgMan.GetCVar(CCVars.SolreignMarkEnabled), Is.False,
            "precondition: the Mark runtime kill switch must be off");

        // The default-on real-ticker setup may already have materialized the fallback garden before
        // the test body can flip the CVar. The settling ticks above let any setup-time async
        // projection finish before we preserve that honest baseline; the off-state must create no
        // additional fixture or projection.
        var gardensBefore = CollectGardens(entMan).ToHashSet();
        var marksBefore = CollectMarks(entMan).Select(m => m.Uid).ToHashSet();

        EntityUid mob = default;
        EntityUid station = default;
        await server.WaitPost(() =>
        {
            mob = ServerSession!.AttachedEntity!.Value;
            station = stations.GetOwningStation(mob)
                      ?? throw new InvalidOperationException("Connected mob has no owning station.");
            mark.ResetRoundStateForTests();
            mark.MaterializeAndProjectForTests(station);
        });
        await server.WaitRunTicks(10);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(CollectGardens(entMan), Is.EquivalentTo(gardensBefore),
                    "enabled=false must neither add nor replace a garden");
                Assert.That(CollectMarks(entMan).Select(m => m.Uid), Is.EquivalentTo(marksBefore),
                    "enabled=false must neither add nor replace a Mark entity");
            });
        });

        // Even with a garden physically present (admin-spawned), planting must be inert while off.
        EntityUid garden = default;
        await server.WaitPost(() =>
        {
            garden = entMan.SpawnEntity(SolreignMarkGardenSystem.GardenPrototypeId, entMan.GetComponent<TransformComponent>(mob).Coordinates);
            mark.TryPlantForTests(garden, mob, MarkKind.Sapling);
        });
        await server.WaitRunTicks(20);

        var account = ServerSession!.UserId.UserId;
        Assert.That(await ledger.GetMarkAsync(account), Is.Null,
            "enabled=false must be a full kill switch: the plant path may never touch a row");
    }

    [Test]
    public async Task Plant_OnceEver_OneRowOneStageZeroEntity_SecondAttemptRefused()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mark = server.System<SolreignMarkGardenSystem>();
        var ledger = server.System<SeasonLedgerSystem>();

        server.CfgMan.SetCVar(CCVars.SolreignMarkEnabled, true);

        var account = ServerSession!.UserId.UserId;
        EntityUid mob = default;
        EntityUid garden = default;
        await server.WaitPost(() =>
        {
            mark.ResetRoundStateForTests();
            mob = ServerSession!.AttachedEntity!.Value;
            garden = entMan.SpawnEntity(SolreignMarkGardenSystem.GardenPrototypeId, entMan.GetComponent<TransformComponent>(mob).Coordinates);
            PaintFloorAround(garden, entMan.GetComponent<SolreignMarkGardenComponent>(garden).SlotOffsets);
            mark.TryPlantForTests(garden, mob, MarkKind.Sapling);
        });

        // The claim is async (real ledger I/O) — poll for the row to land.
        MarkRecord? record = null;
        var claimed = false;
        for (var i = 0; i < 60 && !claimed; i++)
        {
            await server.WaitRunTicks(1);
            record = await ledger.GetMarkAsync(account);
            claimed = record != null;
        }

        Assert.That(record, Is.Not.Null, "planting must claim exactly one mark row");
        Assert.That(record!.Kind, Is.EqualTo(MarkKinds.SaplingLedger));
        Assert.That(record.SlotIndex, Is.GreaterThanOrEqualTo(0));

        var projected = await PollAsync(() => CollectMarks(entMan).Count == 1);
        Assert.That(projected, Is.True, "planting must seat exactly one stage-0 projection");

        await server.WaitAssertion(() =>
        {
            var marks = CollectMarks(entMan);
            Assert.That(marks, Has.Count.EqualTo(1));
            var (uid, comp) = marks[0];
            Assert.Multiple(() =>
            {
                Assert.That(comp.Account, Is.EqualTo(account));
                Assert.That(comp.Kind, Is.EqualTo(MarkKind.Sapling));
                Assert.That(comp.Stage, Is.EqualTo(0));
                Assert.That(entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID,
                    Is.EqualTo("SolreignMarkSapling0"));
                Assert.That(entMan.GetComponent<TransformComponent>(uid).Anchored, Is.True,
                    "a seated mark must be anchored (an anchored keepsake is not draggable — T3)");
            });
        });

        // Second attempt — different kind, same account: the once-EVER law.
        await server.WaitPost(() => mark.TryPlantForTests(garden, mob, MarkKind.Lamp));
        await server.WaitRunTicks(30);

        var after = await ledger.GetMarkAsync(account);
        Assert.That(after!.Kind, Is.EqualTo(MarkKinds.SaplingLedger),
            "a second plant attempt must never overwrite the claimed kind");
        await server.WaitAssertion(() =>
        {
            Assert.That(CollectMarks(entMan), Has.Count.EqualTo(1),
                "a second plant attempt must never seat a second entity");
        });
    }

    [Test]
    public async Task Projection_SeededAgedRows_StageMatchedPrototypesAtDeterministicSlots()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mapSystem = server.System<SharedMapSystem>();
        var mark = server.System<SolreignMarkGardenSystem>();
        var ledger = server.System<SeasonLedgerSystem>();
        var stations = server.System<StationSystem>();

        server.CfgMan.SetCVar(CCVars.SolreignMarkEnabled, true);

        // Three genuinely-claimed rows, clocks rewound to pin all the aging bands the spec names:
        // 30 days -> stage 3, 3 days -> stage 1, fresh -> stage 0 (MarkAgeRules thresholds 2/7/21).
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        var accountC = Guid.NewGuid();
        var expected = new Dictionary<Guid, (string Kind, string Proto, int Stage)>
        {
            [accountA] = (MarkKinds.SaplingLedger, "SolreignMarkSapling3", 3),
            [accountB] = (MarkKinds.LampLedger, "SolreignMarkLamp1", 1),
            [accountC] = (MarkKinds.NamePlateLedger, "SolreignMarkPlate0", 0),
        };

        Assert.That((await ledger.TryClaimMarkAsync(accountA, MarkKinds.SaplingLedger, 1, "test-map", "Subject A", 0)).Claimed);
        Assert.That((await ledger.TryClaimMarkAsync(accountB, MarkKinds.LampLedger, 1, "test-map", "Subject B", 0)).Claimed);
        Assert.That((await ledger.TryClaimMarkAsync(accountC, MarkKinds.NamePlateLedger, 1, "test-map", "Subject C", 0)).Claimed);
        await ledger.SetMarkPlantedUtcForTests(accountA, DateTime.UtcNow.AddDays(-30).ToString("o"));
        await ledger.SetMarkPlantedUtcForTests(accountB, DateTime.UtcNow.AddDays(-3).ToString("o"));

        EntityUid garden = default;
        EntityUid station = default;
        await server.WaitPost(() =>
        {
            var mob = ServerSession!.AttachedEntity!.Value;
            station = stations.GetOwningStation(mob)
                      ?? throw new InvalidOperationException("Connected mob has no owning station.");
            garden = entMan.SpawnEntity(SolreignMarkGardenSystem.GardenPrototypeId, entMan.GetComponent<TransformComponent>(mob).Coordinates);
            PaintFloorAround(garden, entMan.GetComponent<SolreignMarkGardenComponent>(garden).SlotOffsets);
        });
        await server.WaitRunTicks(1);

        // Snapshot the rows the projection will read (residue-proof: assert against real slot
        // indices, not assumed 0/1/2).
        var slots = server.CfgMan.GetCVar(CCVars.SolreignMarkSlots);
        var rows = await ledger.GetAllMarksAsync(slots);

        await server.WaitPost(() =>
        {
            mark.ResetRoundStateForTests();
            mark.MaterializeAndProjectForTests(station);
        });

        var projected = await PollAsync(() => CollectMarks(entMan).Count >= rows.Count);
        Assert.That(projected, Is.True,
            $"round-start projection must seat one entity per recorded mark ({rows.Count} rows)");

        await server.WaitAssertion(() =>
        {
            var marks = CollectMarks(entMan);
            Assert.That(marks, Has.Count.EqualTo(rows.Count),
                "exactly one projection per row, nothing extra");

            var gardenXform = entMan.GetComponent<TransformComponent>(garden);
            var gridUid = gardenXform.GridUid!.Value;
            var grid = entMan.GetComponent<Robust.Shared.Map.Components.MapGridComponent>(gridUid);
            var gardenComp = entMan.GetComponent<SolreignMarkGardenComponent>(garden);
            var gardenTile = mapSystem.TileIndicesFor(gridUid, grid, gardenXform.Coordinates);

            foreach (var row in rows)
            {
                var found = marks.FindAll(m => m.Comp.Account == row.User);
                Assert.That(found, Has.Count.EqualTo(1), $"row {row.User} must project exactly once");
                var (uid, comp) = found[0];

                var xform = entMan.GetComponent<TransformComponent>(uid);
                var actualTile = mapSystem.TileIndicesFor(gridUid, grid, xform.Coordinates);
                var expectedTile = gardenTile + MarkGardenLayout.SlotOffset(row.SlotIndex, gardenComp.SlotOffsets);

                Assert.Multiple(() =>
                {
                    Assert.That(xform.Anchored, Is.True, "every projection must be anchored");
                    Assert.That(actualTile, Is.EqualTo(expectedTile),
                        $"row {row.User} (slot {row.SlotIndex}) must stand at its deterministic slot tile");

                    if (!expected.TryGetValue(row.User, out var want))
                        return; // residue row from pool setup — position law asserted above is enough

                    Assert.That(comp.Stage, Is.EqualTo(want.Stage), "stage must match MarkAgeRules for the aged clock");
                    Assert.That(row.Kind, Is.EqualTo(want.Kind));
                    Assert.That(entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID,
                        Is.EqualTo(want.Proto),
                        "the projected prototype must be the (kind, stage) cell of the closed table");
                });
            }
        });
    }

    [Test]
    public async Task Examine_OwnerGetsTheirName_StrangerNever()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mark = server.System<SolreignMarkGardenSystem>();
        var ledger = server.System<SeasonLedgerSystem>();

        server.CfgMan.SetCVar(CCVars.SolreignMarkEnabled, true);

        var account = ServerSession!.UserId.UserId;
        EntityUid mob = default;
        EntityUid garden = default;
        var characterName = string.Empty;
        await server.WaitPost(() =>
        {
            mark.ResetRoundStateForTests();
            mob = ServerSession!.AttachedEntity!.Value;
            characterName = entMan.GetComponent<MetaDataComponent>(mob).EntityName;
            garden = entMan.SpawnEntity(SolreignMarkGardenSystem.GardenPrototypeId, entMan.GetComponent<TransformComponent>(mob).Coordinates);
            PaintFloorAround(garden, entMan.GetComponent<SolreignMarkGardenComponent>(garden).SlotOffsets);
            mark.TryPlantForTests(garden, mob, MarkKind.NamePlate);
        });

        Assert.That(characterName, Is.Not.Empty, "precondition: the planted character has a name");
        Assert.That(await PollAsync(() => CollectMarks(entMan).Count == 1), Is.True,
            "setup failed: mark never seated");
        Assert.That(await ledger.GetMarkAsync(account), Is.Not.Null);

        await server.WaitAssertion(() =>
        {
            var (uid, _) = CollectMarks(entMan)[0];

            // Owner examine: the ONLY surface that ever renders the planted name (rail 5).
            var ownerEvent = new ExaminedEvent(new FormattedMessage(), uid, mob, isInDetailsRange: true, hasDescription: false);
            entMan.EventBus.RaiseLocalEvent(uid, ownerEvent);
            var ownerText = ownerEvent.GetTotalMessage().ToString();
            Assert.Multiple(() =>
            {
                Assert.That(ownerText, Does.Contain("brass plate"),
                    "examine must lead with the public stage-0 plate line");
                Assert.That(ownerText, Does.Contain(characterName),
                    "the owner's examine must carry the C3 owner suffix, by name");
            });

            // Stranger examine: any sessionless examiner entity takes the C4 path — no name, ever.
            var strangerEvent = new ExaminedEvent(new FormattedMessage(), uid, garden, isInDetailsRange: true, hasDescription: false);
            entMan.EventBus.RaiseLocalEvent(uid, strangerEvent);
            var strangerText = strangerEvent.GetTotalMessage().ToString();
            Assert.Multiple(() =>
            {
                Assert.That(strangerText, Does.Contain("brass plate"),
                    "the public stage line renders for everyone");
                Assert.That(strangerText, Does.Not.Contain(characterName),
                    "a non-owner examine must NEVER render the planted character name");
            });
        });
    }

    [Test]
    public async Task FallbackPlacement_NoMapGarden_SpawnsBesideTheWingmateBeacon()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mapSystem = server.System<SharedMapSystem>();
        var turf = server.System<TurfSystem>();
        var mark = server.System<SolreignMarkGardenSystem>();
        var stations = server.System<StationSystem>();

        server.CfgMan.SetCVar(CCVars.SolreignMarkEnabled, true);

        EntityUid beacon = default;
        EntityUid station = default;
        await server.WaitPost(() =>
        {
            mark.ResetRoundStateForTests();
            var mob = ServerSession!.AttachedEntity!.Value;
            station = stations.GetOwningStation(mob)
                      ?? throw new InvalidOperationException("Connected mob has no owning station.");
            // Default-on setup may already have materialized this station's fallback garden.
            // Remove only that station-owned fixture; never rewrite another station's world.
            DeleteGardensForStation(entMan, stations, station);
            Assert.That(CollectGardens(entMan).Where(g => stations.GetOwningStation(g) == station), Is.Empty,
                "precondition: this station has no garden yet");
            beacon = entMan.SpawnEntity("SolreignWingmateBeacon", entMan.GetComponent<TransformComponent>(mob).Coordinates);
            PaintFloorAround(beacon, Ring(2));
            mark.MaterializeAndProjectForTests(station);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var gardens = CollectGardens(entMan);
            Assert.That(gardens, Has.Count.EqualTo(1),
                "the §4.3 fallback must spawn exactly one garden when a beacon exists and no fixture does");

            var gardenXform = entMan.GetComponent<TransformComponent>(gardens[0]);
            var beaconXform = entMan.GetComponent<TransformComponent>(beacon);
            Assert.Multiple(() =>
            {
                Assert.That(gardenXform.Anchored, Is.True, "the fallback garden must be anchored");
                Assert.That(gardenXform.GridUid, Is.EqualTo(beaconXform.GridUid),
                    "the fallback garden must share the beacon's grid");

                var gridUid = gardenXform.GridUid!.Value;
                var grid = entMan.GetComponent<Robust.Shared.Map.Components.MapGridComponent>(gridUid);
                var gardenTile = mapSystem.TileIndicesFor(gridUid, grid, gardenXform.Coordinates);
                var beaconTile = mapSystem.TileIndicesFor(gridUid, grid, beaconXform.Coordinates);
                var delta = gardenTile - beaconTile;
                Assert.That(Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y)), Is.LessThanOrEqualTo(2),
                    "the fallback garden must sit within the fixed candidate ring around the beacon");

                var tileRef = turf.GetTileRef(gardenXform.Coordinates);
                Assert.That(tileRef, Is.Not.Null);
                Assert.That(turf.IsSpace(tileRef!.Value), Is.False,
                    "the fallback garden must not sit on a space tile");
            });
        });
    }

    [Test]
    public async Task RealRoundRestart_EventWiring_NeverThrows_NoOrphans()
    {
        // Proves the actual StationPostInitEvent subscription runs safely on a real round start
        // with the feature LIVE — on a map with neither a placed garden nor (normally) a beacon,
        // the resolve-or-fallback path must skip cleanly (log-once law), never throw, and never
        // leave an unanchored orphan.
        var server = Server;
        var entMan = server.EntMan;
        var ticker = server.System<GameTicker>();

        server.CfgMan.SetCVar(CCVars.SolreignMarkEnabled, true);

        await server.WaitPost(() => ticker.RestartRound());
        var backInRound = await PollAsync(() => ticker.RunLevel == GameRunLevel.InRound, maxTicks: 120);
        Assert.That(backInRound, Is.True, "a fresh round must start cleanly with the mark CVar on");
        await server.WaitRunTicks(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(CollectMarks(entMan), Is.Empty,
                "no rows in this instance's ledger -> a real round start must project nothing");
            foreach (var garden in CollectGardens(entMan))
            {
                // The pool map normally has no beacon (no garden at all); if one exists, the
                // fallback garden it produced must at least obey the no-orphans law.
                Assert.That(entMan.GetComponent<TransformComponent>(garden).Anchored, Is.True,
                    "any fallback garden from a real round start must be anchored (no orphans)");
            }
        });
    }
}
