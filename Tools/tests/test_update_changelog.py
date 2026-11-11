import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
import yaml

SCRIPT_PATH = str(Path(__file__).resolve().parents[1] / "update_changelog.py")


def run_update_changelog(changelog_file: str, parts_dir: str, category: str | None = None) -> subprocess.CompletedProcess:
    cmd = [sys.executable, SCRIPT_PATH, changelog_file, parts_dir]
    if category is not None:
        cmd.extend(["--category", category])
    return subprocess.run(cmd, capture_output=True, text=True)


class TestUpdateChangelog(unittest.TestCase):
    def setUp(self):
        self.temp_dir = tempfile.TemporaryDirectory()
        self.changelog_path = os.path.join(self.temp_dir.name, "Changelog.yml")
        self.parts_dir = os.path.join(self.temp_dir.name, "parts")
        os.makedirs(self.parts_dir, exist_ok=True)

    def tearDown(self):
        self.temp_dir.cleanup()

    def test_append_new_entry(self):
        initial_data = {
            "Entries": [
                {
                    "author": "Alice",
                    "changes": [{"message": "Initial commit", "type": "Add"}],
                    "id": 1,
                    "time": "2026-01-01T00:00:00+00:00",
                    "url": "https://example.com/pr/1",
                }
            ]
        }
        with open(self.changelog_path, "w", encoding="utf-8-sig") as f:
            yaml.safe_dump(initial_data, f)

        part_data = {
            "author": "Bob",
            "changes": [{"message": "Fixed an issue", "type": "Fix"}],
            "url": "https://example.com/pr/2",
        }
        part_path = os.path.join(self.parts_dir, "part1.yml")
        with open(part_path, "w", encoding="utf-8-sig") as f:
            yaml.safe_dump(part_data, f)

        proc = run_update_changelog(self.changelog_path, self.parts_dir)
        self.assertEqual(proc.returncode, 0, f"Script failed: {proc.stderr}")

        with open(self.changelog_path, "r", encoding="utf-8-sig") as f:
            updated_data = yaml.safe_load(f)

        self.assertEqual(len(updated_data["Entries"]), 2)
        new_entry = updated_data["Entries"][1]
        self.assertEqual(new_entry["author"], "Bob")
        self.assertEqual(new_entry["id"], 2)
        self.assertEqual(new_entry["changes"], [{"message": "Fixed an issue", "type": "Fix"}])
        self.assertEqual(new_entry["url"], "https://example.com/pr/2")
        self.assertIn("time", new_entry)
        self.assertFalse(os.path.exists(part_path), "Processed part file should be removed")

    def test_entries_sorted_by_id(self):
        initial_data = {
            "Entries": [
                {
                    "author": "Alice",
                    "changes": [{"message": "First", "type": "Add"}],
                    "id": 10,
                    "time": "2026-01-01T00:00:00+00:00",
                },
                {
                    "author": "Bob",
                    "changes": [{"message": "Second", "type": "Fix"}],
                    "id": 20,
                    "time": "2026-01-02T00:00:00+00:00",
                },
            ]
        }
        with open(self.changelog_path, "w", encoding="utf-8-sig") as f:
            yaml.safe_dump(initial_data, f)

        for i in range(1, 4):
            part_data = {
                "author": f"Author{i}",
                "changes": [{"message": f"Change {i}", "type": "Tweak"}],
            }
            part_path = os.path.join(self.parts_dir, f"part_{i}.yml")
            with open(part_path, "w", encoding="utf-8-sig") as f:
                yaml.safe_dump(part_data, f)

        proc = run_update_changelog(self.changelog_path, self.parts_dir)
        self.assertEqual(proc.returncode, 0, f"Script failed: {proc.stderr}")

        with open(self.changelog_path, "r", encoding="utf-8-sig") as f:
            updated_data = yaml.safe_load(f)

        ids = [entry["id"] for entry in updated_data["Entries"]]
        self.assertEqual(ids, sorted(ids))
        self.assertEqual(ids, [10, 20, 21, 22, 23])

    def test_overflow_beyond_cap_is_trimmed(self):
        # MAX_ENTRIES is 500 in update_changelog.py
        total_initial = 499
        initial_entries = [
            {
                "author": f"User{i}",
                "changes": [{"message": f"Initial change {i}", "type": "Tweak"}],
                "id": i,
                "time": "2026-01-01T00:00:00+00:00",
            }
            for i in range(1, total_initial + 1)
        ]
        initial_data = {"Entries": initial_entries}
        with open(self.changelog_path, "w", encoding="utf-8-sig") as f:
            yaml.safe_dump(initial_data, f)

        for i in range(1, 4):
            part_data = {
                "author": f"NewUser{i}",
                "changes": [{"message": f"New change {i}", "type": "Add"}],
            }
            part_path = os.path.join(self.parts_dir, f"new_{i}.yml")
            with open(part_path, "w", encoding="utf-8-sig") as f:
                yaml.safe_dump(part_data, f)

        proc = run_update_changelog(self.changelog_path, self.parts_dir)
        self.assertEqual(proc.returncode, 0, f"Script failed: {proc.stderr}")

        with open(self.changelog_path, "r", encoding="utf-8-sig") as f:
            updated_data = yaml.safe_load(f)

        entries = updated_data["Entries"]
        self.assertEqual(len(entries), 500, "Entries count should be capped at MAX_ENTRIES (500)")
        # Total was 499 + 3 = 502, overflow was 2, oldest 2 entries (ids 1 and 2) removed
        self.assertEqual(entries[0]["id"], 3)
        self.assertEqual(entries[-1]["id"], 502)

    def test_pre_existing_entries_survive_intact(self):
        pre_existing = [
            {
                "author": "Contributor_1",
                "changes": [
                    {"message": "Feature alpha", "type": "Add"},
                    {"message": "Bugfix beta", "type": "Fix"},
                ],
                "id": 101,
                "time": "2026-03-15T12:00:00+00:00",
                "url": "https://github.com/space-wizards/space-station-14/pull/101",
            },
            {
                "author": "Contributor_2",
                "changes": [{"message": "Balance gamma", "type": "Tweak"}],
                "id": 102,
                "time": "2026-03-16T14:30:00+00:00",
                "url": "https://github.com/space-wizards/space-station-14/pull/102",
            },
        ]
        extra_key_value = "CustomMetadata"
        initial_data = {
            "Entries": list(pre_existing),
            "CustomHeader": extra_key_value,
        }
        with open(self.changelog_path, "w", encoding="utf-8-sig") as f:
            yaml.safe_dump(initial_data, f)

        part_data = {
            "author": "Contributor_3",
            "changes": [{"message": "Feature delta", "type": "Add"}],
            "url": "https://github.com/space-wizards/space-station-14/pull/103",
        }
        with open(os.path.join(self.parts_dir, "part.yml"), "w", encoding="utf-8-sig") as f:
            yaml.safe_dump(part_data, f)

        proc = run_update_changelog(self.changelog_path, self.parts_dir)
        self.assertEqual(proc.returncode, 0, f"Script failed: {proc.stderr}")

        with open(self.changelog_path, "r", encoding="utf-8-sig") as f:
            updated_data = yaml.safe_load(f)

        self.assertEqual(updated_data["CustomHeader"], extra_key_value)
        entries = updated_data["Entries"]
        self.assertEqual(len(entries), 3)
        self.assertEqual(entries[0], pre_existing[0])
        self.assertEqual(entries[1], pre_existing[1])

    def test_malformed_fixture_does_not_corrupt_original_file(self):
        initial_content = (
            "Entries:\n"
            "- author: Alice\n"
            "  changes:\n"
            "  - message: Stable entry\n"
            "    type: Add\n"
            "  id: 1\n"
            "  time: '2026-01-01T00:00:00+00:00'\n"
            "  url: null\n"
        )
        with open(self.changelog_path, "w", encoding="utf-8-sig") as f:
            f.write(initial_content)

        bad_part = os.path.join(self.parts_dir, "bad_part.yml")
        with open(bad_part, "w", encoding="utf-8-sig") as f:
            f.write("invalid: yaml: : [unbalanced bracket")

        proc = run_update_changelog(self.changelog_path, self.parts_dir)
        self.assertNotEqual(proc.returncode, 0, "Script should fail when encountering malformed part file")

        with open(self.changelog_path, "r", encoding="utf-8-sig") as f:
            current_content = f.read()

        self.assertEqual(current_content, initial_content, "Original changelog file must remain uncorrupted and intact")


if __name__ == "__main__":
    unittest.main()
