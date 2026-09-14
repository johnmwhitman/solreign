"""RED-first tests for the `suggest` subcommand — the decoded-tile candidate finder.

Promotes the throwaway inline scripts from the 2026-08-02 zoo-fauna / A9 sessions
(JOURNAL.md: two beacons placed in space, four new floating wallmounts) into a
first-class, tested subcommand. The four scars this suite pins:

  1. A cell whose decoded tile is Space is NEVER a candidate.
  2. Each candidate reports its occupant set EXACTLY (proto, uid pairs).
  3. Wall adjacency (the wallmount float-ratchet precondition) is flagged.
  4. A cell holding a SpawnPointLatejoin is excluded.
"""
from __future__ import annotations

import base64
import importlib.util
import json
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / "map_patch.py"
MAP_ROOT = Path("Resources/Maps/_Solreign")

SPEC = importlib.util.spec_from_file_location("solreign_map_patch_suggest", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
MAP_PATCH = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MAP_PATCH)


def encode_mixed_chunk(tile_for_cell) -> str:
    """Encode one 16x16 chunk where tile_for_cell(local_x, local_y) -> tile id."""
    raw = b"".join(
        struct.pack("<IBBB", tile_for_cell(x, y), 0, 0, 0)
        for y in range(16)
        for x in range(16)
    )
    return base64.b64encode(raw).decode("ascii")


def entity(proto: str, uid: int, pos: tuple[float, float], parent: int = 2) -> str:
    return (
        f"- proto: {proto}\n"
        "  entities:\n"
        f"  - uid: {uid}\n"
        "    components:\n"
        "    - type: Transform\n"
        f"      pos: {pos[0]},{pos[1]}\n"
        f"      parent: {parent}\n"
    )


def suggest_map_text() -> str:
    """Synthetic format-7 map.

    Chunk (0,0): row y=0 is Space (tile id 0); everything else FloorSteel (93).
    Entities on grid uid 2:
      WallSolid            uid 20 at (3.5, 2.5)   -> makes (2,2)/(4,2)/(3,1)/(3,3) wall-adjacent
      SpawnPointLatejoin   uid 21 at (6.5, 2.5)   -> excludes cell (6,2)
      Table                uid 22 at (2.5, 2.5)   -> physical occupant on cell (2,2)
      CableApcExtension    uid 23 at (2.5, 2.5)   -> underfloor occupant, same cell
    """

    def tile_for_cell(x: int, y: int) -> int:
        return 0 if y == 0 else 93

    chunk = encode_mixed_chunk(tile_for_cell)
    return (
        "meta:\n"
        "  format: 7\n"
        "  category: Map\n"
        "  entityCount: 6 # synthetic fixture\n"
        "maps:\n"
        "- 1\n"
        "grids:\n"
        "- 2\n"
        "orphans: []\n"
        "nullspace: []\n"
        "tilemap:\n"
        "  0: Space\n"
        "  93: FloorSteel\n"
        "entities:\n"
        "- proto: TestMap\n"
        "  entities:\n"
        "  - uid: 1\n"
        "    components:\n"
        "    - type: Transform\n"
        "      pos: 0,0\n"
        "- proto: TestGrid\n"
        "  entities:\n"
        "  - uid: 2\n"
        "    components:\n"
        "    - type: MapGrid\n"
        "      chunks:\n"
        "        - ind: 0,0\n"
        f"          tiles: {chunk}\n"
        + entity("WallSolid", 20, (3.5, 2.5))
        + entity("SpawnPointLatejoin", 21, (6.5, 2.5))
        + entity("Table", 22, (2.5, 2.5))
        + entity("CableApcExtension", 23, (2.5, 2.5))
    )


def run_suggest(text: str, center: tuple[float, float], radius: int):
    return MAP_PATCH.suggest_candidates(text, parent=2, center=center, radius=radius)


def by_cell(candidates):
    return {tuple(candidate["cell"]): candidate for candidate in candidates}


