"""RED-first tests for the `move` subcommand — block-scoped mover for UNOWNED entities.

Promotes the beacon_move_v2 pattern from the 2026-08-02 A9 session into a
manifest-driven, fail-closed operation. The guards this suite pins:

  1. Apply rewrites ONLY the targeted record's pos line (byte-stable).
  2. A space target tile is refused, regardless of what the manifest claims.
  3. An entity at neither endpoint is DRIFT — a loud error naming it.
  4. --check distinguishes pending (exit 1) from applied (exit 0).
  5. Target-cell occupant set is pinned exactly; mismatch refuses.
  6. A forward move then its reverse restores the original bytes.
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

SPEC = importlib.util.spec_from_file_location("solreign_map_patch_move", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
MAP_PATCH = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MAP_PATCH)


def encode_mixed_chunk(tile_for_cell) -> str:
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


def move_map_text(beacon_pos: tuple[float, float] = (2.5, 2.5)) -> str:
    """Row y=0 is Space; the rest FloorSteel. Cable at (5,3) as the pinned
    target occupant; a Table at (7,3) as an unexpected-occupant hazard."""

    def tile_for_cell(x: int, y: int) -> int:
        return 0 if y == 0 else 93

    chunk = encode_mixed_chunk(tile_for_cell)
    return (
        "meta:\n"
        "  format: 7\n"
        "  category: Map\n"
        "  entityCount: 5 # synthetic fixture\n"
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
        + entity("SolreignWingmateBeacon", 30, beacon_pos)
        + entity("CableApcExtension", 31, (5.5, 3.5))
        + entity("Table", 32, (7.5, 3.5))
    )


def move_entry(
    *,
    from_pos: list[float] | None = None,
    to_pos: list[float] | None = None,
    expected_tile: str = "FloorSteel",
    expected_occupants: list | None = None,
) -> dict:
    return {
        "proto": "SolreignWingmateBeacon",
        "uid": 30,
        "parent": 2,
        "from": from_pos if from_pos is not None else [2.5, 2.5],
        "to": to_pos if to_pos is not None else [5.5, 3.5],
        "expected_tile": expected_tile,
        "expected_occupants": expected_occupants
        if expected_occupants is not None
        else [{"proto": "CableApcExtension", "uid": 31}],
    }


def plan(text: str, moves: list[dict]):
    return MAP_PATCH.plan_moves(text, moves)


class MoveCoreTests(unittest.TestCase):
    def test_apply_rewrites_only_the_pos_line(self) -> None:
        text = move_map_text()
        report, new_text = plan(text, [move_entry()])
        self.assertEqual([item["state"] for item in report], ["pending"])
        old_lines = text.splitlines()
        new_lines = new_text.splitlines()
        self.assertEqual(len(old_lines), len(new_lines))
        changed = [
            (old, new) for old, new in zip(old_lines, new_lines) if old != new
        ]
        self.assertEqual(changed, [("      pos: 2.5,2.5", "      pos: 5.5,3.5")])

    def test_apply_preserves_nested_entity_pos_indentation(self) -> None:
        """Catches a move rewrite that flattens a valid nested entity record."""
        text = move_map_text().replace(
            "  - uid: 30\n"
            "    components:\n"
            "    - type: Transform\n"
            "      pos: 2.5,2.5\n"
            "      parent: 2\n",
            "    - uid: 30\n"
            "      components:\n"
            "      - type: Transform\n"
            "        pos: 2.5,2.5\n"
            "        parent: 2\n",
            1,
        )

        report, new_text = plan(text, [move_entry()])

        self.assertEqual([item["state"] for item in report], ["pending"])
        changed = [
            (old, new)
            for old, new in zip(text.splitlines(), new_text.splitlines())
            if old != new
        ]
        self.assertEqual(changed, [("        pos: 2.5,2.5", "        pos: 5.5,3.5")])

    def test_space_target_is_refused_even_if_the_manifest_claims_it(self) -> None:
        text = move_map_text()
        with self.assertRaisesRegex(MAP_PATCH.PatchError, "[Ss]pace"):
            plan(text, [move_entry(to_pos=[5.5, 0.5], expected_tile="Space")])
        # A manifest lying about the tile name still refuses via the DECODED
        # Space verdict, not the generic tile-name mismatch — the refusal must
        # not depend on the manifest telling the truth.
        with self.assertRaisesRegex(MAP_PATCH.PatchError, "decodes to Space; refusing"):
            plan(text, [move_entry(to_pos=[5.5, 0.5], expected_tile="FloorSteel")])

    def test_entity_at_neither_endpoint_is_drift(self) -> None:
        text = move_map_text(beacon_pos=(9.5, 9.5))
        with self.assertRaisesRegex(MAP_PATCH.PatchError, "drift"):
            plan(text, [move_entry()])

    def test_entity_already_at_target_reports_applied_and_writes_nothing(self) -> None:
        text = move_map_text(beacon_pos=(5.5, 3.5))
        report, new_text = plan(text, [move_entry()])
        self.assertEqual([item["state"] for item in report], ["applied"])
        self.assertEqual(new_text, text)

    def test_unexpected_target_occupant_refuses(self) -> None:
        text = move_map_text()
        # (7,3) holds a Table the manifest does not pin.
        with self.assertRaisesRegex(MAP_PATCH.PatchError, "occupant"):
            plan(text, [move_entry(to_pos=[7.5, 3.5])])

    def test_round_trip_restores_original_bytes(self) -> None:
        text = move_map_text()
        _, forward = plan(text, [move_entry()])
        reverse = move_entry(
            from_pos=[5.5, 3.5],
            to_pos=[2.5, 2.5],
            expected_occupants=[],
        )
        _, restored = plan(forward, [reverse])
        self.assertEqual(restored, text)


class MoveCliTests(unittest.TestCase):
    def make_root(self, tmp: str) -> Path:
        root = Path(tmp)
        map_dir = root / MAP_ROOT
        map_dir.mkdir(parents=True)
        (map_dir / "fixture.yml").write_text(move_map_text(), encoding="utf-8")
        manifest = {
            "maps": [
                {
                    "path": str(MAP_ROOT / "fixture.yml"),
                    "moves": [move_entry()],
                }
            ]
        }
        (root / "moves.json").write_text(json.dumps(manifest), encoding="utf-8")
        return root

    def run_cli(self, root: Path, *argv: str) -> subprocess.CompletedProcess:
        return subprocess.run(
            [
                sys.executable,
                str(SCRIPT),
                "move",
                "--root",
                str(root),
                "--manifest",
                str(root / "moves.json"),
                *argv,
            ],
            capture_output=True,
            text=True,
            cwd=root,
        )

    def test_dry_run_reports_pending_and_writes_nothing(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = self.make_root(tmp)
            before = (root / MAP_ROOT / "fixture.yml").read_text(encoding="utf-8")
            result = self.run_cli(root)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertIn("pending", result.stdout)
            after = (root / MAP_ROOT / "fixture.yml").read_text(encoding="utf-8")
            self.assertEqual(before, after)

    def test_check_fails_while_pending_then_passes_after_apply(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = self.make_root(tmp)
            check_before = self.run_cli(root, "--check")
            self.assertEqual(check_before.returncode, 1, check_before.stderr)
            apply_run = self.run_cli(root, "--apply")
            self.assertEqual(apply_run.returncode, 0, apply_run.stderr)
            check_after = self.run_cli(root, "--check")
            self.assertEqual(check_after.returncode, 0, check_after.stderr)
            moved = (root / MAP_ROOT / "fixture.yml").read_text(encoding="utf-8")
            self.assertIn("      pos: 5.5,3.5", moved)
            self.assertNotIn("      pos: 2.5,2.5", moved)


if __name__ == "__main__":
    unittest.main()
