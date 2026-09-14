#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using YamlDotNet.RepresentationModel;

namespace Content.Tests._Solreign.Roles;

/// <summary>
///     Placement rules for interactive wallmounts. Two are gated here, and they are the two
///     halves of one player report (2026-07-30, idiot4733): they were "overlapping, and they
///     are kinda floating in the air."
///       * <see cref="NoTwoInteractiveWallmountsShareATile"/> — the overlap.
///       * <see cref="NoInteractiveWallmountFloatsWithoutAWall"/> — the float.
///
///     Two interactive wall-mounted entities must never share a tile: a player can only click
///     the one on top, so the other is unreachable no matter how it was placed.
///
///     THIS GUARDS A DEFECT A PLAYER FOUND. On 2026-07-30 idiot4733 reported "the first-shift
///     beacon and corporate projects terminal are overlapping, and they are kinda floating in
///     the air." They were, on ALL SEVEN maps, at distance 0.00 — commit 9f0d51ccccc had placed
///     the console by CLONING the beacon's position, and cloning puts an entity exactly on top
///     of its source.
///
///     WHY THE PREDICATE IS THIS NARROW — two broader versions were measured and rejected,
///     because a gate that cries wolf gets deleted:
///
///       * "no two wallmounts per tile" flags 113 tiles, almost all legitimate. Directional
///         signs are deliberately stacked 3-4 to a tile, each rotated to point a different way.
///         That is standard upstream mapping, not a defect.
///       * "no two ActivatableUI entities per tile" flags 184 tiles, also mostly legitimate:
///         paper piles on desks, duplicate oxygen tanks in storage racks, and DefaultStationBeacon
///         markers, which are invisible and overlap real furniture everywhere by design.
///
///     The distinguishing property is the CONJUNCTION. A loose item can share a tile — you pick
///     it up. A wallmount is anchored to the wall and cannot be moved aside, so if it also owns
///     a UI, stacking it makes that UI unreachable. Conjoined, the predicate originally found 9
///     tiles out of 801 interactive wallmounts, 7 of them the exact pair the player reported.
///
///     ⚠️ RE-MEASURED 2026-08-10 (A9 leviathan+meridian): meridian beacon/console stack dissolved; list ratcheted. Relocating the wingmate
///     beacon into the arrival path on five maps dissolved five beacon/console stacks as a side
///     effect — the consoles never moved; the beacon left the tile. A count measured under one
///     map state does not survive an edit to the maps it counts.
/// </summary>
[TestFixture]
public sealed class InteractiveWallmountCollisionTests
{
    /// <summary>
    ///     Prototypes inheriting from a wallmount base are anchored to a wall and cannot be
    ///     shifted aside by a player.
    /// </summary>
    private const string WallmountBasePrefix = "BaseWallmount";

    /// <summary>
    ///     The component that gives an entity a clickable UI.
    /// </summary>
    private const string InteractiveComponent = "ActivatableUI";

