#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Phase-3 Wave 22 (docs/ROADMAP-PHASE3.md § Wave 22; docs/AUDIT-2026-07-12.md S6/S8/S13/S14/S20/S21/S22
///     recurrence class): permanent, CI-cheap, no-game-boot integrity gate for the "built ≠ reachable"
///     defect class — a Solreign entity prototype that exists under <c>Resources/Prototypes/**</c>
///     but is never placed on a map, stocked in a vendor, listed in a spawner, referenced by a game
///     rule / craft path / seed product / contract reward / loadout, or explicitly declared admin-only
///     on the allowlist.
///
///     Why engine / YAML linter checks do NOT already catch this: prototype load validation only
///     asserts that referenced ids exist when a field is deserialized. An entity that nobody ever
///     references is still a perfectly valid prototype — it just never appears in a round. That is
///     exactly the orphan-report failure mode (e.g. re-orphaned <c>SolreignEgg01</c> /
///     <c>SolreignPoolNoodle</c>, portal-pack props with no map placement, gear items with no
///     contract reward path). This test converts that class from "found by an audit scout's
///     word-boundary grep" into "found by CI before merge."
///
///     Method (deterministic, pure filesystem — no <c>GameTest</c> / IoC / prototype manager boot):
///     <list type="number">
///       <item>Locate repo <c>Resources/</c> by walking up from <see cref="AppContext.BaseDirectory"/>.</item>
///       <item>DEFINITIONS: parse every <c>Resources/Prototypes/**/*.yml</c> for <c>- type: entity</c>
///            blocks whose <c>id</c> matches <c>^Solreign</c>. Record (id, file, abstract?). Skip
///            <c>abstract: true</c> templates when asserting reachability (they are parents, not
///            content). Count all Solreign entity definitions (abstract included) for the self-test
///            guard.</item>
///       <item>COMMENT-STRIP every scanned file (<c>#</c> to EOL, respecting simple single/double
///            quoted strings) before any reachability match — a comment mention must NEVER count
///            (that is the S8 "lying comment" failure mode).</item>
///       <item>REACHABILITY SOURCES (word-boundary match of each known non-abstract id), only in:
///            map <c>- proto:</c> placements; vendor <c>startingInventory:</c> /
///            <c>contrabandInventory:</c> / <c>emaggedInventory:</c> keys (all three tiers are
///            equally player-reachable in a live round, emag is not admin-only); <c>prototypes:</c> /
///            <c>protos:</c> spawner lists inside other entity defs (one level — no transitive
///            closure); <c>Prototypes/**/GameRules/</c> and <c>- type: gameRule</c> blocks; lathe
///            <c>result:</c> / construction <c>entity:</c>/<c>prototype:</c> / botany
///            <c>packetPrototype:</c>/<c>plantId:</c> / effects-engine <c>effectPrototype:</c>
///            single-line links; plant <c>productPrototypes:</c>/<c>products:</c>;
///            <c>weightedRandom</c>
///            <c>weights:</c> pools (e.g. objective selection); <c>SpawnEntitiesBehavior</c>
///            <c>spawn:</c> destructible-drop tables; <c>entityTable</c>/<c>EntityTableContainerFill</c>
///            <c>children:</c> selector lists (<c>- id: &lt;x&gt;</c>); contracts season-1 reward
///            fields; startingGear / loadout equipment values; allowlist membership with a required
///            <c>reason:</c>.</item>
///       <item>EXCLUDED contexts: the entity's own <c>id:</c> line, any <c>parent:</c> line
///            (inheritance is not reachability), comments, locale files, C# files.</item>
///     </list>
///
///     Limitation (intentional, documented): spawner reachability is one-level only. If A spawns B
///     and B spawns C, C is not considered reachable via A→B unless C also appears in a direct
///     source. That matches the scouts' grep method and keeps the gate deterministic and cheap.
///     Likewise, an entity spawned only by C# code (e.g. an in-place mob-replacement system) has NO
///     reachability source under this algorithm by design — the scan never executes or parses C# —
///     so such entities must be declared on the allowlist with a reason pointing at the spawning
///     system, exactly like an admin-only-by-design entity. That is a scope limitation of a pure
///     YAML scan, not a bug.
///
///     Allowlist path: <c>Resources/_Solreign/orphan_allowlist.yml</c> (deliberately OUTSIDE
///     Resources/Prototypes/ — see LocateResourcesDirectory's sibling constant note) — every
///     admin-only-by-design entity (golf-club variants, Awakened-Tree glow variants per audit S23,
///     etc.) must be declared with a non-empty <c>reason:</c>. Scoped-as-designed is never silent.
/// </summary>
[TestFixture]
public sealed class SolreignOrphanReachabilityTest
{
    // NOTE: deliberately OUTSIDE Resources/Prototypes/. That whole subtree is recursively
    // scanned by RobustToolbox's PrototypeManager.LoadDirectory as *.yml prototype documents
    // (both the live server/client boot and Content.YAMLLinter) — an allowlist entry's
    // `{id, reason}` shape has no `type:` key, so a copy placed under Prototypes/ crashes
    // prototype loading with a KeyNotFoundException instead of being silently ignored.
    // Verified: dotnet run --project Content.YAMLLinter crashes with the file at
    // Prototypes/_Solreign/orphan_allowlist.yml, passes clean at this path.
    private const string AllowlistRelativePath =
        "_Solreign/orphan_allowlist.yml";

