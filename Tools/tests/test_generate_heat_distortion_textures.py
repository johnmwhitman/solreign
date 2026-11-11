"""Determinism tests for Tools/generate_heat_distortion_textures.py.

Verifies that fixed seed/parameters produce output images of the expected dimensions
and mode, byte-identical across two runs, using synthetic fixtures only.
"""
from __future__ import annotations

import io
from pathlib import Path
import tempfile
import unittest
from unittest import mock

from PIL import Image

import Tools.generate_heat_distortion_textures as heat_distortion


class TestGenerateHeatDistortionTextures(unittest.TestCase):
    def test_generate_noise_image_determinism_and_format(self) -> None:
        """Fixed seed/params produce an RGBA 512x512 image that is byte-identical across runs."""
        with tempfile.TemporaryDirectory() as tmp_dir:
            run1_path = Path(tmp_dir) / "noise_run1.png"
            run2_path = Path(tmp_dir) / "noise_run2.png"

            with mock.patch("sys.stdout", new_callable=io.StringIO):
                heat_distortion.generate_noise_image(str(run1_path))
                heat_distortion.generate_noise_image(str(run2_path))

            self.assertTrue(run1_path.exists())
            self.assertTrue(run2_path.exists())

            with Image.open(run1_path) as img:
                self.assertEqual(img.size, (512, 512))
                self.assertEqual(img.mode, "RGBA")

            run1_bytes = run1_path.read_bytes()
            run2_bytes = run2_path.read_bytes()

            self.assertGreater(len(run1_bytes), 0)
            self.assertEqual(run1_bytes, run2_bytes)

    def test_generate_soft_circle_texture_determinism_and_format(self) -> None:
        """Fixed params produce an RGBA 64x64 image that is byte-identical across runs."""
        with tempfile.TemporaryDirectory() as tmp_dir:
            run1_path = Path(tmp_dir) / "circle_run1.png"
            run2_path = Path(tmp_dir) / "circle_run2.png"

            with mock.patch("sys.stdout", new_callable=io.StringIO):
                heat_distortion.generate_soft_circle_texture(str(run1_path))
                heat_distortion.generate_soft_circle_texture(str(run2_path))

            self.assertTrue(run1_path.exists())
            self.assertTrue(run2_path.exists())

            with Image.open(run1_path) as img:
                self.assertEqual(img.size, (64, 64))
                self.assertEqual(img.mode, "RGBA")

            run1_bytes = run1_path.read_bytes()
            run2_bytes = run2_path.read_bytes()

            self.assertGreater(len(run1_bytes), 0)
            self.assertEqual(run1_bytes, run2_bytes)


if __name__ == "__main__":
    unittest.main()