    [Test]
    public void NoTwoInteractiveWallmountsShareATile()
    {
        var prototypes = LoadPrototypeGraph();

        var qualifying = prototypes.Keys
            .Where(id => IsInteractiveWallmount(id, prototypes))
            .ToHashSet(StringComparer.Ordinal);

        Assert.That(
            qualifying,
            Is.Not.Empty,
            "No interactive wallmount prototypes were resolved at all — this test would pass " +
            "vacuously. The prototype parser or the base-class names have probably drifted.");

        var collisions = new List<string>();

        foreach (var mapPath in Directory.EnumerateFiles(MapRoot(), "solreign_*.yml"))
        {
            // key: (grid, tileX, tileY) -> prototypes standing on it
            var byTile = new Dictionary<(int, int, int), List<string>>();

            foreach (var (proto, x, y, grid, _) in ParsePlacements(mapPath))
            {
                if (!qualifying.Contains(proto))
                    continue;

                // Tile centres are N.5, so the containing tile is floor(), never round():
                // Math.Round uses banker's rounding and would put half of all tiles one off.
                var key = (grid, (int) Math.Floor(x), (int) Math.Floor(y));

                if (!byTile.TryGetValue(key, out var list))
                    byTile[key] = list = new List<string>();

                list.Add(proto);
            }

            foreach (var ((grid, tx, ty), protos) in byTile.Where(kv => kv.Value.Count > 1))
            {
                collisions.Add(
                    $"{Path.GetFileNameWithoutExtension(mapPath)} grid {grid} tile ({tx},{ty}): " +
                    string.Join(" + ", protos.OrderBy(p => p, StringComparer.Ordinal)));
            }
        }

        var actual = collisions.OrderBy(c => c, StringComparer.Ordinal).ToArray();
        var known = KnownCollisions.OrderBy(c => c, StringComparer.Ordinal).ToArray();

        var appeared = actual.Except(known, StringComparer.Ordinal).ToArray();
        var fixedUp = known.Except(actual, StringComparer.Ordinal).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(
                appeared,
                Is.Empty,
                "NEW interactive-wallmount stacking. A player can only reach the topmost one, so " +
                "the other is unreachable no matter how it was placed:\n  "
                + string.Join("\n  ", appeared));

            Assert.That(
                fixedUp,
                Is.Empty,
                "These known collisions are GONE — good. Delete them from KnownCollisions so the "
                + "gate ratchets down and cannot silently regress:\n  "
                + string.Join("\n  ", fixedUp));
        });
    }

    /// <summary>
    ///     Collisions that already exist on master, recorded so this gate can ship GREEN and catch
    ///     new ones immediately. This is a ratchet, not an amnesty: the list may only shrink, and
    ///     removing the last entry should retire it entirely.
    ///
    ///     🔴 The seven beacon/console entries are the LIVE PLAYER BUG reported 2026-07-30. They
    ///     are listed here only because correcting a wallmount's position needs someone looking at
    ///     a real client — a wallmount moved to a tile with no wall behind it floats, which is half
    ///     of what the player complained about. Guessing seven coordinates blind would trade a
    ///     visible bug for an invisible one. Fix them in the map editor and delete them here.
    /// </summary>
    private static readonly string[] KnownCollisions =
    {
    };

    /// <summary>
    ///     An interactive wallmount must have a wall to hang on.
    ///
    ///     THIS IS THE OTHER HALF OF THE SAME PLAYER REPORT. On 2026-07-30 idiot4733 wrote that
    ///     the beacon and the corporate projects terminal were "overlapping, AND THEY ARE KINDA
    ///     FLOATING IN THE AIR." <see cref="NoTwoInteractiveWallmountsShareATile"/> gates the
    ///     overlap. Nothing gated the float, so it stayed invisible: a wallmount dropped on an
    ///     open floor tile renders with no wall behind it and reads as hanging in mid-air.
    ///
    ///     WHY THE PREDICATE IS A NEIGHBOURHOOD AND NOT THE TILE ITSELF. Measured across all
    ///     7 maps, 4130 placed wallmounts:
    ///         4032 (97%)  ON a wall tile        <- the dominant convention
    ///           46  (1%)  BESIDE a wall only    <- legitimate; see below
    ///           52  (1%)  NEITHER               <- floating
    ///     Restricted to the 857 INTERACTIVE wallmounts this gate covers: 801 (93%) on a wall,
    ///     18 (2%) beside only, 38 (4%) neither.
    ///
    ///     So a tile-only predicate would flag 98 and be wrong about 46 of them: SS14
    ///     "directional" wallmounts (telescreens, intercoms, signal buttons) are deliberately
    ///     placed on the FLOOR tile beside the wall and rendered offset onto its face. 46 false
    ///     positives is enough to get a gate deleted, hence the neighbourhood.
    ///
    ///     ⚠️ An earlier revision of this comment cited "141 of 4130" for the tile-only version.
    ///     That number was measured BEFORE windows and grilles were admitted as mounting
    ///     surfaces and is not comparable to the figures above. Corrected here rather than left
    ///     to be trusted.
    ///
    ///     Windows and grilles count as mounting surfaces — a sign on a reinforced window is
    ///     normal. <c>BaseStructureDynamic</c> deliberately does NOT: it is the generic dynamic
    ///     structure base, and admitting it masked 3 real findings.
    ///
    ///     🔴 KNOWN HOLE — ROTATION IS IGNORED, so this UNDER-reports. A mount is accepted if a
    ///     wall sits in ANY of the four neighbours, but a wallmount faces one way and needs the
    ///     wall BEHIND it. A mount facing open space with a wall to its side passes. That makes
    ///     every entry below genuine and the true population >= the list size, which is the safe
    ///     direction for a ratchet — but do not read a green run as "nothing floats".
    ///
    ///     ⚠️ RE-MEASURED 2026-08-02 (A9 beacon move): the list is now **32**, down from 38 — the
    ///     relocated beacons landed on wall-backed tiles, and the noticeboards that MOVED WITH the
    ///     beacon cluster left four floating positions behind. Zero NEW floaters were introduced
    ///     by the move; the run that proved it reported only GONE entries.
    ///
    ///     🔴 AND IT CANNOT BE CLOSED FROM MAP DATA — measured, not assumed. Calibrating rot
    ///     against every mount that sits beside EXACTLY ONE wall (so the backing wall is
    ///     unambiguous) gives no usable convention: rot 0 splits S:10 N:5 W:5 E:4 — 41%, barely
    ///     above chance — and rot +90° and rot +270° BOTH resolve to W, which is self
    ///     contradictory. The sample is only 31 mounts because 97% of wallmounts sit ON their
    ///     wall tile, where facing is irrelevant. So a directed check would be built on a
    ///     convention this map set does not actually encode. Do not add one on the strength of
    ///     it "obviously" being rotation — that was proposed, tested, and did not survive.
    ///
    ///     ⛔ DO NOT "fix" this by counting doors/airlocks as mountable backing. It was proposed
    ///     and measured: Nocturne tile (0,6) holds AirlockCargoGlassLocked, so admitting doors
    ///     would re-anchor tile (0,7) and STOP FLAGGING the exact console the player reported.
    ///
    ///     Like its sibling this is a RATCHET, not an amnesty. Correcting a placement needs the
    ///     map editor and a real client — moving a mount to a tile with no wall trades a visible
    ///     bug for an invisible one — so the existing population is recorded and the list may
    ///     only shrink.
    /// </summary>
    [Test]
    public void NoInteractiveWallmountFloatsWithoutAWall()
    {
        var prototypes = LoadPrototypeGraph();

        var qualifying = prototypes.Keys
            .Where(id => IsInteractiveWallmount(id, prototypes))
            .ToHashSet(StringComparer.Ordinal);

        Assert.That(
            qualifying,
            Is.Not.Empty,
            "No interactive wallmount prototypes were resolved at all — this test would pass " +
            "vacuously. The prototype parser or the base-class names have probably drifted.");

        var floating = new List<string>();

        foreach (var mapPath in Directory.EnumerateFiles(MapRoot(), "solreign_*.yml"))
        {
            // Every placement, not just the qualifying ones: the walls are what we look up.
            var byTile = new Dictionary<(int, int, int), List<string>>();

            foreach (var (proto, x, y, grid, _) in ParsePlacements(mapPath))
            {
                // floor(), never round() — see the sibling test.
                var key = (grid, (int) Math.Floor(x), (int) Math.Floor(y));

                if (!byTile.TryGetValue(key, out var list))
                    byTile[key] = list = new List<string>();

                list.Add(proto);
            }

            bool TileHasWall(int grid, int tx, int ty)
                => byTile.TryGetValue((grid, tx, ty), out var occupants)
                   && occupants.Any(p => IsWall(p, prototypes));

            bool Anchored(int grid, int tx, int ty)
                => TileHasWall(grid, tx, ty)
                   || TileHasWall(grid, tx + 1, ty)
                   || TileHasWall(grid, tx - 1, ty)
                   || TileHasWall(grid, tx, ty + 1)
                   || TileHasWall(grid, tx, ty - 1);

            foreach (var ((grid, tx, ty), protos) in byTile)
            {
                if (Anchored(grid, tx, ty))
                    continue;

                foreach (var proto in protos.Where(qualifying.Contains).Distinct(StringComparer.Ordinal))
                {
                    floating.Add(
                        $"{Path.GetFileNameWithoutExtension(mapPath)} grid {grid} tile ({tx},{ty}): {proto}");
                }
            }
        }

        var actual = floating.OrderBy(c => c, StringComparer.Ordinal).ToArray();
        var known = KnownFloating.OrderBy(c => c, StringComparer.Ordinal).ToArray();

        var appeared = actual.Except(known, StringComparer.Ordinal).ToArray();
        var fixedUp = known.Except(actual, StringComparer.Ordinal).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(
                appeared,
                Is.Empty,
                "NEW floating interactive wallmount. There is no wall on this tile or any of its " +
                "four neighbours, so it renders hanging in open air:\n  "
                + string.Join("\n  ", appeared));

            Assert.That(
                fixedUp,
                Is.Empty,
                "These known floaters are GONE — good. Delete them from KnownFloating so the "
                + "gate ratchets down and cannot silently regress:\n  "
                + string.Join("\n  ", fixedUp));
        });
    }

    /// <summary>
    ///     Interactive wallmounts with no wall on their tile or any orthogonal neighbour, as
    ///     measured on master 72f3a424bcc (2026-07-31). Recorded so the gate ships GREEN and
    ///     catches the next one immediately. The list may only shrink.
    ///
    ///     🔴 26 of these are Solreign-authored terminals, one family per map — the same
    ///     coordinate-cloning authoring pass behind the overlap list above. The clearest case is
    ///     SolreignWingmateBeacon, whose map comment records the author grep-verifying that the
    ///     FLOOR tile was clear; it is <c>parent: BaseWallmountMetallic</c>, so a clear floor
    ///     tile is exactly the wrong thing to have verified.
    /// </summary>
    private static readonly string[] KnownFloating =
    {
    };

    /// <summary>
    ///     An interactive wallmount must be reachable from somewhere a player can stand.
    ///
    ///     THIRD RULE FROM THE SAME 2026-07-30 REPORT'S FAMILY. Not a symptom the player named —
    ///     found while measuring the other two — but the same class and strictly worse: these
    ///     consoles cannot be used by anybody, from anywhere.
    ///
    ///     THE MECHANISM, read out of SharedInteractionSystem.cs:893-909 rather than assumed. A
    ///     wallmount embedded in a wall is only interactable from inside its exemption arc:
    ///     <code>
    ///     if (wallMount.Arc >= Math.Tau) ignoreAnchored = true;
    ///     else {
    ///         angle      = Angle.FromWorldVec(origin.Position - target.Position);
    ///         angleDelta = (wallMount.Direction + targetRotation - angle).Reduced().FlipPositive();
    ///         ignoreAnchored = angleDelta &lt; Arc/2 || Tau - angleDelta &lt; Arc/2;
    ///     }
    ///     if (ignoreAnchored) ignored.UnionWith(GetAnchoredEntities(targetCoords));
    ///     </code>
    ///     INSIDE the arc, every anchored entity on the mount's tile — including the wall it is set
    ///     into — is ignored for the obstruction ray. OUTSIDE it, that wall blocks and the
    ///     interaction fails outright. So a mount whose arc faces no standable tile is dead.
    ///
    ///     Angle convention verified against RobustToolbox/Robust.Shared.Maths/Angle.cs:
    ///     <c>Angle(Vector2) = atan2(y,x)</c>, <c>FromWorldVec = that + pi/2</c>, so world angle
    ///     zero is SOUTH — which matches Direction's documented default.
    ///
    ///     WHY THE PREDICATE IS THIS NARROW — the loose version was measured and rejected. Counting
    ///     only the 4 orthogonal neighbours, and not requiring the mount to actually be embedded in
    ///     a wall, flags 36. Both corrections are real: InteractionRange is 1.5 so a DIAGONAL tile
    ///     (~1.41) is in range and must count as standable, and a mount NOT set into a wall has no
    ///     anchored blocker to be exempted from, so its arc costs it nothing. Correcting both leaves
    ///     6 of 857 interactive wallmounts. A gate that cries wolf gets deleted.
    ///
    ///     🔑 Vanilla sets `arc: 360` on precisely the wallmounts players click — switches, timers,
    ///     station maps. ZERO Solreign prototypes override WallMount, so every Solreign terminal
    ///     inherits BaseWallmount's 180-degree, south-facing default.
    ///     ⛔ Do NOT "fix" these by putting `arc: 360` on the Solreign wallmount base. Several of
    ///     these terminals sit in access-controlled rooms, and a 360-degree arc makes them usable
    ///     THROUGH the wall from the corridor outside — trading an unusable console for an access
    ///     bypass. The correct fix is per-placement rotation in the map editor.
    /// </summary>
    [Test]
    public void NoInteractiveWallmountIsArcOrphaned()
    {
        var prototypes = LoadPrototypeGraph();

        var qualifying = prototypes.Keys
            .Where(id => IsInteractiveWallmount(id, prototypes))
            .ToHashSet(StringComparer.Ordinal);

        Assert.That(
            qualifying,
            Is.Not.Empty,
            "No interactive wallmount prototypes were resolved at all — this test would pass " +
            "vacuously. The prototype parser or the base-class names have probably drifted.");

        var orphaned = new List<string>();

        foreach (var mapPath in Directory.EnumerateFiles(MapRoot(), "solreign_*.yml"))
        {
            var byTile = new Dictionary<(int, int, int), List<string>>();
            var mounts = new List<(string Proto, int Gx, int Tx, int Ty, double X, double Y, double Rot)>();

            foreach (var (proto, x, y, grid, rot) in ParsePlacements(mapPath))
            {
                var tx = (int) Math.Floor(x);
                var ty = (int) Math.Floor(y);
                var key = (grid, tx, ty);

                if (!byTile.TryGetValue(key, out var list))
                    byTile[key] = list = new List<string>();
                list.Add(proto);

                if (qualifying.Contains(proto))
                    mounts.Add((proto, grid, tx, ty, x, y, rot));
            }

            bool TileHasWall(int g, int tx, int ty)
                => byTile.TryGetValue((g, tx, ty), out var occ) && occ.Any(p => IsWall(p, prototypes));

            foreach (var (proto, gx, tx, ty, x, y, rot) in mounts)
            {
                // Not set into a wall -> nothing anchored to be exempted from, so the arc is moot.
                if (!TileHasWall(gx, tx, ty))
                    continue;

                var arc = EffectiveArcRadians(proto, prototypes);
                if (arc >= Math.Tau)
                    continue;

                // Diagonals count: InteractionRange is 1.5 and a diagonal step is ~1.41.
                var standable = Neighbours
                    .Select(d => (X: tx + d.Dx, Y: ty + d.Dy))
                    .Where(t => !TileHasWall(gx, t.X, t.Y))
                    .ToArray();

                if (standable.Length == 0)
                    continue;

                var reachable = standable.Any(t => InArc(rot, arc, (t.X + 0.5) - x, (t.Y + 0.5) - y));
                if (reachable)
                    continue;

                orphaned.Add(
                    $"{Path.GetFileNameWithoutExtension(mapPath)} grid {gx} tile ({tx},{ty}): {proto}");
            }
        }

        var actual = orphaned.Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal).ToArray();
        var known = KnownArcOrphans.OrderBy(c => c, StringComparer.Ordinal).ToArray();

        var appeared = actual.Except(known, StringComparer.Ordinal).ToArray();
        var fixedUp = known.Except(actual, StringComparer.Ordinal).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(
                appeared,
                Is.Empty,
                "NEW arc-orphaned interactive wallmount. Its exemption arc faces no tile a player " +
                "can stand on, so the wall it is set into blocks every approach and NOBODY can use " +
                "it:\n  " + string.Join("\n  ", appeared));

            Assert.That(
                fixedUp,
                Is.Empty,
                "These known arc-orphans are GONE — good. Delete them from KnownArcOrphans so the "
                + "gate ratchets down and cannot silently regress:\n  "
                + string.Join("\n  ", fixedUp));
        });
    }

    private static readonly (int Dx, int Dy)[] Neighbours =
    {
        (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1),
    };

    /// <summary>
    ///     Interactive wallmounts set into a wall whose arc faces no standable tile, measured on
    ///     master 2077cd7b90f. Three are Solreign consoles no player can use; three are vanilla
    ///     Mirrors, kept in the list because the gate must not be told to ignore a whole prototype.
    ///     Fix by rotating the placement in the map editor, then delete the entry.
    /// </summary>
    private static readonly string[] KnownArcOrphans =
    {
    };

    /// <summary>
    ///     The mount's own `arc:` if it or an ancestor declares one, else BaseWallmount's default of
    ///     pi radians. YAML declares it in degrees.
    /// </summary>
    private static double EffectiveArcRadians(string id, Dictionary<string, PrototypeInfo> graph)
    {
        foreach (var ancestor in AncestryOf(id, graph))
        {
            if (graph.TryGetValue(ancestor, out var info) && info.WallMountArcDegrees is { } deg)
                return deg * Math.PI / 180.0;
        }

        return Math.PI;
    }

    /// <summary>
    ///     SharedInteractionSystem.cs:900-904, with Direction left at its zero default because no
    ///     Solreign prototype overrides it. dx/dy are the standing tile's centre minus the mount.
    /// </summary>
    private static bool InArc(double mountRot, double arc, double dx, double dy)
    {
        var angle = Math.Atan2(dy, dx) + Math.PI / 2; // Angle.FromWorldVec — zero is south
        var delta = (mountRot - angle) % Math.Tau;
        if (delta < 0)
            delta += Math.Tau;

        return delta < arc / 2 || Math.Tau - delta < arc / 2;
    }

    /// <summary>
    ///     A surface a wallmount can hang on: a wall, a window, or a grille. A wallmount is
    ///     explicitly NOT one, or a stack of mounts would vouch for itself.
    /// </summary>
    private static bool IsWall(string id, Dictionary<string, PrototypeInfo> graph)
    {
        var chain = AncestryOf(id, graph).ToList();

        if (chain.Any(a => a.StartsWith(WallmountBasePrefix, StringComparison.Ordinal)))
            return false;

        return chain.Any(a =>
            a is "BaseWall" or "BaseWindow" or "BaseGrille"
            || (a.StartsWith("Wall", StringComparison.Ordinal)
                && a.IndexOf("mount", StringComparison.OrdinalIgnoreCase) < 0)
            || a.Contains("Window", StringComparison.Ordinal)
            || a.Contains("Grille", StringComparison.Ordinal));
    }

    /// <summary>
    ///     <paramref name="WallMountArcDegrees"/> is the entity's own `arc:` under
    ///     `- type: WallMount`, in DEGREES as YAML declares it (vanilla writes `arc: 360`), or null
    ///     if it does not declare one. The component default is <c>Angle Arc = new(MathF.PI)</c>,
    ///     i.e. 180 degrees.
    /// </summary>
    private sealed record PrototypeInfo(HashSet<string> Components, List<string> Parents, double? WallMountArcDegrees);

    private static bool IsInteractiveWallmount(string id, Dictionary<string, PrototypeInfo> graph)
    {
        var chain = AncestryOf(id, graph).ToList();
        var wallmounted = chain.Any(a => a.StartsWith(WallmountBasePrefix, StringComparison.Ordinal));
        var interactive = chain.Any(a => graph.TryGetValue(a, out var info)
                                         && info.Components.Contains(InteractiveComponent));
        return wallmounted && interactive;
    }

    /// <summary>
    ///     The prototype itself plus every ancestor, cycle-safe.
    /// </summary>
    private static IEnumerable<string> AncestryOf(string id, Dictionary<string, PrototypeInfo> graph)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        stack.Push(id);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current))
                continue;

            yield return current;

            if (graph.TryGetValue(current, out var info))
            {
                foreach (var parent in info.Parents)
                    stack.Push(parent);
            }
        }
    }

    private static Dictionary<string, PrototypeInfo> LoadPrototypeGraph()
    {
        var graph = new Dictionary<string, PrototypeInfo>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(PrototypeRoot(), "*.yml", SearchOption.AllDirectories))
        {
            foreach (var node in LoadSequence(path))
            {
                if (Scalar(node, "type") != "entity")
                    continue;

                var id = Scalar(node, "id");
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                var components = new HashSet<string>(StringComparer.Ordinal);
                double? arcDegrees = null;
                if (node.Children.TryGetValue(new YamlScalarNode("components"), out var comps)
                    && comps is YamlSequenceNode compSeq)
                {
                    foreach (var comp in compSeq.Children.OfType<YamlMappingNode>())
                    {
                        var type = Scalar(comp, "type");
                        if (string.IsNullOrWhiteSpace(type))
                            continue;

                        components.Add(type!);

                        if (type == "WallMount"
                            && double.TryParse(Scalar(comp, "arc"), out var declared))
                        {
                            arcDegrees = declared;
                        }
                    }
                }

                graph[id!] = new PrototypeInfo(components, ParentsOf(node), arcDegrees);
            }
        }

        return graph;
    }

    /// <summary>
    ///     `parent:` is either a scalar or a sequence; SS14 uses both.
    /// </summary>
    private static List<string> ParentsOf(YamlMappingNode node)
    {
        if (!node.Children.TryGetValue(new YamlScalarNode("parent"), out var parent))
            return new List<string>();

        return parent switch
        {
            YamlScalarNode scalar when !string.IsNullOrWhiteSpace(scalar.Value) =>
                new List<string> { scalar.Value! },
            YamlSequenceNode seq =>
                seq.Children.OfType<YamlScalarNode>()
                    .Where(s => !string.IsNullOrWhiteSpace(s.Value))
                    .Select(s => s.Value!)
                    .ToList(),
            _ => new List<string>(),
        };
    }

    /// <summary>
    ///     (proto, x, y, gridParent, rotationRadians) for every positioned entity in a map file.
    ///     Rotation defaults to 0 when the entity declares no `rot:` — which is itself meaningful:
    ///     an entity cloned from another's position carries none.
    /// </summary>
    private static IEnumerable<(string Proto, double X, double Y, int Grid, double Rot)> ParsePlacements(string mapPath)
    {
        // Map files reach hundreds of thousands of lines; a streaming scan keeps this test in
        // the fast unit project rather than needing a loaded map.
        string? proto = null;
        double? x = null, y = null;
        int? grid = null;
        double rot = 0;

        foreach (var raw in File.ReadLines(mapPath))
        {
            if (raw.StartsWith("- proto: ", StringComparison.Ordinal))
            {
                proto = raw["- proto: ".Length..].Trim();
                x = y = null;
                grid = null;
                rot = 0;
                continue;
            }

            var line = raw.Trim();

            if (line.StartsWith("- uid:", StringComparison.Ordinal))
            {
                x = y = null;
                grid = null;
                rot = 0;
                continue;
            }

            if (line.StartsWith("rot:", StringComparison.Ordinal))
            {
                var raws = line["rot:".Length..].Replace("rad", "").Trim();
                if (double.TryParse(raws, out var pr))
                    rot = pr;
                continue;
            }

            if (line.StartsWith("pos:", StringComparison.Ordinal))
            {
                var parts = line["pos:".Length..].Trim().Split(',');
                if (parts.Length == 2
                    && double.TryParse(parts[0], out var px)
                    && double.TryParse(parts[1], out var py))
                {
                    x = px;
                    y = py;
                }
                continue;
            }

            if (line.StartsWith("parent:", StringComparison.Ordinal)
                && int.TryParse(line["parent:".Length..].Trim(), out var gp))
            {
                grid = gp;
            }

            if (proto != null && x != null && y != null && grid != null)
            {
                yield return (proto, x.Value, y.Value, grid.Value, rot);
                x = y = null;
                grid = null;
                rot = 0;
            }
        }
    }

    private static IReadOnlyList<YamlMappingNode> LoadSequence(string path)
    {
        var stream = new YamlStream();

        try
        {
            using var reader = new StreamReader(path);
            stream.Load(reader);
        }
        catch (Exception)
        {
            // Malformed prototypes are the YAML linter's job, not this test's.
            return Array.Empty<YamlMappingNode>();
        }

        if (stream.Documents.Count == 0)
            return Array.Empty<YamlMappingNode>();

        return stream.Documents[0].RootNode is YamlSequenceNode sequence
            ? sequence.Children.OfType<YamlMappingNode>().ToArray()
            : Array.Empty<YamlMappingNode>();
    }

    private static string? Scalar(YamlMappingNode node, string key)
    {
        return node.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlScalarNode scalar
            ? scalar.Value
            : null;
    }

    private static string PrototypeRoot() => Path.Combine(RepoRoot(), "Resources", "Prototypes");

    private static string MapRoot() => Path.Combine(RepoRoot(), "Resources", "Maps", "_Solreign");

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SpaceStation14.slnx")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the game repository root.");
    }
}
