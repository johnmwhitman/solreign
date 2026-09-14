#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Solreign.PlayerDelight.Mark;
using Content.Server.Station.Events;
using Content.Server.Station.Systems;
using Content.Server._Solreign.Providence;
using Content.Server._Solreign.SeasonLedger;
using Content.Shared.CCVar;
using Content.Shared.Examine;
using Content.Shared._Solreign.PlayerDelight.Mark;
using Content.Shared.Station.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     "Echoes of the Departed" garden wave wiring (v14, EOD-spec §13) — drives
///     <c>SolreignMarkGardenSystem</c>'s fourth job end-to-end against the real connected pool
///     session: runtime rollback (enabled=false is zero behavior), round-start
///     projection of already-claimed <c>first_death</c> rows onto deterministic slots with
///     stage-matched prototypes, the closed-vocabulary public examine surface (no owner/stranger
///     split — every examiner reads the identical composition), the independent gate against
///     Mark's own CVar, the capacity cap (most-recent-death-first), and the no-orphans law.
///
///     Pure mechanics (Echo bed layout, prototype table) are unit-tested without a server in
///     Content.Tests/_Solreign/EchoProjectionTests.cs — this file only covers the ECS wiring
///     those tests cannot reach. Same aged-fixture idiom as MarkGardenIntegrationTest: real claims
///     via <c>TryClaimFirstDeathAsync</c>, clocks rewound via <c>SetFirstDeathDiedAtUtcForTests</c>
///     rather than hand-inserted rows, so every projected record went through the real atomic
///     claim.
/// </summary>
[TestFixture]
public sealed class EchoGardenIntegrationTest : GameTest
{
    // Dirty: flips CCVars directly, spawns fixtures onto the station grid, and rewinds ledger
    // clocks — this server must never be handed back to the pool.
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        DummyTicker = false,
    };

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

    private List<(EntityUid Uid, SolreignEchoComponent Comp)> CollectEchoes(IEntityManager entMan)
    {
        var echoes = new List<(EntityUid, SolreignEchoComponent)>();
        var query = entMan.AllEntityQueryEnumerator<SolreignEchoComponent>();
        while (query.MoveNext(out var uid, out var comp))
            echoes.Add((uid, comp));
        return echoes;
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

    // --- 1. Runtime rollback pin (test plan §13.4) ---------------------------------------------------

    [Test]
    public async Task RuntimeKillSwitchOff_IsZeroBehavior_AndDoesNotDisturbMark()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mark = server.System<SolreignMarkGardenSystem>();
        var ledger = server.System<SeasonLedgerSystem>();
        var stations = server.System<Content.Server.Station.Systems.StationSystem>();

        // The features now ship enabled. Exercise the actual operator rollback posture explicitly;
        // Mark remains an independent gate, so turn both off for this zero-behavior proof.
        server.CfgMan.SetCVar(CCVars.SolreignEchoEnabled, false);
        server.CfgMan.SetCVar(CCVars.SolreignMarkEnabled, false);
        await server.WaitRunTicks(10);

        Assert.Multiple(() =>
        {
            Assert.That(server.CfgMan.GetCVar(CCVars.SolreignEchoEnabled), Is.False,
                "precondition: the Echo runtime kill switch must be off");
            Assert.That(server.CfgMan.GetCVar(CCVars.SolreignMarkEnabled), Is.False,
                "precondition: Mark must also be off so Echo rollback is tested independently");
        });

        // The default-on real-ticker setup may already have materialized the shared garden before
        // the test body can flip either CVar. The settling ticks above let any setup-time async
        // projection finish before we pin that honest baseline; the off-state must then add no
        // garden, Echo, or Mark projection.
        var gardensBefore = CollectGardens(entMan).ToHashSet();
        var echoesBefore = CollectEchoes(entMan).Select(e => e.Uid).ToHashSet();
        var marksBefore = CollectMarks(entMan).Select(m => m.Uid).ToHashSet();

        // A genuinely claimed first-death row exists in the ledger before projection runs.
        var account = Guid.NewGuid();
        Assert.That(await ledger.TryClaimFirstDeathAsync(
            account, 1, "Subject Zero", "VACUUM", 0, "Probationary Asset", "01"), Is.True);

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
            var echoesAfter = CollectEchoes(entMan);
            Assert.Multiple(() =>
            {
                Assert.That(CollectGardens(entMan), Is.EquivalentTo(gardensBefore),
                    "enabled=false (both flags) must neither add nor replace any garden");
                Assert.That(echoesAfter.Select(e => e.Uid), Is.EquivalentTo(echoesBefore),
                    "enabled=false must neither add nor replace an Echo entity");
                Assert.That(echoesAfter.Select(e => e.Comp.CharacterName), Does.Not.Contain("Subject Zero"),
                    "the newly claimed row must never leak through the disabled Echo projection");
                Assert.That(CollectMarks(entMan).Select(m => m.Uid), Is.EquivalentTo(marksBefore),
                    "Echo rollback must neither add nor replace Mark projection state");
            });
        });
    }

    // --- 2. Projection + ledger-read wiring (test plan §13.1/§13.2/§13.3) --------------------------

    [Test]
    public async Task Projection_SeededClaimedDeaths_StageMatchedPrototypesAtDeterministicSlots()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mapSystem = server.System<SharedMapSystem>();
        var mark = server.System<SolreignMarkGardenSystem>();
        var ledger = server.System<SeasonLedgerSystem>();
        var stations = server.System<Content.Server.Station.Systems.StationSystem>();

        server.CfgMan.SetCVar(CCVars.SolreignEchoEnabled, true);
        // Deliberately oversize the CVar — production must clamp to the garden bed (EchoSlotOffsets
        // count / default 10). Asserting the clamp is the capacity law under test here; a raw 500
        // would stack hundreds of physics entities onto ten tiles (review P1).
        server.CfgMan.SetCVar(CCVars.SolreignEchoSlots, 500);

        // Three genuinely-claimed rows, clocks rewound to pin all the aging bands MarkAgeRules
        // names (2/7/21 days): 30 days -> stage 3, 3 days -> stage 1, fresh -> stage 0.
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        var accountC = Guid.NewGuid();

        Assert.That(await ledger.TryClaimFirstDeathAsync(accountA, 1, "Subject A", "VIOLENCE", 7, "Quarterly Standout", "09"), Is.True);
        Assert.That(await ledger.TryClaimFirstDeathAsync(accountB, 1, "Subject B", "VACUUM", 0, "Probationary Asset", "01"), Is.True);
        Assert.That(await ledger.TryClaimFirstDeathAsync(accountC, 1, "Subject C", "BURN", 3, "Chain Closer", "11"), Is.True);
        await ledger.SetFirstDeathDiedAtUtcForTests(accountA, DateTime.UtcNow.AddDays(-30).ToString("o"));
        await ledger.SetFirstDeathDiedAtUtcForTests(accountB, DateTime.UtcNow.AddDays(-10).ToString("o"));
        await ledger.SetFirstDeathDiedAtUtcForTests(accountC, DateTime.UtcNow.AddDays(-3).ToString("o"));

        EntityUid garden = default;
        EntityUid station = default;
        await server.WaitPost(() =>
        {
            var mob = ServerSession!.AttachedEntity!.Value;
            station = stations.GetOwningStation(mob)
                      ?? throw new InvalidOperationException("Connected mob has no owning station.");
            garden = entMan.SpawnEntity(SolreignMarkGardenSystem.GardenPrototypeId, entMan.GetComponent<TransformComponent>(mob).Coordinates);
            PaintFloorAround(garden, entMan.GetComponent<SolreignMarkGardenComponent>(garden).EchoSlotOffsets);
        });
        await server.WaitRunTicks(1);

        // Snapshot the exact rows the projection will read — same clamp production applies.
        var requestedSlots = server.CfgMan.GetCVar(CCVars.SolreignEchoSlots);
        Assert.That(requestedSlots, Is.EqualTo(500), "precondition: oversize CVar still stores raw value");
        var offsetCount = 0;
        await server.WaitPost(() =>
            offsetCount = entMan.GetComponent<SolreignMarkGardenComponent>(garden).EchoSlotOffsets.Count);
        Assert.That(offsetCount, Is.EqualTo(MarkGardenLayout.EchoColumns * MarkGardenLayout.EchoRows),
            "default Echo bed is 10 offsets — the hard ceiling for projection");
        var slots = Math.Clamp(requestedSlots, 0, offsetCount);
        Assert.That(slots, Is.EqualTo(offsetCount),
            "solreign.echo.slots must clamp to the garden's EchoSlotOffsets.Count at read (never 500 entities)");
        var rows = await ledger.GetAllFirstDeathsAsync(slots);
        Assert.That(rows.Select(r => r.User), Does.Contain(accountA).And.Contain(accountB).And.Contain(accountC),
            "precondition: all three of our seeded rows must be present in the clamped top-N read");

        await server.WaitPost(() =>
        {
            mark.ResetRoundStateForTests();
            mark.MaterializeAndProjectForTests(station);
        });

        var projected = await PollAsync(() => CollectEchoes(entMan).Count >= rows.Count);
        Assert.That(projected, Is.True, $"round-start projection must seat one entity per recorded first-death ({rows.Count} rows)");

        var expected = new Dictionary<Guid, int>
        {
            [accountA] = 3,
            [accountB] = 2,
            [accountC] = 1,
        };

        await server.WaitAssertion(() =>
        {
            var echoes = CollectEchoes(entMan);
            Assert.That(echoes, Has.Count.EqualTo(rows.Count), "exactly one projection per row, nothing extra");

            var gardenXform = entMan.GetComponent<TransformComponent>(garden);
            var gridUid = gardenXform.GridUid!.Value;
            var grid = entMan.GetComponent<Robust.Shared.Map.Components.MapGridComponent>(gridUid);
            var gardenComp = entMan.GetComponent<SolreignMarkGardenComponent>(garden);
            var gardenTile = mapSystem.TileIndicesFor(gridUid, grid, gardenXform.Coordinates);

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var found = echoes.FindAll(e => e.Comp.CharacterName == row.CharacterName);
                Assert.That(found, Has.Count.EqualTo(1), $"row for {row.CharacterName} must project exactly once");
                var (uid, comp) = found[0];

                var xform = entMan.GetComponent<TransformComponent>(uid);
                var actualTile = mapSystem.TileIndicesFor(gridUid, grid, xform.Coordinates);
                var expectedTile = gardenTile + MarkGardenLayout.SlotOffset(i, gardenComp.EchoSlotOffsets);

                Assert.Multiple(() =>
                {
                    Assert.That(xform.Anchored, Is.True, "every Echo projection must be anchored");
                    Assert.That(actualTile, Is.EqualTo(expectedTile),
                        $"row {i} ({row.CharacterName}) must stand at its deterministic ordinal-position slot tile");

                    if (!expected.TryGetValue(row.User, out var wantStage))
                        return; // residue row from pool setup — position law asserted above is enough

                    Assert.That(comp.Stage, Is.EqualTo(wantStage), "stage must match MarkAgeRules for the aged death clock");
                    Assert.That(entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID,
                        Is.EqualTo(EchoProjectionRules.PrototypeFor(wantStage)),
                        "the projected prototype must be the closed stage table's cell");
                });
            }
        });
    }

    // --- 3. Capacity cap (test plan §13.5) ----------------------------------------------------------

    [Test]
    public async Task Capacity_OverCapClaims_OnlyTheMostRecentSlotsCountProject()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mark = server.System<SolreignMarkGardenSystem>();
        var ledger = server.System<SeasonLedgerSystem>();
        var stations = server.System<Content.Server.Station.Systems.StationSystem>();

        server.CfgMan.SetCVar(CCVars.SolreignEchoEnabled, true);
        // Explicitly pinned (not just read) — another test in this fixture may have mutated the
        // shared CVar state via this same dirty-recycled pool; this is the dedicated cap test, so
        // it must exercise a known value at the bed ceiling regardless of run order.
        const int slots = 10;
        server.CfgMan.SetCVar(CCVars.SolreignEchoSlots, slots);
        var seedCount = slots + 5; // N > solreign.echo.slots

        var accounts = new List<Guid>();
        for (var i = 0; i < seedCount; i++)
        {
            var account = Guid.NewGuid();
            accounts.Add(account);
            Assert.That(await ledger.TryClaimFirstDeathAsync(account, 1, $"Subject {i}", "UNKNOWN", 0, "Probationary Asset", "01"), Is.True);
            // Strictly increasing died_at_utc, pinned a year INTO THE FUTURE so index i is the
            // i-th oldest of OUR OWN set and — crucially — every one of our 15 rows outranks any
            // residue row a prior test left behind in this dirty-recycled pool's shared on-disk
            // ledger (the MarkGardenIntegrationTest "residue row from pool setup" phenomenon).
            // A fixture-only trick (StageAt clamps a future/negative elapsed span to stage 0,
            // MarkAgeRules.cs — harmless here since this test never asserts stage); never a
            // production code path.
            await ledger.SetFirstDeathDiedAtUtcForTests(account, DateTime.UtcNow.AddYears(1).AddDays(-seedCount + i).ToString("o"));
        }

        EntityUid garden = default;
        EntityUid station = default;
        await server.WaitPost(() =>
        {
            var mob = ServerSession!.AttachedEntity!.Value;
            station = stations.GetOwningStation(mob)
                      ?? throw new InvalidOperationException("Connected mob has no owning station.");
            garden = entMan.SpawnEntity(SolreignMarkGardenSystem.GardenPrototypeId, entMan.GetComponent<TransformComponent>(mob).Coordinates);
            PaintFloorAround(garden, entMan.GetComponent<SolreignMarkGardenComponent>(garden).EchoSlotOffsets);
            mark.ResetRoundStateForTests();
            mark.MaterializeAndProjectForTests(station);
        });

        var projected = await PollAsync(() => CollectEchoes(entMan).Count >= slots);
        Assert.That(projected, Is.True, $"exactly {slots} echoes must project despite {seedCount} claimed rows");

        // Expect the LAST `slots` accounts we seeded (the most recently "died").
        var expectedNames = new HashSet<string>();
        for (var i = seedCount - slots; i < seedCount; i++)
            expectedNames.Add($"Subject {i}");

        await server.WaitAssertion(() =>
        {
            var echoes = CollectEchoes(entMan);
            Assert.That(echoes, Has.Count.EqualTo(slots), "over-capacity records must never project a physical marker");
            Assert.That(echoes.Select(e => e.Comp.CharacterName), Is.EquivalentTo(expectedNames),
                "the projected set must be exactly the most-recent-death N, never the oldest");
        });

        // Demote capacity fixtures so year-ahead timestamps do not monopolize the clamped top-N
        // for later tests (Projection needs multi-day-past ages under a 10-slot ceiling).
        for (var i = 0; i < accounts.Count; i++)
            await ledger.SetFirstDeathDiedAtUtcForTests(accounts[i],
                DateTime.UtcNow.AddYears(-50).AddDays(i).ToString("o"));
    }

    // --- 4. Independent gate (test plan §13.6) ------------------------------------------------------

    [Test]
    public async Task IndependentGate_MarkOff_EchoOn_GardenResolvesAndEchoesProject_NoPlantingVerbs()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mark = server.System<SolreignMarkGardenSystem>();
        var ledger = server.System<SeasonLedgerSystem>();
        var stations = server.System<Content.Server.Station.Systems.StationSystem>();

        // The gate-split under fire: Mark OFF, Echo ON.
        server.CfgMan.SetCVar(CCVars.SolreignMarkEnabled, false);
        server.CfgMan.SetCVar(CCVars.SolreignEchoEnabled, true);
        // Bed-sized cap (clamped max) — find ITS OWN entity by character name below.
        server.CfgMan.SetCVar(CCVars.SolreignEchoSlots, 10);

        var account = Guid.NewGuid();
        const string characterName = "Solo Subject";
        Assert.That(await ledger.TryClaimFirstDeathAsync(account, 1, characterName, "MISADVENTURE", 2, "Probationary Asset", "03"), Is.True);
        // Outrank dirty-pool residue under the clamped top-N (Capacity pins a year ahead).
        await ledger.SetFirstDeathDiedAtUtcForTests(account, DateTime.UtcNow.AddYears(2).ToString("o"));

        EntityUid mob = default;
        EntityUid beacon = default;
        EntityUid station = default;
        await server.WaitPost(() =>
        {
            mark.ResetRoundStateForTests();
            mob = ServerSession!.AttachedEntity!.Value;
            station = stations.GetOwningStation(mob)
                      ?? throw new InvalidOperationException("Connected mob has no owning station.");
            // Default-on setup may already have materialized this station's fallback garden.
            // Remove only that station-owned fixture; never rewrite another station's world.
            DeleteGardensForStation(entMan, stations, station);
            Assert.That(CollectGardens(entMan).Where(g => stations.GetOwningStation(g) == station), Is.Empty,
                "precondition: this station has no garden yet");
            beacon = entMan.SpawnEntity("SolreignWingmateBeacon", entMan.GetComponent<TransformComponent>(mob).Coordinates);
            // Paint generously BEFORE production StationPostInitEvent runs MaterializeAndProject
            // (fallback-spawns the garden AND kicks off async projection) — the garden's own tile
            // isn't known yet, so cover the full fallback ring plus the Echo bed.
            PaintFloorAround(beacon, Ring(7));
            // Production path: directed+broadcast StationPostInitEvent (StationSystem shape), not
            // the MaterializeAndProjectForTests seam — proves the SubscribeLocalEvent wiring.
            var data = entMan.GetComponent<StationDataComponent>(station);
            var ev = new StationPostInitEvent((station, data));
            entMan.EventBus.RaiseLocalEvent(station, ref ev, true);
        });

        var gardenAppeared = await PollAsync(() => CollectGardens(entMan).Count == 1);
        Assert.That(gardenAppeared, Is.True,
            "the garden must resolve/fallback-spawn even with Mark OFF, as long as Echo is ON");

        var garden = CollectGardens(entMan)[0];

        var projected = await PollAsync(() => CollectEchoes(entMan).Any(e => e.Comp.CharacterName == characterName));
        Assert.That(projected, Is.True, "Echo projection must run independently of Mark's own CVar");

        await server.WaitAssertion(() =>
        {
            Assert.That(CollectMarks(entMan), Is.Empty,
                "the gate split must never leak Mark projection behavior while solreign.mark.enabled is false");
        });

        // No planting verbs while Mark is off: TryPlant's own _enabled gate (OnGardenGetVerbs'
        // exact companion check, SolreignMarkGardenSystem.cs, unmodified by this feature) must
        // still refuse — the gate split must never leak the planting SURFACE either, only the
        // garden's physical presence.
        await server.WaitPost(() => mark.TryPlantForTests(garden, mob, MarkKind.Sapling));
        await server.WaitRunTicks(10);

        var plantAccount = ServerSession!.UserId.UserId;
        Assert.That(await ledger.GetMarkAsync(plantAccount), Is.Null,
            "planting must stay a full no-op while solreign.mark.enabled is false, even with Echo on and the garden physically present");
    }

    private static IEnumerable<Vector2i> Ring(int radius)
    {
        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
                yield return new Vector2i(x, y);
        }
    }

    // --- 5. Examine composition (test plan §13.7) ----------------------------------------------------

    [Test]
    public async Task Examine_ComposesHeaderNameEpitaphAndCauseLabel_IdenticallyForEveryExaminer()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mark = server.System<SolreignMarkGardenSystem>();
        var ledger = server.System<SeasonLedgerSystem>();
        var stations = server.System<Content.Server.Station.Systems.StationSystem>();

        server.CfgMan.SetCVar(CCVars.SolreignEchoEnabled, true);
        // Bed-sized cap (clamped max). Find ITS OWN entity by character name below.
        server.CfgMan.SetCVar(CCVars.SolreignEchoSlots, 10);

        var account = Guid.NewGuid();
        const string characterName = "Juno Pike";
        const int tours = 7;
        const string title = "Quarterly Standout";
        const FirstDeathCause cause = FirstDeathCause.Violence;

        Assert.That(await ledger.TryClaimFirstDeathAsync(account, 1, characterName, cause.ToString().ToUpperInvariant(), tours, title, "09"), Is.True);
        // Outrank dirty-pool residue under the clamped top-N (stage is not asserted here).
        await ledger.SetFirstDeathDiedAtUtcForTests(account, DateTime.UtcNow.AddYears(2).ToString("o"));

        EntityUid mob = default;
        EntityUid garden = default;
        EntityUid station = default;
        await server.WaitPost(() =>
        {
            mark.ResetRoundStateForTests();
            mob = ServerSession!.AttachedEntity!.Value;
            station = stations.GetOwningStation(mob)
                      ?? throw new InvalidOperationException("Connected mob has no owning station.");
            garden = entMan.SpawnEntity(SolreignMarkGardenSystem.GardenPrototypeId, entMan.GetComponent<TransformComponent>(mob).Coordinates);
            PaintFloorAround(garden, entMan.GetComponent<SolreignMarkGardenComponent>(garden).EchoSlotOffsets);
            mark.MaterializeAndProjectForTests(station);
        });

        Assert.That(await PollAsync(() => CollectEchoes(entMan).Any(e => e.Comp.CharacterName == characterName)), Is.True,
            "setup failed: echo never seated");

        var expectedEpitaph = FirstDeathEpitaphPicker.Pick(tours, cause, title);
        var expectedCauseLabel = FirstDeathCopy.CauseLabelFor(cause);

        await server.WaitAssertion(() =>
        {
            var (uid, comp) = CollectEchoes(entMan).Single(e => e.Comp.CharacterName == characterName);
            Assert.Multiple(() =>
            {
                Assert.That(comp.CharacterName, Is.EqualTo(characterName));
                Assert.That(comp.Cause, Is.EqualTo(cause));
                Assert.That(comp.ToursAtDeath, Is.EqualTo(tours));
                Assert.That(comp.TitleAtDeath, Is.EqualTo(title));
            });

            // Owner examine and a total stranger's examine must read byte-identical — no
            // owner/stranger split on an Echo (EOD-spec §8): a first death is already public.
            var ownerEvent = new ExaminedEvent(new FormattedMessage(), uid, mob, isInDetailsRange: true, hasDescription: false);
            entMan.EventBus.RaiseLocalEvent(uid, ownerEvent);
            var ownerText = ownerEvent.GetTotalMessage().ToString();

            var strangerEvent = new ExaminedEvent(new FormattedMessage(), uid, garden, isInDetailsRange: true, hasDescription: false);
            entMan.EventBus.RaiseLocalEvent(uid, strangerEvent);
            var strangerText = strangerEvent.GetTotalMessage().ToString();

            Assert.Multiple(() =>
            {
                Assert.That(ownerText, Is.EqualTo(strangerText), "an Echo's examine must be identical for every examiner — no owner/stranger split");
                Assert.That(ownerText, Does.Contain(characterName), "the public naming line must carry the character name");
                Assert.That(ownerText, Does.Contain(expectedEpitaph.Text),
                    "the live-recomputed epitaph must be byte-identical to what the crypt plaque already carries — no drift");
                Assert.That(ownerText, Does.Contain(expectedCauseLabel), "the closed-vocabulary cause label must render");
            });
        });
    }

    // --- 6. No-orphans (test plan §13.8) -------------------------------------------------------------

    [Test]
    public async Task NoOrphans_UnpaintedSlotTile_SkipsTheSpawn_NeverLeavesAnUnanchoredEntity()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mark = server.System<SolreignMarkGardenSystem>();
        var ledger = server.System<SeasonLedgerSystem>();
        var stations = server.System<Content.Server.Station.Systems.StationSystem>();

        server.CfgMan.SetCVar(CCVars.SolreignEchoEnabled, true);
        server.CfgMan.SetCVar(CCVars.SolreignEchoSlots, 10);

        var account = Guid.NewGuid();
        Assert.That(await ledger.TryClaimFirstDeathAsync(account, 1, "Orphan Case", "UNKNOWN", 0, "Probationary Asset", "01"), Is.True);
        // Must be among the clamped top-N or the test observes zero Echoes for the wrong reason
        // (Capacity's future-dated residue outranking this row). Stage is not asserted here.
        await ledger.SetFirstDeathDiedAtUtcForTests(account, DateTime.UtcNow.AddYears(2).ToString("o"));
        var topN = await ledger.GetAllFirstDeathsAsync(10);
        Assert.That(topN.Select(r => r.CharacterName), Does.Contain("Orphan Case"),
            "precondition: Orphan Case must be in the projection top-N so a no-spawn is the no-orphans law, not a capacity miss");

        var completedBefore = 0;
        var failedDeletesBefore = 0;
        var skippedBefore = 0;
        EntityUid garden = default;
        EntityUid station = default;
        await server.WaitPost(() =>
        {
            mark.ResetRoundStateForTests();
            completedBefore = mark.EchoProjectionCompletedCountForTests;
            failedDeletesBefore = mark.EchoFailedAnchorDeletesForTests;
            skippedBefore = mark.EchoSkippedTilesForTests;
            var mob = ServerSession!.AttachedEntity!.Value;
            station = stations.GetOwningStation(mob)
                      ?? throw new InvalidOperationException("Connected mob has no owning station.");
            garden = entMan.SpawnEntity(SolreignMarkGardenSystem.GardenPrototypeId, entMan.GetComponent<TransformComponent>(mob).Coordinates);
            // Deliberately DO NOT paint floor around the garden — slot 0 (the only claimed row's
            // ordinal slot) resolves to an unpainted/space tile on the pool's tiny default map.
            mark.MaterializeAndProjectForTests(station);
        });

        // Deterministic: wait for the async-void projection's finally (ledger await may take
        // longer than a fixed 10-tick budget — that was a false-pass window for zero Echoes).
        Assert.That(await PollAsync(() => mark.EchoProjectionCompletedCountForTests > completedBefore),
            Is.True, "ProjectEchoes must finish (completion signal) before asserting zero Echoes");

        await server.WaitAssertion(() =>
        {
            // The skip branch itself must have executed — completion alone could mean an early
            // abort/exception exit (the completion counter increments on EVERY exit path).
            Assert.That(mark.EchoSkippedTilesForTests, Is.GreaterThan(skippedBefore),
                "the space/missing-tile skip branch must actually run for the projected top-N row");
            var echoes = CollectEchoes(entMan);
            Assert.That(echoes, Is.Empty,
                "a space/missing slot tile must skip the spawn entirely — never seat, then delete, an entity");
            // Non-vacuous: with a top-N claim present and projection complete, zero Echoes is the
            // skip law — and the failed-anchor counter must stay put (never spawn-then-Del).
            Assert.That(mark.EchoFailedAnchorDeletesForTests, Is.EqualTo(failedDeletesBefore),
                "space/missing tiles must skip before Spawn — the failed-anchor Del counter must not move");
        });
    }

    [Test]
    public async Task NoOrphans_FailedAnchor_DeletesTheEntity_AndCountsIt()
    {
        var server = Server;
        var entMan = server.EntMan;
        var mark = server.System<SolreignMarkGardenSystem>();
        var ledger = server.System<SeasonLedgerSystem>();
        var stations = server.System<Content.Server.Station.Systems.StationSystem>();

        server.CfgMan.SetCVar(CCVars.SolreignEchoEnabled, true);
        server.CfgMan.SetCVar(CCVars.SolreignEchoSlots, 10);

        var account = Guid.NewGuid();
        const string characterName = "Anchor Fail Case";
        Assert.That(await ledger.TryClaimFirstDeathAsync(account, 1, characterName, "UNKNOWN", 0, "Probationary Asset", "01"), Is.True);
        // Outrank dirty-pool residue under the clamped top-N so this row is actually projected.
        await ledger.SetFirstDeathDiedAtUtcForTests(account, DateTime.UtcNow.AddYears(2).ToString("o"));
        var topN = await ledger.GetAllFirstDeathsAsync(10);
        Assert.That(topN.Select(r => r.CharacterName), Does.Contain(characterName),
            "precondition: claim must be in the projection top-N so a no-seat is the failed-anchor law, not a capacity miss");

        var completedBefore = 0;
        var failedDeletesBefore = 0;
        EntityUid garden = default;
        EntityUid station = default;
        await server.WaitPost(() =>
        {
            mark.ResetRoundStateForTests();
            mark.ForceEchoAnchorFailureForTests = true;
            completedBefore = mark.EchoProjectionCompletedCountForTests;
            failedDeletesBefore = mark.EchoFailedAnchorDeletesForTests;
            var mob = ServerSession!.AttachedEntity!.Value;
            station = stations.GetOwningStation(mob)
                      ?? throw new InvalidOperationException("Connected mob has no owning station.");
            garden = entMan.SpawnEntity(SolreignMarkGardenSystem.GardenPrototypeId, entMan.GetComponent<TransformComponent>(mob).Coordinates);
            // Paint the Echo bed so the tile check PASSES and Spawn runs — then the force seam
            // makes the post-spawn anchor check fail so Del (no-orphans) is the only exit.
            PaintFloorAround(garden, entMan.GetComponent<SolreignMarkGardenComponent>(garden).EchoSlotOffsets);
            mark.MaterializeAndProjectForTests(station);
        });

        try
        {
            Assert.That(await PollAsync(() => mark.EchoProjectionCompletedCountForTests > completedBefore),
                Is.True, "ProjectEchoes must finish before asserting failed-anchor deletions");

            await server.WaitAssertion(() =>
            {
                Assert.That(CollectEchoes(entMan), Is.Empty,
                    "a failed-to-anchor Echo must be deleted — SolreignEchoComponent is only attached after a successful seat");
                Assert.That(mark.EchoFailedAnchorDeletesForTests, Is.GreaterThan(failedDeletesBefore),
                    "the failed-anchor Del branch must run and increment the deletion counter");
                // UID-level proof: the concrete spawned entity must be gone — the counter alone
                // would still tick if Del() were removed or failed silently.
                Assert.That(mark.EchoLastFailedAnchorUidForTests, Is.Not.Null,
                    "the failed-anchor path must record the spawned-then-deleted UID");
                Assert.That(entMan.Deleted(mark.EchoLastFailedAnchorUidForTests!.Value), Is.True,
                    "the recorded failed-anchor entity must actually be deleted from the entity manager");
                Assert.That(CollectEchoes(entMan).Any(e => e.Comp.CharacterName == characterName), Is.False,
                    "the projected claim must not leave a live Echo entity under its character name");
            });
        }
        finally
        {
            // Dirty pool: never leave the force seam armed for a later test in this fixture.
            await server.WaitPost(() => mark.ForceEchoAnchorFailureForTests = false);
        }
    }
}
