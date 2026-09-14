"""Tests for MapAttach — component attachment onto existing map entities.

The fixture deliberately reproduces the two shapes that broke naive implementations
during the 2026-07-29 afterlife work:
  * an entity whose Transform is followed by ANOTHER component (BlockGameArcade +
    SpamEmitSound) — the attachment must land between them, exactly as Oasis has it;
  * an entity carrying multi-line trailing mapper comments on its `pos:` line
    (solreign_nocturne.yml uids 3000/3003) — those comments must survive byte-identical.
"""
from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from map_attach import AttachError, apply_attachments, main, remove_attachments  # noqa: E402


FIXTURE = """meta:
  format: 7
entities:
- proto: BlockGameArcade
  entities:
  - uid: 19106
    components:
    - type: Transform
      rot: 3.141592653589793 rad
      pos: -46.5,-41.5
      parent: 2
    - type: SpamEmitSound
      enabled: False
- proto: Jukebox
  entities:
  - uid: 3000
    components:
    - type: Transform
      pos: -10.5,4.5 # W17 verifier: relocated from -6.5,4.5, where it sat blocking the
                     # AirlockServiceGlassLocked between Bar and Kitchen. Moved into the
                     # open bar-floor service row so it reads as the noir bar centerpiece
      parent: 2
- proto: KitchenMicrowave
  entities:
  - uid: 503
    components:
    - type: Transform
      pos: -8.5,22.5
      parent: 2
"""

ARCADE = {
    "key": "arcade",
    "proto": "BlockGameArcade",
    "uid": 19106,
    "component": {
        "type": "SolreignGhostActivity",
        "fields": {
            "name": "Arcade Corner (Recreation)",
            "description": "Spectate the block-game high-score chase.",
        },
    },
}
JUKEBOX = {
    "key": "jukebox",
    "proto": "Jukebox",
    "uid": 3000,
    "component": {
        "type": "SolreignGhostActivity",
        "fields": {"name": "Jukebox Vigil (Bar)", "description": "Haunt the playlist."},
    },
}


