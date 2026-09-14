"""Pure stdlib tests for Tools/_Solreign/MapFoundry/map_foundry.py.

These tests exercise the Foundry's two read-only operations against synthetic
prototype trees, manifests, and CLI invocations. The Foundry must never mutate
map files; every test isolates its work to a TemporaryDirectory so no real
prototype is touched.
"""

from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "map_foundry.py"
POOL_FILE = "Resources/Prototypes/_Solreign/map_pool.yml"
PROTOTYPE_ROOT = "Resources/Prototypes"
MAP_ROOT = "Resources/Maps/_Solreign"

SPEC = importlib.util.spec_from_file_location("solreign_map_foundry", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
MAP_FOUNDRY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MAP_FOUNDRY)


# --------------------------------------------------------------------------- #
# Synthetic fixture builders                                                   #
# --------------------------------------------------------------------------- #


def game_map_yml(
    map_id: str,
    map_path: str = "/Maps/_Solreign/placeholder.yml",
    min_players: int = 0,
    max_players: int | None = 35,
    *,
    extra: str = "",
) -> str:
    """Return a minimal `- type: gameMap` block.

    The map_path argument is the engine-style `/Maps/...` form; tests are
    responsible for placing the actual file on disk if they want the resolver
    to accept the path. ``max_players=None`` OMITS the ``maxPlayers:`` line
    entirely so the block matches the canonical real-repo shape for
    ``SolreignTerminus`` (unbounded upper range).
    """
    max_line = (
        f"  maxPlayers: {max_players}\n" if max_players is not None else ""
    )
    return (
        f"- type: gameMap\n"
        f"  id: {map_id}\n"
        f"  mapPath: {map_path}\n"
        f"  minPlayers: {min_players}\n"
        + max_line
        + extra
    )


def pool_yml(pool_id: str, map_ids: list[str]) -> str:
    body = "\n".join(f"  - {map_id}" for map_id in map_ids)
    return (
        f"- type: gameMapPool\n"
        f"  id: {pool_id}\n"
        f"  maps:\n"
        f"{body}\n"
    )


def parallax_yml(parallax_id: str) -> str:
    return f"- type: parallax\n  id: {parallax_id}\n  layers: []\n"


def map_file_text() -> str:
    return "meta:\n  format: 7\n  category: Map\n"


def manifest_entry(path: str) -> dict[str, object]:
    return {
        "path": path,
        "anchor": {
            "proto": "SolreignWingmateBeacon",
            "uid": 900000,
            "pos": [1.5, 1.5],
            "parent": 2,
        },
        "max_manhattan_tiles": 4,
        "placements": [
            {
                "key": "fixture",
                "proto": "SolreignFixture",
                "uid": 901000,
                "pos": [2.5, 1.5],
                "parent": 2,
                "expected_tile": "FloorSteel",
                "expected_occupants": [],
                "component": {"type": "SolreignFixture", "fields": {"id": "x"}},
            }
        ],
    }


def write_file(root: Path, relative: str, content: str) -> Path:
    path = root / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")
    return path


def write_map_fixture(
    root: Path,
    map_id: str,
    *,
    file_name: str | None = None,
    map_path: str | None = None,
    min_players: int = 0,
    max_players: int | None = 35,
    prototype_subdir: str = "Resources/Prototypes/Maps/_Solreign",
) -> tuple[Path, Path, str]:
    """Write a gameMap prototype + matching map file; return (proto, map, resolved_path).

    ``max_players=None`` omits the ``maxPlayers:`` line (unbounded upper
    range, matching the canonical real-repo ``SolreignTerminus``).
    """
    file_name = file_name or f"{map_id.lower()}.yml"
    map_path = map_path or f"/Maps/_Solreign/{file_name}"
    resolved = f"Resources/Maps/_Solreign/{file_name}"
    proto_rel = f"{prototype_subdir}/{file_name}"
    map_rel = resolved
    write_file(root, proto_rel, game_map_yml(map_id, map_path, min_players, max_players))
    write_file(root, map_rel, map_file_text())
    return root / proto_rel, root / map_rel, resolved


# --------------------------------------------------------------------------- #
# Prototype parser tests                                                       #
# --------------------------------------------------------------------------- #


