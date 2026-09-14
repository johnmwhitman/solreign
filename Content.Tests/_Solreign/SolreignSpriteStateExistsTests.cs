#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using YamlDotNet.RepresentationModel;

namespace Content.Tests._Solreign;

/// <summary>
///     Permanent, CI-cheap, no-game-boot gate for the "prototype points at a sprite state that does
///     not exist" defect class. A missing state does not fail the server, does not fail the YAML
///     linter, and does not fail any other test — Sprite/DamageStateVisuals render the engine's
///     error sprite while Item/Clothing defaults simply omit the held/worn layer in the CLIENT.
///     This is the same blind spot that produced the two client-load outages
///     (see <see cref="SolreignRealSandboxCheckTest"/>).
///
///     WHY THIS EXISTS (a defect this repo actually shipped):
///     Wave 18 (<c>a2736d9bb7</c>, sprite factory batch beta01) repointed <c>MobCorgiIan</c>'s
///     <c>Sprite.sprite</c> from <c>Mobs/Pets/corgi.rsi</c> to <c>_Solreign/scarlet.rsi</c>.
///     <c>MobCorgiLisa</c> and <c>MobCorgiMouse</c> inherit from Ian and override only <c>state</c>,
///     not <c>sprite</c> — SS14 merges components per-DATAFIELD, so both silently followed the parent
///     onto an RSI with no <c>lisa</c>/<c>real_mouse</c> state. Both shipped broken and were found by
///     hand later. Scoped repo-wide, not to <c>_Solreign/</c>: the Wave 18 break was in an UPSTREAM
///     file, so a Solreign-scoped gate would have missed it entirely.
///
///     ENGINE SEMANTICS THIS MIRRORS (verified in
///     <c>RobustToolbox/Robust.Client/GameObjects/Components/Renderable/SpriteComponent.cs</c> —
///     do not "fix" these to match intuition):
///     <list type="bullet">
///       <item>A layer's RSI is <c>layer.sprite ?? Sprite.sprite</c>.</item>
///       <item>The component-level <c>state:</c> is promoted to layer 0 <b>only when there are no
///             layers at all</b> (the <c>layerDatums.Count == 0</c> branch). If layers exist —
///             including INHERITED layers — a sibling <c>state:</c> is dead config that never
///             renders. Flagging it would false-positive on <c>CowToolbox</c>,
///             <c>SpawnPointObserver</c> and <c>FoodKebabSkewer</c>, all correct today.</item>
///       <item><c>DamageStateVisuals</c> sets states on the layer named by its MAP KEY, so those
///             states resolve against that mapped layer's RSI, not blindly against the base
///             (185 layers in this repo carry their own <c>sprite:</c>). And
///             <c>DamageStateVisualizerSystem</c> does <c>if (!LayerMapTryGet(key)) continue;</c> —
///             a state whose key is not mapped is never applied, so it is dead config, not a broken
///             sprite. Checking those anyway false-positives on <c>MobCarpHolo</c>,
///             <c>MobCarpMagic</c> and <c>MobSheepSpace</c>, which inherit an abstract parent's
///             <c>BaseUnshaded: mouth</c> while only ever declaring a <c>Base</c> layer.</item>
///       <item><c>abstract: true</c> prototypes are templates, never instantiated; their placeholder
///             states are legitimately unresolvable (6 exist today, e.g.
///             <c>DrinkBottleGlassBase</c>) and are skipped.</item>
///     </list>
///
///     WHAT THIS GATE DOES **NOT** COVER — read this before trusting a green run. In addition to
///     <c>Sprite</c> (base + layers) and <c>DamageStateVisuals</c>, it validates the default
///     left/right in-hand and equipped states synthesized when an <c>Item.heldPrefix</c>,
///     <c>Clothing.equippedPrefix</c>, or <c>Clothing.equippedState</c> is declared. It deliberately
///     skips a hand/slot with explicit <c>inhandVisuals</c>/<c>clothingVisuals</c>: those are
///     arbitrary layer specs and belong to the later explicit-visual slice. It does NOT check:
///     prefix-free Item/Clothing defaults, explicit visual layer specs,
///     <c>GenericVisualizer</c>, <c>RandomSprite</c>, <c>MagazineVisuals</c> and other runtime
///     state synthesisers, <c>Inventory.displacements</c>, non-entity prototypes (decals,
///     construction graphs, markings, status icons), or any <c>SpriteSpecifier</c> built in C#.
///     A green result here means "the states these two components name exist" — NOT "every sprite
///     in the game resolves". Those are queued, not silently assumed safe.
///
///     SELF-TEST FLOORS AND THE SYNTHETIC DETECTOR TEST are load-bearing, not decoration. While
///     this was built the probe was wrong three separate times and every failure was SILENT — it
///     shrank coverage while still reporting "clean": SS14 <c>!type:</c> tags made a strict loader
///     skip 1001 of 2209 files; YAML 1.1 retyped <c>state: on</c> into a boolean; a UTF-8 BOM made
///     69 real RSIs look missing. A gate that can pass by parsing nothing is not a gate, so this
///     fixture asserts a minimum volume of work, fails on ANY unreadable or non-sequence prototype
///     file, and proves on every run that the detector still fires (see
///     <see cref="Detector_ActuallyFires_OnASyntheticallyBrokenPrototype"/>).
/// </summary>
[TestFixture]
public sealed class SolreignSpriteStateExistsTests
{
    /// <summary>Floors sit ~10% under the 2026-07-26 measurement: loose enough for normal content
    /// churn, tight enough that a parser regression dropping a subtree cannot stay green.</summary>
    private const int MinPrototypeFiles = 1980;    // actual: 2209
    private const int MinEntityDefs = 10000;       // actual: 11133
    private const int MinStateRefsChecked = 19300; // actual after prefix slice: 21515
    private const int MinHeldPrefixRefsChecked = 1420;     // actual: 1586
    private const int MinEquippedPrefixRefsChecked = 126;  // upstream-v286 actual: 140