class SuggestCandidateTests(unittest.TestCase):
    def test_space_tile_is_refused_as_candidate(self) -> None:
        """The trap that bit three times on 2026-08-02: row y=0 decodes to Space."""
        candidates = run_suggest(suggest_map_text(), center=(3.5, 1.5), radius=2)
        cells = set(by_cell(candidates))
        self.assertTrue(cells, "expected at least one candidate on the floor rows")
        space_cells = {cell for cell in cells if cell[1] == 0}
        self.assertEqual(space_cells, set(), f"space tiles offered as candidates: {space_cells}")
        for candidate in candidates:
            self.assertNotEqual(candidate["tile"], "Space")

    def test_occupant_set_is_exact(self) -> None:
        candidates = run_suggest(suggest_map_text(), center=(2.5, 2.5), radius=0)
        self.assertEqual(len(candidates), 1)
        occupants = {tuple(pair) for pair in candidates[0]["occupants"]}
        self.assertEqual(occupants, {("Table", 22), ("CableApcExtension", 23)})

    def test_wall_adjacency_is_flagged(self) -> None:
        candidates = run_suggest(suggest_map_text(), center=(3.5, 2.5), radius=2)
        cells = by_cell(candidates)
        # (2,2) is directly beside the WallSolid at cell (3,2).
        self.assertIn((2, 2), cells)
        self.assertTrue(cells[(2, 2)]["wall_adjacent"])
        # (2,4) has no wall in any of its four neighbours.
        self.assertIn((2, 4), cells)
        self.assertFalse(cells[(2, 4)]["wall_adjacent"])
        # The wall's own cell is never a candidate: a wallmount target is the
        # open floor tile BESIDE a wall, never the wall tile itself.
        self.assertNotIn((3, 2), cells)

    def test_cryo_latejoin_spawner_cell_is_excluded(self) -> None:
        """Production terminus uses CryogenicSleepUnitSpawnerLateJoin (capital J),
        not SpawnPointLatejoin — the name-vs-thing scar. Both spellings exclude."""
        text = suggest_map_text() + entity("CryogenicSleepUnitSpawnerLateJoin", 24, (5.5, 4.5))
        candidates = run_suggest(text, center=(5.5, 4.5), radius=1)
        cells = set(by_cell(candidates))
        self.assertNotIn((5, 4), cells)
        self.assertIn((4, 4), cells)

    def test_latejoin_cell_is_excluded(self) -> None:
        candidates = run_suggest(suggest_map_text(), center=(6.5, 2.5), radius=1)
        cells = set(by_cell(candidates))
        self.assertNotIn((6, 2), cells)
        self.assertIn((5, 2), cells)


class SuggestCliTests(unittest.TestCase):
    def run_cli(self, *argv: str, cwd: Path) -> subprocess.CompletedProcess:
        return subprocess.run(
            [sys.executable, str(SCRIPT), *argv],
            capture_output=True,
            text=True,
            cwd=cwd,
        )

    def test_suggest_subcommand_emits_json_and_exit_zero(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            map_dir = root / MAP_ROOT
            map_dir.mkdir(parents=True)
            (map_dir / "fixture.yml").write_text(suggest_map_text(), encoding="utf-8")
            result = self.run_cli(
                "suggest",
                "--root", str(root),
                "--map", str(MAP_ROOT / "fixture.yml"),
                "--parent", "2",
                "--near", "3.5,2.5",
                "--radius", "2",
                cwd=root,
            )
            self.assertEqual(result.returncode, 0, result.stderr)
            payload = json.loads(result.stdout)
            self.assertTrue(payload["candidates"])
            for candidate in payload["candidates"]:
                self.assertNotEqual(candidate["tile"], "Space")

    def test_suggest_with_no_eligible_cells_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            map_dir = root / MAP_ROOT
            map_dir.mkdir(parents=True)
            (map_dir / "fixture.yml").write_text(suggest_map_text(), encoding="utf-8")
            # Radius 0 on a space tile: nothing eligible -> exit 1, loud.
            result = self.run_cli(
                "suggest",
                "--root", str(root),
                "--map", str(MAP_ROOT / "fixture.yml"),
                "--parent", "2",
                "--near", "3.5,0.5",
                "--radius", "0",
                cwd=root,
            )
            self.assertEqual(result.returncode, 1)


if __name__ == "__main__":
    unittest.main()
