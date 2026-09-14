"""RED-first tests for MapLens — the MapRenderer wrapper.

The scars this suite pins (2026-08-02 A9 render session):
  1. Tile bounds come from decoded NON-SPACE tiles, never chunk-index math —
     a chunk's 16-tile granularity shifted every crop.
  2. The bounds are cross-checked against the RENDER: a PNG whose dimensions
     disagree with the computed bounds fails closed instead of producing a
     silently mis-anchored crop.
  3. world→pixel honors the renderer's vertical flip.
  4. The marker overlay actually draws at the converted pixel.
  5. The render command runs maps SEQUENTIALLY with the nofile limit raised
     (the ulimit fix baked in, not remembered).
"""
from __future__ import annotations

import base64
import importlib.util
from pathlib import Path
import resource
import struct
import sys
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / "maplens.py"

SPEC = importlib.util.spec_from_file_location("solreign_maplens", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
MAPLENS = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = MAPLENS  # dataclasses need the module registered (3.14)
SPEC.loader.exec_module(MAPLENS)

TILE_PX = 32


def encode_chunk(tile_for_cell) -> str:
    raw = b"".join(
        struct.pack("<IBBB", tile_for_cell(x, y), 0, 0, 0)
        for y in range(16)
        for x in range(16)
    )
    return base64.b64encode(raw).decode("ascii")


def bounds_map_text() -> str:
    """One grid, one chunk at ind 0,0. Non-space tiles occupy exactly
    x in [2..5], y in [1..3] — chunk math would claim [0..15]x[0..15]."""

    def tile_for_cell(x: int, y: int) -> int:
        return 93 if 2 <= x <= 5 and 1 <= y <= 3 else 0

    chunk = encode_chunk(tile_for_cell)
    return (
        "meta:\n"
        "  format: 7\n"
        "  category: Map\n"
        "  entityCount: 2 # synthetic fixture\n"
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
    )


class BoundsTests(unittest.TestCase):
    def test_bounds_are_nonspace_tiles_not_chunk_extents(self) -> None:
        bounds = MAPLENS.grid_tile_bounds(bounds_map_text(), parent=2)
        self.assertEqual(
            (bounds.min_x, bounds.min_y, bounds.max_x, bounds.max_y),
            (2, 1, 5, 3),
        )

    def test_all_space_grid_fails_closed(self) -> None:
        def all_space(x, y):
            return 0

        text = bounds_map_text().replace(
            encode_chunk(
                lambda x, y: 93 if 2 <= x <= 5 and 1 <= y <= 3 else 0
            ),
            encode_chunk(all_space),
        )
        with self.assertRaises(MAPLENS.LensError):
            MAPLENS.grid_tile_bounds(text, parent=2)


class WorldToPixelTests(unittest.TestCase):
    def bounds(self):
        return MAPLENS.grid_tile_bounds(bounds_map_text(), parent=2)

    def test_expected_image_size(self) -> None:
        bounds = self.bounds()
        self.assertEqual(MAPLENS.expected_image_size(bounds), (4 * TILE_PX, 3 * TILE_PX))

    def test_world_to_pixel_honors_vertical_flip(self) -> None:
        bounds = self.bounds()
        image_size = MAPLENS.expected_image_size(bounds)
        # Bottom-left corner of the tile span, world (2.0, 1.0) -> pixel
        # x=0 and y=image_height (bottom row after the flip).
        self.assertEqual(
            MAPLENS.world_to_pixel((2.0, 1.0), bounds, image_size), (0, 96)
        )
        # Centre of tile (2,1) -> half a tile in, half a tile up from bottom.
        self.assertEqual(
            MAPLENS.world_to_pixel((2.5, 1.5), bounds, image_size), (16, 80)
        )
        # Top-right world corner -> right edge, pixel y=0.
        self.assertEqual(
            MAPLENS.world_to_pixel((6.0, 4.0), bounds, image_size), (128, 0)
        )

    def test_mismatched_render_dimensions_fail_closed(self) -> None:
        bounds = self.bounds()
        with self.assertRaisesRegex(MAPLENS.LensError, "does not match"):
            MAPLENS.world_to_pixel((2.5, 1.5), bounds, (999, 96))


class CropTests(unittest.TestCase):
    def test_crop_with_marker_writes_cross_at_target(self) -> None:
        from PIL import Image

        bounds = MAPLENS.grid_tile_bounds(bounds_map_text(), parent=2)
        width, height = MAPLENS.expected_image_size(bounds)
        image = Image.new("RGBA", (width, height), (0, 0, 0, 255))
        cropped, marker_px = MAPLENS.crop_with_marker(
            image, bounds, world=(3.5, 2.5), radius_tiles=1
        )
        # Marker pixel inside the crop is the magenta crosshair centre.
        self.assertEqual(cropped.getpixel(marker_px), MAPLENS.MARKER_COLOR)
        # The crop is (2*radius+1) tiles square when not clamped by edges.
        self.assertEqual(cropped.size, (3 * TILE_PX, 3 * TILE_PX))

    def test_crop_of_wrong_sized_render_fails_closed(self) -> None:
        from PIL import Image

        bounds = MAPLENS.grid_tile_bounds(bounds_map_text(), parent=2)
        image = Image.new("RGBA", (10, 10), (0, 0, 0, 255))
        with self.assertRaisesRegex(MAPLENS.LensError, "does not match"):
            MAPLENS.crop_with_marker(image, bounds, world=(3.5, 2.5), radius_tiles=1)


class RenderOrchestrationTests(unittest.TestCase):
    def test_render_commands_are_one_per_map_sequential(self) -> None:
        commands = MAPLENS.render_commands(
            ["Resources/Maps/_Solreign/a.yml", "Resources/Maps/_Solreign/b.yml"],
            output_dir=Path("/tmp/out"),
        )
        self.assertEqual(len(commands), 2)
        for command, map_path in zip(
            commands,
            ["Resources/Maps/_Solreign/a.yml", "Resources/Maps/_Solreign/b.yml"],
        ):
            self.assertIn("--files", command)
            self.assertIn(map_path, command)
            self.assertEqual(command.count("Resources/Maps/_Solreign/a.yml") +
                             command.count("Resources/Maps/_Solreign/b.yml"), 1)

    def test_nofile_limit_is_raised(self) -> None:
        soft, hard = resource.getrlimit(resource.RLIMIT_NOFILE)
        try:
            MAPLENS.ensure_nofile(4096)
            new_soft, _ = resource.getrlimit(resource.RLIMIT_NOFILE)
            self.assertGreaterEqual(new_soft, min(4096, hard))
        finally:
            resource.setrlimit(resource.RLIMIT_NOFILE, (soft, hard))

    def test_render_nofile_target_covers_leviathan_under_ambient_pressure(self) -> None:
        # 2026-08-03: leviathan hit Too-many-open-files at the old 4096 target during a
        # 6-map run while other lanes' MSBuild daemons held system handles, then rendered
        # clean solo. The per-process target must be high enough that ambient pressure
        # doesn't decide the outcome (ensure_nofile still caps at the OS hard limit).
        self.assertGreaterEqual(MAPLENS.RENDER_NOFILE_TARGET, 65536)

    def test_render_env_disables_msbuild_node_reuse(self) -> None:
        # Each `dotnet run` otherwise leaves nodeReuse daemons holding file handles
        # system-wide between maps — the ambient pressure the previous test guards against.
        env = MAPLENS.render_env()
        self.assertEqual(env.get("MSBUILDDISABLENODEREUSE"), "1")
        self.assertIn("PATH", env, "render env must extend os.environ, not replace it")


if __name__ == "__main__":
    unittest.main()