    /// <summary>
    ///     Exact pre-existing prefix debt measured when this slice first reached the
    ///     repository on 2026-07-26. ItemSystem fails soft for these (the held layer is absent,
    ///     rather than an error sprite), so this gate prevents any NEW misses while the inherited
    ///     held-visual debt is retired separately. Each entry permits exactly the named prefix's left and
    ///     right state; the debt-count assertion below fails when either side is fixed so this list
    ///     cannot silently become a permanent blanket exemption.
    /// </summary>
    private sealed record HeldPrefixDebtSpec(string Rsi, string Prefix);

    private static readonly IReadOnlyDictionary<string, HeldPrefixDebtSpec> KnownHeldPrefixDebt =
        new Dictionary<string, HeldPrefixDebtSpec>(StringComparer.Ordinal)
        {
            ["CableMVStack"] = new("Objects/Tools/cable-coils.rsi", "composite"),
            ["CableMVStack1"] = new("Objects/Tools/cable-coils.rsi", "composite"),
            ["CableMVStack10"] = new("Objects/Tools/cable-coils.rsi", "composite"),
            ["ClothingEyesGlassesGarGiga"] = new("Clothing/Eyes/Glasses/gar.rsi", "super"),
            ["CrayonBorg"] = new("Objects/Fun/crayons.rsi", "electric"),
            ["FloorTileItemArcadeBlue"] = new("Objects/Tiles/tile.rsi", "arcadeblue"),
            ["FloorTileItemArcadeRed"] = new("Objects/Tiles/tile.rsi", "arcadered"),
            ["FloorTileItemDarkAstroGrass"] = new("Objects/Tiles/tile.rsi", "darkgrass"),
            ["FloorTileItemGrassJungle"] = new("Objects/Tiles/tile.rsi", "grassjungle"),
            ["FloorTileItemShuttleBlack"] = new("Objects/Tiles/tile.rsi", "shuttleblack"),
            ["FloorTileItemShuttleBlue"] = new("Objects/Tiles/tile.rsi", "shuttleblue"),
            ["FloorTileItemShuttleGrey"] = new("Objects/Tiles/tile.rsi", "shuttlegrey"),
            ["FloorTileItemShuttleOrange"] = new("Objects/Tiles/tile.rsi", "shuttleorange"),
            ["FloorTileItemShuttlePurple"] = new("Objects/Tiles/tile.rsi", "shuttlepurple"),
            ["FloorTileItemShuttleRed"] = new("Objects/Tiles/tile.rsi", "shuttlered"),
            ["FloorTileItemShuttleWhite"] = new("Objects/Tiles/tile.rsi", "shuttlewhite"),
            ["FoodBoxNugget"] = new("Objects/Consumable/Food/Baked/nuggets.rsi", "box"),
            ["GasPipeBendAlt1"] = new("Structures/Piping/Atmospherics/pipe_alt1.rsi", "Bend"),
            ["GasPipeBendAlt2"] = new("Structures/Piping/Atmospherics/pipe_alt2.rsi", "Bend"),
            ["GasPipeFourwayAlt1"] = new("Structures/Piping/Atmospherics/pipe_alt1.rsi", "Fourway"),
            ["GasPipeFourwayAlt2"] = new("Structures/Piping/Atmospherics/pipe_alt2.rsi", "Fourway"),
            ["GasPipeTJunctionAlt1"] = new("Structures/Piping/Atmospherics/pipe_alt1.rsi", "TJunction"),
            ["GasPipeTJunctionAlt2"] = new("Structures/Piping/Atmospherics/pipe_alt2.rsi", "TJunction"),
            ["GorlexMatchbox"] = new("Objects/Tools/Lighters/gorlex.rsi", "matchbox"),
            ["LuxuryPen"] = new("Objects/Misc/pens.rsi", "luxury_pen"),
            ["OrganArachnidLungs"] = new("Mobs/Species/Arachnid/organs.rsi", "lungs"),
            ["PlushieSheep"] = new("Objects/Fun/Plushies/carp.rsi", "sheeptoy"),
            ["PlushieSnake"] = new("Objects/Fun/Plushies/snake.rsi", "plushiesnake"),
            ["PlushieSpaceSheep"] = new("Objects/Fun/Plushies/carp.rsi", "sheeptoy"),
            ["SolreignEgg09"] = new("_Solreign/EasterEggs/egg09_astral_berry.rsi", "produce"),
            ["SolreignEgg11"] = new("_Solreign/EasterEggs/egg11_winged_berry.rsi", "produce"),
            ["SolreignEgg13"] = new("_Solreign/EasterEggs/egg13_fungal_growth.rsi", "produce"),
            ["SolreignEgg18"] = new("_Solreign/EasterEggs/egg18_antiviral_herb.rsi", "produce"),
            ["TrashBananiumPeel"] = new("Objects/Materials/materials.rsi", "peel"),
            ["TrashCherryPit"] = new("Objects/Specific/Hydroponics/cherry.rsi", "pit"),
        };

