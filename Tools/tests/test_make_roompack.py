#!/usr/bin/env python3
"""Unit tests for Tools/make_roompack.py.

Tests cover:
- Pure helpers / dataclasses / constants (Box2, RoomPackBitmap, subdivisions).
- analyze_bitmap on synthetic bitmaps with known shapes (guarded on cv2 import).
- CLI execution on synthetic images and missing input file error handling.
"""

from __future__ import annotations

import ast
import importlib.util
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest

TOOL_PATH = Path(__file__).resolve().parents[1] / "make_roompack.py"

HAS_CV2 = importlib.util.find_spec("cv2") is not None
if HAS_CV2:
    try:
        import cv2  # noqa: F401
    except Exception:
        HAS_CV2 = False


def load_pure_roompack():
    """Extract and compile pure AST nodes from make_roompack.py without importing cv2."""
    source = TOOL_PATH.read_text(encoding="utf-8")
    tree = ast.parse(source)
    pure_nodes = [
        node for node in tree.body
        if not (isinstance(node, ast.Import) and any(alias.name == "cv2" for alias in node.names))
        and not (isinstance(node, ast.FunctionDef) and node.name in ("analyze_bitmap", "main"))
    ]
    mod = ast.Module(body=pure_nodes, type_ignores=[])
    code = compile(mod, filename=str(TOOL_PATH), mode="exec")
    namespace: dict[str, object] = {}
    exec(code, namespace)
    return namespace


def create_grayscale_bmp(width: int, height: int, pixels: list[list[int]]) -> bytes:
    """Generate a standard uncompressed 8-bit grayscale BMP image in memory."""
    row_bytes = width
    padding = (4 - (row_bytes % 4)) % 4
    pixel_data_size = (row_bytes + padding) * height
    palette_size = 256 * 4
    offset = 14 + 40 + palette_size
    file_size = offset + pixel_data_size

    header = struct.pack("<2sIHHI", b"BM", file_size, 0, 0, offset)
    dib = struct.pack(
        "<IIIHHIIIIII",
        40,          # DIB header size
        width,
        height,
        1,           # Color planes
        8,           # Bits per pixel (8-bit grayscale)
        0,           # BI_RGB (uncompressed)
        pixel_data_size,
        2835,        # Horizontal resolution (~72 DPI)
        2835,        # Vertical resolution (~72 DPI)
        256,         # Colors in palette
        256,         # Important colors
    )
    palette = bytearray()
    for i in range(256):
        palette.extend((i, i, i, 0))

    pixel_bytes = bytearray()
    # BMP scanlines are stored bottom-to-top
    for y in reversed(range(height)):
        for x in range(width):
            pixel_bytes.append(pixels[y][x])
        pixel_bytes.extend(b"\x00" * padding)

    return bytes(header + dib + palette + pixel_bytes)


class TestPureRoompack(unittest.TestCase):
    """Tests for pure helpers, dataclasses, and constants in make_roompack.py."""

    def setUp(self):
        self.pure = load_pure_roompack()

    def test_constants(self):
        self.assertEqual(self.pure["SUBDIVISIONS"], 128)
        self.assertEqual(self.pure["MIN_VALUE"], 1)
        self.assertEqual(self.pure["MAX_VALUE"], 256)
        self.assertEqual(self.pure["MAX_VALUE"] % self.pure["SUBDIVISIONS"], 0)

    def test_box2_dataclass(self):
        box_cls = self.pure["Box2"]
        box = box_cls(left=2, bottom=4, right=10, top=12)
        self.assertEqual(box.left, 2)
        self.assertEqual(box.bottom, 4)
        self.assertEqual(box.right, 10)
        self.assertEqual(box.top, 12)

    def test_roompack_bitmap_dataclass(self):
        box_cls = self.pure["Box2"]
        bitmap_cls = self.pure["RoomPackBitmap"]
        box = box_cls(left=0, bottom=0, right=5, top=5)
        bitmap = bitmap_cls(width=64, height=64, rooms=[box])
        self.assertEqual(bitmap.width, 64)
        self.assertEqual(bitmap.height, 64)
        self.assertEqual(len(bitmap.rooms), 1)
        self.assertEqual(bitmap.rooms[0], box)


