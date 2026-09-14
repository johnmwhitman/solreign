"""Production MapPatch manifests must match their generated map blocks."""

from __future__ import annotations

from pathlib import Path
import subprocess
import sys
import unittest


REPO_ROOT = Path(__file__).resolve().parents[4]
SCRIPT = REPO_ROOT / "Tools/_Solreign/MapPatch/map_patch.py"
MANIFESTS_DIR = SCRIPT.parent / "manifests"


class ProductionManifestContractTests(unittest.TestCase):
    def test_each_production_manifest_matches_its_owned_generated_blocks(self) -> None:
        """Catches a manifest and its owned generated blocks diverging."""
        manifests = sorted(MANIFESTS_DIR.glob("*.json"))
        self.assertNotEqual(manifests, [], "expected at least one production MapPatch manifest")

        for manifest in manifests:
            with self.subTest(manifest=manifest.name):
                result = subprocess.run(
                    [
                        sys.executable,
                        str(SCRIPT),
                        "--root",
                        str(REPO_ROOT),
                        "--manifest",
                        str(manifest),
                        "--check",
                    ],
                    text=True,
                    capture_output=True,
                    check=False,
                )
                self.assertEqual(
                    result.returncode,
                    0,
                    f"{manifest.name} diverges from its owned generated blocks "
                    f"(stdout={result.stdout!r} stderr={result.stderr!r})",
                )


if __name__ == "__main__":
    unittest.main()
