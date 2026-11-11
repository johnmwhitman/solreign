#!/usr/bin/env python3
"""
Unit tests for Tools/actions_changelogs_since_last_run.py.

Tests cover changelog diffing by entry ID:
1. Entries present only in the current changelog are returned.
2. Entries present in both (on/before cutoff) are excluded.
3. An empty or missing old changelog returns all entries from current (first-run behavior).
4. Entries are matched by ID, not position or timestamp.
5. Debug-mode old-file workflow using synthetic temp fixtures.
"""

import sys
import unittest
import io
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest import mock

import yaml

# Ensure repository root is on sys.path
REPO_ROOT = Path(__file__).resolve().parents[2]
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

from Tools import actions_changelogs_since_last_run as changelog_tool


class TestActionsChangelogsDiff(unittest.TestCase):
    def test_diff_changelog_returns_only_new_entries(self):
        """Entries present only in the current changelog are returned."""
        old = {
            "Entries": [
                {"id": 1, "author": "Alice", "changes": [{"type": "Fix", "message": "Fixed bug A"}]},
                {"id": 2, "author": "Bob", "changes": [{"type": "Add", "message": "Added feature B"}]},
            ]
        }
        cur = {
            "Entries": [
                {"id": 1, "author": "Alice", "changes": [{"type": "Fix", "message": "Fixed bug A"}]},
                {"id": 2, "author": "Bob", "changes": [{"type": "Add", "message": "Added feature B"}]},
                {"id": 3, "author": "Charlie", "changes": [{"type": "Tweak", "message": "Tweaked item C"}]},
            ]
        }

        diff = list(changelog_tool.diff_changelog(old, cur))

        self.assertEqual(len(diff), 1)
        self.assertEqual(diff[0]["id"], 3)
        self.assertEqual(diff[0]["author"], "Charlie")

    def test_diff_changelog_excludes_entries_present_in_both(self):
        """Entries present in both (on/before the cutoff) are excluded."""
        old = {
            "Entries": [
                {"id": 10, "author": "Alice", "changes": []},
                {"id": 11, "author": "Bob", "changes": []},
            ]
        }
        cur = {
            "Entries": [
                {"id": 10, "author": "Alice", "changes": []},
                {"id": 11, "author": "Bob", "changes": []},
            ]
        }

        diff = list(changelog_tool.diff_changelog(old, cur))

        self.assertEqual(diff, [])

    def test_diff_changelog_empty_old_returns_everything(self):
        """An empty old changelog returns all entries from current (first-run behavior)."""
        old = {"Entries": []}
        cur = {
            "Entries": [
                {"id": 101, "author": "DevA", "changes": [{"type": "Add", "message": "New feature 1"}]},
                {"id": 102, "author": "DevB", "changes": [{"type": "Fix", "message": "Bugfix 2"}]},
            ]
        }

        diff = list(changelog_tool.diff_changelog(old, cur))

        self.assertEqual([e["id"] for e in diff], [101, 102])

    def test_diff_changelog_matches_by_id_not_position_or_timestamp(self):
        """Entries are matched strictly by ID, regardless of ordering or timestamp."""
        old = {
            "Entries": [
                {"id": 5, "time": "2026-01-01T00:00:00Z", "author": "AuthorA", "changes": []},
                {"id": 2, "time": "2026-01-03T00:00:00Z", "author": "AuthorB", "changes": []},
            ]
        }
        # In cur, entry 2 appears earlier in list than 5, with modified timestamp/position,
        # plus entry 1 has an older timestamp and entry 6 has a newer timestamp.
        cur = {
            "Entries": [
                {"id": 1, "time": "2025-12-31T00:00:00Z", "author": "AuthorC", "changes": []},
                {"id": 2, "time": "2026-01-05T00:00:00Z", "author": "AuthorB", "changes": []},
                {"id": 5, "time": "2026-01-01T00:00:00Z", "author": "AuthorA", "changes": []},
                {"id": 6, "time": "2026-01-06T00:00:00Z", "author": "AuthorD", "changes": []},
            ]
        }

        diff = list(changelog_tool.diff_changelog(old, cur))

        self.assertEqual([e["id"] for e in diff], [1, 6])