class ParsePrototypeTests(unittest.TestCase):
    def test_extracts_game_map_block(self) -> None:
        text = game_map_yml("SolreignOasis", "/Maps/_Solreign/solreign_oasis.yml", 0, 35)
        blocks = MAP_FOUNDRY.parse_prototype_text(text, source="synthetic.yml")
        self.assertEqual(len(blocks), 1)
        block = blocks[0]
        self.assertEqual(block["type"], "gameMap")
        self.assertEqual(block["id"], "SolreignOasis")
        self.assertEqual(block["mapPath"], "/Maps/_Solreign/solreign_oasis.yml")
        self.assertEqual(block["minPlayers"], 0)
        self.assertEqual(block["maxPlayers"], 35)

    def test_extracts_game_map_pool_block(self) -> None:
        text = pool_yml("SolreignMapPool", ["SolreignOasis", "SolreignLeviathan"])
        blocks = MAP_FOUNDRY.parse_prototype_text(text, source="pool.yml")
        self.assertEqual(len(blocks), 1)
        block = blocks[0]
        self.assertEqual(block["type"], "gameMapPool")
        self.assertEqual(block["id"], "SolreignMapPool")
        self.assertEqual(block["maps"], ["SolreignOasis", "SolreignLeviathan"])

    def test_ignores_non_game_map_blocks_with_reused_ids(self) -> None:
        text = (
            game_map_yml("SolreignNocturne", "/Maps/_Solreign/solreign_nocturne.yml")
            + "\n"
            + parallax_yml("SolreignNocturne")
        )
        blocks = MAP_FOUNDRY.parse_prototype_text(text, source="mixed.yml")
        game_maps = [b for b in blocks if b["type"] == "gameMap"]
        parallaxes = [b for b in blocks if b["type"] == "parallax"]
        self.assertEqual(len(game_maps), 1)
        self.assertEqual(len(parallaxes), 1)
        # The parallax id must not be conflated with the gameMap id.
        self.assertEqual(game_maps[0]["id"], "SolreignNocturne")
        self.assertEqual(parallaxes[0]["id"], "SolreignNocturne")

    def test_collects_only_game_maps_across_files(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            oasis = write_file(
                root,
                "Resources/Prototypes/Maps/_Solreign/oasis.yml",
                game_map_yml("SolreignOasis"),
            )
            _ = write_file(
                root,
                "Resources/Prototypes/_Solreign/parallax_stations.yml",
                parallax_yml("SolreignOasis"),
            )
            collected = MAP_FOUNDRY.collect_game_maps(root, [oasis, root / "Resources/Prototypes/_Solreign/parallax_stations.yml"])
            self.assertIn("SolreignOasis", collected)
            # The parallax source must not register a second gameMap definition.
            oasis_occurrences = [spec for spec in collected.values() if spec["id"] == "SolreignOasis"]
            self.assertEqual(len(oasis_occurrences), 1)
            # Symlink resolution may rewrite the path (e.g., /tmp -> /private/tmp);
            # compare resolved forms.
            self.assertEqual(Path(oasis_occurrences[0]["source"]).resolve(), oasis.resolve())

    def test_duplicate_game_map_id_raises(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            a = write_file(
                root,
                "Resources/Prototypes/Maps/_Solreign/a.yml",
                game_map_yml("SolreignOasis", "/Maps/_Solreign/a.yml"),
            )
            b = write_file(
                root,
                "Resources/Prototypes/Maps/_Solreign/b.yml",
                game_map_yml("SolreignOasis", "/Maps/_Solreign/b.yml"),
            )
            with self.assertRaises(MAP_FOUNDRY.FoundryError) as ctx:
                MAP_FOUNDRY.collect_game_maps(root, [a, b])
            self.assertIn("duplicate", str(ctx.exception).lower())
            self.assertIn("SolreignOasis", str(ctx.exception))


# --------------------------------------------------------------------------- #
# Path normalization tests                                                     #
# --------------------------------------------------------------------------- #


class PathNormalizationTests(unittest.TestCase):
    def test_normalize_map_path_strips_leading_slash_and_prepends_resources(self) -> None:
        result = MAP_FOUNDRY.normalize_map_path("/Maps/_Solreign/solreign_oasis.yml")
        self.assertEqual(result, "Resources/Maps/_Solreign/solreign_oasis.yml")

    def test_normalize_map_path_passes_through_resources_prefix(self) -> None:
        result = MAP_FOUNDRY.normalize_map_path("Resources/Maps/_Solreign/solreign_oasis.yml")
        self.assertEqual(result, "Resources/Maps/_Solreign/solreign_oasis.yml")

    def test_normalize_manifest_path_rejects_absolute(self) -> None:
        with self.assertRaises(MAP_FOUNDRY.FoundryError):
            MAP_FOUNDRY.normalize_manifest_path("/etc/passwd")

    def test_normalize_manifest_path_rejects_parent_traversal(self) -> None:
        with self.assertRaises(MAP_FOUNDRY.FoundryError):
            MAP_FOUNDRY.normalize_manifest_path("../outside.yml")

    def test_normalize_manifest_path_rejects_non_yaml_suffix(self) -> None:
        with self.assertRaises(MAP_FOUNDRY.FoundryError):
            MAP_FOUNDRY.normalize_manifest_path("Resources/Maps/_Solreign/foo.txt")


# --------------------------------------------------------------------------- #
# Pool resolution tests                                                        #
# --------------------------------------------------------------------------- #


class ResolvePoolTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.root = Path(self.temp_dir.name)

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def write_default_seven_map_pool(self) -> dict[str, str]:
        ids = [
            "SolreignOasis",
            "SolreignNocturne",
            "SolreignPerihelion",
            "SolreignVerdant",
            "SolreignMeridian",
            "SolreignLeviathan",
            "SolreignTerminus",
        ]
        file_names = {
            "SolreignOasis": "solreign_oasis.yml",
            "SolreignNocturne": "solreign_nocturne.yml",
            "SolreignPerihelion": "solreign_perihelion.yml",
            "SolreignVerdant": "solreign_verdant.yml",
            "SolreignMeridian": "solreign_meridian.yml",
            "SolreignLeviathan": "solreign_leviathan.yml",
            "SolreignTerminus": "solreign_terminus.yml",
        }
        bands = {
            "SolreignOasis": (0, 35),
            "SolreignNocturne": (0, 15),
            "SolreignPerihelion": (0, 35),
            "SolreignVerdant": (0, 35),
            "SolreignMeridian": (35, 70),
            "SolreignLeviathan": (0, 90),
            "SolreignTerminus": (70, None),
        }
        resolved: dict[str, str] = {}
        for map_id in ids:
            _, _, path = write_map_fixture(
                self.root,
                map_id,
                file_name=file_names[map_id],
                min_players=bands[map_id][0],
                max_players=bands[map_id][1],
            )
            resolved[map_id] = path
        write_file(self.root, POOL_FILE, pool_yml("SolreignMapPool", ids))
        # A parallax that reuses one of the ids must not break resolution.
        write_file(
            self.root,
            "Resources/Prototypes/_Solreign/parallax_stations.yml",
            parallax_yml("SolreignNocturne") + parallax_yml("SolreignPerihelion") + parallax_yml("SolreignVerdant"),
        )
        return resolved

    def test_resolves_seven_maps_with_paths_and_bands(self) -> None:
        resolved = self.write_default_seven_map_pool()
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        self.assertFalse(result.errors, msg=str(result.errors))
        self.assertEqual(len(result.maps), 7)
        for spec in result.maps:
            self.assertEqual(spec["path"], resolved[spec["id"]])
        bands = {spec["id"]: (spec["population"]["min"], spec["population"]["max"]) for spec in result.maps}
        self.assertEqual(bands["SolreignLeviathan"], (0, 90))
        self.assertEqual(bands["SolreignMeridian"], (35, 70))
        # SolreignTerminus omits maxPlayers; the Foundry must surface that
        # as None (which JSON-serializes to null) — not as a malformed band.
        self.assertEqual(bands["SolreignTerminus"], (70, None))

    def _assert_error_code(self, result: MAP_FOUNDRY.PoolResult, code_substring: str) -> None:
        self.assertTrue(result.errors, msg=f"expected errors, got {result!r}")
        joined = " ".join(e["code"] + " " + e["message"] for e in result.errors).lower()
        self.assertIn(code_substring.lower(), joined, msg=joined)

    def test_missing_pool_id_fails_closed(self) -> None:
        self.write_default_seven_map_pool()
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="NoSuchPool",
            pool_file=self.root / POOL_FILE,
        )
        self.assertEqual(result.maps, [])
        self._assert_error_code(result, "NoSuchPool")
        self._assert_error_code(result, "pool")

    def test_pool_duplicate_map_id_fails_closed(self) -> None:
        write_map_fixture(self.root, "SolreignOasis", file_name="oasis.yml")
        write_file(
            self.root,
            POOL_FILE,
            pool_yml("SolreignMapPool", ["SolreignOasis", "SolreignOasis"]),
        )
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        self._assert_error_code(result, "SolreignOasis")
        self._assert_error_code(result, "duplicate")

    def test_unresolved_map_id_fails_closed(self) -> None:
        write_file(
            self.root,
            POOL_FILE,
            pool_yml("SolreignMapPool", ["SolreignOasis", "SolreignPhantom"]),
        )
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        self._assert_error_code(result, "SolreignPhantom")
        self._assert_error_code(result, "unresolved")

    def test_invalid_map_path_fails_closed(self) -> None:
        write_map_fixture(self.root, "SolreignOasis", file_name="oasis.yml")
        # Replace the prototype with a broken mapPath.
        write_file(
            self.root,
            "Resources/Prototypes/Maps/_Solreign/oasis.yml",
            game_map_yml("SolreignOasis", "/NotMaps/broken.yml"),
        )
        write_file(
            self.root,
            POOL_FILE,
            pool_yml("SolreignMapPool", ["SolreignOasis"]),
        )
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        self._assert_error_code(result, "SolreignOasis")
        self._assert_error_code(result, "map_path")

    def test_duplicate_resolved_path_fails_closed(self) -> None:
        write_file(
            self.root,
            "Resources/Prototypes/Maps/_Solreign/oasis.yml",
            game_map_yml("SolreignOasis", "/Maps/_Solreign/shared.yml"),
        )
        write_file(
            self.root,
            "Resources/Prototypes/Maps/_Solreign/oasis2.yml",
            game_map_yml("SolreignOasisAlt", "/Maps/_Solreign/shared.yml"),
        )
        write_file(self.root, "Resources/Maps/_Solreign/shared.yml", map_file_text())
        write_file(
            self.root,
            POOL_FILE,
            pool_yml("SolreignMapPool", ["SolreignOasis", "SolreignOasisAlt"]),
        )
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        self._assert_error_code(result, "shared.yml")
        self._assert_error_code(result, "duplicate")

    def test_invalid_population_band_fails_closed(self) -> None:
        write_map_fixture(self.root, "SolreignOasis", file_name="oasis.yml")
        write_file(
            self.root,
            "Resources/Prototypes/Maps/_Solreign/oasis.yml",
            game_map_yml(
                "SolreignOasis",
                map_path="/Maps/_Solreign/oasis.yml",
                min_players=10,
                max_players=5,
            ),
        )
        write_file(
            self.root,
            POOL_FILE,
            pool_yml("SolreignMapPool", ["SolreignOasis"]),
        )
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        self._assert_error_code(result, "population")

    def test_omitted_max_players_resolves_as_unbounded(self) -> None:
        # Canonical real-repo SolreignTerminus shape: minPlayers present,
        # maxPlayers omitted. Must resolve cleanly with population.max = None.
        write_map_fixture(
            self.root,
            "SolreignTerminus",
            file_name="terminus.yml",
            min_players=70,
            max_players=None,
        )
        write_file(
            self.root,
            POOL_FILE,
            pool_yml("SolreignMapPool", ["SolreignTerminus"]),
        )
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        self.assertFalse(result.errors, msg=str(result.errors))
        self.assertEqual(len(result.maps), 1)
        spec = result.maps[0]
        self.assertEqual(spec["id"], "SolreignTerminus")
        self.assertEqual(spec["population"]["min"], 70)
        self.assertIsNone(spec["population"]["max"])

    def test_omitted_max_players_renders_as_json_null(self) -> None:
        # Determinism guarantee: omitted max must serialize to JSON null,
        # never to a number, a string, or be silently dropped.
        write_map_fixture(
            self.root,
            "SolreignTerminus",
            file_name="terminus.yml",
            min_players=70,
            max_players=None,
        )
        write_file(
            self.root,
            POOL_FILE,
            pool_yml("SolreignMapPool", ["SolreignTerminus"]),
        )
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        payload = MAP_FOUNDRY.render_inventory(result)
        text = json.dumps(payload)
        # The exact substring that must appear for an omitted max:
        self.assertIn('"min": 70', text)
        self.assertIn('"max": null', text)
        # And the negated must-nots: never "max": -1, never "max": 0,
        # never "max": "null" (string), never a key with no value.
        self.assertNotIn('"max": -1', text)
        self.assertNotIn('"max": 0', text)
        self.assertNotIn('"max": "null"', text)

    def test_explicit_malformed_max_players_fails_closed(self) -> None:
        # maxPlayers must be an integer; the parser sees a non-numeric
        # string and the resolver must surface the error code rather than
        # silently coercing or dropping the value.
        write_file(
            self.root,
            "Resources/Prototypes/Maps/_Solreign/oasis.yml",
            game_map_yml(
                "SolreignOasis",
                map_path="/Maps/_Solreign/oasis.yml",
                min_players=0,
                max_players=None,
            ).replace(
                "  minPlayers: 0\n",
                "  minPlayers: 0\n  maxPlayers: many\n",
            ),
        )
        write_file(self.root, "Resources/Maps/_Solreign/oasis.yml", map_file_text())
        write_file(
            self.root,
            POOL_FILE,
            pool_yml("SolreignMapPool", ["SolreignOasis"]),
        )
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        self._assert_error_code(result, "SolreignOasis")
        self._assert_error_code(result, "population")
        # The error must specifically call out maxPlayers, not minPlayers.
        joined = " ".join(e["message"] for e in result.errors)
        self.assertIn("maxPlayers", joined)
        self.assertNotIn("minPlayers", joined)

    def test_explicit_max_below_min_fails_closed(self) -> None:
        # An explicit maxPlayers below minPlayers must fail closed even
        # when both are present. This is the "explicit max < min" case
        # the contract forbids.
        write_file(
            self.root,
            "Resources/Prototypes/Maps/_Solreign/oasis.yml",
            game_map_yml(
                "SolreignOasis",
                map_path="/Maps/_Solreign/oasis.yml",
                min_players=10,
                max_players=5,
            ),
        )
        write_file(self.root, "Resources/Maps/_Solreign/oasis.yml", map_file_text())
        write_file(
            self.root,
            POOL_FILE,
            pool_yml("SolreignMapPool", ["SolreignOasis"]),
        )
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        self._assert_error_code(result, "population")
        joined = " ".join(e["message"] for e in result.errors)
        self.assertIn("maxPlayers", joined)
        self.assertIn("minPlayers", joined)

    def test_omitted_max_preserves_inventory_determinism_with_explicit_max(
        self,
    ) -> None:
        # Determinism: mixing omitted-max and explicit-max maps in the
        # same pool must produce a stable, sorted inventory with null
        # exactly where the prototype omitted the field.
        ids = [
            "SolreignLeviathan",
            "SolreignOasis",
            "SolreignMeridian",
            "SolreignTerminus",
        ]
        bands = {
            "SolreignLeviathan": (0, 90),
            "SolreignOasis": (0, 35),
            "SolreignMeridian": (35, 70),
            "SolreignTerminus": (70, None),
        }
        for map_id in ids:
            write_map_fixture(
                self.root,
                map_id,
                file_name=f"{map_id.lower()}.yml",
                min_players=bands[map_id][0],
                max_players=bands[map_id][1],
            )
        write_file(self.root, POOL_FILE, pool_yml("SolreignMapPool", ids))
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        self.assertFalse(result.errors, msg=str(result.errors))
        payload = MAP_FOUNDRY.render_inventory(result)
        self.assertEqual([m["id"] for m in payload["maps"]], sorted(ids))
        by_id = {m["id"]: m for m in payload["maps"]}
        self.assertEqual(by_id["SolreignLeviathan"]["population"], {"min": 0, "max": 90})
        self.assertEqual(by_id["SolreignMeridian"]["population"], {"min": 35, "max": 70})
        self.assertEqual(by_id["SolreignTerminus"]["population"], {"min": 70, "max": None})


# --------------------------------------------------------------------------- #
# Validate-manifest tests                                                      #
# --------------------------------------------------------------------------- #


class ValidateManifestTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.root = Path(self.temp_dir.name)

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def write_full_seven_fixture(self) -> dict[str, str]:
        ids = [
            "SolreignOasis",
            "SolreignNocturne",
            "SolreignPerihelion",
            "SolreignVerdant",
            "SolreignMeridian",
            "SolreignLeviathan",
            "SolreignTerminus",
        ]
        file_names = {
            "SolreignOasis": "solreign_oasis.yml",
            "SolreignNocturne": "solreign_nocturne.yml",
            "SolreignPerihelion": "solreign_perihelion.yml",
            "SolreignVerdant": "solreign_verdant.yml",
            "SolreignMeridian": "solreign_meridian.yml",
            "SolreignLeviathan": "solreign_leviathan.yml",
            "SolreignTerminus": "solreign_terminus.yml",
        }
        resolved: dict[str, str] = {}
        for map_id in ids:
            _, _, path = write_map_fixture(
                self.root, map_id, file_name=file_names[map_id]
            )
            resolved[map_id] = path
        write_file(self.root, POOL_FILE, pool_yml("SolreignMapPool", ids))
        return resolved

    def write_manifest(self, entries: list[dict[str, object]], patch_id: str = "test-patch") -> Path:
        manifest_path = self.root / "manifest.json"
        manifest_path.write_text(
            json.dumps(
                {
                    "schema_version": 1,
                    "patch_id": patch_id,
                    "maps": entries,
                },
                indent=2,
            )
            + "\n",
            encoding="utf-8",
        )
        return manifest_path

    def test_complete_coverage_reports_complete(self) -> None:
        resolved = self.write_full_seven_fixture()
        manifest = self.write_manifest(
            [manifest_entry(resolved[map_id]) for map_id in sorted(resolved)]
        )
        result = MAP_FOUNDRY.validate_manifest(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
            manifest_path=manifest,
        )
        self.assertEqual(result["manifest_coverage"], "complete")
        self.assertEqual(result["manifest"]["missing_paths"], [])
        self.assertEqual(result["manifest"]["unexpected_paths"], [])
        self.assertEqual(result["manifest"]["duplicate_paths"], [])
        self.assertEqual(result["manifest"]["unresolved_paths"], [])
        self.assertEqual(result["manifest"]["out_of_root_paths"], [])

    def test_missing_pool_map_reported(self) -> None:
        resolved = self.write_full_seven_fixture()
        omitted = "SolreignTerminus"
        declared = [
            manifest_entry(resolved[map_id])
            for map_id in sorted(resolved)
            if map_id != omitted
        ]
        manifest = self.write_manifest(declared)
        result = MAP_FOUNDRY.validate_manifest(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
            manifest_path=manifest,
        )
        self.assertEqual(result["manifest_coverage"], "incomplete")
        self.assertIn(resolved[omitted], result["manifest"]["missing_paths"])

    def test_unexpected_manifest_path_reported(self) -> None:
        resolved = self.write_full_seven_fixture()
        # Use the leviathan path twice (once in place of the expected 7th map)
        entries = [manifest_entry(resolved[map_id]) for map_id in sorted(resolved)[:6]]
        entries.append(manifest_entry("Resources/Maps/_Solreign/stray.yml"))
        write_file(self.root, "Resources/Maps/_Solreign/stray.yml", map_file_text())
        manifest = self.write_manifest(entries)
        result = MAP_FOUNDRY.validate_manifest(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
            manifest_path=manifest,
        )
        self.assertEqual(result["manifest_coverage"], "incomplete")
        self.assertIn(
            "Resources/Maps/_Solreign/stray.yml",
            result["manifest"]["unexpected_paths"],
        )

    def test_duplicate_manifest_path_reported(self) -> None:
        resolved = self.write_full_seven_fixture()
        entries = [manifest_entry(resolved[map_id]) for map_id in sorted(resolved)[:6]]
        entries.append(entries[0])  # duplicate first entry's path
        manifest = self.write_manifest(entries)
        result = MAP_FOUNDRY.validate_manifest(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
            manifest_path=manifest,
        )
        self.assertEqual(result["manifest_coverage"], "incomplete")
        self.assertEqual(len(result["manifest"]["duplicate_paths"]), 1)
        self.assertEqual(result["manifest"]["duplicate_paths"][0], entries[0]["path"])

    def test_unresolved_manifest_path_reported(self) -> None:
        resolved = self.write_full_seven_fixture()
        entries = [manifest_entry(resolved[map_id]) for map_id in sorted(resolved)[:6]]
        entries.append(manifest_entry("Resources/Maps/_Solreign/missing.yml"))
        manifest = self.write_manifest(entries)
        result = MAP_FOUNDRY.validate_manifest(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
            manifest_path=manifest,
        )
        self.assertEqual(result["manifest_coverage"], "incomplete")
        self.assertIn(
            "Resources/Maps/_Solreign/missing.yml",
            result["manifest"]["unresolved_paths"],
        )

    def test_out_of_root_manifest_path_reported(self) -> None:
        resolved = self.write_full_seven_fixture()
        entries = [manifest_entry(resolved[map_id]) for map_id in sorted(resolved)[:6]]
        entries.append(manifest_entry("../outside.yml"))
        manifest = self.write_manifest(entries)
        result = MAP_FOUNDRY.validate_manifest(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
            manifest_path=manifest,
        )
        self.assertEqual(result["manifest_coverage"], "incomplete")
        self.assertIn("../outside.yml", result["manifest"]["out_of_root_paths"])


# --------------------------------------------------------------------------- #
# JSON determinism tests                                                      #
# --------------------------------------------------------------------------- #


class JsonDeterminismTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.root = Path(self.temp_dir.name)

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def test_inventory_json_is_sorted_by_id(self) -> None:
        ids = ["SolreignLeviathan", "SolreignOasis", "SolreignMeridian"]
        file_names = {m: f"{m.lower()}.yml" for m in ids}
        for map_id in ids:
            write_map_fixture(self.root, map_id, file_name=file_names[map_id])
        write_file(
            self.root,
            POOL_FILE,
            pool_yml("SolreignMapPool", ids),
        )
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        payload = MAP_FOUNDRY.render_inventory(result)
        self.assertEqual([m["id"] for m in payload["maps"]], sorted(ids))

    def test_inventory_json_has_no_absolute_paths(self) -> None:
        ids = ["SolreignOasis"]
        write_map_fixture(self.root, ids[0])
        write_file(self.root, POOL_FILE, pool_yml("SolreignMapPool", ids))
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        payload = MAP_FOUNDRY.render_inventory(result)
        text = json.dumps(payload)
        self.assertNotIn("/Users/", text)
        self.assertNotIn("/tmp/", text)
        self.assertNotIn(self.root.as_posix(), text)

    def test_inventory_json_has_no_timestamps(self) -> None:
        ids = ["SolreignOasis"]
        write_map_fixture(self.root, ids[0])
        write_file(self.root, POOL_FILE, pool_yml("SolreignMapPool", ids))
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        payload = MAP_FOUNDRY.render_inventory(result)
        text = json.dumps(payload)
        for forbidden in ("timestamp", "generated_at", "created_at", "isoformat", "T00:00", "2026"):
            self.assertNotIn(forbidden, text)

    def test_errors_are_sorted_by_code(self) -> None:
        result = MAP_FOUNDRY.PoolResult(
            pool_id="SolreignMapPool",
            maps=[],
            errors=[
                {"code": "zeta", "message": "z"},
                {"code": "alpha", "message": "a"},
                {"code": "mu", "message": "m"},
            ],
        )
        payload = MAP_FOUNDRY.render_inventory(result)
        codes = [e["code"] for e in payload["errors"]]
        self.assertEqual(codes, sorted(codes))


# --------------------------------------------------------------------------- #
# CLI tests                                                                   #
# --------------------------------------------------------------------------- #


class CliTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.root = Path(self.temp_dir.name)

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def run_cli(self, *args: str, expected_returncode: int = 0) -> subprocess.CompletedProcess[str]:
        result = subprocess.run(
            [sys.executable, str(SCRIPT), "--root", str(self.root), *args],
            text=True,
            capture_output=True,
            check=False,
        )
        self.assertEqual(
            result.returncode,
            expected_returncode,
            f"stdout:\n{result.stdout}\nstderr:\n{result.stderr}",
        )
        return result

    def write_full_seven_fixture(self) -> dict[str, str]:
        ids = [
            "SolreignOasis",
            "SolreignNocturne",
            "SolreignPerihelion",
            "SolreignVerdant",
            "SolreignMeridian",
            "SolreignLeviathan",
            "SolreignTerminus",
        ]
        file_names = {m: f"{m.lower()}.yml" for m in ids}
        resolved: dict[str, str] = {}
        for map_id in ids:
            _, _, path = write_map_fixture(self.root, map_id, file_name=file_names[map_id])
            resolved[map_id] = path
        write_file(self.root, POOL_FILE, pool_yml("SolreignMapPool", ids))
        return resolved

    def test_inventory_emits_json_and_exits_zero(self) -> None:
        self.write_full_seven_fixture()
        result = self.run_cli("inventory")
        payload = json.loads(result.stdout)
        self.assertEqual(payload["schema_version"], 1)
        self.assertEqual(payload["pool_id"], "SolreignMapPool")
        self.assertEqual(len(payload["maps"]), 7)
        self.assertEqual(payload["errors"], [])

    def test_inventory_exits_nonzero_on_missing_pool(self) -> None:
        write_file(
            self.root,
            POOL_FILE,
            pool_yml("SolreignMapPool", ["SolreignPhantom"]),
        )
        result = self.run_cli("inventory", expected_returncode=2)
        payload = json.loads(result.stdout)
        self.assertTrue(any(e["code"] == "unresolved_map_id" for e in payload["errors"]))

    def test_validate_manifest_exits_zero_on_complete(self) -> None:
        resolved = self.write_full_seven_fixture()
        manifest = self.root / "manifest.json"
        manifest.write_text(
            json.dumps(
                {
                    "schema_version": 1,
                    "patch_id": "fixtures-v1",
                    "maps": [manifest_entry(resolved[m]) for m in sorted(resolved)],
                }
            )
            + "\n",
            encoding="utf-8",
        )
        result = self.run_cli("validate-manifest", "--manifest", str(manifest))
        payload = json.loads(result.stdout)
        self.assertEqual(payload["manifest_coverage"], "complete")
        self.assertEqual(payload["patch_id"], "fixtures-v1")

    def test_validate_manifest_exits_nonzero_on_partial(self) -> None:
        resolved = self.write_full_seven_fixture()
        manifest = self.root / "manifest.json"
        manifest.write_text(
            json.dumps(
                {
                    "schema_version": 1,
                    "patch_id": "fixtures-v1",
                    "maps": [manifest_entry(resolved[m]) for m in sorted(resolved)[:6]],
                }
            )
            + "\n",
            encoding="utf-8",
        )
        result = self.run_cli(
            "validate-manifest", "--manifest", str(manifest), expected_returncode=1
        )
        payload = json.loads(result.stdout)
        self.assertEqual(payload["manifest_coverage"], "incomplete")
        # Error must mention the missing map.
        codes = [e["code"] for e in payload["errors"]]
        self.assertIn("manifest_missing_pool_map", codes)

    def test_validate_manifest_exits_two_on_unreadable_manifest(self) -> None:
        self.write_full_seven_fixture()
        missing = self.root / "absent.json"
        result = self.run_cli(
            "validate-manifest", "--manifest", str(missing), expected_returncode=2
        )
        self.assertIn("manifest", result.stderr.lower())

    def test_validate_manifest_emits_diagnostic_to_stderr(self) -> None:
        resolved = self.write_full_seven_fixture()
        manifest = self.root / "manifest.json"
        manifest.write_text(
            json.dumps(
                {
                    "schema_version": 1,
                    "patch_id": "fixtures-v1",
                    "maps": [manifest_entry(resolved[m]) for m in sorted(resolved)[:6]],
                }
            )
            + "\n",
            encoding="utf-8",
        )
        result = self.run_cli(
            "validate-manifest", "--manifest", str(manifest), expected_returncode=1
        )
        self.assertTrue(result.stderr.strip())
        # The stderr line must be human-readable, not JSON.
        self.assertNotIn("schema_version", result.stderr)


# --------------------------------------------------------------------------- #
# Real-repo integration test (smoke)                                           #
# --------------------------------------------------------------------------- #


class RealRepoIntegrationTests(unittest.TestCase):
    """Run the Foundry against the actual SolreignMapPool and community manifest.

    This test runs only when the real repository is on disk; it is skipped
    otherwise so the suite stays green in clean checkouts. The Foundry
    contract is: ``SolreignTerminus`` deliberately omits ``maxPlayers`` to
    mean an unbounded upper range, and the inventory + manifest validation
    against the real repo must both succeed cleanly with all seven maps.
    """

    REAL_REPO = Path(__file__).resolve().parents[4]

    def test_real_inventory_resolves_all_seven_maps_with_omitted_terminus_max(
        self,
    ) -> None:
        real_root = self.REAL_REPO
        pool_file = real_root / "Resources" / "Prototypes" / "_Solreign" / "map_pool.yml"
        if not pool_file.exists():
            self.skipTest("real repository not available")
        result = MAP_FOUNDRY.resolve_pool(
            real_root,
            pool_id="SolreignMapPool",
            pool_file=pool_file,
        )
        # No errors: SolreignTerminus' omitted maxPlayers is the canonical
        # "unbounded upper range" shape, not a defect.
        self.assertFalse(
            result.errors,
            msg=f"expected clean inventory, got errors: {result.errors!r}",
        )
        all_seven = {
            "SolreignOasis",
            "SolreignNocturne",
            "SolreignPerihelion",
            "SolreignVerdant",
            "SolreignMeridian",
            "SolreignLeviathan",
            "SolreignTerminus",
        }
        self.assertEqual({m["id"] for m in result.maps}, all_seven)
        by_id = {m["id"]: m for m in result.maps}
        # SolreignTerminus' minPlayers is 70, maxPlayers is omitted (None).
        self.assertEqual(by_id["SolreignTerminus"]["population"]["min"], 70)
        self.assertIsNone(by_id["SolreignTerminus"]["population"]["max"])
        for entry in result.maps:
            self.assertTrue(entry["path"].startswith("Resources/Maps/_Solreign/"))
            self.assertTrue(entry["path"].endswith(".yml"))
            self.assertGreaterEqual(entry["population"]["min"], 0)
            if entry["population"]["max"] is not None:
                self.assertGreaterEqual(
                    entry["population"]["max"],
                    entry["population"]["min"],
                )

    def test_real_community_manifest_matches_real_pool(self) -> None:
        real_root = self.REAL_REPO
        manifest = (
            real_root
            / "Tools"
            / "_Solreign"
            / "MapPatch"
            / "manifests"
            / "community-fixtures-v1.json"
        )
        if not manifest.exists():
            self.skipTest("real community manifest not available")
        result = MAP_FOUNDRY.validate_manifest(
            real_root,
            pool_id="SolreignMapPool",
            pool_file=real_root / "Resources" / "Prototypes" / "_Solreign" / "map_pool.yml",
            manifest_path=manifest,
        )
        # The Foundry now resolves all seven pool maps (Terminus' omitted
        # maxPlayers is the canonical unbounded shape), so the manifest's
        # Terminus path is expected, not "unexpected", and coverage must be
        # complete.
        self.assertEqual(
            result["manifest_coverage"],
            "complete",
            msg=str(result),
        )
        self.assertEqual(
            result["manifest"]["missing_paths"],
            [],
            msg=result["manifest"]["missing_paths"],
        )
        self.assertEqual(
            result["manifest"]["unexpected_paths"],
            [],
            msg=result["manifest"]["unexpected_paths"],
        )
        self.assertEqual(
            result["manifest"]["unresolved_paths"],
            [],
            msg=result["manifest"]["unresolved_paths"],
        )
        self.assertEqual(
            result["manifest"]["duplicate_paths"],
            [],
            msg=result["manifest"]["duplicate_paths"],
        )
        self.assertEqual(
            result["manifest"]["out_of_root_paths"],
            [],
            msg=result["manifest"]["out_of_root_paths"],
        )
        self.assertEqual(result["errors"], [], msg=result["errors"])
        declared = set(result["manifest"]["declared_paths"])
        for path in (
            "Resources/Maps/_Solreign/solreign_leviathan.yml",
            "Resources/Maps/_Solreign/solreign_meridian.yml",
            "Resources/Maps/_Solreign/solreign_nocturne.yml",
            "Resources/Maps/_Solreign/solreign_oasis.yml",
            "Resources/Maps/_Solreign/solreign_perihelion.yml",
            "Resources/Maps/_Solreign/solreign_verdant.yml",
            "Resources/Maps/_Solreign/solreign_terminus.yml",
        ):
            self.assertIn(path, declared, msg=f"{path} not declared")


# --------------------------------------------------------------------------- #
# P1: parse_prototype_text must tolerate blank and comment lines inside lists #
# --------------------------------------------------------------------------- #


class ParsePrototypeListToleranceTests(unittest.TestCase):
    """P1: blank and comment lines inside a direct list must not truncate it."""

    def test_blank_line_in_maps_list_preserves_later_ids(self) -> None:
        text = (
            "- type: gameMapPool\n"
            "  id: SolreignMapPool\n"
            "  maps:\n"
            "  - SolreignOasis\n"
            "\n"
            "  - SolreignNocturne\n"
            "\n"
            "  - SolreignPerihelion\n"
        )
        blocks = MAP_FOUNDRY.parse_prototype_text(text, source="pool.yml")
        self.assertEqual(len(blocks), 1)
        self.assertEqual(
            blocks[0]["maps"],
            ["SolreignOasis", "SolreignNocturne", "SolreignPerihelion"],
        )

    def test_comment_line_in_maps_list_preserves_later_ids(self) -> None:
        text = (
            "# A leading file comment that must not be confused with a list line.\n"
            "- type: gameMapPool\n"
            "  id: SolreignMapPool\n"
            "  maps:\n"
            "  - SolreignOasis\n"
            "  # mid-list comment that must not truncate the list\n"
            "  - SolreignNocturne\n"
            "  # trailing comment that must not truncate the list\n"
            "  - SolreignPerihelion\n"
        )
        blocks = MAP_FOUNDRY.parse_prototype_text(text, source="pool.yml")
        self.assertEqual(len(blocks), 1)
        self.assertEqual(
            blocks[0]["maps"],
            ["SolreignOasis", "SolreignNocturne", "SolreignPerihelion"],
        )

    def test_mixed_blank_and_comment_lines_in_maps_list(self) -> None:
        text = (
            "- type: gameMapPool\n"
            "  id: SolreignMapPool\n"
            "  maps:\n"
            "  # first comment\n"
            "  - SolreignOasis\n"
            "\n"
            "  # second comment\n"
            "  - SolreignNocturne\n"
            "\n"
            "  - SolreignPerihelion\n"
        )
        blocks = MAP_FOUNDRY.parse_prototype_text(text, source="pool.yml")
        self.assertEqual(len(blocks), 1)
        self.assertEqual(
            blocks[0]["maps"],
            ["SolreignOasis", "SolreignNocturne", "SolreignPerihelion"],
        )

    def test_real_pool_yml_with_blanks_and_comments_parses_all_seven(self) -> None:
        # Mirrors the real Resources/Prototypes/_Solreign/map_pool.yml shape:
        # a long file-leading comment block, a blank line between `maps:`
        # and the first list item, and no inline comments.
        text = (
            "# Solreign map pool — long file-leading comment that exercises\n"
            "# the comment-skipping behavior of the parser.\n"
            "\n"
            "- type: gameMapPool\n"
            "  id: SolreignMapPool\n"
            "  maps:\n"
            "  - SolreignOasis\n"
            "  - SolreignNocturne\n"
            "  - SolreignPerihelion\n"
            "  - SolreignVerdant\n"
            "  - SolreignMeridian\n"
            "  - SolreignLeviathan\n"
            "  - SolreignTerminus\n"
        )
        blocks = MAP_FOUNDRY.parse_prototype_text(text, source="map_pool.yml")
        self.assertEqual(len(blocks), 1)
        self.assertEqual(
            blocks[0]["maps"],
            [
                "SolreignOasis",
                "SolreignNocturne",
                "SolreignPerihelion",
                "SolreignVerdant",
                "SolreignMeridian",
                "SolreignLeviathan",
                "SolreignTerminus",
            ],
        )

    def test_real_field_still_terminates_list(self) -> None:
        # A real sibling field at the same indent closes the list — only
        # blank and comment lines are inert.
        text = (
            "- type: gameMapPool\n"
            "  id: SolreignMapPool\n"
            "  maps:\n"
            "  - SolreignOasis\n"
            "  unrelatedField: value\n"
            "  - SolreignNocturne\n"
        )
        blocks = MAP_FOUNDRY.parse_prototype_text(text, source="pool.yml")
        self.assertEqual(len(blocks), 1)
        # The first item survives; the second is treated as a value of
        # unrelatedField or otherwise dropped — but the LIST is closed
        # by the new field, which is the correct behavior.
        self.assertEqual(blocks[0]["maps"], ["SolreignOasis"])


# --------------------------------------------------------------------------- #
# P1: population-band validation — missing min, float, bool, max<min          #
# --------------------------------------------------------------------------- #


class PopulationBandValidationTests(unittest.TestCase):
    """P1: validate the population band strictly and fail closed on bad input."""

    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.root = Path(self.temp_dir.name)

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def _write_pool_with_one_map(
        self,
        prototype_body: str,
        map_id: str = "SolreignOasis",
    ) -> None:
        write_file(
            self.root,
            "Resources/Prototypes/Maps/_Solreign/oasis.yml",
            prototype_body,
        )
        write_file(self.root, "Resources/Maps/_Solreign/oasis.yml", map_file_text())
        write_file(
            self.root,
            POOL_FILE,
            pool_yml("SolreignMapPool", [map_id]),
        )

    def test_missing_min_with_explicit_max_uses_exact_code(self) -> None:
        # No minPlayers line at all, but explicit maxPlayers. Must not
        # TypeError on `max < min` — must surface the exact code.
        body = (
            "- type: gameMap\n"
            "  id: SolreignOasis\n"
            "  mapPath: /Maps/_Solreign/oasis.yml\n"
            "  maxPlayers: 35\n"
        )
        self._write_pool_with_one_map(body)
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        self.assertEqual(result.maps, [])
        # The error must surface the exact code AND mention minPlayers so
        # the caller knows which field is bad. It must NOT crash.
        self.assertEqual(
            [e["code"] for e in result.errors],
            ["game_map_invalid_population_band"],
        )
        joined = " ".join(e["message"] for e in result.errors)
        self.assertIn("minPlayers", joined)

    def test_float_min_players_fails_closed(self) -> None:
        body = (
            "- type: gameMap\n"
            "  id: SolreignOasis\n"
            "  mapPath: /Maps/_Solreign/oasis.yml\n"
            "  minPlayers: 1.5\n"
            "  maxPlayers: 35\n"
        )
        self._write_pool_with_one_map(body)
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        self.assertEqual(result.maps, [])
        self.assertEqual(
            [e["code"] for e in result.errors],
            ["game_map_invalid_population_band"],
        )
        joined = " ".join(e["message"] for e in result.errors)
        self.assertIn("minPlayers", joined)

    def test_float_max_players_fails_closed(self) -> None:
        body = (
            "- type: gameMap\n"
            "  id: SolreignOasis\n"
            "  mapPath: /Maps/_Solreign/oasis.yml\n"
            "  minPlayers: 0\n"
            "  maxPlayers: 35.7\n"
        )
        self._write_pool_with_one_map(body)
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        self.assertEqual(result.maps, [])
        self.assertEqual(
            [e["code"] for e in result.errors],
            ["game_map_invalid_population_band"],
        )
        joined = " ".join(e["message"] for e in result.errors)
        self.assertIn("maxPlayers", joined)

    def test_bool_min_players_fails_closed(self) -> None:
        # `minPlayers: true` parses to Python True. bool is a subclass of
        # int, so naive int() would coerce; the strict path must reject.
        body = (
            "- type: gameMap\n"
            "  id: SolreignOasis\n"
            "  mapPath: /Maps/_Solreign/oasis.yml\n"
            "  minPlayers: true\n"
            "  maxPlayers: 35\n"
        )
        self._write_pool_with_one_map(body)
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        self.assertEqual(result.maps, [])
        self.assertEqual(
            [e["code"] for e in result.errors],
            ["game_map_invalid_population_band"],
        )

    def test_coercible_string_min_players_fails_closed(self) -> None:
        # `minPlayers: "1.5"` (quoted) parses to a string. Naive int() would
        # ValueError, but the strict path must reject the fractional string
        # without int-truncating.
        body = (
            "- type: gameMap\n"
            "  id: SolreignOasis\n"
            "  mapPath: /Maps/_Solreign/oasis.yml\n"
            "  minPlayers: '1.5'\n"
            "  maxPlayers: 35\n"
        )
        self._write_pool_with_one_map(body)
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        self.assertEqual(result.maps, [])
        self.assertEqual(
            [e["code"] for e in result.errors],
            ["game_map_invalid_population_band"],
        )

    def test_max_below_min_uses_exact_code(self) -> None:
        body = (
            "- type: gameMap\n"
            "  id: SolreignOasis\n"
            "  mapPath: /Maps/_Solreign/oasis.yml\n"
            "  minPlayers: 10\n"
            "  maxPlayers: 5\n"
        )
        self._write_pool_with_one_map(body)
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        self.assertEqual(result.maps, [])
        self.assertEqual(
            [e["code"] for e in result.errors],
            ["game_map_invalid_population_band"],
        )
        joined = " ".join(e["message"] for e in result.errors)
        self.assertIn("maxPlayers", joined)
        self.assertIn("minPlayers", joined)

    def test_negative_min_players_fails_closed(self) -> None:
        body = (
            "- type: gameMap\n"
            "  id: SolreignOasis\n"
            "  mapPath: /Maps/_Solreign/oasis.yml\n"
            "  minPlayers: -1\n"
            "  maxPlayers: 35\n"
        )
        self._write_pool_with_one_map(body)
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        self.assertEqual(result.maps, [])
        self.assertEqual(
            [e["code"] for e in result.errors],
            ["game_map_invalid_population_band"],
        )
        joined = " ".join(e["message"] for e in result.errors)
        self.assertIn("minPlayers", joined)

    def test_valid_min_and_max_still_resolve(self) -> None:
        # Sanity: the strict path must still accept the canonical shape.
        body = (
            "- type: gameMap\n"
            "  id: SolreignOasis\n"
            "  mapPath: /Maps/_Solreign/oasis.yml\n"
            "  minPlayers: 0\n"
            "  maxPlayers: 35\n"
        )
        self._write_pool_with_one_map(body)
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        self.assertEqual(result.errors, [], msg=str(result.errors))
        self.assertEqual(len(result.maps), 1)
        self.assertEqual(result.maps[0]["id"], "SolreignOasis")
        self.assertEqual(result.maps[0]["population"], {"min": 0, "max": 35})


# --------------------------------------------------------------------------- #
# P1: symlink escape rejection                                                #
# --------------------------------------------------------------------------- #


def _supports_symlinks() -> bool:
    """Symlink creation may fail on some platforms/filesystems."""
    try:
        with tempfile.TemporaryDirectory() as raw:
            link = Path(raw) / "link"
            link.symlink_to(Path(raw) / "target")
            return link.is_symlink()
    except (OSError, NotImplementedError):
        return False


class SymlinkEscapeTests(unittest.TestCase):
    """P1: a symlink that escapes the repository root must be rejected."""

    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.root = Path(self.temp_dir.name)
        # The repo's prototype + map roots.
        (self.root / "Resources" / "Prototypes" / "Maps" / "_Solreign").mkdir(
            parents=True, exist_ok=True
        )
        (self.root / "Resources" / "Prototypes" / "_Solreign").mkdir(
            parents=True, exist_ok=True
        )
        (self.root / "Resources" / "Maps" / "_Solreign").mkdir(
            parents=True, exist_ok=True
        )
        # An external root that lives outside the repo.
        self.external = Path(tempfile.mkdtemp(prefix="foundry-external-"))
        (self.external / "outside.yml").write_text(map_file_text(), encoding="utf-8")
        (self.external / "outside_proto.yml").write_text(
            game_map_yml("SolreignOutside", "/Maps/_Solreign/outside.yml"),
            encoding="utf-8",
        )

    def tearDown(self) -> None:
        self.temp_dir.cleanup()
        # Best-effort cleanup of the external dir.
        import shutil
        shutil.rmtree(self.external, ignore_errors=True)

    def test_symlinked_map_file_escape_fails_closed(self) -> None:
        if not _supports_symlinks():
            self.skipTest("symlinks not supported on this platform")
        # Place a real gameMap inside the repo pointing at the escape map.
        write_file(
            self.root,
            "Resources/Prototypes/Maps/_Solreign/oasis.yml",
            game_map_yml("SolreignOasis", "/Maps/_Solreign/oasis.yml"),
        )
        # Create the real map file first, then replace it with a symlink
        # that points outside the repo.
        real = self.root / "Resources" / "Maps" / "_Solreign" / "oasis.yml"
        real.write_text(map_file_text(), encoding="utf-8")
        real.unlink()
        real.symlink_to(self.external / "outside.yml")
        write_file(
            self.root,
            POOL_FILE,
            pool_yml("SolreignMapPool", ["SolreignOasis"]),
        )
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        # The map id is unresolved because the symlink escape is caught.
        codes = [e["code"] for e in result.errors]
        self.assertIn("game_map_invalid_map_path", codes)
        joined = " ".join(e["message"] for e in result.errors)
        # The error must mention the map id and not leak the external path.
        self.assertIn("SolreignOasis", joined)

    def test_symlinked_prototype_file_escape_fails_closed(self) -> None:
        if not _supports_symlinks():
            self.skipTest("symlinks not supported on this platform")
        # The prototype file itself is a symlink pointing outside.
        proto = self.root / "Resources" / "Prototypes" / "Maps" / "_Solreign" / "oasis.yml"
        proto.symlink_to(self.external / "outside_proto.yml")
        # The pool still lists the map.
        write_file(
            self.root,
            POOL_FILE,
            pool_yml("SolreignMapPool", ["SolreignOasis"]),
        )
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        # The gameMap is invisible to the resolver because its prototype
        # file is an escape.
        codes = [e["code"] for e in result.errors]
        self.assertIn("unresolved_map_id", codes)
        # The error must not leak the external path.
        joined = " ".join(e["message"] for e in result.errors)
        self.assertIn("SolreignOasis", joined)
        self.assertNotIn(self.external.as_posix(), joined)

    def test_symlinked_directory_escape_fails_closed(self) -> None:
        if not _supports_symlinks():
            self.skipTest("symlinks not supported on this platform")
        # The whole Resources/Prototypes/Maps/_Solreign directory is a
        # symlink to an external directory.
        target = self.external / "protos"
        target.mkdir()
        (target / "oasis.yml").write_text(
            game_map_yml("SolreignOasis", "/Maps/_Solreign/oasis.yml"),
            encoding="utf-8",
        )
        link = self.root / "Resources" / "Prototypes" / "Maps" / "_Solreign"
        # The link must be replaced.
        import shutil
        shutil.rmtree(link)
        link.symlink_to(target)
        write_file(
            self.root,
            POOL_FILE,
            pool_yml("SolreignMapPool", ["SolreignOasis"]),
        )
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        # Same shape as above: the escape is rejected.
        codes = [e["code"] for e in result.errors]
        self.assertIn("unresolved_map_id", codes)

    def test_internal_symlink_within_repo_is_accepted(self) -> None:
        # A symlink that stays inside the repo is fine.
        if not _supports_symlinks():
            self.skipTest("symlinks not supported on this platform")
        write_file(
            self.root,
            "Resources/Prototypes/Maps/_Solreign/oasis.yml",
            game_map_yml("SolreignOasis", "/Maps/_Solreign/oasis.yml"),
        )
        # Replace the real map file with a symlink to a sibling file
        # inside the same Resources/Maps root.
        alt = self.root / "Resources" / "Maps" / "_Solreign" / "alias.yml"
        alt.write_text(map_file_text(), encoding="utf-8")
        real = self.root / "Resources" / "Maps" / "_Solreign" / "oasis.yml"
        real.symlink_to(alt)
        write_file(
            self.root,
            POOL_FILE,
            pool_yml("SolreignMapPool", ["SolreignOasis"]),
        )
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        # The map resolves cleanly because the symlink stays in the repo.
        self.assertEqual(result.errors, [], msg=str(result.errors))
        self.assertEqual(len(result.maps), 1)
        self.assertEqual(result.maps[0]["id"], "SolreignOasis")

    def test_external_pool_file_path_fails_closed(self) -> None:
        if not _supports_symlinks():
            self.skipTest("symlinks not supported on this platform")
        # A pool file outside the repo is rejected.
        external_pool = self.external / "external_pool.yml"
        external_pool.write_text(
            pool_yml("SolreignMapPool", ["SolreignOasis"]),
            encoding="utf-8",
        )
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=external_pool,
        )
        codes = [e["code"] for e in result.errors]
        self.assertEqual(["pool_file_not_found"], codes)
        joined = " ".join(e["message"] for e in result.errors)
        # No leak of the external path.
        self.assertNotIn(self.external.as_posix(), joined)