    private const string ContractsSeason1RelativePath =
        "Prototypes/_Solreign/Contracts/contracts_season1.yml";

    /// <summary>The rotation pool — the ONLY maps a player can be dropped onto.</summary>
    private const string MapPoolRelativePath =
        "Prototypes/_Solreign/map_pool.yml";

    /// <summary>Must match the <c>game.map_pool</c> CVar set in the Solreign rotation preset.</summary>
    private const string RotationPoolId = "SolreignMapPool";

    /// <summary>Self-test floor: a regressed definition matcher must not fake green on an empty set.</summary>
    private const int MinSolreignEntityDefinitions = 100;

    private static readonly Regex EntityTypeLine =
        new(@"^\s*-\s*type:\s*entity\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex IdLine =
        new(@"^\s*id:\s*[""']?([A-Za-z0-9_]+)[""']?\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AbstractTrueLine =
        new(@"^\s*abstract:\s*true\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ProtoPlacementLine =
        new(@"^\s*-\s*proto:\s*[""']?([A-Za-z0-9_]+)[""']?\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex GameMapPoolTypeLine =
        new(@"^\s*-\s*type:\s*gameMapPool\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex MapsHeaderLine =
        new(@"^\s*maps:\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex MapPathLine =
        new(@"^\s*mapPath:\s*[""']?(/[^""'\s]+\.yml)[""']?\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex InventoryKeyLine =
        new(@"^\s+([A-Za-z0-9_]+)\s*:\s*\d+\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    ///     All three real vending-machine inventory tiers (upstream <c>VendingMachineInventoryPrototype</c>):
    ///     startingInventory ships day one, contrabandInventory/emaggedInventory need an emag but are
    ///     equally player-reachable in a live round — a real Solreign easter-egg placement idiom
    ///     (e.g. SolreignFeatheredMask/SolreignStealthBox/SolreignSplatPie), not admin-only.
    /// </summary>
    private static readonly Regex InventoryHeaderLine =
        new(@"^\s*(startingInventory|contrabandInventory|emaggedInventory)\s*:\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PrototypesHeader =
        new(@"^\s*(prototypes|protos)\s*:\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SequenceItemLine =
        new(@"^\s*-\s*[""']?([A-Za-z0-9_]+)[""']?\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    ///     <c>result:</c> (lathe recipe), <c>packetPrototype:</c> / <c>plantId:</c> (botany entity
    ///     links), and <c>effectPrototype:</c> (W2 effects-engine payload link) are single-line
    ///     "this id names another entity that IS the reachable thing" references.
    /// </summary>
    private static readonly Regex ResultLine =
        new(@"^\s*(?:result|packetPrototype|plantId|effectPrototype)\s*:\s*[""']?([A-Za-z0-9_]+)[""']?\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Header for a <c>weightedRandom</c> prototype's weighted pool (selection = reachable).</summary>
    private static readonly Regex WeightsHeaderLine =
        new(@"^\s*weights\s*:\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    ///     Header for <c>SpawnEntitiesBehavior</c>'s destructible-drop table (<c>spawn: &lt;id&gt;: {min,max}</c>
    ///     — a bare mapping key with no inline value, the entity id starting a nested block).
    /// </summary>
    private static readonly Regex SpawnBehaviorHeaderLine =
        new(@"^\s*spawn\s*:\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>A bare "key:" mapping line with no inline value (nested block follows).</summary>
    private static readonly Regex BareMappingKeyLine =
        new(@"^\s*([A-Za-z0-9_]+)\s*:\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    ///     Header for an <c>entityTable</c> selector's <c>children:</c> list — used by both a
    ///     top-level <c>- type: entityTable</c> prototype's <c>table: !type:AllSelector children:</c>
    ///     and <c>EntityTableContainerFill</c>'s per-slot <c>!type:AllSelector children:</c> (e.g. the
    ///     Season-1 Vault Reward crate, the fireworks launcher supply crate).
    /// </summary>
    private static readonly Regex ChildrenHeaderLine =
        new(@"^\s*children\s*:\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>A <c>- id: &lt;id&gt;</c> sequence item, the shape <c>children:</c> lists use.</summary>
    private static readonly Regex SequenceIdItemLine =
        new(@"^\s*-\s*id\s*:\s*[""']?([A-Za-z0-9_]+)[""']?\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex EntityOrPrototypeKeyLine =
        new(@"^\s*(entity|prototype)\s*:\s*[""']?([A-Za-z0-9_]+)[""']?\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ProductListHeader =
        new(@"^\s*(productPrototypes|products)\s*:\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex InlineProductList =
        new(@"^\s*(?:productPrototypes|products)\s*:\s*\[(.*?)\]\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex GameRuleTypeLine =
        new(@"^\s*-\s*type:\s*gameRule\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex StartingGearTypeLine =
        new(@"^\s*-\s*type:\s*(startingGear|loadout|roleLoadout)\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex EquipmentHeader =
        new(@"^\s*equipment\s*:\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex EquipmentValueLine =
        new(@"^\s+[A-Za-z0-9_]+\s*:\s*[""']?([A-Za-z0-9_]+)[""']?\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex IdOrParentLine =
        new(@"^\s*(id|parent)\s*:", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex TopLevelTypeLine =
        new(@"^\s*-\s*type:\s*\S+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AllowlistIdLine =
        new(@"^\s*-\s*id:\s*[""']?([A-Za-z0-9_]+)[""']?\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AllowlistReasonLine =
        new(@"^\s*reason\s*:\s*(.+?)\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SolreignIdToken =
        new(@"\b(Solreign[A-Za-z0-9_]*)\b", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly record struct EntityDef(string Id, string RelativePath, bool Abstract);

    [Test]
    public void AllNonAbstractSolreignEntities_AreReachable_OrAllowlisted()
    {
        var resources = LocateResourcesDirectory();
        var prototypesRoot = Path.Combine(resources, "Prototypes");
        var mapsRoot = Path.Combine(resources, "Maps");

        Assert.That(Directory.Exists(prototypesRoot), Is.True,
            $"Expected Prototypes/ under Resources at '{resources}'.");
        Assert.That(Directory.Exists(mapsRoot), Is.True,
            $"Expected Maps/ under Resources at '{resources}'.");

        var protoFiles = Directory.EnumerateFiles(prototypesRoot, "*.yml", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        // Only maps THIS FORK ACTUALLY LOADS count as reachable.
        //
        // This used to enumerate every *.yml under Resources/Maps, which quietly included
        // upstream's stock chassis (box, saltern, packed, …). Those files are valid, they
        // parse, they load — and SOLREIGN never selects them: map_pool.yml restricts round
        // selection and the map vote to the seven Solreign stations. So "is this entity on
        // a map?" was the wrong question, and it answered YES for content no player can ever
        // walk up to. It shipped two consoles (MemorialConsole, ComplianceTerminal — placed
        // on box and saltern) straight past a green run.
        //
        // The right question is NOT "is it in the rotation pool" either, which is narrower
        // than reality: zone_hell.yml and zone_heaven.yml are Solreign grids that no pool
        // lists, reached in-world through the Sublevel-H gates rather than by round start.
        // Scoring those as unreachable would condemn 22 legitimately-placed props.
        //
        // So: every grid under Maps/_Solreign/ — the maps this fork ships and loads —
        // and nothing from upstream's parked chassis. ResolveRotationMapFiles additionally
        // asserts every pool entry lands inside that set, so a pool pointing at a map we do
        // not ship still fails loudly.
        var mapFiles = ResolveReachableMapFiles(resources, mapsRoot);

        var definitions = CollectEntityDefinitions(protoFiles, resources);
        Assert.That(definitions.Count, Is.GreaterThan(MinSolreignEntityDefinitions),
            "The definition sweep found ≤ " + MinSolreignEntityDefinitions +
            " Solreign entity prototypes (found " + definitions.Count + "). That almost certainly " +
            "means the matcher regressed — not that the tree stopped defining Solreign content. " +
            "Check CollectEntityDefinitions / comment-strip before trusting a green run.");

        var nonAbstract = definitions
            .Where(d => !d.Abstract)
            .Select(d => d.Id)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        var allowlist = LoadAllowlist(Path.Combine(resources, AllowlistRelativePath));
        var reachable = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var id in nonAbstract)
            reachable[id] = new HashSet<string>(StringComparer.Ordinal);

        // 1) Map placements: - proto: <id>
        foreach (var mapFile in mapFiles)
        {
            foreach (var line in ReadCommentStrippedLines(mapFile))
            {
                if (IsIdOrParentLine(line))
                    continue;
                var m = ProtoPlacementLine.Match(line);
                if (!m.Success)
                    continue;
                MarkReachable(reachable, m.Groups[1].Value, "map:-proto");
            }
        }

        // 2–9) Prototype-side sources (vendors, spawners, game rules, craft, botany, loadouts)
        foreach (var protoFile in protoFiles)
        {
            var rel = ToRelative(resources, protoFile);
            // Allowlist file is membership-only, never a free-form reachability corpus.
            if (rel.Replace('\\', '/').Equals(AllowlistRelativePath, StringComparison.Ordinal))
                continue;

            var lines = ReadCommentStrippedLines(protoFile);
            var underGameRulesDir = rel.Replace('\\', '/')
                .Contains("/GameRules/", StringComparison.Ordinal);

            CollectStartingInventoryKeys(lines, reachable);
            CollectSpawnerLists(lines, reachable);
            CollectCraftPaths(lines, reachable);
            CollectSeedProducts(lines, reachable);
            CollectLoadoutGear(lines, reachable);
            CollectWeightedRandomPools(lines, reachable);
            CollectSpawnEntitiesBehaviorDrops(lines, reachable);
            CollectEntityTableChildren(lines, reachable);

            if (underGameRulesDir)
                CollectWordBoundaryHits(lines, reachable, "gamerule-dir");

            CollectGameRuleBlocks(lines, reachable);
        }

        // Contracts season-1 reward / sabotage / any field references
        var contractsPath = Path.Combine(resources, ContractsSeason1RelativePath);
        if (File.Exists(contractsPath))
        {
            CollectWordBoundaryHits(ReadCommentStrippedLines(contractsPath), reachable, "contracts");
        }

        // Allowlist membership = declared-reachable (requires non-empty reason)
        foreach (var (id, reason) in allowlist)
        {
            if (string.IsNullOrWhiteSpace(reason))
                continue;
            MarkReachable(reachable, id, "allowlist");
        }

        var orphans = nonAbstract
            .Where(id => reachable[id].Count == 0)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        if (orphans.Length == 0)
            return;

        var sb = new StringBuilder();
        sb.AppendLine(
            $"Orphan-reachability gate failed: {orphans.Length} non-abstract Solreign entit" +
            $"{(orphans.Length == 1 ? "y is" : "ies are")} unreachable.");
        sb.AppendLine(
            "Sources checked: map -proto; startingInventory keys; prototypes/protos spawner " +
            "lists (one-level, no transitive closure); Prototypes/**/GameRules/ + type: gameRule " +
            "blocks; lathe result: + construction entity:/prototype:; botany packetPrototype:/" +
            "plantId:/productPrototypes:/products:; Contracts/contracts_season1.yml; " +
            "startingGear/loadout equipment; " +
            "orphan_allowlist.yml membership.");
        sb.AppendLine(
            "Excluded: id: lines, parent: lines, comments (#), locale, C#.");
        sb.AppendLine(
            "By-design admin-only content (or content only ever spawned by C# code) must be " +
            "declared in Resources/_Solreign/orphan_allowlist.yml with a non-empty " +
            "reason: (see audit S23 golf-club / Awakened-Tree glow variants).");
        sb.AppendLine("Orphans:");
        foreach (var id in orphans)
        {
            var def = definitions.First(d => d.Id == id && !d.Abstract);
            sb.AppendLine($"  - {id}  (defined in {def.RelativePath})");
        }

        Assert.Fail(sb.ToString());
    }

    /// <summary>
    ///     The grid files for the maps SOLREIGN actually rotates, resolved
    ///     <c>map_pool.yml → gameMap prototype → mapPath</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Every step FAILS LOUDLY rather than falling back to "all maps". A silent fallback
    ///     here would restore the precise bug this method exists to remove — and it would do
    ///     it invisibly, because the gate would still be green.
    ///     </para>
    /// </remarks>
    private static string[] ResolveReachableMapFiles(string resources, string mapsRoot)
    {
        var solreignMaps = Path.Combine(mapsRoot, "_Solreign");
        Assert.That(Directory.Exists(solreignMaps), Is.True,
            $"Expected this fork's own grids under '{solreignMaps}'.");

        var shipped = Directory.EnumerateFiles(solreignMaps, "*.yml", SearchOption.AllDirectories)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        Assert.That(shipped, Is.Not.Empty,
            $"Found 0 grids under '{solreignMaps}'. Refusing to run: an empty map set would mark " +
            "every placed entity an orphan and bury the real signal.");

        // Cross-check: every rotation-pool entry must resolve to a grid we actually ship.
        var rotation = ResolveRotationMapFiles(resources, mapsRoot);
        var shippedSet = new HashSet<string>(shipped, StringComparer.Ordinal);
        var outside = rotation.Where(p => !shippedSet.Contains(p)).ToArray();
        Assert.That(outside, Is.Empty,
            "The rotation pool selects grids that do not live under Maps/_Solreign/:" +
            Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", outside) +
            Environment.NewLine +
            "Either the map moved or the pool points at an upstream chassis — both are bugs.");

        return shipped;
    }

    /// <summary>
    ///     The grid files for the maps SOLREIGN rotates at round start, resolved
    ///     <c>map_pool.yml → gameMap prototype → mapPath</c>. Used to cross-check the shipped
    ///     map set; reachability itself is broader (see <see cref="ResolveReachableMapFiles"/>).
    /// </summary>
    private static string[] ResolveRotationMapFiles(string resources, string mapsRoot)
    {
        var poolPath = Path.Combine(resources, MapPoolRelativePath);
        Assert.That(File.Exists(poolPath), Is.True,
            $"Expected the rotation pool at '{poolPath}'. Reachability is defined by the pool; " +
            "without it this gate cannot tell a played map from a parked upstream chassis.");

        // Read the `maps:` sequence out of the SolreignMapPool block.
        var poolIds = new List<string>();
        var inPool = false;
        var inMaps = false;
        foreach (var line in ReadCommentStrippedLines(poolPath))
        {
            if (GameMapPoolTypeLine.IsMatch(line))
            {
                inPool = false;
                inMaps = false;
                continue;
            }

            var idMatch = IdLine.Match(line);
            if (idMatch.Success)
            {
                inPool = string.Equals(idMatch.Groups[1].Value, RotationPoolId, StringComparison.Ordinal);
                inMaps = false;
                continue;
            }

            if (!inPool)
                continue;

            if (MapsHeaderLine.IsMatch(line))
            {
                inMaps = true;
                continue;
            }

            if (!inMaps)
                continue;

            var item = SequenceItemLine.Match(line);
            if (item.Success)
                poolIds.Add(item.Groups[1].Value);
            else if (line.Trim().Length > 0)
                inMaps = false;
        }

        Assert.That(poolIds, Is.Not.Empty,
            $"Parsed 0 map ids out of '{poolPath}' for pool '{RotationPoolId}'. The pool format " +
            "changed and this parser did not — refusing to run, because an empty rotation would " +
            "mark every mapped entity an orphan and bury the real signal.");

        // gameMap prototype id -> mapPath, harvested from Prototypes/Maps/**.
        var mapProtoRoot = Path.Combine(resources, "Prototypes", "Maps");
        var idToPath = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(mapProtoRoot, "*.yml", SearchOption.AllDirectories))
        {
            string? pendingId = null;
            foreach (var line in ReadCommentStrippedLines(file))
            {
                var idMatch = IdLine.Match(line);
                if (idMatch.Success)
                {
                    pendingId = idMatch.Groups[1].Value;
                    continue;
                }

                var pathMatch = MapPathLine.Match(line);
                if (pathMatch.Success && pendingId is not null)
                    idToPath[pendingId] = pathMatch.Groups[1].Value;
            }
        }

        var resolved = new List<string>();
        var missing = new List<string>();
        foreach (var id in poolIds)
        {
            if (!idToPath.TryGetValue(id, out var rel))
            {
                missing.Add($"{id} (no gameMap prototype with a mapPath)");
                continue;
            }

            // mapPath is engine-rooted ("/Maps/_Solreign/x.yml") — rebase onto Resources/.
            var abs = Path.Combine(resources, rel.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(abs))
                missing.Add($"{id} -> {rel} (file not found)");
            else
                resolved.Add(abs);
        }

        Assert.That(missing, Is.Empty,
            "Rotation pool references maps this gate could not resolve to grid files:" +
            Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", missing) +
            Environment.NewLine +
            $"(searched gameMap prototypes under '{mapProtoRoot}', grids under '{mapsRoot}')");

        return resolved
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    ///     Walk parents of <see cref="AppContext.BaseDirectory"/> until a directory containing
    ///     both <c>Resources/Prototypes</c> and <c>Resources/Maps</c> is found (repo root from
    ///     <c>bin/Content.Tests/</c> is two levels up on a normal checkout).
    /// </summary>
    private static string LocateResourcesDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var resources = Path.Combine(dir.FullName, "Resources");
            if (Directory.Exists(Path.Combine(resources, "Prototypes")) &&
                Directory.Exists(Path.Combine(resources, "Maps")))
            {
                return resources;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate Resources/ (with Prototypes/ and Maps/) by walking up from " +
            $"AppContext.BaseDirectory='{AppContext.BaseDirectory}'.");
    }

    private static List<EntityDef> CollectEntityDefinitions(IEnumerable<string> protoFiles, string resourcesRoot)
    {
        var defs = new List<EntityDef>();

        foreach (var file in protoFiles)
        {
            var rel = ToRelative(resourcesRoot, file);
            var lines = ReadCommentStrippedLines(file);
            string? currentId = null;
            var currentAbstract = false;
            var inEntity = false;

            void Flush()
            {
                if (!inEntity || currentId is null)
                {
                    currentId = null;
                    currentAbstract = false;
                    inEntity = false;
                    return;
                }

                // Scope by WHERE IT IS DEFINED, not by what it is called.
                //
                // This used to be `currentId.StartsWith("Solreign")`, which silently excluded
                // every piece of Solreign content that does not carry the prefix — and that is
                // not a hypothetical: MemorialConsole and ComplianceTerminal are both defined in
                // Prototypes/_Solreign/Entities/ and were never once examined by this gate. They
                // then shipped placed on box and saltern, maps this fork never rotates, and the
                // gate reported green because it had never heard of them.
                //
                // A naming convention is not an inventory. The fork owns what lives under
                // _Solreign/, whatever it happens to be called.
                var normalised = rel.Replace('\\', '/');
                var isSolreignContent =
                    normalised.Contains("/_Solreign/", StringComparison.Ordinal) ||
                    normalised.StartsWith("_Solreign/", StringComparison.Ordinal) ||
                    currentId.StartsWith("Solreign", StringComparison.Ordinal);

                if (isSolreignContent)
                    defs.Add(new EntityDef(currentId, normalised, currentAbstract));

                currentId = null;
                currentAbstract = false;
                inEntity = false;
            }

            foreach (var line in lines)
            {
                // New top-level document item: - type: ...
                if (TopLevelTypeLine.IsMatch(line))
                {
                    Flush();
                    if (EntityTypeLine.IsMatch(line))
                        inEntity = true;
                    continue;
                }

                if (!inEntity)
                    continue;

                if (AbstractTrueLine.IsMatch(line))
                {
                    currentAbstract = true;
                    continue;
                }

                var idMatch = IdLine.Match(line);
                if (idMatch.Success && currentId is null)
                    currentId = idMatch.Groups[1].Value;
            }

            Flush();
        }

        // Deterministic order for stable failure messages / diffs.
        return defs
            .OrderBy(d => d.Id, StringComparer.Ordinal)
            .ThenBy(d => d.RelativePath, StringComparer.Ordinal)
            .ToList();
    }

    private static Dictionary<string, string> LoadAllowlist(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            Assert.Fail(
                $"Allowlist file missing at '{path}'. Create " +
                "Resources/_Solreign/orphan_allowlist.yml with id + reason entries " +
                "for admin-only-by-design content (never leave scoped-as-designed silent).");
        }

        string? pendingId = null;
        string? pendingReason = null;

        void FlushEntry()
        {
            if (pendingId is null)
            {
                pendingReason = null;
                return;
            }

            if (string.IsNullOrWhiteSpace(pendingReason))
            {
                Assert.Fail(
                    $"Allowlist entry '{pendingId}' in orphan_allowlist.yml is missing a non-empty " +
                    "reason: — scoped-as-designed must be declared, never silent.");
            }

            // Last write wins on duplicate ids; still deterministic because file is single-pass.
            result[pendingId] = pendingReason!.Trim().Trim('"', '\'');
            pendingId = null;
            pendingReason = null;
        }

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = StripComments(raw);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var idMatch = AllowlistIdLine.Match(line);
            if (idMatch.Success)
            {
                FlushEntry();
                pendingId = idMatch.Groups[1].Value;
                continue;
            }

            var reasonMatch = AllowlistReasonLine.Match(line);
            if (reasonMatch.Success && pendingId is not null)
            {
                pendingReason = reasonMatch.Groups[1].Value;
            }
        }

        FlushEntry();
        return result;
    }

    private static void CollectStartingInventoryKeys(IReadOnlyList<string> lines,
        Dictionary<string, HashSet<string>> reachable)
    {
        var inInventory = false;
        var inventoryIndent = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var indent = LeadingWhitespaceCount(line);

            if (InventoryHeaderLine.IsMatch(line))
            {
                inInventory = true;
                inventoryIndent = indent;
                continue;
            }

            // Strictly-less: YAML commonly writes sequence/mapping children at the SAME indent as
            // their own header key (not just deeper), e.g. "prototypes:\n- SolreignFoo". Using <=
            // here would exit the block on the very first child line and silently under-match.
            if (inInventory && indent < inventoryIndent)
                inInventory = false;

            if (!inInventory)
                continue;

            if (IsIdOrParentLine(line))
                continue;

            var m = InventoryKeyLine.Match(line);
            if (m.Success)
                MarkReachable(reachable, m.Groups[1].Value, "vendor:inventory");
        }
    }

    private static void CollectSpawnerLists(IReadOnlyList<string> lines,
        Dictionary<string, HashSet<string>> reachable)
    {
        var inList = false;
        var listIndent = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var indent = LeadingWhitespaceCount(line);

            if (PrototypesHeader.IsMatch(line))
            {
                inList = true;
                listIndent = indent;
                continue;
            }

            if (inList && indent < listIndent)
                inList = false;

            if (!inList)
                continue;

            if (IsIdOrParentLine(line))
                continue;

            var m = SequenceItemLine.Match(line);
            if (m.Success)
                MarkReachable(reachable, m.Groups[1].Value, "spawner:prototypes");
        }
    }

    /// <summary>
    ///     <c>weightedRandom</c> prototypes' <c>weights:</c> pool — membership in the pool is a
    ///     genuine selection path (e.g. changeling round-objective groups), same "declared list of
    ///     ids, one level, no transitive closure" idiom as <see cref="CollectStartingInventoryKeys"/>.
    /// </summary>
    private static void CollectWeightedRandomPools(IReadOnlyList<string> lines,
        Dictionary<string, HashSet<string>> reachable)
    {
        var inPool = false;
        var poolIndent = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var indent = LeadingWhitespaceCount(line);

            if (WeightsHeaderLine.IsMatch(line))
            {
                inPool = true;
                poolIndent = indent;
                continue;
            }

            if (inPool && indent < poolIndent)
                inPool = false;

            if (!inPool)
                continue;

            if (IsIdOrParentLine(line))
                continue;

            var m = InventoryKeyLine.Match(line);
            if (m.Success)
                MarkReachable(reachable, m.Groups[1].Value, "selector:weights");
        }
    }

    /// <summary>
    ///     <c>SpawnEntitiesBehavior</c>'s <c>spawn:</c> destructible-drop table — a bare
    ///     "<c>&lt;id&gt;:</c>" mapping key naming the entity spawned on destruction (e.g. the
    ///     companion-cube structure dropping the carryable cube). No inline value, so this uses a
    ///     generic bare-key match rather than <see cref="InventoryKeyLine"/>'s "key: number" shape.
    /// </summary>
    private static void CollectSpawnEntitiesBehaviorDrops(IReadOnlyList<string> lines,
        Dictionary<string, HashSet<string>> reachable)
    {
        var inSpawnTable = false;
        var spawnIndent = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var indent = LeadingWhitespaceCount(line);

            if (SpawnBehaviorHeaderLine.IsMatch(line))
            {
                inSpawnTable = true;
                spawnIndent = indent;
                continue;
            }

            if (inSpawnTable && indent < spawnIndent)
                inSpawnTable = false;

            if (!inSpawnTable)
                continue;

            if (IsIdOrParentLine(line))
                continue;

            var m = BareMappingKeyLine.Match(line);
            if (m.Success)
                MarkReachable(reachable, m.Groups[1].Value, "destructible:spawn");
        }
    }

    /// <summary>
    ///     <c>entityTable</c>/<c>EntityTableContainerFill</c> selector <c>children:</c> lists
    ///     (<c>!type:AllSelector</c>/<c>!type:NestedSelector</c> idiom) — used both by top-level
    ///     event tables (e.g. the weather event table admin `event start` draws from) and by
    ///     container-fill crates/supply items (e.g. the Season-1 Vault Reward crate, the fireworks
    ///     launcher). List items are "<c>- id: &lt;id&gt;</c>", not a bare sequence item.
    /// </summary>
    private static void CollectEntityTableChildren(IReadOnlyList<string> lines,
        Dictionary<string, HashSet<string>> reachable)
    {
        var inChildren = false;
        var childrenIndent = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var indent = LeadingWhitespaceCount(line);

            if (ChildrenHeaderLine.IsMatch(line))
            {
                inChildren = true;
                childrenIndent = indent;
                continue;
            }

            if (inChildren && indent < childrenIndent)
                inChildren = false;

            if (!inChildren)
                continue;

            var m = SequenceIdItemLine.Match(line);
            if (m.Success)
                MarkReachable(reachable, m.Groups[1].Value, "selector:children");
        }
    }

    private static void CollectCraftPaths(IReadOnlyList<string> lines,
        Dictionary<string, HashSet<string>> reachable)
    {
        foreach (var line in lines)
        {
            if (IsIdOrParentLine(line))
                continue;

            var result = ResultLine.Match(line);
            if (result.Success)
            {
                MarkReachable(reachable, result.Groups[1].Value, "prototype:single-link");
                continue;
            }

            var ent = EntityOrPrototypeKeyLine.Match(line);
            if (ent.Success)
                MarkReachable(reachable, ent.Groups[2].Value, "craft:construction");
        }
    }

    private static void CollectSeedProducts(IReadOnlyList<string> lines,
        Dictionary<string, HashSet<string>> reachable)
    {
        var inList = false;
        var listIndent = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var indent = LeadingWhitespaceCount(line);

            var inline = InlineProductList.Match(line);
            if (inline.Success)
            {
                foreach (Match product in SolreignIdToken.Matches(inline.Groups[1].Value))
                    MarkReachable(reachable, product.Groups[1].Value, "botany:products");
                inList = false;
                continue;
            }

            if (ProductListHeader.IsMatch(line))
            {
                inList = true;
                listIndent = indent;
                continue;
            }

            if (inList && indent < listIndent)
                inList = false;

            if (!inList)
                continue;

            if (IsIdOrParentLine(line))
                continue;

            var m = SequenceItemLine.Match(line);
            if (m.Success)
                MarkReachable(reachable, m.Groups[1].Value, "botany:products");
        }
    }

    private static void CollectLoadoutGear(IReadOnlyList<string> lines,
        Dictionary<string, HashSet<string>> reachable)
    {
        var inStartingGearDoc = false;
        var inEquipment = false;
        var equipmentIndent = 0;

        foreach (var line in lines)
        {
            if (TopLevelTypeLine.IsMatch(line))
            {
                inStartingGearDoc = StartingGearTypeLine.IsMatch(line);
                inEquipment = false;
                continue;
            }

            if (!inStartingGearDoc)
                continue;

            if (EquipmentHeader.IsMatch(line))
            {
                inEquipment = true;
                equipmentIndent = LeadingWhitespaceCount(line);
                continue;
            }

            if (inEquipment)
            {
                var indent = LeadingWhitespaceCount(line);
                if (indent < equipmentIndent && !string.IsNullOrWhiteSpace(line))
                    inEquipment = false;
            }

            if (!inEquipment)
                continue;

            if (IsIdOrParentLine(line))
                continue;

            var m = EquipmentValueLine.Match(line);
            if (m.Success)
                MarkReachable(reachable, m.Groups[1].Value, "loadout:equipment");
        }
    }

    private static void CollectGameRuleBlocks(IReadOnlyList<string> lines,
        Dictionary<string, HashSet<string>> reachable)
    {
        var inGameRule = false;

        foreach (var line in lines)
        {
            if (TopLevelTypeLine.IsMatch(line))
            {
                inGameRule = GameRuleTypeLine.IsMatch(line);
                continue;
            }

            if (!inGameRule)
                continue;

            if (IsIdOrParentLine(line))
                continue;

            foreach (Match m in SolreignIdToken.Matches(line))
                MarkReachable(reachable, m.Groups[1].Value, "gamerule-block");
        }
    }

    private static void CollectWordBoundaryHits(IReadOnlyList<string> lines,
        Dictionary<string, HashSet<string>> reachable, string sourceTag)
    {
        foreach (var line in lines)
        {
            if (IsIdOrParentLine(line))
                continue;

            foreach (Match m in SolreignIdToken.Matches(line))
                MarkReachable(reachable, m.Groups[1].Value, sourceTag);
        }
    }

    private static void MarkReachable(Dictionary<string, HashSet<string>> reachable, string id, string source)
    {
        if (reachable.TryGetValue(id, out var set))
            set.Add(source);
    }

    private static bool IsIdOrParentLine(string line) => IdOrParentLine.IsMatch(line);

    private static int LeadingWhitespaceCount(string line)
    {
        var n = 0;
        while (n < line.Length && (line[n] == ' ' || line[n] == '\t'))
            n++;
        return n;
    }

    private static string ToRelative(string root, string fullPath)
    {
        var rel = Path.GetRelativePath(root, fullPath);
        return rel.Replace('\\', '/');
    }

    private static List<string> ReadCommentStrippedLines(string path)
    {
        var raw = File.ReadAllLines(path);
        var result = new List<string>(raw.Length);
        foreach (var line in raw)
            result.Add(StripComments(line));
        return result;
    }

    /// <summary>
    ///     Strip <c>#</c>-to-EOL comments while respecting simple single- and double-quoted
    ///     string spans (a <c>#</c> inside quotes is content, not a comment). YAML multi-line
    ///     scalars and complex escapes are out of scope — determinism beats elegance; this is
    ///     exactly enough to kill the S8 "lying comment" false-positive path.
    /// </summary>
    private static string StripComments(string line)
    {
        if (line.Length == 0)
            return line;

        var inSingle = false;
        var inDouble = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '\'' && !inDouble)
            {
                // YAML single-quoted escape is '' — keep it simple: toggle on each '.
                inSingle = !inSingle;
                continue;
            }

            if (c == '"' && !inSingle)
            {
                // Treat \" as escaped only when inside double quotes.
                if (inDouble && i > 0 && line[i - 1] == '\\')
                    continue;
                inDouble = !inDouble;
                continue;
            }

            if (c == '#' && !inSingle && !inDouble)
                return line.Substring(0, i);
        }

        return line;
    }
}
