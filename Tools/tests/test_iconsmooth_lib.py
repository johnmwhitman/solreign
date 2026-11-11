"""Unit tests for Tools/iconsmooth_lib.py.

Tests cover:
- Metric and size parsing helpers (parse_size, parse_metric_mode_base, parse_metric_mode).
- ConversionMode definitions and mode dictionary completeness.
- Image-smoothing quadrant extraction and composition helpers on tiny synthetic PNGs.
- Preservation of dimensions and RGBA alpha channels.
- Full cycle smoothing and inverse reconstruction with known pixel patterns.
- Purely synthetic fixtures only (no external files).
"""

from __future__ import annotations

import io
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from PIL import Image

REPO_ROOT = Path(__file__).resolve().parents[2]
TOOLS_DIR = REPO_ROOT / "Tools"
if str(TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIR))

import iconsmooth_lib  # noqa: E402


def slice_tile_quadrants(
    tile_img: Image.Image,
    subtile_w: int,
    subtile_h: int,
    remtile_w: int,
    remtile_h: int,
) -> list[Image.Image]:
    """Quadrant slicing helper as defined in iconsmooth.py.

    Quadrant ordering:
    0: tile_a -> Bottom-Right (remtile_w, remtile_h)
    1: tile_b -> Top-Left (subtile_w, subtile_h)
    2: tile_c -> Top-Right (remtile_w, subtile_h)
    3: tile_d -> Bottom-Left (subtile_w, remtile_h)
    """
    tile_a = Image.new("RGBA", (remtile_w, remtile_h))
    tile_a.paste(tile_img, (-subtile_w, -subtile_h))

    tile_b = Image.new("RGBA", (subtile_w, subtile_h))
    tile_b.paste(tile_img, (0, 0))

    tile_c = Image.new("RGBA", (remtile_w, subtile_h))
    tile_c.paste(tile_img, (-subtile_w, 0))

    tile_d = Image.new("RGBA", (subtile_w, remtile_h))
    tile_d.paste(tile_img, (0, -subtile_h))

    return [tile_a, tile_b, tile_c, tile_d]


def assemble_state_image(
    tiles: list[list[Image.Image]],
    state_spec: list[int],
    subtile_w: int,
    subtile_h: int,
    tile_w: int,
    tile_h: int,
) -> Image.Image:
    """Compose full state image (2x tile dimensions) as in iconsmooth.py."""
    state_size = (tile_w * 2, tile_h * 2)
    full = Image.new("RGBA", state_size)
    full.paste(tiles[state_spec[0]][0], (subtile_w, subtile_h))
    full.paste(tiles[state_spec[1]][1], (tile_w, 0))
    full.paste(tiles[state_spec[2]][2], (subtile_w, tile_h))
    full.paste(tiles[state_spec[3]][3], (tile_w, tile_h + subtile_h))
    return full


def assemble_full_finale_image(
    tiles: list[list[Image.Image]],
    state_0_spec: list[int],
    subtile_w: int,
    subtile_h: int,
    tile_w: int,
    tile_h: int,
) -> Image.Image:
    """Compose full.png finale image (tile dimensions) as in iconsmooth.py."""
    full_finale = Image.new("RGBA", (tile_w, tile_h))
    full_finale.paste(tiles[state_0_spec[0]][0], (subtile_w, subtile_h))
    full_finale.paste(tiles[state_0_spec[1]][1], (0, 0))
    full_finale.paste(tiles[state_0_spec[2]][2], (subtile_w, 0))
    full_finale.paste(tiles[state_0_spec[3]][3], (0, subtile_h))
    return full_finale