# --------------------------------------------------------------------------- #
# P2: --pool is the sole source of the named gameMapPool                      #
# --------------------------------------------------------------------------- #


class PoolFileSourceOfTruthTests(unittest.TestCase):
    """P2: a same-id pool defined outside --pool must not satisfy the request."""

    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.root = Path(self.temp_dir.name)
        # A small prototype tree: one real gameMap, one pool in a different
        # file, and a (different) --pool file.
        write_file(
            self.root,
            "Resources/Prototypes/Maps/_Solreign/oasis.yml",
            game_map_yml("SolreignOasis", "/Maps/_Solreign/oasis.yml"),
        )
        write_file(self.root, "Resources/Maps/_Solreign/oasis.yml", map_file_text())

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def test_pool_in_other_file_does_not_satisfy_request(self) -> None:
        # A gameMapPool with the requested id in ANOTHER prototype file
        # must not satisfy the --pool request. The --pool file is empty,
        # so the named pool must be reported as not found.
        write_file(
            self.root,
            "Resources/Prototypes/_Solreign/other.yml",
            pool_yml("SolreignMapPool", ["SolreignOasis"]),
        )
        # The --pool file exists but defines a different id.
        write_file(
            self.root,
            POOL_FILE,
            pool_yml("OtherMapPool", ["SolreignOasis"]),
        )
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        codes = [e["code"] for e in result.errors]
        self.assertEqual(["pool_not_found"], codes)
        joined = " ".join(e["message"] for e in result.errors)
        self.assertIn("SolreignMapPool", joined)

    def test_pool_only_in_other_file_yet_default_pool_yields_not_found(self) -> None:
        # The default --pool path does not exist; the only SolreignMapPool
        # definition is in a sibling file. Must surface pool_not_found.
        write_file(
            self.root,
            "Resources/Prototypes/_Solreign/stray.yml",
            pool_yml("SolreignMapPool", ["SolreignOasis"]),
        )
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        codes = [e["code"] for e in result.errors]
        self.assertEqual(["pool_file_not_found"], codes)

    def test_duplicate_pool_definition_in_same_file_fails_closed(self) -> None:
        # Two `- type: gameMapPool` blocks with the same id in the --pool
        # file must surface a fail-closed error.
        text = (
            "- type: gameMapPool\n"
            "  id: SolreignMapPool\n"
            "  maps:\n"
            "  - SolreignOasis\n"
            "- type: gameMapPool\n"
            "  id: SolreignMapPool\n"
            "  maps:\n"
            "  - SolreignOasis\n"
        )
        write_file(self.root, POOL_FILE, text)
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        codes = [e["code"] for e in result.errors]
        self.assertEqual(["pool_duplicate_definition"], codes)
        joined = " ".join(e["message"] for e in result.errors)
        self.assertIn("SolreignMapPool", joined)


