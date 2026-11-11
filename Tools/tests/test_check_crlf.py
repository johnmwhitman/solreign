#!/usr/bin/env python3
"""Unit tests for Tools/check_crlf.py using synthetic temp files only."""

import io
import os
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from pathlib import Path
from unittest import mock

# Ensure Tools package or script can be imported regardless of execution path
REPO_ROOT = Path(__file__).resolve().parents[2]
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

from Tools import check_crlf


class TestCheckCrlf(unittest.TestCase):
    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.dir_path = Path(self.temp_dir.name)

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def create_temp_file(self, filename: str, content: bytes) -> str:
        file_path = self.dir_path / filename
        file_path.write_bytes(content)
        return str(file_path)

    def test_is_file_crlf_lf_only_passes(self) -> None:
        """LF-only line endings should not be detected as CRLF."""
        path = self.create_temp_file("lf.txt", b"hello\nworld\n")
        self.assertFalse(check_crlf.is_file_crlf(path))

    def test_is_file_crlf_crlf_detected(self) -> None:
        """CRLF line endings should be detected."""
        path = self.create_temp_file("crlf.txt", b"hello\r\nworld\r\n")
        self.assertTrue(check_crlf.is_file_crlf(path))

    def test_is_file_crlf_mixed_endings_detected(self) -> None:
        """Files with mixed LF and CRLF line endings should be detected as containing CRLF."""
        path = self.create_temp_file("mixed_lf_first.txt", b"hello\nworld\r\nfoo\n")
        self.assertTrue(check_crlf.is_file_crlf(path))

        path2 = self.create_temp_file("mixed_crlf_first.txt", b"hello\r\nworld\n")
        self.assertTrue(check_crlf.is_file_crlf(path2))

    def test_is_file_crlf_cr_only_passes(self) -> None:
        """Isolated CR (Mac-style / carriage return without LF) is not CRLF."""
        path = self.create_temp_file("cr.txt", b"hello\rworld\r")
        self.assertFalse(check_crlf.is_file_crlf(path))

    def test_is_file_crlf_empty_file_handled(self) -> None:
        """Empty files should be handled cleanly without error and pass."""
        path = self.create_temp_file("empty.txt", b"")
        self.assertFalse(check_crlf.is_file_crlf(path))

    def test_is_file_crlf_binary_file_handled(self) -> None:
        """Binary files without \\r\\n sequence should pass, while binary with \\r\\n is detected."""
        # Binary data without \r\n (e.g. random binary bytes)
        binary_no_crlf = self.create_temp_file("binary_clean.bin", bytes(range(1, 10)) + bytes(range(14, 255)))
        self.assertFalse(check_crlf.is_file_crlf(binary_no_crlf))

        # Binary data containing \r\n byte sequence
        binary_with_crlf = self.create_temp_file("binary_crlf.bin", b"\x00\x01\x02\r\n\x03\x04")
        self.assertTrue(check_crlf.is_file_crlf(binary_with_crlf))

    def test_main_lf_only_passes(self) -> None:
        """main() should return 0 and produce no error output when all files are LF-only."""
        file1 = self.create_temp_file("file1.txt", b"line 1\nline 2\n")
        file2 = self.create_temp_file("file2.txt", b"single line\n")

        with mock.patch.object(check_crlf, "get_text_files", return_value=[file1, file2]):
            stdout = io.StringIO()
            with redirect_stdout(stdout):
                exit_code = check_crlf.main()

            self.assertEqual(exit_code, 0)
            self.assertEqual(stdout.getvalue(), "")

    def test_main_crlf_detected_and_named(self) -> None:
        """main() should return 1 and output the file name in the GitHub error annotation when CRLF is detected."""
        file_lf = self.create_temp_file("good.txt", b"fine\n")
        file_crlf = self.create_temp_file("bad_crlf.txt", b"bad\r\n")

        with mock.patch.object(check_crlf, "get_text_files", return_value=[file_lf, file_crlf]):
            stdout = io.StringIO()
            with redirect_stdout(stdout):
                exit_code = check_crlf.main()

            self.assertEqual(exit_code, 1)
            output = stdout.getvalue()
            # File name must be named in the error message
            self.assertIn(f"::error file={file_crlf}", output)
            self.assertIn(f"The file '{file_crlf}' was committed with CRLF new lines", output)
            self.assertNotIn("good.txt", output)

    def test_main_mixed_endings_detected_and_named(self) -> None:
        """main() should detect and name files with mixed LF/CRLF endings."""
        file_mixed = self.create_temp_file("mixed.txt", b"lf line\ncr lf line\r\n")

        with mock.patch.object(check_crlf, "get_text_files", return_value=[file_mixed]):
            stdout = io.StringIO()
            with redirect_stdout(stdout):
                exit_code = check_crlf.main()

            self.assertEqual(exit_code, 1)
            output = stdout.getvalue()
            self.assertIn(f"::error file={file_mixed}", output)
            self.assertIn(f"The file '{file_mixed}' was committed with CRLF new lines", output)

    def test_main_empty_and_binary_files_handled(self) -> None:
        """main() should handle empty files and binary files without crashing."""
        empty_file = self.create_temp_file("empty.txt", b"")
        binary_file = self.create_temp_file("data.bin", b"\x00\xff\xfe\x01\x02\n")

        with mock.patch.object(check_crlf, "get_text_files", return_value=[empty_file, binary_file]):
            stdout = io.StringIO()
            with redirect_stdout(stdout):
                exit_code = check_crlf.main()

            self.assertEqual(exit_code, 0)
            self.assertEqual(stdout.getvalue(), "")


if __name__ == "__main__":
    unittest.main()