class MapAttachTests(unittest.TestCase):
    def test_inserts_after_transform_and_before_next_component(self) -> None:
        out, pending, present = apply_attachments(FIXTURE, [ARCADE])
        self.assertEqual((pending, present), (1, 0))
        lines = out.splitlines()
        i = lines.index("    - type: SolreignGhostActivity")
        self.assertEqual(lines[i - 1], "      parent: 2")
        self.assertEqual(lines[i + 1], "      name: Arcade Corner (Recreation)")
        self.assertEqual(lines[i + 3], "    - type: SpamEmitSound")

    def test_preserves_multiline_mapper_comments(self) -> None:
        out, _, _ = apply_attachments(FIXTURE, [JUKEBOX])
        for comment in (
            "# W17 verifier: relocated from -6.5,4.5, where it sat blocking the",
            "# AirlockServiceGlassLocked between Bar and Kitchen. Moved into the",
            "# open bar-floor service row so it reads as the noir bar centerpiece",
        ):
            self.assertIn(comment, out, "mapper comment was destroyed")

    def test_idempotent_reapply_is_a_noop(self) -> None:
        once, _, _ = apply_attachments(FIXTURE, [ARCADE])
        twice, pending, present = apply_attachments(once, [ARCADE])
        self.assertEqual((pending, present), (0, 1))
        self.assertEqual(once, twice)

    def test_refuses_wrong_enclosing_proto(self) -> None:
        bad = dict(ARCADE, proto="KitchenMicrowave")
        with self.assertRaises(AttachError) as ctx:
            apply_attachments(FIXTURE, [bad])
        self.assertIn("manifest expects", str(ctx.exception))

    def test_refuses_missing_uid(self) -> None:
        bad = dict(ARCADE, uid=999999)
        with self.assertRaises(AttachError) as ctx:
            apply_attachments(FIXTURE, [bad])
        self.assertIn("does not exist", str(ctx.exception))

    def test_refuses_drift_when_values_differ(self) -> None:
        applied, _, _ = apply_attachments(FIXTURE, [ARCADE])
        drifted = json.loads(json.dumps(ARCADE))
        drifted["component"]["fields"]["name"] = "Something Else"
        with self.assertRaises(AttachError) as ctx:
            apply_attachments(applied, [drifted])
        self.assertIn("drift", str(ctx.exception))

    def test_refuses_entity_without_transform(self) -> None:
        text = "entities:\n- proto: Ghostly\n  entities:\n  - uid: 7\n    components:\n    - type: Other\n      x: 1\n"
        att = dict(ARCADE, proto="Ghostly", uid=7)
        with self.assertRaises(AttachError) as ctx:
            apply_attachments(text, [att])
        self.assertIn("no Transform", str(ctx.exception))

    def test_refuses_duplicate_uid(self) -> None:
        dup = FIXTURE + "- proto: BlockGameArcade\n  entities:\n  - uid: 19106\n    components:\n    - type: Transform\n      pos: 0,0\n      parent: 2\n"
        with self.assertRaises(AttachError) as ctx:
            apply_attachments(dup, [ARCADE])
        self.assertIn("appears 2 times", str(ctx.exception))

    def test_remove_round_trips_byte_identical(self) -> None:
        applied, _, _ = apply_attachments(FIXTURE, [ARCADE, JUKEBOX])
        self.assertNotEqual(applied, FIXTURE)
        restored, removed = remove_attachments(applied, [ARCADE, JUKEBOX])
        self.assertEqual(removed, 2)
        self.assertEqual(restored, FIXTURE, "remove did not restore the original bytes")

    def test_multiple_attachments_in_one_map(self) -> None:
        out, pending, _ = apply_attachments(FIXTURE, [ARCADE, JUKEBOX])
        self.assertEqual(pending, 2)
        self.assertEqual(out.count("    - type: SolreignGhostActivity"), 2)


class MapAttachCliTests(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.map_rel = "Resources/Maps/_Solreign/fixture.yml"
        self.map_path = self.root / self.map_rel
        self.map_path.parent.mkdir(parents=True)
        self.map_path.write_text(FIXTURE, encoding="utf-8")
        self.manifest = self.root / "m.json"
        self.manifest.write_text(
            json.dumps(
                {
                    "schema_version": 1,
                    "patch_id": "afterlife-test",
                    "maps": [{"path": self.map_rel, "attachments": [ARCADE, JUKEBOX]}],
                }
            ),
            encoding="utf-8",
        )

    def tearDown(self) -> None:
        self.tmp.cleanup()

    def cli(self, *flags: str) -> int:
        return main(["--root", str(self.root), "--manifest", str(self.manifest), *flags])

    def test_check_fails_before_apply_and_passes_after(self) -> None:
        self.assertEqual(self.cli("--check"), 1)
        self.assertEqual(self.cli("--apply"), 0)
        self.assertEqual(self.cli("--check"), 0)

    def test_dry_run_writes_nothing(self) -> None:
        self.assertEqual(self.cli(), 0)
        self.assertEqual(self.map_path.read_text(encoding="utf-8"), FIXTURE)

    def test_apply_then_remove_restores_file(self) -> None:
        self.assertEqual(self.cli("--apply"), 0)
        self.assertNotEqual(self.map_path.read_text(encoding="utf-8"), FIXTURE)
        self.assertEqual(self.cli("--remove", "--apply"), 0)
        self.assertEqual(self.map_path.read_text(encoding="utf-8"), FIXTURE)

    def test_bad_manifest_exits_two_not_crash(self) -> None:
        self.manifest.write_text(json.dumps({"schema_version": 2}), encoding="utf-8")
        self.assertEqual(self.cli("--check"), 2)


if __name__ == "__main__":
    unittest.main()