# --------------------------------------------------------------------------- #
# P2: error-path privacy                                                      #
# --------------------------------------------------------------------------- #


def _scan_for_absolute_leaks(text: str) -> list[str]:
    """Return a list of substrings that suggest an absolute path leak."""
    leaks: list[str] = []
    for hint in ("/Users/", "/home/", "/tmp/", "/var/", "/private/", "/etc/"):
        if hint in text:
            leaks.append(hint)
    return leaks


class ErrorPathPrivacyTests(unittest.TestCase):
    """P2: JSON payloads and stderr diagnostics must not leak absolute paths."""

    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.root = Path(self.temp_dir.name)

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def _run_cli(self, *args: str, expected_returncode: int | None = None) -> subprocess.CompletedProcess[str]:
        result = subprocess.run(
            [sys.executable, str(SCRIPT), "--root", str(self.root), *args],
            text=True,
            capture_output=True,
            check=False,
        )
        if expected_returncode is not None:
            self.assertEqual(
                result.returncode,
                expected_returncode,
                f"stdout:\n{result.stdout}\nstderr:\n{result.stderr}",
            )
        return result

    def test_missing_pool_file_message_has_no_absolute_path(self) -> None:
        # No pool file written at all.
        result = self._run_cli("inventory", expected_returncode=2)
        text = result.stdout + "\n" + result.stderr
        self.assertEqual(_scan_for_absolute_leaks(text), [])
        # The temp dir path itself must not appear either.
        self.assertNotIn(self.root.as_posix(), text)

    def test_duplicate_prototypes_message_has_no_absolute_path(self) -> None:
        # Two gameMap prototypes with the same id in different files.
        write_file(
            self.root,
            "Resources/Prototypes/Maps/_Solreign/a.yml",
            game_map_yml("SolreignOasis", "/Maps/_Solreign/a.yml"),
        )
        write_file(
            self.root,
            "Resources/Prototypes/Maps/_Solreign/b.yml",
            game_map_yml("SolreignOasis", "/Maps/_Solreign/b.yml"),
        )
        write_file(
            self.root,
            POOL_FILE,
            pool_yml("SolreignMapPool", ["SolreignOasis"]),
        )
        result = self._run_cli("inventory", expected_returncode=2)
        text = result.stdout + "\n" + result.stderr
        self.assertEqual(_scan_for_absolute_leaks(text), [])
        self.assertNotIn(self.root.as_posix(), text)

    def test_unreadable_manifest_message_has_no_absolute_path(self) -> None:
        # Pass a manifest path that does not exist; the diagnostic must not
        # echo the caller-supplied absolute path.
        result = self._run_cli(
            "validate-manifest",
            "--manifest",
            str(self.root / "absent.json"),
            expected_returncode=2,
        )
        text = result.stdout + "\n" + result.stderr
        self.assertEqual(_scan_for_absolute_leaks(text), [])
        # The temp dir absolute path must not appear (it would otherwise
        # be echoed back via the absent manifest path).
        self.assertNotIn(self.root.as_posix(), text)

    def test_absolute_manifest_path_in_manifest_does_not_leak(self) -> None:
        # A manifest that declares an absolute path under maps[].path must
        # be classified as out-of-root, with the raw value redacted.
        manifest = self.root / "manifest.json"
        manifest.write_text(
            json.dumps(
                {
                    "schema_version": 1,
                    "patch_id": "abs-paths",
                    "maps": [
                        {
                            "path": "/etc/passwd",
                            "anchor": {"proto": "X", "uid": 1, "pos": [0, 0], "parent": 1},
                            "max_manhattan_tiles": 1,
                            "placements": [],
                        }
                    ],
                }
            )
            + "\n",
            encoding="utf-8",
        )
        result = self._run_cli(
            "validate-manifest", "--manifest", str(manifest), expected_returncode=1
        )
        text = result.stdout + "\n" + result.stderr
        self.assertNotIn("/etc/passwd", text)
        self.assertNotIn("/etc/", text)
        # The pool is empty (no pool file), but the manifest is parsed,
        # so the path-privacy check above is still meaningful.

    def test_outside_root_manifest_value_does_not_leak(self) -> None:
        # A manifest entry whose path escapes the repo via parent
        # traversal must be reported as out-of-root, without echoing the
        # raw traversal path.
        manifest = self.root / "manifest.json"
        manifest.write_text(
            json.dumps(
                {
                    "schema_version": 1,
                    "patch_id": "escape",
                    "maps": [
                        {
                            "path": "../../../etc/passwd",
                            "anchor": {"proto": "X", "uid": 1, "pos": [0, 0], "parent": 1},
                            "max_manhattan_tiles": 1,
                            "placements": [],
                        }
                    ],
                }
            )
            + "\n",
            encoding="utf-8",
        )
        result = self._run_cli(
            "validate-manifest", "--manifest", str(manifest), expected_returncode=1
        )
        text = result.stdout + "\n" + result.stderr
        self.assertNotIn("../", text)
        self.assertNotIn("passwd", text)

    def test_inventory_payload_has_no_absolute_paths(self) -> None:
        # A clean inventory against a real-shape fixture must not leak any
        # absolute path through the JSON.
        ids = ["SolreignOasis", "SolreignNocturne"]
        for map_id in ids:
            write_map_fixture(self.root, map_id)
        write_file(self.root, POOL_FILE, pool_yml("SolreignMapPool", ids))
        result = self._run_cli("inventory", expected_returncode=0)
        text = result.stdout + "\n" + result.stderr
        self.assertEqual(_scan_for_absolute_leaks(text), [])
        self.assertNotIn(self.root.as_posix(), text)


