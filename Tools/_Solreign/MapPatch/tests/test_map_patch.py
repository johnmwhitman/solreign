from __future__ import annotations

import base64
import importlib.util
import json
import os
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest
from unittest import mock


SCRIPT = Path(__file__).resolve().parents[1] / "map_patch.py"
MAP_ROOT = Path("Resources/Maps/_Solreign")
MANIFESTS_DIR = Path(__file__).resolve().parents[1] / "manifests"

SPEC = importlib.util.spec_from_file_location("solreign_map_patch", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
MAP_PATCH = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MAP_PATCH)


def encode_chunk(tile_id: int) -> str:
    raw = b"".join(struct.pack("<IBBB", tile_id, 0, 0, 0) for _ in range(16 * 16))
    return base64.b64encode(raw).decode("ascii")


def map_text(
    *,
    anchor_uid: int = 10,
    anchor_pos: tuple[float, float] = (1.5, 1.5),
    extra_entities: str = "",
    negative_chunks: bool = False,
) -> str:
    chunk_entries = [
        "        - ind: 0,0",
        f"          tiles: {encode_chunk(0 if negative_chunks else 93)}",
    ]
    if negative_chunks:
        chunk_entries.extend(
            [
                "        - ind: -1,-1",
                f"          tiles: {encode_chunk(93)}",
            ]
        )

    return (
        "meta:\n"
        "  format: 7\n"
        "  category: Map\n"
        "  entityCount: 3 # synthetic fixture\n"
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
        + "\n".join(chunk_entries)
        + "\n"
        "- proto: SolreignWingmateBeacon\n"
        "  entities:\n"
        f"  - uid: {anchor_uid}\n"
        "    components:\n"
        "    - type: Transform\n"
        f"      pos: {anchor_pos[0]},{anchor_pos[1]}\n"
        "      parent: 2\n"
        + extra_entities
    )


def placement(
    *,
    key: str,
    proto: str,
    uid: int,
    pos: tuple[float, float],
    component_type: str,
    component_field: str,
    occupants: list[dict[str, object]] | None = None,
) -> dict[str, object]:
    return {
        "key": key,
        "proto": proto,
        "uid": uid,
        "pos": [pos[0], pos[1]],
        "parent": 2,
        "expected_tile": "FloorSteel",
        "expected_occupants": occupants or [],
        "component": {
            "type": component_type,
            "fields": {component_field: "main"},
        },
    }


def component_less_placement(
    *,
    key: str,
    proto: str,
    uid: int,
    pos: tuple[float, float],
    expected_tile: str,
    occupants: list[dict[str, object]] | None = None,
) -> dict[str, object]:
    """A placement that intentionally omits the map-level `component` override.

    The MapPatch engine must accept a placement whose prototype has no
    map-level component to override (e.g. SolreignGolfBall — its physics,
    stroke counter, and trigger are baked into the prototype and need no
    field-by-field YAML re-declaration). When `component` is absent the
    rendered entity block must contain ONLY the Transform component, no
    extra `- type: <...>` line.
    """
    return {
        "key": key,
        "proto": proto,
        "uid": uid,
        "pos": [pos[0], pos[1]],
        "parent": 2,
        "expected_tile": expected_tile,
        "expected_occupants": occupants or [],
    }


def manifest_entry(
    path: str,
    *,
    anchor_uid: int = 10,
    anchor_pos: tuple[float, float] = (1.5, 1.5),
    library_pos: tuple[float, float] = (2.5, 1.5),
    noticeboard_pos: tuple[float, float] = (1.5, 2.5),
    library_occupants: list[dict[str, object]] | None = None,
) -> dict[str, object]:
    return {
        "path": path,
        "anchor": {
            "proto": "SolreignWingmateBeacon",
            "uid": anchor_uid,
            "pos": [anchor_pos[0], anchor_pos[1]],
            "parent": 2,
        },
        "max_manhattan_tiles": 4,
        "placements": [
            placement(
                key="library",
                proto="SolreignLibraryAnnex",
                uid=901100,
                pos=library_pos,
                component_type="SolreignLibraryAnnex",
                component_field="archiveId",
                occupants=library_occupants,
            ),
            placement(
                key="noticeboard",
                proto="SolreignNoticeboard",
                uid=901101,
                pos=noticeboard_pos,
                component_type="SolreignNoticeboard",
                component_field="boardId",
            ),
        ],
    }


class MapPatchTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.root = Path(self.temp_dir.name)
        (self.root / MAP_ROOT).mkdir(parents=True)

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def write_map(self, name: str, content: str) -> Path:
        path = self.root / MAP_ROOT / name
        path.write_text(content, encoding="utf-8")
        return path

    def write_manifest(
        self,
        entries: list[dict[str, object]],
        name: str = "manifest.json",
        patch_id: str = "test-community-fixtures-v1",
    ) -> Path:
        path = self.root / name
        path.write_text(
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
        return path

    def run_tool(
        self,
        manifest: Path,
        *args: str,
        expected_returncode: int = 0,
    ) -> subprocess.CompletedProcess[str]:
        result = subprocess.run(
            [
                sys.executable,
                str(SCRIPT),
                "--root",
                str(self.root),
                "--manifest",
                str(manifest),
                *args,
            ],
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

    def test_default_dry_run_prints_diff_without_writing(self) -> None:
        map_path = self.write_map("alpha.yml", map_text())
        before = map_path.read_bytes()
        manifest = self.write_manifest([manifest_entry(str(MAP_ROOT / "alpha.yml"))])

        result = self.run_tool(manifest)

        self.assertEqual(map_path.read_bytes(), before)
        self.assertIn("+# BEGIN SOLREIGN MAP PATCH test-community-fixtures-v1", result.stdout)
        self.assertIn("+  - uid: 901100", result.stdout)
        self.assertIn("+  - uid: 901101", result.stdout)

    def test_apply_is_idempotent_and_check_requires_exact_applied_state(self) -> None:
        map_path = self.write_map("alpha.yml", map_text())
        manifest = self.write_manifest([manifest_entry(str(MAP_ROOT / "alpha.yml"))])

        self.run_tool(manifest, "--check", expected_returncode=1)
        self.run_tool(manifest, "--apply")
        applied = map_path.read_bytes()
        self.run_tool(manifest, "--apply")
        self.run_tool(manifest, "--check")

        self.assertEqual(map_path.read_bytes(), applied)
        text = applied.decode("utf-8")
        self.assertEqual(text.count("uid: 901100"), 1)
        self.assertEqual(text.count("uid: 901101"), 1)
        self.assertIn("  entityCount: 3 # synthetic fixture", text)

    def test_update_requires_exact_prior_manifest_and_reaches_new_check_state(self) -> None:
        map_path = self.write_map("alpha.yml", map_text())
        old_entry = manifest_entry(str(MAP_ROOT / "alpha.yml"))
        old_manifest = self.write_manifest([old_entry], "old.json")
        self.run_tool(old_manifest, "--apply")

        new_entry = manifest_entry(str(MAP_ROOT / "alpha.yml"))
        new_entry["placements"][0]["pos"] = [3.5, 1.5]  # type: ignore[index]
        new_entry["placements"][0]["expected_occupants"] = []  # type: ignore[index]
        new_manifest = self.write_manifest([new_entry], "new.json")

        refused = self.run_tool(new_manifest, "--apply", expected_returncode=2)
        self.assertIn("modified", refused.stderr)

        self.run_tool(
            new_manifest,
            "--apply",
            "--update-from",
            str(old_manifest),
        )
        self.run_tool(new_manifest, "--check")

        text = map_path.read_text(encoding="utf-8")
        self.assertIn("      pos: 3.5,1.5", text)
        self.assertNotIn("      pos: 2.5,1.5", text)

    def test_remove_apply_restores_original_bytes(self) -> None:
        map_path = self.write_map("alpha.yml", map_text())
        original = map_path.read_bytes()
        manifest = self.write_manifest([manifest_entry(str(MAP_ROOT / "alpha.yml"))])
        self.run_tool(manifest, "--apply")

        dry_run = self.run_tool(manifest, "--remove")
        self.assertNotEqual(map_path.read_bytes(), original)
        self.assertIn("-# BEGIN SOLREIGN MAP PATCH", dry_run.stdout)

        self.run_tool(manifest, "--remove", "--apply")
        self.assertEqual(map_path.read_bytes(), original)

    def test_uid_collision_fails_before_any_map_is_written(self) -> None:
        alpha = self.write_map("alpha.yml", map_text())
        collision = (
            "- proto: SomeOtherPrototype\n"
            "  entities:\n"
            "  - uid: 901100\n"
            "    components:\n"
            "    - type: Transform\n"
            "      pos: 8.5,8.5\n"
            "      parent: 2\n"
        )
        beta = self.write_map("beta.yml", map_text(extra_entities=collision))
        alpha_before = alpha.read_bytes()
        beta_before = beta.read_bytes()
        manifest = self.write_manifest(
            [
                manifest_entry(str(MAP_ROOT / "alpha.yml")),
                manifest_entry(str(MAP_ROOT / "beta.yml")),
            ]
        )

        result = self.run_tool(manifest, "--apply", expected_returncode=2)

        self.assertIn("uid 901100", result.stderr)
        self.assertEqual(alpha.read_bytes(), alpha_before)
        self.assertEqual(beta.read_bytes(), beta_before)

    def test_unexpected_occupant_fails_before_any_map_is_written(self) -> None:
        table = (
            "- proto: Table\n"
            "  entities:\n"
            "  - uid: 12\n"
            "    components:\n"
            "    - type: Transform\n"
            "      pos: 2.5,1.5\n"
            "      parent: 2\n"
        )
        map_path = self.write_map("alpha.yml", map_text(extra_entities=table))
        before = map_path.read_bytes()
        manifest = self.write_manifest([manifest_entry(str(MAP_ROOT / "alpha.yml"))])

        result = self.run_tool(manifest, "--apply", expected_returncode=2)

        self.assertIn("occupant", result.stderr.lower())
        self.assertEqual(map_path.read_bytes(), before)

    def test_manifest_can_pin_expected_underfloor_occupants(self) -> None:
        cable = (
            "- proto: CableMV\n"
            "  entities:\n"
            "  - uid: 11\n"
            "    components:\n"
            "    - type: Transform\n"
            "      pos: 2.5,1.5\n"
            "      parent: 2\n"
        )
        self.write_map("alpha.yml", map_text(extra_entities=cable))
        manifest = self.write_manifest(
            [
                manifest_entry(
                    str(MAP_ROOT / "alpha.yml"),
                    library_occupants=[{"proto": "CableMV", "uid": 11}],
                )
            ]
        )

        self.run_tool(manifest, "--apply")
        self.run_tool(manifest, "--check")

    def test_manifest_cannot_allowlist_physical_floor_equipment(self) -> None:
        vent = (
            "- proto: GasVentPump\n"
            "  entities:\n"
            "  - uid: 12\n"
            "    components:\n"
            "    - type: Transform\n"
            "      pos: 2.5,1.5\n"
            "      parent: 2\n"
        )
        map_path = self.write_map("alpha.yml", map_text(extra_entities=vent))
        before = map_path.read_bytes()
        manifest = self.write_manifest(
            [
                manifest_entry(
                    str(MAP_ROOT / "alpha.yml"),
                    library_occupants=[{"proto": "GasVentPump", "uid": 12}],
                )
            ]
        )

        result = self.run_tool(manifest, "--apply", expected_returncode=2)

        self.assertIn("physical occupant", result.stderr)
        self.assertEqual(map_path.read_bytes(), before)

    def test_anchor_drift_fails_closed(self) -> None:
        map_path = self.write_map("alpha.yml", map_text(anchor_uid=20))
        before = map_path.read_bytes()
        manifest = self.write_manifest([manifest_entry(str(MAP_ROOT / "alpha.yml"))])

        result = self.run_tool(manifest, "--apply", expected_returncode=2)

        self.assertIn("anchor", result.stderr.lower())
        self.assertEqual(map_path.read_bytes(), before)

    def test_target_prototype_already_outside_owned_block_fails_closed(self) -> None:
        duplicate = (
            "- proto: SolreignLibraryAnnex\n"
            "  entities:\n"
            "  - uid: 77\n"
            "    components:\n"
            "    - type: Transform\n"
            "      pos: 8.5,8.5\n"
            "      parent: 2\n"
        )
        self.write_map("alpha.yml", map_text(extra_entities=duplicate))
        manifest = self.write_manifest([manifest_entry(str(MAP_ROOT / "alpha.yml"))])

        result = self.run_tool(manifest, "--apply", expected_returncode=2)

        self.assertIn("SolreignLibraryAnnex", result.stderr)

    def test_non_centered_coordinate_fails_closed(self) -> None:
        self.write_map("alpha.yml", map_text())
        entry = manifest_entry(str(MAP_ROOT / "alpha.yml"))
        entry["placements"][0]["pos"] = [2.25, 1.5]  # type: ignore[index]
        manifest = self.write_manifest([entry])

        result = self.run_tool(manifest, "--apply", expected_returncode=2)

        self.assertIn(".5-centered", result.stderr)

    def test_negative_coordinates_use_mathematical_floor_for_tile_lookup(self) -> None:
        self.write_map(
            "negative.yml",
            map_text(anchor_pos=(-1.5, -1.5), negative_chunks=True),
        )
        manifest = self.write_manifest(
            [
                manifest_entry(
                    str(MAP_ROOT / "negative.yml"),
                    anchor_pos=(-1.5, -1.5),
                    library_pos=(-0.5, -0.5),
                    noticeboard_pos=(-1.5, -0.5),
                )
            ]
        )

        self.run_tool(manifest, "--apply")
        self.run_tool(manifest, "--check")

    def test_real_v7_chunk_mapping_shape_is_decoded(self) -> None:
        content = map_text().replace(
            "        - ind: 0,0\n",
            "        0,0:\n          ind: 0,0\n",
        )
        self.write_map("mapped-chunks.yml", content)
        manifest = self.write_manifest(
            [manifest_entry(str(MAP_ROOT / "mapped-chunks.yml"))]
        )

        self.run_tool(manifest, "--apply")
        self.run_tool(manifest, "--check")

    def test_owned_block_is_inserted_before_yaml_document_end(self) -> None:
        map_path = self.write_map("document-end.yml", map_text() + "...\n")
        manifest = self.write_manifest(
            [manifest_entry(str(MAP_ROOT / "document-end.yml"))]
        )

        self.run_tool(manifest, "--apply")

        text = map_path.read_text(encoding="utf-8")
        self.assertTrue(text.endswith("# END SOLREIGN MAP PATCH test-community-fixtures-v1\n...\n"))

    def test_remove_refuses_a_modified_owned_block(self) -> None:
        map_path = self.write_map("alpha.yml", map_text())
        manifest = self.write_manifest([manifest_entry(str(MAP_ROOT / "alpha.yml"))])
        self.run_tool(manifest, "--apply")
        map_path.write_text(
            map_path.read_text(encoding="utf-8").replace(
                "      archiveId: main",
                "      archiveId: tampered",
            ),
            encoding="utf-8",
        )

        result = self.run_tool(manifest, "--remove", "--apply", expected_returncode=2)

        self.assertIn("owned block", result.stderr.lower())
        self.assertIn("archiveId: tampered", map_path.read_text(encoding="utf-8"))

    def test_partial_owned_state_across_maps_fails_before_writing(self) -> None:
        alpha = self.write_map(
            "alpha.yml",
            map_text() + "# BEGIN SOLREIGN MAP PATCH test-community-fixtures-v1\n",
        )
        beta = self.write_map("beta.yml", map_text())
        alpha_before = alpha.read_bytes()
        beta_before = beta.read_bytes()
        manifest = self.write_manifest(
            [
                manifest_entry(str(MAP_ROOT / "alpha.yml")),
                manifest_entry(str(MAP_ROOT / "beta.yml")),
            ]
        )

        result = self.run_tool(manifest, "--apply", expected_returncode=2)

        self.assertIn("sentinel", result.stderr.lower())
        self.assertEqual(alpha.read_bytes(), alpha_before)
        self.assertEqual(beta.read_bytes(), beta_before)

    def test_second_replace_failure_restores_the_first_map(self) -> None:
        alpha = self.write_map("alpha.yml", "alpha-before\n")
        beta = self.write_map("beta.yml", "beta-before\n")
        real_replace = os.replace
        failed = False

        def replace_with_second_commit_failure(source: object, destination: object) -> None:
            nonlocal failed
            source_path = Path(source)
            destination_path = Path(destination)
            if (
                not failed
                and source_path.suffix == ".tmp"
                and destination_path == beta
            ):
                failed = True
                raise OSError("injected second-map commit failure")
            real_replace(source, destination)

        with mock.patch.object(
            MAP_PATCH.os,
            "replace",
            side_effect=replace_with_second_commit_failure,
        ):
            with self.assertRaisesRegex(
                MAP_PATCH.PatchError,
                "every replaced map was restored",
            ):
                MAP_PATCH.write_all_with_rollback(
                    [
                        (alpha, "alpha-after\n"),
                        (beta, "beta-after\n"),
                    ]
                )

        self.assertEqual(alpha.read_text(encoding="utf-8"), "alpha-before\n")
        self.assertEqual(beta.read_text(encoding="utf-8"), "beta-before\n")
        self.assertEqual(list(alpha.parent.glob(".*.tmp")), [])
        self.assertEqual(list(alpha.parent.glob(".*.bak")), [])

    def test_restore_failure_preserves_exact_backup_for_manual_recovery(self) -> None:
        alpha = self.write_map("alpha.yml", "alpha-before\n")
        beta = self.write_map("beta.yml", "beta-before\n")
        real_replace = os.replace
        commit_failed = False

        def replace_with_commit_and_restore_failure(
            source: object,
            destination: object,
        ) -> None:
            nonlocal commit_failed
            source_path = Path(source)
            destination_path = Path(destination)
            if (
                not commit_failed
                and source_path.suffix == ".tmp"
                and destination_path == beta
            ):
                commit_failed = True
                raise OSError("injected second-map commit failure")
            if source_path.suffix == ".bak" and destination_path == alpha:
                raise OSError("injected first-map restore failure")
            real_replace(source, destination)

        with mock.patch.object(
            MAP_PATCH.os,
            "replace",
            side_effect=replace_with_commit_and_restore_failure,
        ):
            with self.assertRaisesRegex(
                MAP_PATCH.PatchError,
                "byte-exact backup\\(s\\) preserved",
            ) as error:
                MAP_PATCH.write_all_with_rollback(
                    [
                        (alpha, "alpha-after\n"),
                        (beta, "beta-after\n"),
                    ]
                )

        backups = list(alpha.parent.glob(".alpha.yml.*.bak"))
        self.assertEqual(alpha.read_text(encoding="utf-8"), "alpha-after\n")
        self.assertEqual(beta.read_text(encoding="utf-8"), "beta-before\n")
        self.assertEqual(len(backups), 1)
        self.assertEqual(backups[0].read_text(encoding="utf-8"), "alpha-before\n")
        self.assertIn(str(alpha), str(error.exception))
        self.assertIn(str(backups[0]), str(error.exception))

    def test_manifest_path_cannot_escape_map_root(self) -> None:
        outside = self.root / "outside.yml"
        outside.write_text(map_text(), encoding="utf-8")
        manifest = self.write_manifest([manifest_entry("outside.yml")])

        result = self.run_tool(manifest, "--apply", expected_returncode=2)

        self.assertIn("Resources/Maps/_Solreign", result.stderr)

    def test_placement_may_omit_map_level_component_override(self) -> None:
        """A placement whose prototype needs no field-level override (e.g. a
        SolreignGolfBall — physics, stroke counter, and sound are all baked
        into the prototype) must apply with no `component` key in the
        manifest entry, and the rendered entity block must contain only the
        Transform component.
        """
        map_path = self.write_map("alpha.yml", map_text())
        manifest = self.write_manifest(
            [
                {
                    "path": str(MAP_ROOT / "alpha.yml"),
                    "anchor": {
                        "proto": "SolreignWingmateBeacon",
                        "uid": 10,
                        "pos": [1.5, 1.5],
                        "parent": 2,
                    },
                    "max_manhattan_tiles": 4,
                    "placements": [
                        component_less_placement(
                            key="ball",
                            proto="SolreignGolfBall",
                            uid=901200,
                            pos=(2.5, 1.5),
                            expected_tile="FloorSteel",
                        ),
                    ],
                }
            ]
        )

        self.run_tool(manifest, "--apply")
        self.run_tool(manifest, "--check")

        text = map_path.read_text(encoding="utf-8")
        self.assertEqual(text.count("uid: 901200"), 1)
        self.assertIn("- proto: SolreignGolfBall", text)
        # No extra component line beyond the Transform — the placement has
        # no map-level component override, so the owned block must not
        # fabricate one.
        self.assertNotIn("- type: SolreignGolfBall", text)
        # The owned block must still be a complete Transform-only entity.
        self.assertIn(
            "  - uid: 901200\n    components:\n    - type: Transform",
            text,
        )

    def test_check_and_reapply_preserve_multiple_owned_block_order(self) -> None:
        map_path = self.write_map("alpha.yml", map_text())
        first_manifest = self.write_manifest(
            [manifest_entry(str(MAP_ROOT / "alpha.yml"))],
            name="first.json",
            patch_id="first-patch",
        )
        second_entry = {
            "path": str(MAP_ROOT / "alpha.yml"),
            "anchor": {
                "proto": "SolreignWingmateBeacon",
                "uid": 10,
                "pos": [1.5, 1.5],
                "parent": 2,
            },
            "max_manhattan_tiles": 4,
            "placements": [
                component_less_placement(
                    key="ball",
                    proto="SolreignGolfBall",
                    uid=901200,
                    pos=(3.5, 3.5),
                    expected_tile="FloorSteel",
                ),
            ],
        }
        second_manifest = self.write_manifest(
            [second_entry],
            name="second.json",
            patch_id="second-patch",
        )

        self.run_tool(first_manifest, "--apply")
        self.run_tool(second_manifest, "--apply")
        applied = map_path.read_bytes()

        self.run_tool(first_manifest, "--check")
        self.run_tool(first_manifest, "--apply")

        self.assertEqual(map_path.read_bytes(), applied)
        text = applied.decode("utf-8")
        self.assertLess(
            text.index("# BEGIN SOLREIGN MAP PATCH first-patch"),
            text.index("# BEGIN SOLREIGN MAP PATCH second-patch"),
        )

    def test_real_minigolf_v1_manifest_is_checkable_and_has_required_protos(self) -> None:
        """The committed minigolf-v1.json must be well-formed, --check on
        the actual solreign_terminus.yml, and contain the three required
        unique prototypes: SolreignGolfBall, SolreignGolfClubPutter, and
        SolreignGolfHole.
        """
        manifest_path = MANIFESTS_DIR / "minigolf-v1.json"
        self.assertTrue(manifest_path.is_file(), f"missing manifest {manifest_path}")

        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        self.assertEqual(manifest.get("schema_version"), 1)
        self.assertEqual(manifest.get("patch_id"), "minigolf-v1")

        maps = manifest.get("maps")
        self.assertIsInstance(maps, list)
        self.assertEqual(len(maps), 1)
        map_entry = maps[0]
        self.assertEqual(
            map_entry["path"],
            "Resources/Maps/_Solreign/solreign_terminus.yml",
        )

        protos = {p["proto"] for p in map_entry["placements"]}
        self.assertEqual(
            protos,
            {"SolreignGolfBall", "SolreignGolfClubPutter", "SolreignGolfHole"},
        )

        # Every placement must omit the optional `component` override —
        # the whole reason this manifest exists is to exercise that
        # back-compat path (no map-level fields to override).
        for placement in map_entry["placements"]:
            self.assertNotIn("component", placement)

        # --check on the real terminus map (relative to the repo root)
        repo_root = Path(__file__).resolve().parents[4]
        result = subprocess.run(
            [
                sys.executable,
                str(SCRIPT),
                "--root",
                str(repo_root),
                "--manifest",
                str(manifest_path),
                "--check",
            ],
            text=True,
            capture_output=True,
            check=False,
        )
        self.assertEqual(
            result.returncode,
            0,
            f"minigolf-v1 --check failed (stdout={result.stdout!r} stderr={result.stderr!r})",
        )

        # The owned block must be present, byte-for-byte, in the terminus
        # map — proves the apply landed and was not subsequently mutated.
        map_text = (repo_root / map_entry["path"]).read_text(encoding="utf-8")
        begin, end = MAP_PATCH.sentinel_strings("minigolf-v1")
        self.assertIn(begin, map_text)
        self.assertIn(end, map_text)
        self.assertEqual(map_text.count(begin), 1)
        self.assertEqual(map_text.count(end), 1)


if __name__ == "__main__":
    unittest.main()
