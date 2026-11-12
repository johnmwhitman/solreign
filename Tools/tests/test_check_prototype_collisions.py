"""Tests for Tools/_Solreign/check_prototype_collisions.py.

Verifies:
  - Clean tree of unique prototype IDs passes clean (exit 0).
  - Two files declaring the same prototype ID (same kind) are reported as a
    collision naming the ID (exit 1).
  - Clean run when same ID is used under different kinds (SS14 keys by (kind, id)).
  - Non-existent directory returns error code 2.
  - main() CLI invocation against synthetic directory paths.
"""
from __future__ import annotations

import io
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import patch

from Tools._Solreign import check_prototype_collisions as cpc


class CheckPrototypeCollisionsTests(unittest.TestCase):
    def test_unique_ids_pass_clean(self) -> None:
        """A tree of unique IDs passes clean with exit code 0."""
        with TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            (root / "proto_a.yml").write_text(
                "- type: entity\n"
                "  id: WeaponLaser\n"
                "- type: entity\n"
                "  id: WeaponPlasma\n",
                encoding="utf-8",
            )
            (root / "proto_b.yaml").write_text(
                "- type: entity\n"
                "  id: WeaponDisabler\n"
                "- type: tile\n"
                "  id: FloorSteel\n",
                encoding="utf-8",
            )

            stdout = io.StringIO()
            stderr = io.StringIO()
            with patch("sys.stdout", stdout), patch("sys.stderr", stderr):
                exit_code = cpc.check_tree(root)

            self.assertEqual(exit_code, 0)
            self.assertEqual(stderr.getvalue(), "")
            self.assertIn("OK: 4 prototypes in 2 files, 0 (kind, id) collisions", stdout.getvalue())

    def test_two_files_declaring_same_prototype_id_reported_as_collision(self) -> None:
        """Two files declaring the same prototype ID are reported as a collision naming the ID."""
        with TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            (root / "catalog_old.yml").write_text(
                "- type: uplinkCatalogEntry\n"
                "  id: StunBaton\n",
                encoding="utf-8",
            )
            (root / "catalog_split.yml").write_text(
                "- type: uplinkCatalogEntry\n"
                "  id: StunBaton\n",
                encoding="utf-8",
            )

            stdout = io.StringIO()
            stderr = io.StringIO()
            with patch("sys.stdout", stdout), patch("sys.stderr", stderr):
                exit_code = cpc.check_tree(root)

            self.assertEqual(exit_code, 1)
            err_output = stderr.getvalue()
            self.assertIn("FAIL: 1 (kind, id) collision(s)", err_output)
            self.assertIn("[uplinkCatalogEntry]", err_output)
            self.assertIn("id='StunBaton'", err_output)
            self.assertIn("catalog_old.yml:1", err_output)
            self.assertIn("catalog_split.yml:1", err_output)
            self.assertIn("summary: 2 prototypes, 2 files, 1 collisions", err_output)

    def test_same_id_different_kinds_passes_clean(self) -> None:
        """SS14 keys prototypes by (kind, id); same ID across different kinds is valid."""
        with TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            (root / "levels.yml").write_text(
                "- type: accessLevel\n"
                "  id: Engineering\n",
                encoding="utf-8",
            )
            (root / "groups.yml").write_text(
                "- type: accessGroup\n"
                "  id: Engineering\n",
                encoding="utf-8",
            )

            stdout = io.StringIO()
            stderr = io.StringIO()
            with patch("sys.stdout", stdout), patch("sys.stderr", stderr):
                exit_code = cpc.check_tree(root)

            self.assertEqual(exit_code, 0)
            self.assertEqual(stderr.getvalue(), "")
            self.assertIn("OK: 2 prototypes in 2 files, 0 (kind, id) collisions", stdout.getvalue())

    def test_nonexistent_directory_fails(self) -> None:
        """Checking a non-existent directory reports error and exits with code 2."""
        missing = Path("/nonexistent/path/to/prototypes")
        stderr = io.StringIO()
        with patch("sys.stderr", stderr):
            exit_code = cpc.check_tree(missing)

        self.assertEqual(exit_code, 2)
        self.assertIn("error: not a directory", stderr.getvalue())

    def test_main_cli_reports_clean_and_collision(self) -> None:
        """main() correctly handles positional argument and exit status."""
        with TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            (root / "a.yml").write_text(
                "- type: entity\n"
                "  id: Proto1\n",
                encoding="utf-8",
            )
            stdout = io.StringIO()
            with patch("sys.stdout", stdout):
                exit_code = cpc.main([str(root)])
            self.assertEqual(exit_code, 0)

            # Introduce collision in a second file
            (root / "b.yml").write_text(
                "- type: entity\n"
                "  id: \"Proto1\"\n",
                encoding="utf-8",
            )
            stderr = io.StringIO()
            with patch("sys.stderr", stderr):
                exit_code = cpc.main([str(root)])
            self.assertEqual(exit_code, 1)
            self.assertIn("id='Proto1'", stderr.getvalue())


if __name__ == "__main__":
    unittest.main()