# --------------------------------------------------------------------------- #
# P3: pool_invalid_map_id code + (code, message) stable sort                  #
# --------------------------------------------------------------------------- #


class ErrorCodeAndSortTests(unittest.TestCase):
    """P3: distinct code for non-string ids, and (code, message) sort order."""

    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.root = Path(self.temp_dir.name)

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def test_non_string_pool_entry_uses_pool_invalid_map_id(self) -> None:
        # The default pool helper writes string ids only. We hand-craft a
        # pool file with a list item that is just a bare word with no
        # quoting: the parser will see it as a string, so to exercise the
        # non-string branch we use the pool_yml helper with a list that
        # contains an empty slot (which the parser will treat as an empty
        # string). The fail-closed code is pool_invalid_map_id.
        text = (
            "- type: gameMapPool\n"
            "  id: SolreignMapPool\n"
            "  maps:\n"
            "  -\n"
            "  - SolreignOasis\n"
        )
        write_file(self.root, POOL_FILE, text)
        write_file(
            self.root,
            "Resources/Prototypes/Maps/_Solreign/oasis.yml",
            game_map_yml("SolreignOasis", "/Maps/_Solreign/oasis.yml"),
        )
        write_file(self.root, "Resources/Maps/_Solreign/oasis.yml", map_file_text())
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        codes = [e["code"] for e in result.errors]
        self.assertIn("pool_invalid_map_id", codes)
        # The duplicate id is NOT used for non-string entries.
        self.assertNotIn("pool_duplicate_map_id", codes)

    def test_duplicate_pool_entry_keeps_pool_duplicate_map_id(self) -> None:
        # A genuine duplicate must use the dedicated duplicate code, not
        # the invalid-id code.
        write_file(
            self.root,
            "Resources/Prototypes/Maps/_Solreign/oasis.yml",
            game_map_yml("SolreignOasis", "/Maps/_Solreign/oasis.yml"),
        )
        write_file(self.root, "Resources/Maps/_Solreign/oasis.yml", map_file_text())
        write_file(
            self.root,
            POOL_FILE,
            pool_yml("SolreignMapPool", ["SolreignOasis", "SolreignOasis"]),
        )
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        codes = [e["code"] for e in result.errors]
        self.assertIn("pool_duplicate_map_id", codes)
        self.assertNotIn("pool_invalid_map_id", codes)

    def test_same_code_messages_sorted_alphabetically(self) -> None:
        # Build a PoolResult with three errors that share a code but
        # differ in message; the rendered JSON must order them by message.
        result = MAP_FOUNDRY.PoolResult(
            pool_id="SolreignMapPool",
            maps=[],
            errors=[
                {"code": "alpha", "message": "z last"},
                {"code": "alpha", "message": "a first"},
                {"code": "alpha", "message": "m middle"},
            ],
        )
        payload = MAP_FOUNDRY.render_inventory(result)
        messages = [e["message"] for e in payload["errors"]]
        self.assertEqual(messages, ["a first", "m middle", "z last"])

    def test_validate_manifest_also_sorts_by_code_then_message(self) -> None:
        # The validate-manifest output must also use the same (code, message)
        # sort. Build a fixture with two errors of the same code but
        # different messages, and check the order.
        write_map_fixture(self.root, "SolreignOasis")
        write_file(
            self.root,
            POOL_FILE,
            pool_yml("SolreignMapPool", ["SolreignOasis", "SolreignOasis"]),
        )
        manifest = self.root / "manifest.json"
        manifest.write_text(
            json.dumps(
                {
                    "schema_version": 1,
                    "patch_id": "stable-sort",
                    "maps": [
                        manifest_entry("Resources/Maps/_Solreign/solreign_oasis.yml"),
                        manifest_entry("Resources/Maps/_Solreign/solreign_oasis.yml"),
                    ],
                }
            )
            + "\n",
            encoding="utf-8",
        )
        result = MAP_FOUNDRY.validate_manifest(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
            manifest_path=manifest,
        )
        errors = result["errors"]
        # The first error should be the duplicate pool entry; within the
        # duplicate path errors, they must be sorted by message.
        for prev, curr in zip(errors, errors[1:]):
            self.assertLessEqual(
                (prev["code"], prev["message"]),
                (curr["code"], curr["message"]),
                msg=f"errors out of order: {errors!r}",
            )