    private static HashSet<string> ExpectedKnownDebtKeys()
    {
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, spec) in KnownHeldPrefixDebt)
        {
            expected.Add(DebtKey(id, spec.Rsi, $"{spec.Prefix}-inhand-left", "Item.heldPrefix.left"));
            expected.Add(DebtKey(id, spec.Rsi, $"{spec.Prefix}-inhand-right", "Item.heldPrefix.right"));
        }
        return expected;
    }

    private sealed record LayerDef(string? State, string? Sprite, List<string> Map);

    private sealed record DamageStateRef(string LayerKey, string State, string DamageKey);

    private sealed record EntityDef(
        string Id,
        List<string> Parents,
        bool IsAbstract,
        string? Sprite,
        List<LayerDef>? Layers,
        string? TopState,
        List<DamageStateRef>? DamageStates,
        string? ItemSprite,
        string? HeldPrefix,
        HashSet<string>? InhandVisualKeys,
        string? ClothingSprite,
        string? EquippedPrefix,
        string? EquippedState,
        List<string>? ClothingSlots,
        HashSet<string>? ClothingVisualKeys,
        string File);

    private sealed record CheckResult(
        int FilesScanned,
        int EntityCount,
        int RefsChecked,
        int HeldPrefixRefsChecked,
        int EquippedPrefixRefsChecked,
        List<string> Unreadable,
        List<string> DuplicateIds,
        List<string> UnresolvedRsi,
        HashSet<string> KnownPrefixDebt,
        List<string> Violations);

    [Test]
    public void EveryPrototype_SpriteStates_ExistInTheRsiTheyResolveTo()
    {
        var repoRoot = LocateRepoRoot();
        var result = Run(
            Path.Combine(repoRoot, "Resources", "Prototypes"),
            new[]
            {
                // Content textures first, engine second: `error.rsi` and other builtins live under
                // RobustToolbox and are legitimately referenced by content prototypes.
                Path.Combine(repoRoot, "Resources", "Textures"),
                Path.Combine(repoRoot, "RobustToolbox", "Resources", "Textures"),
            });

        // Printed so the floor MARGIN is visible in CI output instead of inferred from a green tick.
        TestContext.Out.WriteLine(
            $"sprite-state gate: {result.FilesScanned} files (floor {MinPrototypeFiles}), " +
            $"{result.EntityCount} prototypes (floor {MinEntityDefs}), " +
            $"{result.RefsChecked} state refs resolved (floor {MinStateRefsChecked}), " +
            $"{result.HeldPrefixRefsChecked} held-prefix refs, " +
            $"{result.EquippedPrefixRefsChecked} equipped-prefix refs, " +
            $"{result.KnownPrefixDebt.Count} exact legacy prefix misses quarantined.");

        Assert.Multiple(() =>
        {
            // --- anti-silence: a file we cannot read is a COVERAGE HOLE, not a pass -------------
            Assert.That(result.Unreadable, Is.Empty,
                "Prototype files could not be read as a YAML sequence, so this gate's coverage is " +
                "silently reduced. Fix the parser or the file; never ignore:" + Environment.NewLine +
                "  " + string.Join(Environment.NewLine + "  ", result.Unreadable.Take(20)));

            Assert.That(result.FilesScanned, Is.GreaterThanOrEqualTo(MinPrototypeFiles),
                $"Only {result.FilesScanned} prototype files scanned — this gate must not pass by " +
                "scanning nothing.");
            Assert.That(result.EntityCount, Is.GreaterThanOrEqualTo(MinEntityDefs),
                $"Only {result.EntityCount} entity prototypes parsed — the reader is probably " +
                "dropping documents it does not understand.");
            Assert.That(result.RefsChecked, Is.GreaterThanOrEqualTo(MinStateRefsChecked),
                $"Only {result.RefsChecked} sprite-state references were actually resolved.");
            Assert.That(result.HeldPrefixRefsChecked, Is.GreaterThanOrEqualTo(MinHeldPrefixRefsChecked),
                $"Only {result.HeldPrefixRefsChecked} held-prefix references were resolved.");
            Assert.That(result.EquippedPrefixRefsChecked, Is.GreaterThanOrEqualTo(MinEquippedPrefixRefsChecked),
                $"Only {result.EquippedPrefixRefsChecked} equipped-prefix references were resolved.");
            var expectedDebt = ExpectedKnownDebtKeys();
            var missingDebt = expectedDebt.Except(result.KnownPrefixDebt).OrderBy(v => v).ToList();
            var unexpectedDebt = result.KnownPrefixDebt.Except(expectedDebt).OrderBy(v => v).ToList();
            Assert.That(result.KnownPrefixDebt, Is.EquivalentTo(expectedDebt),
                "The exact legacy prefix debt changed. Fix both hands and remove the prototype " +
                "ledger entry, or repair a rebinding/new miss; never rebaseline it blindly." +
                Environment.NewLine + "Missing expected debt:" + Environment.NewLine + "  " +
                string.Join(Environment.NewLine + "  ", missingDebt) +
                Environment.NewLine + "Unexpected debt:" + Environment.NewLine + "  " +
                string.Join(Environment.NewLine + "  ", unexpectedDebt));

            Assert.That(result.DuplicateIds, Is.Empty,
                "Duplicate entity prototype ids — this gate would silently validate only one of " +
                "them:" + Environment.NewLine + "  " +
                string.Join(Environment.NewLine + "  ", result.DuplicateIds.Take(20)));

            Assert.That(result.UnresolvedRsi, Is.Empty,
                "Prototypes reference an RSI that exists in neither the content nor the engine " +
                "texture root (typo'd path, or art never committed):" + Environment.NewLine + "  " +
                string.Join(Environment.NewLine + "  ", result.UnresolvedRsi.Take(30)));

            Assert.That(result.Violations, Is.Empty,
                $"{result.Violations.Count} prototype(s) name a sprite state that does not exist in " +
                "the RSI they resolve to. Sprite/DamageStateVisuals misses render as the engine " +
                "ERROR sprite; Item/Clothing defaults fail soft by rendering no held/worn layer. " +
                "No other gate catches either class. Remember SS14 inherits Sprite per-datafield: a " +
                "child overriding only 'state' still follows its parent's 'sprite'." +
                Environment.NewLine + "  " +
                string.Join(Environment.NewLine + "  ", result.Violations));
        });
    }

    /// <summary>
    ///     Proves the detector still has teeth. The repo-wide test above passing is consistent with
    ///     BOTH "content is clean" and "the checker silently stopped checking" — this test
    ///     distinguishes them on every run by feeding it a prototype tree that is known-broken in
    ///     exactly the way Wave 18 was: a child that overrides only <c>state</c> while its parent's
    ///     <c>sprite</c> moves to an RSI that has no such state.
    /// </summary>
    [Test]
    public void Detector_ActuallyFires_OnASyntheticallyBrokenPrototype()
    {
        var temp = Path.Combine(Path.GetTempPath(), "solreign-sprite-gate-" + Guid.NewGuid().ToString("N"));
        var protoDir = Path.Combine(temp, "Prototypes");
        var goodRsi = Path.Combine(temp, "Textures", "fake_good.rsi");
        var badRsi = Path.Combine(temp, "Textures", "fake_bad.rsi");

        try
        {
            Directory.CreateDirectory(protoDir);
            Directory.CreateDirectory(goodRsi);
            Directory.CreateDirectory(badRsi);

            File.WriteAllText(Path.Combine(goodRsi, "meta.json"),
                """{"version":1,"size":{"x":32,"y":32},"states":[{"name":"pet"},{"name":"pet_dead"}]}""");
            File.WriteAllText(Path.Combine(badRsi, "meta.json"),
                """{"version":1,"size":{"x":32,"y":32},"states":[{"name":"other"}]}""");

            File.WriteAllText(Path.Combine(protoDir, "fixture.yml"),
                """
                - type: entity
                  id: FixtureParent
                  components:
                  - type: Sprite
                    sprite: fake_good.rsi
                    layers:
                    - map: ["enum.DamageStateVisualLayers.Base"]
                      state: pet

                - type: entity
                  id: FixtureHealthyChild
                  parent: FixtureParent
                  components:
                  - type: DamageStateVisuals
                    states:
                      Alive:
                        Base: pet
                      Dead:
                        Base: pet_dead

                - type: entity
                  id: FixtureAbstractIsSkipped
                  parent: FixtureParent
                  abstract: true
                  components:
                  - type: Sprite
                    sprite: fake_bad.rsi

                - type: entity
                  id: FixtureRepointedChild
                  parent: FixtureParent
                  components:
                  - type: Sprite
                    sprite: fake_bad.rsi
                """);

            var result = Run(protoDir, new[] { Path.Combine(temp, "Textures") });

            Assert.Multiple(() =>
            {
                Assert.That(result.Unreadable, Is.Empty);
                Assert.That(result.EntityCount, Is.EqualTo(4));

                // The repointed child inherits layer state 'pet', which fake_bad.rsi does not have.
                Assert.That(result.Violations.Any(v => v.Contains("FixtureRepointedChild", StringComparison.Ordinal)),
                    Is.True,
                    "Detector FAILED to fire on the Wave 18 pattern (child repoints sprite, inherits " +
                    "the parent's layer state). This gate is no longer protecting anything." +
                    Environment.NewLine + string.Join(Environment.NewLine, result.Violations));

                // Inherited DamageStateVisuals must be resolved, not ignored.
                Assert.That(result.Violations.Any(v => v.Contains("FixtureHealthyChild", StringComparison.Ordinal)),
                    Is.False, "False positive on a child whose inherited states all exist.");

                Assert.That(result.Violations.Any(v => v.Contains("FixtureAbstractIsSkipped", StringComparison.Ordinal)),
                    Is.False, "abstract: true prototypes must be skipped.");
            });
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    [Test]
    public void Detector_ActuallyFires_OnAMissingHeldPrefixState()
    {
        var temp = Path.Combine(Path.GetTempPath(), "solreign-held-prefix-gate-" + Guid.NewGuid().ToString("N"));
        var protoDir = Path.Combine(temp, "Prototypes");
        var itemRsi = Path.Combine(temp, "Textures", "held_item.rsi");

        try
        {
            Directory.CreateDirectory(protoDir);
            Directory.CreateDirectory(itemRsi);

            File.WriteAllText(Path.Combine(itemRsi, "meta.json"),
                """
                {"version":1,"size":{"x":32,"y":32},"states":[
                  {"name":"active-inhand-left"}
                ]}
                """);

            File.WriteAllText(Path.Combine(protoDir, "fixture.yml"),
                """
                - type: entity
                  id: FixtureBrokenHeldPrefix
                  components:
                  - type: Item
                    sprite: held_item.rsi
                    heldPrefix: active
                """);

            var result = Run(protoDir, new[] { Path.Combine(temp, "Textures") });

            Assert.That(result.Violations.Any(v =>
                    v.Contains("FixtureBrokenHeldPrefix", StringComparison.Ordinal) &&
                    v.Contains("active-inhand-right", StringComparison.Ordinal)),
                Is.True,
                "Detector FAILED to fire when Item.heldPrefix synthesizes an in-hand state that " +
                "does not exist. The gate is not covering the queued Item prefix blind spot." +
                Environment.NewLine + string.Join(Environment.NewLine, result.Violations));
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    [Test]
    public void Detector_ActuallyFires_OnAMissingEquippedPrefixState()
    {
        var temp = Path.Combine(Path.GetTempPath(), "solreign-equipped-prefix-gate-" + Guid.NewGuid().ToString("N"));
        var protoDir = Path.Combine(temp, "Prototypes");
        var clothingRsi = Path.Combine(temp, "Textures", "prefix_hat.rsi");

        try
        {
            Directory.CreateDirectory(protoDir);
            Directory.CreateDirectory(clothingRsi);

            File.WriteAllText(Path.Combine(clothingRsi, "meta.json"),
                """
                {"version":1,"size":{"x":32,"y":32},"states":[
                  {"name":"open-equipped-HELMET"}
                ]}
                """);

            File.WriteAllText(Path.Combine(protoDir, "fixture.yml"),
                """
                - type: entity
                  id: FixtureBrokenEquippedPrefix
                  components:
                  - type: Clothing
                    sprite: prefix_hat.rsi
                    slots: [head, mask]
                    equippedPrefix: open
                """);

            var result = Run(protoDir, new[] { Path.Combine(temp, "Textures") });

            Assert.That(result.Violations.Any(v =>
                    v.Contains("FixtureBrokenEquippedPrefix", StringComparison.Ordinal) &&
                    v.Contains("open-equipped-MASK", StringComparison.Ordinal)),
                Is.True,
                "Detector FAILED to fire when Clothing.equippedPrefix synthesizes an equipped " +
                "state that does not exist. The gate is not covering the queued Clothing prefix " +
                "blind spot." + Environment.NewLine + string.Join(Environment.NewLine, result.Violations));
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    [Test]
    public void PrefixCoverage_FailsClosed_OnDebtRebindingMissingRsiAndUnknownSlots()
    {
        var temp = Path.Combine(Path.GetTempPath(), "solreign-prefix-fail-closed-" + Guid.NewGuid().ToString("N"));
        var protoDir = Path.Combine(temp, "Prototypes");
        var reboundRsi = Path.Combine(temp, "Textures", "rebound.rsi");

        try
        {
            Directory.CreateDirectory(protoDir);
            Directory.CreateDirectory(reboundRsi);
            File.WriteAllText(Path.Combine(reboundRsi, "meta.json"),
                """{"version":1,"size":{"x":32,"y":32},"states":[{"name":"other"}]}""");

            File.WriteAllText(Path.Combine(protoDir, "fixture.yml"),
                """
                # Same allowlisted id/prefix, DIFFERENT RSI: must not inherit the old debt waiver.
                - type: entity
                  id: CableMVStack
                  components:
                  - type: Item
                    sprite: rebound.rsi
                    heldPrefix: composite

                - type: entity
                  id: FixtureHeldPrefixWithoutRsi
                  components:
                  - type: Item
                    heldPrefix: active

                - type: entity
                  id: FixtureEquippedPrefixWithoutRsi
                  components:
                  - type: Clothing
                    slots: [head]
                    equippedPrefix: open

                - type: entity
                  id: FixtureUnknownVisualSlot
                  components:
                  - type: Clothing
                    sprite: rebound.rsi
                    slots: [tail]
                    equippedPrefix: open
                """);

            var result = Run(protoDir, new[] { Path.Combine(temp, "Textures") });

            Assert.Multiple(() =>
            {
                Assert.That(result.KnownPrefixDebt, Is.Empty,
                    "A debt waiver must bind to the exact RSI, not only a reusable prototype id.");
                Assert.That(result.Violations.Any(v =>
                    v.Contains("CableMVStack", StringComparison.Ordinal) &&
                    v.Contains("rebound.rsi", StringComparison.Ordinal)), Is.True);
                Assert.That(result.Violations.Any(v =>
                    v.Contains("FixtureHeldPrefixWithoutRsi", StringComparison.Ordinal) &&
                    v.Contains("has no RSI", StringComparison.Ordinal)), Is.True);
                Assert.That(result.Violations.Any(v =>
                    v.Contains("FixtureEquippedPrefixWithoutRsi", StringComparison.Ordinal) &&
                    v.Contains("has no RSI", StringComparison.Ordinal)), Is.True);
                Assert.That(result.Violations.Any(v =>
                    v.Contains("FixtureUnknownVisualSlot", StringComparison.Ordinal) &&
                    v.Contains("not mapped", StringComparison.Ordinal)), Is.True);
            });
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    [TestCase("HEAD", "head", "HELMET")]
    [TestCase("eyes", "eyes", "EYES")]
    [TestCase("EARS", "ears", "EARS")]
    [TestCase("mask", "mask", "MASK")]
    [TestCase("outerClothing", "outerClothing", "OUTERCLOTHING")]
    [TestCase("innerclothing", "jumpsuit", "INNERCLOTHING")]
    [TestCase("NECK", "neck", "NECK")]
    [TestCase("back", "back", "BACKPACK")]
    [TestCase("BELT", "belt", "BELT")]
    [TestCase("gloves", "gloves", "HAND")]
    [TestCase("idcard", "id", "IDCARD")]
    [TestCase("LEGS", "legs", "legs")]
    [TestCase("feet", "shoes", "FEET")]
    [TestCase("suitStorage", "suitstorage", "SUITSTORAGE")]
    public void ClothingSlotMap_CoversEveryCurrentWornSlotFlag(
        string rawSlot,
        string expectedRuntimeSlot,
        string expectedRsiSlot)
    {
        var mapped = MapClothingSlots(rawSlot);
        Assert.That(mapped, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(mapped[0].RuntimeSlot, Is.EqualTo(expectedRuntimeSlot));
            Assert.That(mapped[0].RsiSlot, Is.EqualTo(expectedRsiSlot));
        });
    }

    [Test]
    public void ClothingSlotMap_PocketCoversBothRuntimeInventorySlots()
    {
        Assert.That(MapClothingSlots("POCKET"), Is.EqualTo(new[]
        {
            ("pocket1", "POCKET1"),
            ("pocket2", "POCKET2"),
        }));
    }

    /// <summary>
    ///     Ian is the station's named mascot and a deliberate reference dog; the station is dressed
    ///     with corgi iconography (PosterLegitIan, PosterLegitLoveIan, ToyIan, BookIanOcean) and
    ///     <c>SpawnMobCorgi</c> sits on all seven rotation maps. Wave 18 put him — and Old Ian and
    ///     Puppy Ian — on Scarlet's black-cocker-spaniel RSI, which also meant two DIFFERENT named
    ///     station pets rendered as the same body. Pin the intent so a future art batch cannot
    ///     quietly re-skin him again.
    /// </summary>
    [Test]
    public void IanFamily_UsesTheCorgiRsi_AndDoesNotShareABodyWithScarlet()
    {
        var repoRoot = LocateRepoRoot();
        var unreadable = new List<string>();
        var defs = ParseEntities(
                Path.Combine(repoRoot, "Resources", "Prototypes", "Entities", "Mobs", "NPCs", "pets.yml"), unreadable)
            .Concat(ParseEntities(
                Path.Combine(repoRoot, "Resources", "Prototypes", "_Solreign", "Entities", "delighters", "scarlet.yml"), unreadable))
            .ToDictionary(d => d.Id, StringComparer.Ordinal);

        Assert.That(unreadable, Is.Empty);

        var expected = new[]
        {
            ("MobCorgiIan", "ian"),
            ("MobCorgiIanOld", "old_ian"),
            ("MobCorgiIanPup", "puppy"),
            ("MobCorgiLisa", "lisa"),
            ("MobCorgiMouse", "real_mouse"),
        };

        Assert.Multiple(() =>
        {
            foreach (var (id, state) in expected)
            {
                Assert.That(defs, Contains.Key(id));

                Assert.That(defs[id].Sprite, Is.EqualTo("Mobs/Pets/corgi.rsi"),
                    $"{id} must name Mobs/Pets/corgi.rsi EXPLICITLY. Relying on an inherited " +
                    "'sprite:' is exactly how Lisa and real_mouse silently followed Ian onto " +
                    "scarlet.rsi in Wave 18.");

                Assert.That(defs[id].DamageStates?.Select(d => d.State) ?? Enumerable.Empty<string>(),
                    Has.Some.EqualTo(state), $"{id} should render '{state}'.");
            }

            Assert.That(defs["MobScarlet"].Sprite, Is.EqualTo("_Solreign/scarlet.rsi"),
                "Scarlet keeps her own RSI — she is a genuine Solreign delighter.");

            var ian = defs["MobCorgiIan"].DamageStates!.Select(d => d.State).ToHashSet(StringComparer.Ordinal);
            var scarlet = defs["MobScarlet"].DamageStates!.Select(d => d.State).ToHashSet(StringComparer.Ordinal);
            Assert.That(ian.Overlaps(scarlet), Is.False,
                "Ian and Scarlet must not render the same states — they are two different pets.");
        });
    }

    // ---------------------------------------------------------------- core check

    private static CheckResult Run(string protoRoot, IReadOnlyList<string> textureRoots)
    {
        var files = Directory
            .EnumerateFiles(protoRoot, "*.yml", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        var entities = new Dictionary<string, EntityDef>(StringComparer.Ordinal);
        var unreadable = new List<string>();
        var duplicates = new List<string>();

        foreach (var file in files)
        {
            foreach (var def in ParseEntities(file, unreadable))
            {
                if (entities.TryGetValue(def.Id, out var existing))
                    duplicates.Add($"{def.Id}: {existing.File}  AND  {def.File}");
                entities[def.Id] = def;
            }
        }

        var rsiCache = new Dictionary<string, HashSet<string>?>(StringComparer.Ordinal);
        var violations = new List<string>();
        var unresolved = new List<string>();
        var knownPrefixDebt = new HashSet<string>(StringComparer.Ordinal);
        var refsChecked = 0;
        var heldPrefixRefsChecked = 0;
        var equippedPrefixRefsChecked = 0;

        foreach (var (id, def) in entities.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (def.IsAbstract)
                continue;

            var baseSprite = ResolveInherited(entities, id, e => e.Sprite);
            var layers = ResolveInherited(entities, id, e => e.Layers);
            var topState = ResolveInherited(entities, id, e => e.TopState);
            var damageStates = ResolveInherited(entities, id, e => e.DamageStates);
            var heldPrefix = ResolveInherited(entities, id, e => e.HeldPrefix);
            var itemSprite = ResolveInherited(entities, id, e => e.ItemSprite) ?? baseSprite;
            var inhandVisualKeys = ResolveInherited(entities, id, e => e.InhandVisualKeys);
            var equippedPrefix = ResolveInherited(entities, id, e => e.EquippedPrefix);
            var equippedState = ResolveInherited(entities, id, e => e.EquippedState);
            var clothingSprite = ResolveInherited(entities, id, e => e.ClothingSprite) ?? baseSprite;
            var clothingSlots = ResolveInherited(entities, id, e => e.ClothingSlots);
            var clothingVisualKeys = ResolveInherited(entities, id, e => e.ClothingVisualKeys);

            var checks = new List<(string State, string? Rsi, string Context)>();

            if (layers is { Count: > 0 })
            {
                foreach (var layer in layers)
                {
                    if (!string.IsNullOrWhiteSpace(layer.State))
                        checks.Add((layer.State!, layer.Sprite ?? baseSprite, "Sprite.layers"));
                }
            }
            else if (!string.IsNullOrWhiteSpace(topState))
            {
                checks.Add((topState!, baseSprite, "Sprite.state"));
            }

            foreach (var dmg in damageStates ?? new List<DamageStateRef>())
            {
                // DamageStateVisualizerSystem does `if (!LayerMapTryGet(key)) continue;` — a state
                // whose layer key is not mapped is NEVER applied, so it is dead config rather than
                // a broken sprite. Checking it anyway false-positives on MobCarpHolo, MobCarpMagic
                // and MobSheepSpace, which inherit an abstract parent's `BaseUnshaded: mouth` while
                // only ever declaring a `Base` layer. The state resolves against the MAPPED layer's
                // own RSI when it has one (185 layers in this repo do), else the base.
                var target = layers?.FirstOrDefault(l => l.Map.Any(m =>
                    string.Equals(m, dmg.LayerKey, StringComparison.Ordinal) ||
                    m.EndsWith("." + dmg.LayerKey, StringComparison.Ordinal)));

                if (target is null)
                    continue;

                checks.Add((dmg.State, target.Sprite ?? baseSprite, $"DamageStateVisuals.{dmg.DamageKey}"));
            }

            // ItemSystem.TryGetDefaultVisuals synthesizes these exact names only when the hand
            // has no explicit inhandVisuals entry. Middle hands are intentionally excluded: normal
            // humanoid inventory exposes left/right hands and the content convention only supplies
            // those two states. This slice protects prefix-driven art without inventing a third
            // asset requirement that the runtime does not normally request.
            if (!string.IsNullOrWhiteSpace(heldPrefix))
            {
                foreach (var hand in new[] { "left", "right" })
                {
                    if (inhandVisualKeys?.Contains(hand) == true)
                        continue;

                    checks.Add(($"{heldPrefix}-inhand-{hand}", itemSprite, $"Item.heldPrefix.{hand}"));
                }
            }

            // ClientClothingSystem uses the inventory slot name (not the SlotFlags spelling) and
            // then maps it to the historical RSI suffix. Explicit generic visuals win and suppress
            // default synthesis for that slot, so they are outside this prefix-only slice.
            if (!string.IsNullOrWhiteSpace(equippedPrefix) || !string.IsNullOrWhiteSpace(equippedState))
            {
                foreach (var rawSlot in clothingSlots ?? new List<string>())
                {
                    var mappedSlots = MapClothingSlots(rawSlot);
                    if (mappedSlots.Count == 0)
                    {
                        if (!IsNonVisualClothingSlot(rawSlot))
                        {
                            violations.Add(
                                $"{id}: Clothing slot '{rawSlot}' is not mapped by the sprite-state " +
                                $"gate; prefix coverage would be silently skipped [{def.File}]");
                        }
                        continue;
                    }

                    foreach (var (runtimeSlot, rsiSlot) in mappedSlots)
                    {
                        if (clothingVisualKeys?.Contains(runtimeSlot) == true)
                            continue;

                        var state = !string.IsNullOrWhiteSpace(equippedState)
                            ? equippedState!
                            : $"{equippedPrefix}-equipped-{rsiSlot}";
                        checks.Add((state, clothingSprite, $"Clothing.{runtimeSlot}"));
                    }
                }
            }

            foreach (var (state, rsi, context) in checks)
            {
                if (string.IsNullOrWhiteSpace(rsi))
                {
                    if (context.StartsWith("Item.", StringComparison.Ordinal) ||
                        context.StartsWith("Clothing.", StringComparison.Ordinal))
                    {
                        violations.Add(
                            $"{id}: state '{state}' has no RSI to resolve against ({context})" +
                            Environment.NewLine + $"      declared in {def.File}");
                    }
                    continue;
                }

                var states = LookupRsi(rsi!, textureRoots, rsiCache);
                if (states is null)
                {
                    unresolved.Add($"{id} -> '{rsi}'  ({context})  [{def.File}]");
                    continue;
                }

                refsChecked++;
                if (context.StartsWith("Item.heldPrefix.", StringComparison.Ordinal))
                    heldPrefixRefsChecked++;
                else if (context.StartsWith("Clothing.", StringComparison.Ordinal))
                    equippedPrefixRefsChecked++;

                if (!states.Contains(state))
                {
                    var report =
                        $"{id}: state '{state}' does not exist in '{rsi}'  ({context})" +
                        Environment.NewLine + $"      declared in {def.File}" +
                        Environment.NewLine + "      that RSI has: " +
                        string.Join(", ", states.OrderBy(s => s, StringComparer.Ordinal).Take(12)) +
                        (states.Count > 12 ? $", ... (+{states.Count - 12})" : "");

                    var debtKey = DebtKey(id, rsi!, state, context);
                    if (ExpectedKnownDebtKeys().Contains(debtKey))
                        knownPrefixDebt.Add(debtKey);
                    else
                        violations.Add(report);
                }
            }
        }

        return new CheckResult(files.Count, entities.Count, refsChecked,
            heldPrefixRefsChecked, equippedPrefixRefsChecked,
            unreadable, duplicates, unresolved, knownPrefixDebt, violations);
    }

    private static string DebtKey(string id, string rsi, string state, string context) =>
        $"{id}|{rsi}|{state}|{context}";

    private static bool IsNonVisualClothingSlot(string rawSlot) =>
        rawSlot.Equals("NONE", StringComparison.OrdinalIgnoreCase) ||
        rawSlot.Equals("PREVENTEQUIP", StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- parsing

    private static IEnumerable<EntityDef> ParseEntities(string file, List<string> unreadable)
    {
        YamlStream stream;
        try
        {
            // File.ReadAllText strips a UTF-8 BOM; a raw StreamReader would not, and a BOM already
            // cost this gate 69 phantom "missing RSI" reports during development.
            stream = new YamlStream();
            stream.Load(new StringReader(File.ReadAllText(file, Encoding.UTF8)));
        }
        catch (Exception e)
        {
            unreadable.Add($"{file}: {e.GetType().Name}: {e.Message.Split('\n')[0]}");
            yield break;
        }

        foreach (var doc in stream.Documents)
        {
            // An empty document is fine; anything else that is not a sequence would be silently
            // skipped, which is the coverage hole this gate exists to refuse.
            if (doc.RootNode is YamlScalarNode { Value: null or "" })
                continue;

            if (doc.RootNode is not YamlSequenceNode root)
            {
                unreadable.Add($"{file}: root node is {doc.RootNode.GetType().Name}, not a sequence");
                continue;
            }

            foreach (var node in root.Children.OfType<YamlMappingNode>())
            {
                if (Scalar(node, "type") != "entity")
                    continue;

                var id = Scalar(node, "id");
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                string? sprite = null;
                List<LayerDef>? layers = null;
                string? topState = null;
                List<DamageStateRef>? damageStates = null;
                string? itemSprite = null;
                string? heldPrefix = null;
                HashSet<string>? inhandVisualKeys = null;
                string? clothingSprite = null;
                string? equippedPrefix = null;
                string? equippedState = null;
                List<string>? clothingSlots = null;
                HashSet<string>? clothingVisualKeys = null;

                if (Get(node, "components") is YamlSequenceNode components)
                {
                    foreach (var comp in components.Children.OfType<YamlMappingNode>())
                    {
                        switch (Scalar(comp, "type"))
                        {
                            case "Sprite":
                                if (Scalar(comp, "sprite") is { } sp && !string.IsNullOrWhiteSpace(sp))
                                    sprite = sp;
                                if (Scalar(comp, "state") is { } ts && !string.IsNullOrWhiteSpace(ts))
                                    topState = ts;
                                if (Get(comp, "layers") is YamlSequenceNode layerSeq)
                                {
                                    layers = new List<LayerDef>();
                                    foreach (var layer in layerSeq.Children.OfType<YamlMappingNode>())
                                    {
                                        var map = Get(layer, "map") is YamlSequenceNode mapSeq
                                            ? mapSeq.Children.OfType<YamlScalarNode>()
                                                .Select(m => m.Value).Where(v => v is not null).ToList()!
                                            : new List<string>();
                                        layers.Add(new LayerDef(Scalar(layer, "state"), Scalar(layer, "sprite"), map));
                                    }
                                }
                                break;

                            case "Item":
                                itemSprite = NonBlankScalar(comp, "sprite");
                                heldPrefix = NonBlankScalar(comp, "heldPrefix");
                                inhandVisualKeys = MappingKeys(comp, "inhandVisuals");
                                break;

                            case "Clothing":
                                clothingSprite = NonBlankScalar(comp, "sprite");
                                equippedPrefix = NonBlankScalar(comp, "equippedPrefix");
                                equippedState = NonBlankScalar(comp, "equippedState");
                                clothingSlots = ScalarList(comp, "slots");
                                clothingVisualKeys = MappingKeys(comp, "clothingVisuals");
                                break;

                            case "DamageStateVisuals":
                                if (Get(comp, "states") is YamlMappingNode dmgStates)
                                {
                                    damageStates = new List<DamageStateRef>();
                                    foreach (var (damageKey, layerMapNode) in dmgStates.Children)
                                    {
                                        if (layerMapNode is not YamlMappingNode layerMap)
                                            continue;
                                        var dmgName = (damageKey as YamlScalarNode)?.Value ?? "?";
                                        foreach (var (layerKey, stateNode) in layerMap.Children)
                                        {
                                            if (stateNode is YamlScalarNode { Value: { } v } &&
                                                !string.IsNullOrWhiteSpace(v))
                                            {
                                                damageStates.Add(new DamageStateRef(
                                                    (layerKey as YamlScalarNode)?.Value ?? "Base", v, dmgName));
                                            }
                                        }
                                    }
                                }
                                break;
                        }
                    }
                }

                yield return new EntityDef(
                    id!,
                    ParentsOf(node),
                    string.Equals(Scalar(node, "abstract"), "true", StringComparison.OrdinalIgnoreCase),
                    sprite,
                    layers,
                    topState,
                    damageStates,
                    itemSprite,
                    heldPrefix,
                    inhandVisualKeys,
                    clothingSprite,
                    equippedPrefix,
                    equippedState,
                    clothingSlots,
                    clothingVisualKeys,
                    file);
            }
        }
    }

    private static List<string> ParentsOf(YamlMappingNode node)
    {
        var result = new List<string>();
        switch (Get(node, "parent"))
        {
            case YamlScalarNode { Value: { } single } when !string.IsNullOrWhiteSpace(single):
                result.Add(single);
                break;
            case YamlSequenceNode seq:
                result.AddRange(seq.Children.OfType<YamlScalarNode>()
                    .Select(c => c.Value)
                    .Where(v => !string.IsNullOrWhiteSpace(v))!);
                break;
        }
        return result;
    }

    private static string? NonBlankScalar(YamlMappingNode node, string key) =>
        Scalar(node, key) is { } value && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static List<string>? ScalarList(YamlMappingNode node, string key)
    {
        return Get(node, key) switch
        {
            YamlScalarNode { Value: { } single } when !string.IsNullOrWhiteSpace(single) =>
                new List<string> { single },
            YamlSequenceNode sequence =>
                sequence.Children.OfType<YamlScalarNode>()
                    .Select(child => child.Value)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Cast<string>()
                    .ToList(),
            _ => null,
        };
    }

    private static HashSet<string>? MappingKeys(YamlMappingNode node, string key)
    {
        if (Get(node, key) is not YamlMappingNode mapping)
            return null;

        return mapping.Children.Keys
            .OfType<YamlScalarNode>()
            .Select(child => child.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlyList<(string RuntimeSlot, string RsiSlot)> MapClothingSlots(string rawSlot)
    {
        // YAML SlotFlags are enum names (case-insensitive). GetEquipmentVisualsEvent carries the
        // inventory template's slot string, which ClientClothingSystem then maps to the legacy RSI
        // suffix below. POCKET expands to the two standard inventory slots. PREVENTEQUIP/NONE are
        // the only flags that cannot produce a worn visual.
        return rawSlot.ToUpperInvariant() switch
        {
            "HEAD" => new[] { ("head", "HELMET") },
            "EYES" => new[] { ("eyes", "EYES") },
            "EARS" => new[] { ("ears", "EARS") },
            "MASK" => new[] { ("mask", "MASK") },
            "OUTERCLOTHING" => new[] { ("outerClothing", "OUTERCLOTHING") },
            "INNERCLOTHING" => new[] { ("jumpsuit", "INNERCLOTHING") },
            "NECK" => new[] { ("neck", "NECK") },
            "BACK" => new[] { ("back", "BACKPACK") },
            "BELT" => new[] { ("belt", "BELT") },
            "GLOVES" => new[] { ("gloves", "HAND") },
            "IDCARD" => new[] { ("id", "IDCARD") },
            "POCKET" => new[] { ("pocket1", "POCKET1"), ("pocket2", "POCKET2") },
            "LEGS" => new[] { ("legs", "legs") },
            "FEET" => new[] { ("shoes", "FEET") },
            "SUITSTORAGE" => new[] { ("suitstorage", "SUITSTORAGE") },
            _ => Array.Empty<(string, string)>(),
        };
    }

    /// <summary>
    ///     SS14 merges components per-datafield, so a field left unset on a child falls through to
    ///     the first parent that sets it, walking the parent list left to right (mirroring
    ///     <c>SerializationManager</c> composition closely enough for existence checking).
    /// </summary>
    private static T? ResolveInherited<T>(
        IReadOnlyDictionary<string, EntityDef> all,
        string id,
        Func<EntityDef, T?> pick,
        HashSet<string>? seen = null) where T : class
    {
        seen ??= new HashSet<string>(StringComparer.Ordinal);
        if (!seen.Add(id) || !all.TryGetValue(id, out var def))
            return null;

        if (pick(def) is { } own)
            return own;

        foreach (var parent in def.Parents)
        {
            if (ResolveInherited(all, parent, pick, seen) is { } inherited)
                return inherited;
        }

        return null;
    }

    private static HashSet<string>? LookupRsi(
        string rsiPath,
        IReadOnlyList<string> textureRoots,
        Dictionary<string, HashSet<string>?> cache)
    {
        if (cache.TryGetValue(rsiPath, out var cached))
            return cached;

        var relative = rsiPath.TrimStart('/');
        if (relative.StartsWith("Textures/", StringComparison.Ordinal))
            relative = relative["Textures/".Length..];
        relative = relative.Replace('/', Path.DirectorySeparatorChar);

        HashSet<string>? states = null;
        foreach (var root in textureRoots)
        {
            var meta = Path.Combine(root, relative, "meta.json");
            if (!File.Exists(meta))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(meta, Encoding.UTF8));
                if (!doc.RootElement.TryGetProperty("states", out var arr))
                    continue; // shadowed//malformed: keep looking in the next root

                var found = new HashSet<string>(StringComparer.Ordinal);
                foreach (var st in arr.EnumerateArray())
                {
                    if (st.TryGetProperty("name", out var name) && name.GetString() is { } n)
                        found.Add(n);
                }

                states = found;
                break;
            }
            catch (JsonException)
            {
                // Fall through to the next root rather than declaring the RSI unresolvable.
            }
        }

        cache[rsiPath] = states;
        return states;
    }

    // ---------------------------------------------------------------- helpers

    private static YamlNode? Get(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    private static string? Scalar(YamlMappingNode node, string key) =>
        Get(node, key) is YamlScalarNode scalar ? scalar.Value : null;

    private static string LocateRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "Resources", "Prototypes")) &&
                Directory.Exists(Path.Combine(current.FullName, "Resources", "Textures")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the repo root walking up from '{AppContext.BaseDirectory}'.");
    }
}