class TestActionsChangelogsDebugMode(unittest.TestCase):
    def test_debug_mode_synthetic_fixtures_diff_and_dump(self):
        """Debug mode loads old and current synthetic YAML files and writes formatted markdown."""
        with TemporaryDirectory() as temp_dir:
            temp_path = Path(temp_dir)
            old_file = temp_path / "Old.yml"
            cur_file = temp_path / "Changelog.yml"
            dump_file = temp_path / "DiscordDebug.md"

            old_data = {
                "Entries": [
                    {
                        "id": 100,
                        "author": "Alice",
                        "labels": [],
                        "url": "https://github.com/space-wizards/space-station-14/pull/100",
                        "changes": [{"type": "Add", "message": "Base feature"}],
                    }
                ]
            }
            cur_data = {
                "Entries": [
                    {
                        "id": 100,
                        "author": "Alice",
                        "labels": [],
                        "url": "https://github.com/space-wizards/space-station-14/pull/100",
                        "changes": [{"type": "Add", "message": "Base feature"}],
                    },
                    {
                        "id": 101,
                        "author": "Bob",
                        "labels": [changelog_tool.EXPERIMENTAL_LABEL],
                        "url": "https://github.com/space-wizards/space-station-14/pull/101",
                        "changes": [{"type": "Fix", "message": "Experimental fix"}],
                    },
                ]
            }

            old_file.write_text(yaml.safe_dump(old_data), encoding="utf-8-sig")
            cur_file.write_text(yaml.safe_dump(cur_data), encoding="utf-8-sig")

            with mock.patch.object(changelog_tool, "DEBUG", True), \
                 mock.patch.object(changelog_tool, "DEBUG_CHANGELOG_FILE_OLD", old_file), \
                 mock.patch.object(changelog_tool, "CHANGELOG_FILE", str(cur_file)), \
                 mock.patch.object(changelog_tool, "DEBUG_DISCORD_DUMP_FILE", dump_file), \
                 mock.patch("sys.stdout", new=io.StringIO()):
                changelog_tool.main()

            self.assertTrue(dump_file.exists())
            dump_content = dump_file.read_text(encoding="utf-8")
            self.assertIn("**Bob** updated:", dump_content)
            self.assertIn("Experimental fix", dump_content)
            self.assertNotIn("**Alice** updated:", dump_content)

    def test_debug_mode_first_run_empty_old_changelog(self):
        """Debug mode with empty old changelog emits all entries from current changelog."""
        with TemporaryDirectory() as temp_dir:
            temp_path = Path(temp_dir)
            old_file = temp_path / "Old.yml"
            cur_file = temp_path / "Changelog.yml"
            dump_file = temp_path / "DiscordDebug.md"

            old_data = {"Entries": []}
            cur_data = {
                "Entries": [
                    {
                        "id": 50,
                        "author": "InitialDev",
                        "labels": [],
                        "url": None,
                        "changes": [{"type": "Add", "message": "First entry ever"}],
                    }
                ]
            }

            old_file.write_text(yaml.safe_dump(old_data), encoding="utf-8-sig")
            cur_file.write_text(yaml.safe_dump(cur_data), encoding="utf-8-sig")

            with mock.patch.object(changelog_tool, "DEBUG", True), \
                 mock.patch.object(changelog_tool, "DEBUG_CHANGELOG_FILE_OLD", old_file), \
                 mock.patch.object(changelog_tool, "CHANGELOG_FILE", str(cur_file)), \
                 mock.patch.object(changelog_tool, "DEBUG_DISCORD_DUMP_FILE", dump_file), \
                 mock.patch("sys.stdout", new=io.StringIO()):
                changelog_tool.main()

            self.assertTrue(dump_file.exists())
            dump_content = dump_file.read_text(encoding="utf-8")
            self.assertIn("**InitialDev** updated:", dump_content)
            self.assertIn("First entry ever", dump_content)


class TestActionsChangelogsFormatting(unittest.TestCase):
    def test_truncate_to_limit(self):
        """Verifies string truncation respects max character limits."""
        text = "Hello world from changelog"
        truncated = changelog_tool.truncate_to_limit(text, 15)
        self.assertLessEqual(len(truncated), 15)
        self.assertTrue(truncated.endswith(changelog_tool.TRUNCATION_SUFFIX))

    def test_create_change_line_with_and_without_url(self):
        """Verifies formatting of individual change lines with and without PR links."""
        line_with_url = changelog_tool.create_change_line(
            "🐛", "Fixed issue", "https://github.com/space-wizards/space-station-14/pull/1234"
        )
        self.assertEqual(line_with_url, "🐛 - Fixed issue ([#1234](https://github.com/space-wizards/space-station-14/pull/1234))\n")

        line_without_url = changelog_tool.create_change_line("🆕", "Added item", None)
        self.assertEqual(line_without_url, "🆕 - Added item\n")

    def test_changelog_entries_to_message_lines_groups_by_author(self):
        """Changelog entries are grouped by author in the generated message lines."""
        entries = [
            {
                "author": "Alice",
                "labels": [],
                "url": None,
                "changes": [{"type": "Fix", "message": "Fixed A"}],
            },
            {
                "author": "Alice",
                "labels": [changelog_tool.EXPERIMENTAL_LABEL],
                "url": "https://example.com/pr/1",
                "changes": [{"type": "Add", "message": "Added A2"}],
            },
            {
                "author": "Bob",
                "labels": [],
                "url": None,
                "changes": [{"type": "Tweak", "message": "Tweaked B"}],
            },
        ]
        lines = changelog_tool.changelog_entries_to_message_lines(entries)
        content = "".join(lines)
        self.assertIn("**Alice** updated:\n", content)
        self.assertIn("🐛 - Fixed A\n", content)
        self.assertIn(f"🆕{changelog_tool.EXPERIMENTAL_EMOJI} - Added A2 ([#1](https://example.com/pr/1))\n", content)
        self.assertIn("**Bob** updated:\n", content)
        self.assertIn("⚒️ - Tweaked B\n", content)

    def test_split_message_lines_splits_on_limit(self):
        """Splits message lines into chunks within the Discord character limit."""
        long_line = "a" * (changelog_tool.DISCORD_SPLIT_LIMIT - 10) + "\n"
        short_line = "b" * 20 + "\n"
        chunks = changelog_tool.split_message_lines([long_line, short_line])
        self.assertEqual(len(chunks), 2)
        self.assertEqual(chunks[0], [long_line])
        self.assertEqual(chunks[1], [short_line])


if __name__ == "__main__":
    unittest.main()