class TestMakeRoompackExecution(unittest.TestCase):
    """Tests invoking make_roompack.py or calling analyze_bitmap."""

    def test_missing_input_file_fails_with_nonzero_error(self):
        """A missing input file fails with a clear non-zero exit code and error output."""
        nonexistent_path = "/tmp/solreign_nonexistent_roompack_fixture.bmp"
        result = subprocess.run(
            [sys.executable, str(TOOL_PATH), nonexistent_path],
            capture_output=True,
            text=True,
        )
        self.assertNotEqual(result.returncode, 0, "CLI must exit non-zero for missing input")
        error_msg = result.stderr or result.stdout
        self.assertTrue(len(error_msg.strip()) > 0, "Expected non-empty error message")

    @unittest.skipUnless(HAS_CV2, "cv2 required for bitmap analysis")
    def test_analyze_bitmap_synthetic_image(self):
        """Packing a synthetic greyscale image finds exact bounding boxes for known shapes."""
        spec = importlib.util.spec_from_file_location("make_roompack", TOOL_PATH)
        assert spec is not None and spec.loader is not None
        make_roompack = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(make_roompack)

        width, height = 30, 20
        pixels = [[0 for _ in range(width)] for _ in range(height)]
        # Draw a rectangle from x=5..14 (w=10), y=4..11 (h=8) with intensity 120
        for y in range(4, 12):
            for x in range(5, 15):
                pixels[y][x] = 120

        bmp_bytes = create_grayscale_bmp(width, height, pixels)
        with tempfile.NamedTemporaryFile(suffix=".bmp") as f:
            f.write(bmp_bytes)
            f.flush()

            result = make_roompack.analyze_bitmap(f.name, centered=False, offset_x=0, offset_y=0)
            self.assertEqual(result.width, width)
            self.assertEqual(result.height, height)
            self.assertEqual(len(result.rooms), 1)
            room = result.rooms[0]
            self.assertEqual((room.left, room.bottom, room.right, room.top), (5, 4, 15, 12))

    @unittest.skipUnless(HAS_CV2, "cv2 required for bitmap analysis")
    def test_analyze_bitmap_with_center_and_offset(self):
        """Centered and offset coordinates correctly adjust bounding box positions."""
        spec = importlib.util.spec_from_file_location("make_roompack", TOOL_PATH)
        assert spec is not None and spec.loader is not None
        make_roompack = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(make_roompack)

        width, height = 20, 20
        pixels = [[0 for _ in range(width)] for _ in range(height)]
        for y in range(5, 15):
            for x in range(5, 15):
                pixels[y][x] = 200

        bmp_bytes = create_grayscale_bmp(width, height, pixels)
        with tempfile.NamedTemporaryFile(suffix=".bmp") as f:
            f.write(bmp_bytes)
            f.flush()

            # Centered: offset_x -= 10, offset_y -= 10 -> [5..15] becomes [-5..5]
            result_centered = make_roompack.analyze_bitmap(f.name, centered=True, offset_x=0, offset_y=0)
            room = result_centered.rooms[0]
            self.assertEqual((room.left, room.bottom, room.right, room.top), (-5, -5, 5, 5))

            # Offset by (100, 200)
            result_offset = make_roompack.analyze_bitmap(f.name, centered=False, offset_x=100, offset_y=200)
            room_off = result_offset.rooms[0]
            self.assertEqual((room_off.left, room_off.bottom, room_off.right, room_off.top), (105, 205, 115, 215))

    @unittest.skipUnless(HAS_CV2, "cv2 required for bitmap analysis")
    def test_cli_output_format(self):
        """CLI prints size, YAML room list, and summary line for synthetic input."""
        width, height = 10, 10
        pixels = [[0 for _ in range(width)] for _ in range(height)]
        for y in range(2, 6):
            for x in range(3, 8):
                pixels[y][x] = 150

        bmp_bytes = create_grayscale_bmp(width, height, pixels)
        with tempfile.NamedTemporaryFile(suffix=".bmp") as f:
            f.write(bmp_bytes)
            f.flush()

            result = subprocess.run(
                [sys.executable, str(TOOL_PATH), f.name],
                capture_output=True,
                text=True,
            )
            self.assertEqual(result.returncode, 0)
            self.assertIn("size: 10,10", result.stdout)
            self.assertIn("rooms:", result.stdout)
            self.assertIn("- 3,2,8,6", result.stdout)
            self.assertIn("Generated 1 rooms.", result.stdout)


if __name__ == "__main__":
    unittest.main()