# --------------------------------------------------------------------------- #
# P2 (final pass): _strict_int must reject every float, including whole-valued #
# floats (1.0), scientific notation parsed as float (1e2), and `+5`            #
# --------------------------------------------------------------------------- #


class StrictIntRejectionTests(unittest.TestCase):
    """P2 (final pass): _strict_int must reject every float, including whole-valued
    floats such as ``1.0`` and scientific notation parsed as float
    (``1e2`` → ``100.0``). The strict YAML integer grammar (``^-?\\d+$``)
    must not accept ``+5`` either; the parser converts unquoted ``+5`` to
    float ``5.0`` so the float branch is the canonical rejector.
    """

    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.root = Path(self.temp_dir.name)

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def _write_pool_with_one_map(
        self, prototype_body: str, map_id: str = "SolreignOasis"
    ) -> None:
        write_file(
            self.root,
            "Resources/Prototypes/Maps/_Solreign/oasis.yml",
            prototype_body,
        )
        write_file(self.root, "Resources/Maps/_Solreign/oasis.yml", map_file_text())
        write_file(
            self.root,
            POOL_FILE,
            pool_yml("SolreignMapPool", [map_id]),
        )

    def test_whole_valued_float_min_players_rejected(self) -> None:
        # 1.0 is a float in the parsed scalar stream; _strict_int must
        # reject it (the prior pass accepted whole-valued floats).
        body = (
            "- type: gameMap\n"
            "  id: SolreignOasis\n"
            "  mapPath: /Maps/_Solreign/oasis.yml\n"
            "  minPlayers: 1.0\n"
            "  maxPlayers: 35\n"
        )
        self._write_pool_with_one_map(body)
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        self.assertEqual(result.maps, [])
        self.assertEqual(
            [e["code"] for e in result.errors],
            ["game_map_invalid_population_band"],
        )
        joined = " ".join(e["message"] for e in result.errors)
        self.assertIn("minPlayers", joined)
        # The exact float value must be echoed in the error for debuggability.
        self.assertIn("1.0", joined)

    def test_whole_valued_float_max_players_rejected(self) -> None:
        body = (
            "- type: gameMap\n"
            "  id: SolreignOasis\n"
            "  mapPath: /Maps/_Solreign/oasis.yml\n"
            "  minPlayers: 0\n"
            "  maxPlayers: 35.0\n"
        )
        self._write_pool_with_one_map(body)
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        self.assertEqual(result.maps, [])
        self.assertEqual(
            [e["code"] for e in result.errors],
            ["game_map_invalid_population_band"],
        )
        joined = " ".join(e["message"] for e in result.errors)
        self.assertIn("maxPlayers", joined)
        self.assertIn("35.0", joined)

    def test_scientific_notation_min_players_rejected(self) -> None:
        # `1e2` is parsed by the scalar converter to the float 100.0;
        # _strict_int must reject it just like 1.0.
        body = (
            "- type: gameMap\n"
            "  id: SolreignOasis\n"
            "  mapPath: /Maps/_Solreign/oasis.yml\n"
            "  minPlayers: 1e2\n"
            "  maxPlayers: 35\n"
        )
        self._write_pool_with_one_map(body)
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        self.assertEqual(result.maps, [])
        self.assertEqual(
            [e["code"] for e in result.errors],
            ["game_map_invalid_population_band"],
        )
        joined = " ".join(e["message"] for e in result.errors)
        self.assertIn("minPlayers", joined)

    def test_scientific_notation_max_players_rejected(self) -> None:
        # `3.5e1` is the float 35.0; whole-valued but still a float.
        body = (
            "- type: gameMap\n"
            "  id: SolreignOasis\n"
            "  mapPath: /Maps/_Solreign/oasis.yml\n"
            "  minPlayers: 0\n"
            "  maxPlayers: 3.5e1\n"
        )
        self._write_pool_with_one_map(body)
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        self.assertEqual(result.maps, [])
        self.assertEqual(
            [e["code"] for e in result.errors],
            ["game_map_invalid_population_band"],
        )
        joined = " ".join(e["message"] for e in result.errors)
        self.assertIn("maxPlayers", joined)

    def test_plus_sign_min_players_rejected(self) -> None:
        # Unquoted `+5` is parsed to the float 5.0 by the scalar converter;
        # _strict_int must reject it. (The strict YAML integer grammar
        # `^-?\d+$` does not accept `+5` either, so the integer branch is
        # also guarded; the float branch is the canonical rejector.)
        body = (
            "- type: gameMap\n"
            "  id: SolreignOasis\n"
            "  mapPath: /Maps/_Solreign/oasis.yml\n"
            "  minPlayers: +5\n"
            "  maxPlayers: 35\n"
        )
        self._write_pool_with_one_map(body)
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        self.assertEqual(result.maps, [])
        self.assertEqual(
            [e["code"] for e in result.errors],
            ["game_map_invalid_population_band"],
        )
        joined = " ".join(e["message"] for e in result.errors)
        self.assertIn("minPlayers", joined)

    def test_plus_sign_max_players_rejected(self) -> None:
        body = (
            "- type: gameMap\n"
            "  id: SolreignOasis\n"
            "  mapPath: /Maps/_Solreign/oasis.yml\n"
            "  minPlayers: 0\n"
            "  maxPlayers: +35\n"
        )
        self._write_pool_with_one_map(body)
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        self.assertEqual(result.maps, [])
        self.assertEqual(
            [e["code"] for e in result.errors],
            ["game_map_invalid_population_band"],
        )
        joined = " ".join(e["message"] for e in result.errors)
        self.assertIn("maxPlayers", joined)

    def test_unit_strict_int_rejects_whole_valued_float(self) -> None:
        # Direct unit test of _strict_int with the exact bug-class inputs.
        for value in (1.0, 100.0, 0.0, 35.0, 1e2, 2.0e1, 3.5e1):
            with self.assertRaises(
                MAP_FOUNDRY._InvalidInt, msg=f"value={value!r}"
            ):
                MAP_FOUNDRY._strict_int(value, "minPlayers", "TestMap")

    def test_unit_strict_int_rejects_plus_sign_string(self) -> None:
        # The strict YAML integer grammar `^-?\d+$` does not accept `+5`; a
        # bare string `"+5"` must be rejected even though Python's int()
        # accepts it. (The string reaches _strict_int when the YAML value
        # is single-quoted, which the parser passes through as a string.)
        for value in ("+5", "+35", " +5 ", "  +5  "):
            with self.assertRaises(
                MAP_FOUNDRY._InvalidInt, msg=f"value={value!r}"
            ):
                MAP_FOUNDRY._strict_int(value, "minPlayers", "TestMap")

    def test_unit_strict_int_accepts_canonical_ints(self) -> None:
        # Sanity: the strict path must still accept the canonical shape
        # (raw int, integer-string).
        for value in (0, 1, 35, 70, 90, "0", "35", "70", "  0  "):
            self.assertEqual(
                MAP_FOUNDRY._strict_int(value, "minPlayers", "TestMap"),
                int(str(value).strip()),
                msg=f"value={value!r}",
            )

    def test_canonical_integer_fields_still_resolve_cleanly(self) -> None:
        # Sanity at the resolver level: the canonical real-repo shape
        # (minPlayers=0, maxPlayers=35) must still resolve with zero errors.
        body = (
            "- type: gameMap\n"
            "  id: SolreignOasis\n"
            "  mapPath: /Maps/_Solreign/oasis.yml\n"
            "  minPlayers: 0\n"
            "  maxPlayers: 35\n"
        )
        self._write_pool_with_one_map(body)
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        self.assertEqual(result.errors, [], msg=str(result.errors))
        self.assertEqual(len(result.maps), 1)
        self.assertEqual(result.maps[0]["population"], {"min": 0, "max": 35})


