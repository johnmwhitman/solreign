import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from PIL import Image

REPO_ROOT = Path(__file__).resolve().parents[2]
ICONSMOOTH_PATH = REPO_ROOT / "Tools" / "iconsmooth.py"
ICONSMOOTH_INV_PATH = REPO_ROOT / "Tools" / "iconsmooth_inv.py"


class TestIconSmoothCLI(unittest.TestCase):
    def test_iconsmooth_success(self):
        with tempfile.TemporaryDirectory() as td:
            input_png = os.path.join(td, "synthetic_in.png")
            # Create a tiny synthetic RGBA PNG (e.g. 32x32)
            img = Image.new("RGBA", (32, 32), (255, 0, 0, 255))
            img.save(input_png)

            out_prefix = os.path.join(td, "out_")
            res = subprocess.run(
                [sys.executable, str(ICONSMOOTH_PATH), input_png, "32", "tg", out_prefix],
                capture_output=True,
                text=True,
            )

            self.assertEqual(res.returncode, 0, f"iconsmooth.py failed with stderr: {res.stderr}")
            self.assertTrue(
                os.path.exists(os.path.join(td, "out_full.png")),
                "Expected output file out_full.png not found",
            )
            for state in range(8):
                self.assertTrue(
                    os.path.exists(os.path.join(td, f"out_{state}.png")),
                    f"Expected output file out_{state}.png not found",
                )

    def test_iconsmooth_inv_success(self):
        with tempfile.TemporaryDirectory() as td:
            # First create the required 0..7 input slices
            prefix = os.path.join(td, "slice_")
            for i in range(8):
                slice_img = Image.new("RGBA", (64, 64), (0, 255, 0, 255))
                slice_img.save(f"{prefix}{i}.png")

            output_png = os.path.join(td, "reconstructed.png")
            res = subprocess.run(
                [sys.executable, str(ICONSMOOTH_INV_PATH), prefix, "32", "tg", output_png],
                capture_output=True,
                text=True,
            )

            self.assertEqual(res.returncode, 0, f"iconsmooth_inv.py failed with stderr: {res.stderr}")
            self.assertTrue(
                os.path.exists(output_png),
                "Expected output file reconstructed.png not found",
            )

    def test_iconsmooth_missing_input(self):
        with tempfile.TemporaryDirectory() as td:
            missing_input = os.path.join(td, "does_not_exist.png")
            out_prefix = os.path.join(td, "out_")
            res = subprocess.run(
                [sys.executable, str(ICONSMOOTH_PATH), missing_input, "32", "tg", out_prefix],
                capture_output=True,
                text=True,
            )

            self.assertNotEqual(res.returncode, 0)
            combined_output = res.stdout + res.stderr
            self.assertTrue(
                "FileNotFoundError" in combined_output or "No such file or directory" in combined_output,
                f"Expected clear file not found message, got:\n{combined_output}",
            )

    def test_iconsmooth_inv_missing_input(self):
        with tempfile.TemporaryDirectory() as td:
            missing_prefix = os.path.join(td, "missing_prefix_")
            output_png = os.path.join(td, "out.png")
            res = subprocess.run(
                [sys.executable, str(ICONSMOOTH_INV_PATH), missing_prefix, "32", "tg", output_png],
                capture_output=True,
                text=True,
            )

            self.assertNotEqual(res.returncode, 0)
            combined_output = res.stdout + res.stderr
            self.assertTrue(
                "FileNotFoundError" in combined_output or "No such file or directory" in combined_output,
                f"Expected clear file not found message, got:\n{combined_output}",
            )


if __name__ == "__main__":
    unittest.main()