def reconstruct_sheet_from_tiles(
    tiles: list[list[Image.Image]],
    mode: iconsmooth_lib.ConversionMode,
    tile_w: int,
    tile_h: int,
    subtile_w: int,
    subtile_h: int,
) -> Image.Image:
    """Reconstruct spritesheet from 8 states as in iconsmooth_inv.py."""
    output_tw = mode.tw
    output_th = mode.th
    sheet = Image.new("RGBA", (tile_w * output_tw, tile_h * output_th))
    out_states = mode.states
    subtile_ofx = [1, 0, 1, 0]
    subtile_ofy = [1, 0, 0, 1]

    for i in [7, 6, 5, 4, 3, 2, 1, 0]:
        for j in range(4):
            target_tile = out_states[i][j]
            if target_tile != -1:
                target_stx = (target_tile % output_tw) * tile_w
                target_sty = (target_tile // output_tw) * tile_h
                target_stx += subtile_ofx[j] * subtile_w
                target_sty += subtile_ofy[j] * subtile_h
                sheet.paste(tiles[i][j], (target_stx, target_sty))

    return sheet


class TestIconSmoothLibMetrics(unittest.TestCase):
    """Test metric parsing routines and conversion mode registrations."""

    def test_parse_size_square(self):
        self.assertEqual(iconsmooth_lib.parse_size("32"), (32, 32))
        self.assertEqual(iconsmooth_lib.parse_size("2"), (2, 2))

    def test_parse_size_rectangular(self):
        self.assertEqual(iconsmooth_lib.parse_size("32x64"), (32, 64))
        self.assertEqual(iconsmooth_lib.parse_size("10x20"), (10, 20))

    def test_parse_metric_mode_base_inferred_subtiles(self):
        # When no dot is specified, subtiles default to half tile dimensions
        self.assertEqual(iconsmooth_lib.parse_metric_mode_base("32"), (32, 32, 16, 16))
        self.assertEqual(iconsmooth_lib.parse_metric_mode_base("32x64"), (32, 64, 16, 32))
        self.assertEqual(iconsmooth_lib.parse_metric_mode_base("3x5"), (3, 5, 1, 2))

    def test_parse_metric_mode_base_explicit_subtiles(self):
        self.assertEqual(iconsmooth_lib.parse_metric_mode_base("32.8x8"), (32, 32, 8, 8))
        self.assertEqual(iconsmooth_lib.parse_metric_mode_base("32x64.10x12"), (32, 64, 10, 12))

    def test_parse_metric_mode(self):
        # Symmetric tile
        self.assertEqual(
            iconsmooth_lib.parse_metric_mode("32"),
            (32, 32, 16, 16, 16, 16),
        )
        # Asymmetric tile with custom subtile
        # remtile_w = 32 - 10 = 22; remtile_h = 32 - 12 = 20
        self.assertEqual(
            iconsmooth_lib.parse_metric_mode("32.10x12"),
            (32, 32, 10, 12, 22, 20),
        )
        # Rectangular tile with custom subtile
        # remtile_w = 30 - 10 = 20; remtile_h = 40 - 15 = 25
        self.assertEqual(
            iconsmooth_lib.parse_metric_mode("30x40.10x15"),
            (30, 40, 10, 15, 20, 25),
        )

    def test_conversion_modes_catalog(self):
        expected_modes = ["tg", "tg_shuttle", "citadel", "tau", "vxa", "vxap", "rt_states"]
        self.assertEqual(list(iconsmooth_lib.conversion_modes.keys()), expected_modes)
        for name, mode in iconsmooth_lib.conversion_modes.items():
            self.assertIsInstance(mode, iconsmooth_lib.ConversionMode)
            self.assertGreater(mode.tw, 0)
            self.assertGreater(mode.th, 0)
            self.assertEqual(len(mode.states), 8, f"Mode {name} does not define 8 states")
            for idx, st in enumerate(mode.states):
                self.assertEqual(
                    len(st),
                    4,
                    f"Mode {name} state {idx} does not contain 4 quadrant indices",
                )

    def test_all_conv_string(self):
        self.assertEqual(
            iconsmooth_lib.all_conv,
            "tg/citadel/tau/vxa/vxap/rt_states",
        )


class TestIconSmoothingHelpers(unittest.TestCase):
    """Test image-smoothing slicing, layout, and composition helpers on tiny synthetic PNGs."""

    def test_quadrant_slicing_and_alpha_preservation(self):
        """Verify quadrant splitting preserves exact pixels and non-trivial alpha values."""
        # Create a tiny 2x2 synthetic PNG tile with 4 unique colors and distinct alpha values
        tile = Image.new("RGBA", (2, 2))
        px_tl = (255, 0, 0, 255)      # Top-Left
        px_tr = (0, 255, 0, 200)      # Top-Right
        px_bl = (0, 0, 255, 128)      # Bottom-Left
        px_br = (255, 255, 0, 64)     # Bottom-Right
        tile.putpixel((0, 0), px_tl)
        tile.putpixel((1, 0), px_tr)
        tile.putpixel((0, 1), px_bl)
        tile.putpixel((1, 1), px_br)

        # Encode to synthetic PNG in memory to simulate PNG load
        buf = io.BytesIO()
        tile.save(buf, format="PNG")
        buf.seek(0)
        loaded_tile = Image.open(buf)

        sub_w, sub_h, rem_w, rem_h = 1, 1, 1, 1
        quadrants = slice_tile_quadrants(loaded_tile, sub_w, sub_h, rem_w, rem_h)
        self.assertEqual(len(quadrants), 4)

        q_br, q_tl, q_tr, q_bl = quadrants

        # Dimensions preserved
        self.assertEqual(q_br.size, (rem_w, rem_h))
        self.assertEqual(q_tl.size, (sub_w, sub_h))
        self.assertEqual(q_tr.size, (rem_w, sub_h))
        self.assertEqual(q_bl.size, (sub_w, rem_h))

        # Alpha channels and pixel values preserved
        self.assertEqual(q_br.getpixel((0, 0)), px_br)
        self.assertEqual(q_tl.getpixel((0, 0)), px_tl)
        self.assertEqual(q_tr.getpixel((0, 0)), px_tr)
        self.assertEqual(q_bl.getpixel((0, 0)), px_bl)

    def test_asymmetric_dimensions_and_full_finale_identity(self):
        """Verify non-square uneven tile geometry slicing and full_finale reconstruction."""
        # tile_w = 3, tile_h = 4, subtile_w = 1, subtile_h = 3 -> rem_w = 2, rem_h = 1
        tile_w, tile_h, sub_w, sub_h, rem_w, rem_h = iconsmooth_lib.parse_metric_mode("3x4.1x3")
        self.assertEqual((tile_w, tile_h, sub_w, sub_h, rem_w, rem_h), (3, 4, 1, 3, 2, 1))

        tile = Image.new("RGBA", (tile_w, tile_h))
        for y in range(tile_h):
            for x in range(tile_w):
                tile.putpixel((x, y), ((x + 1) * 40, (y + 1) * 30, 100, 150 + x * 10 + y * 5))

        buf = io.BytesIO()
        tile.save(buf, format="PNG")
        buf.seek(0)
        loaded_tile = Image.open(buf)

        quadrants = slice_tile_quadrants(loaded_tile, sub_w, sub_h, rem_w, rem_h)
        dummy_tiles = [quadrants] * 56

        # In TG mode, state 0 is [0, 0, 0, 0]
        tg_mode = iconsmooth_lib.conversion_modes["tg"]
        full_finale = assemble_full_finale_image(
            dummy_tiles,
            tg_mode.states[0],
            sub_w,
            sub_h,
            tile_w,
            tile_h,
        )

        self.assertEqual(full_finale.size, (tile_w, tile_h))
        self.assertEqual(full_finale.mode, "RGBA")

        # Every pixel and alpha value should match the original tile exactly
        for y in range(tile_h):
            for x in range(tile_w):
                self.assertEqual(
                    full_finale.getpixel((x, y)),
                    loaded_tile.getpixel((x, y)),
                    f"Mismatch at ({x}, {y})",
                )

    def test_state_quadrant_layout_and_dimensions(self):
        """Verify that assemble_state_image builds a 2x-dimension canvas with expected quadrants."""
        tile_w, tile_h = 2, 2
        sub_w, sub_h, rem_w, rem_h = 1, 1, 1, 1

        # Tile 0: all red (255, 0, 0, 200)
        t0 = Image.new("RGBA", (tile_w, tile_h), (255, 0, 0, 200))
        # Tile 1: all green (0, 255, 0, 210)
        t1 = Image.new("RGBA", (tile_w, tile_h), (0, 255, 0, 210))
        # Tile 2: all blue (0, 0, 255, 220)
        t2 = Image.new("RGBA", (tile_w, tile_h), (0, 0, 255, 220))
        # Tile 3: all yellow (255, 255, 0, 230)
        t3 = Image.new("RGBA", (tile_w, tile_h), (255, 255, 0, 230))

        tiles = [
            slice_tile_quadrants(t0, sub_w, sub_h, rem_w, rem_h),
            slice_tile_quadrants(t1, sub_w, sub_h, rem_w, rem_h),
            slice_tile_quadrants(t2, sub_w, sub_h, rem_w, rem_h),
            slice_tile_quadrants(t3, sub_w, sub_h, rem_w, rem_h),
        ]

        # Compose state with quadrant selection [BR from tile 0, TL from tile 1, TR from tile 2, BL from tile 3]
        state_spec = [0, 1, 2, 3]
        full_img = assemble_state_image(tiles, state_spec, sub_w, sub_h, tile_w, tile_h)

        # Canvas dimension is (tile_w * 2, tile_h * 2) = (4, 4)
        self.assertEqual(full_img.size, (4, 4))
        self.assertEqual(full_img.mode, "RGBA")

        # Check placed quadrant coordinates:
        # BR (from tile 0) at (sub_w, sub_h) = (1, 1)
        self.assertEqual(full_img.getpixel((1, 1)), (255, 0, 0, 200))
        # TL (from tile 1) at (tile_w, 0) = (2, 0)
        self.assertEqual(full_img.getpixel((2, 0)), (0, 255, 0, 210))
        # TR (from tile 2) at (sub_w, tile_h) = (1, 2)
        self.assertEqual(full_img.getpixel((1, 2)), (0, 0, 255, 220))
        # BL (from tile 3) at (tile_w, tile_h + sub_h) = (2, 3)
        self.assertEqual(full_img.getpixel((2, 3)), (255, 255, 0, 230))

        # Check untouched canvas background remains transparent (0, 0, 0, 0)
        self.assertEqual(full_img.getpixel((0, 0)), (0, 0, 0, 0))

    def test_end_to_end_smoothing_scripts_with_synthetic_fixtures(self):
        """Verify iconsmooth.py and iconsmooth_inv.py cycle with a tiny synthetic sheet."""
        tile_w, tile_h = 2, 2
        # Sheet of 56 tiles, arranged as 7 columns x 8 rows => size (14, 16)
        sheet = Image.new("RGBA", (14, 16), (0, 0, 0, 0))

        # Assign unique patterns to first 8 tiles
        for i in range(8):
            tx = (i % 7) * tile_w
            ty = (i // 7) * tile_h
            sheet.putpixel((tx, ty), (i * 20 + 5, 10, 20, 255))
            sheet.putpixel((tx + 1, ty), (i * 20 + 5, 40, 50, 210))
            sheet.putpixel((tx, ty + 1), (i * 20 + 5, 70, 80, 160))
            sheet.putpixel((tx + 1, ty + 1), (i * 20 + 5, 100, 110, 120))

        with tempfile.TemporaryDirectory() as tmp_dir:
            input_png = os.path.join(tmp_dir, "synth_input.png")
            sheet.save(input_png)

            out_prefix = os.path.join(tmp_dir, "smooth_")
            iconsmooth_script = str(TOOLS_DIR / "iconsmooth.py")
            iconsmooth_inv_script = str(TOOLS_DIR / "iconsmooth_inv.py")

            # Run iconsmooth.py for mode rt_states (1:1 mapping of states 0..7)
            proc = subprocess.run(
                [sys.executable, iconsmooth_script, input_png, "2", "rt_states", out_prefix],
                capture_output=True,
                text=True,
            )
            self.assertEqual(proc.returncode, 0, f"iconsmooth.py failed: {proc.stderr}")

            # Check that out_0.png .. out_7.png and out_full.png were produced
            for s in range(8):
                state_file = f"{out_prefix}{s}.png"
                self.assertTrue(os.path.exists(state_file), f"Missing {state_file}")
                with Image.open(state_file) as s_img:
                    self.assertEqual(s_img.size, (4, 4))
                    self.assertEqual(s_img.mode, "RGBA")

            full_file = f"{out_prefix}full.png"
            self.assertTrue(os.path.exists(full_file))
            with Image.open(full_file) as f_img:
                self.assertEqual(f_img.size, (2, 2))
                self.assertEqual(f_img.mode, "RGBA")
                # full_finale for state 0 should match tile 0 exactly
                self.assertEqual(f_img.getpixel((0, 0)), sheet.getpixel((0, 0)))
                self.assertEqual(f_img.getpixel((1, 0)), sheet.getpixel((1, 0)))
                self.assertEqual(f_img.getpixel((0, 1)), sheet.getpixel((0, 1)))
                self.assertEqual(f_img.getpixel((1, 1)), sheet.getpixel((1, 1)))

            # Run iconsmooth_inv.py to reconstruct
            inv_png = os.path.join(tmp_dir, "reconstructed.png")
            proc2 = subprocess.run(
                [sys.executable, iconsmooth_inv_script, out_prefix, "2", "rt_states", inv_png],
                capture_output=True,
                text=True,
            )
            self.assertEqual(proc2.returncode, 0, f"iconsmooth_inv.py failed: {proc2.stderr}")

            # Verify reconstructed sheet dimensions and pixel accuracy
            # rt_states is 8 columns x 1 row -> width 8*2 = 16, height 1*2 = 2
            with Image.open(inv_png) as r_img:
                self.assertEqual(r_img.size, (16, 2))
                self.assertEqual(r_img.mode, "RGBA")
                for i in range(8):
                    src_tx = (i % 7) * tile_w
                    src_ty = (i // 7) * tile_h
                    for dy in range(tile_h):
                        for dx in range(tile_w):
                            expected_px = sheet.getpixel((src_tx + dx, src_ty + dy))
                            actual_px = r_img.getpixel((i * tile_w + dx, dy))
                            self.assertEqual(
                                actual_px,
                                expected_px,
                                f"Reconstruction mismatch at tile {i}, ({dx}, {dy})",
                            )

    def test_reconstruct_sheet_in_memory_all_modes(self):
        """Test reconstruct_sheet_from_tiles helper across all registered conversion modes."""
        tile_w, tile_h = 2, 2
        sub_w, sub_h, rem_w, rem_h = 1, 1, 1, 1

        # Build 8 synthetic state quadrant groups
        tiles_by_state = []
        for s in range(8):
            q_br = Image.new("RGBA", (rem_w, rem_h), (s * 30, 10, 20, 200))
            q_tl = Image.new("RGBA", (sub_w, sub_h), (s * 30, 40, 50, 210))
            q_tr = Image.new("RGBA", (rem_w, sub_h), (s * 30, 70, 80, 220))
            q_bl = Image.new("RGBA", (sub_w, rem_h), (s * 30, 100, 110, 230))
            tiles_by_state.append([q_br, q_tl, q_tr, q_bl])

        for mode_name, mode in iconsmooth_lib.conversion_modes.items():
            reconstructed = reconstruct_sheet_from_tiles(
                tiles_by_state,
                mode,
                tile_w,
                tile_h,
                sub_w,
                sub_h,
            )
            expected_size = (tile_w * mode.tw, tile_h * mode.th)
            self.assertEqual(
                reconstructed.size,
                expected_size,
                f"Mode {mode_name} produced unexpected dimension {reconstructed.size} vs {expected_size}",
            )
            self.assertEqual(
                reconstructed.mode,
                "RGBA",
                f"Mode {mode_name} produced non-RGBA image",
            )


if __name__ == "__main__":
    unittest.main()