# --------------------------------------------------------------------------- #
# P2 (final pass): read_text on prototype/pool files must fail closed         #
# without leaking paths or tracebacks to stdout/stderr                         #
# --------------------------------------------------------------------------- #


# A byte sequence that is never valid UTF-8. Lone continuation bytes (0x80,
# 0x81, 0x82) and bytes that are never valid in UTF-8 (0xFF, 0xFE) force
# a `UnicodeDecodeError` on `read_text(encoding="utf-8")`.
_INVALID_UTF8_BYTES = b"\x80\x81\x82\xff\xfeinvalid"


def _write_invalid_utf8(path: Path, payload: bytes = _INVALID_UTF8_BYTES) -> None:
    """Write a file containing bytes that are not valid UTF-8."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(payload)


class ReadErrorHandlingTests(unittest.TestCase):
    """P2 (final pass): read_text errors on prototype/pool files must fail
    closed without leaking paths or tracebacks to stdout/stderr.

    Permissions may be unreliable under an elevated user, so the tests
    use invalid UTF-8 bytes (written via write_bytes) to deterministically
    force a UnicodeDecodeError. The same code path handles OSError.
    """

    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.root = Path(self.temp_dir.name)

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def _run_cli(
        self, *args: str, expected_returncode: int | None = None
    ) -> subprocess.CompletedProcess[str]:
        result = subprocess.run(
            [sys.executable, str(SCRIPT), "--root", str(self.root), *args],
            text=True,
            capture_output=True,
            check=False,
        )
        if expected_returncode is not None:
            self.assertEqual(
                result.returncode,
                expected_returncode,
                f"stdout:\n{result.stdout}\nstderr:\n{result.stderr}",
            )
        return result

    def test_collect_game_maps_raises_path_free_foundry_error_on_invalid_utf8(
        self,
    ) -> None:
        # An invalid-UTF-8 prototype file must surface a FoundryError
        # through resolve_pool without leaking the absolute path.
        write_file(
            self.root,
            "Resources/Prototypes/Maps/_Solreign/oasis.yml",
            game_map_yml("SolreignOasis"),
        )
        write_file(self.root, "Resources/Maps/_Solreign/oasis.yml", map_file_text())
        write_file(
            self.root, POOL_FILE, pool_yml("SolreignMapPool", ["SolreignOasis"])
        )
        _write_invalid_utf8(
            self.root / "Resources/Prototypes/Maps/_Solreign/oasis.yml"
        )
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        self.assertEqual(result.maps, [])
        codes = [e["code"] for e in result.errors]
        self.assertIn("foundry_error", codes)
        joined = " ".join(e["message"] for e in result.errors)
        # The error must not leak the absolute path.
        self.assertNotIn(self.root.as_posix(), joined)
        # And it must not contain a Python traceback frame.
        self.assertNotIn("Traceback", joined)

    def test_collect_pool_from_file_raises_path_free_foundry_error_on_invalid_utf8(
        self,
    ) -> None:
        # An invalid-UTF-8 pool file must surface a bounded error
        # response without leaking the absolute path.
        write_file(
            self.root,
            "Resources/Prototypes/Maps/_Solreign/oasis.yml",
            game_map_yml("SolreignOasis", "/Maps/_Solreign/oasis.yml"),
        )
        write_file(self.root, "Resources/Maps/_Solreign/oasis.yml", map_file_text())
        _write_invalid_utf8(self.root / POOL_FILE)
        result = MAP_FOUNDRY.resolve_pool(
            self.root,
            pool_id="SolreignMapPool",
            pool_file=self.root / POOL_FILE,
        )
        # The bounded error response is pool_file_not_found; the message
        # must not leak the absolute path.
        codes = [e["code"] for e in result.errors]
        self.assertIn("pool_file_not_found", codes)
        joined = " ".join(e["message"] for e in result.errors)
        self.assertNotIn(self.root.as_posix(), joined)
        self.assertNotIn("Traceback", joined)

    def test_unit_read_prototype_text_oserror(self) -> None:
        # A non-existent path must produce FoundryError("cannot read
        # prototype file"), not an uncaught OSError.
        bogus = self.root / "Resources" / "Prototypes" / "nope.yml"
        with self.assertRaises(MAP_FOUNDRY.FoundryError) as ctx:
            MAP_FOUNDRY._read_prototype_text(bogus)
        self.assertEqual(str(ctx.exception), "cannot read prototype file")
        # The error must not echo the absolute path of the failed read.
        self.assertNotIn(self.root.as_posix(), str(ctx.exception))

    def test_unit_read_prototype_text_unicodedecodeerror(self) -> None:
        invalid = self.root / "invalid.yml"
        _write_invalid_utf8(invalid)
        with self.assertRaises(MAP_FOUNDRY.FoundryError) as ctx:
            MAP_FOUNDRY._read_prototype_text(invalid)
        self.assertEqual(str(ctx.exception), "prototype file is not valid UTF-8")
        # The error must not echo the absolute path.
        self.assertNotIn(self.root.as_posix(), str(ctx.exception))

    def test_cli_inventory_on_invalid_utf8_prototype_exits_nonzero_with_stable_code(
        self,
    ) -> None:
        # CLI never traceback-leaks; the JSON payload carries a stable
        # code and neither stdout nor stderr leaks the absolute path.
        write_file(
            self.root,
            "Resources/Prototypes/Maps/_Solreign/oasis.yml",
            game_map_yml("SolreignOasis"),
        )
        write_file(self.root, "Resources/Maps/_Solreign/oasis.yml", map_file_text())
        write_file(
            self.root, POOL_FILE, pool_yml("SolreignMapPool", ["SolreignOasis"])
        )
        _write_invalid_utf8(
            self.root / "Resources/Prototypes/Maps/_Solreign/oasis.yml"
        )
        cli = self._run_cli("inventory", expected_returncode=2)
        combined = cli.stdout + "\n" + cli.stderr
        # No Python traceback in either stream.
        self.assertNotIn("Traceback", combined)
        # The temp dir absolute path must not appear in stdout or stderr.
        self.assertNotIn(self.root.as_posix(), combined)
        # And no known absolute-path prefix leaks either.
        for hint in ("/Users/", "/home/", "/tmp/", "/var/", "/private/", "/etc/"):
            self.assertNotIn(hint, combined)
        # The JSON payload must carry a stable error code.
        payload = json.loads(cli.stdout)
        self.assertEqual(payload["schema_version"], 1)
        codes = [e["code"] for e in payload["errors"]]
        self.assertIn("foundry_error", codes)

    def test_cli_inventory_on_invalid_utf8_pool_exits_nonzero_with_stable_code(
        self,
    ) -> None:
        # CLI never traceback-leaks; the JSON payload carries the
        # established bounded error code (pool_file_not_found) and
        # neither stdout nor stderr leaks the absolute path.
        write_file(
            self.root,
            "Resources/Prototypes/Maps/_Solreign/oasis.yml",
            game_map_yml("SolreignOasis", "/Maps/_Solreign/oasis.yml"),
        )
        write_file(self.root, "Resources/Maps/_Solreign/oasis.yml", map_file_text())
        _write_invalid_utf8(self.root / POOL_FILE)
        cli = self._run_cli("inventory", expected_returncode=2)
        combined = cli.stdout + "\n" + cli.stderr
        self.assertNotIn("Traceback", combined)
        self.assertNotIn(self.root.as_posix(), combined)
        for hint in ("/Users/", "/home/", "/tmp/", "/var/", "/private/", "/etc/"):
            self.assertNotIn(hint, combined)
        payload = json.loads(cli.stdout)
        self.assertEqual(payload["schema_version"], 1)
        codes = [e["code"] for e in payload["errors"]]
        self.assertIn("pool_file_not_found", codes)

    def test_cli_validate_manifest_on_invalid_utf8_pool_exits_nonzero(self) -> None:
        # The validate-manifest command must also fail closed with no
        # traceback when the pool file is unreadable (since the pool
        # resolution step is shared). The bounded error response is the
        # stable `pool_file_not_found` code; coverage comes back
        # incomplete, so the CLI exits with code 1 (incomplete coverage),
        # not 2 (catastrophic). The contract being verified is that
        # there is no traceback and no absolute path in stdout/stderr.
        write_file(
            self.root,
            "Resources/Prototypes/Maps/_Solreign/oasis.yml",
            game_map_yml("SolreignOasis", "/Maps/_Solreign/oasis.yml"),
        )
        write_file(self.root, "Resources/Maps/_Solreign/oasis.yml", map_file_text())
        _write_invalid_utf8(self.root / POOL_FILE)
        # A valid manifest is required so the failure is unambiguously
        # the pool file, not the manifest.
        manifest = self.root / "manifest.json"
        manifest.write_text(
            json.dumps(
                {
                    "schema_version": 1,
                    "patch_id": "x",
                    "maps": [
                        {
                            "path": "Resources/Maps/_Solreign/oasis.yml",
                            "anchor": {"proto": "X", "uid": 1, "pos": [0, 0], "parent": 1},
                            "max_manhattan_tiles": 1,
                            "placements": [],
                        }
                    ],
                }
            )
            + "\n",
            encoding="utf-8",
        )
        # Exit code 1 = incomplete coverage (not 2 = catastrophic); the
        # bounded error response is the stable `pool_file_not_found` code.
        cli = self._run_cli(
            "validate-manifest", "--manifest", str(manifest), expected_returncode=1
        )
        self.assertNotEqual(cli.returncode, 0)
        combined = cli.stdout + "\n" + cli.stderr
        self.assertNotIn("Traceback", combined)
        self.assertNotIn(self.root.as_posix(), combined)
        for hint in ("/Users/", "/home/", "/tmp/", "/var/", "/private/", "/etc/"):
            self.assertNotIn(hint, combined)
        payload = json.loads(cli.stdout)
        codes = [e["code"] for e in payload["errors"]]
        self.assertIn("pool_file_not_found", codes)


if __name__ == "__main__":
    unittest.main()
