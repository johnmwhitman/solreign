from __future__ import annotations

import json
import os
import subprocess
import tempfile
import time
import unittest
from pathlib import Path
from unittest import mock

from Tools._Solreign.UpstreamDrift import upstream_drift as upstream_module
from Tools._Solreign.UpstreamDrift.upstream_drift import (
    AuditError,
    AuditLimits,
    audit_repository,
    render_json,
    render_markdown,
)


class GitFixture:
    def __init__(self) -> None:
        self._temporary_directory = tempfile.TemporaryDirectory()
        self.path = Path(self._temporary_directory.name).resolve()
        self._run("init", "--initial-branch=main")
        self._run("config", "user.name", "SOLREIGN Test")
        self._run("config", "user.email", "solreign-test@example.invalid")
        self._run("config", "core.hooksPath", "/dev/null")

    def close(self) -> None:
        self._temporary_directory.cleanup()

    def commit(self, subject: str, files: dict[str, str]) -> str:
        for relative_path, contents in files.items():
            path = self.path / relative_path
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(contents, encoding="utf-8")
        self._run("add", "--all")
        self._run("commit", "--quiet", "-m", subject)
        return self._run("rev-parse", "HEAD").stdout.strip()

    def _run(self, *arguments: str) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            ["git", *arguments],
            cwd=self.path,
            check=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
        )


class UpstreamDriftAuditTests(unittest.TestCase):
    def setUp(self) -> None:
        self.fixture = GitFixture()

    def tearDown(self) -> None:
        self.fixture.close()

    def test_reports_exact_ahead_and_behind_counts(self) -> None:
        common = self.fixture.commit("common", {"README.md": "common\n"})
        self.fixture._run("branch", "fork")
        self.fixture._run("switch", "--quiet", "-c", "upstream")
        upstream_commit = self.fixture.commit(
            "upstream network change",
            {"Content.Shared/Network/Handshake.cs": "upstream\n"},
        )
        self.fixture._run("switch", "--quiet", "fork")
        base_commit = self.fixture.commit(
            "fork-only content",
            {"Resources/Prototypes/_Solreign/item.yml": "fork\n"},
        )

        report = audit_repository(
            self.fixture.path,
            "refs/heads/fork",
            "refs/heads/upstream",
        )

        self.assertEqual(report["schema_version"], 1)
        self.assertEqual(report["refs"]["base"]["commit"], base_commit)
        self.assertEqual(report["refs"]["upstream"]["commit"], upstream_commit)
        self.assertEqual(report["merge_bases"], [common])
        self.assertEqual(
            report["divergence"],
            {"base_only_commits": 1, "upstream_only_commits": 1},
        )

    def test_missing_upstream_ref_fails_closed_with_stable_code(self) -> None:
        self.fixture.commit("common", {"README.md": "common\n"})

        with self.assertRaises(AuditError) as raised:
            audit_repository(
                self.fixture.path,
                "refs/heads/main",
                "refs/heads/missing-upstream",
            )

        self.assertEqual(raised.exception.code, "UPSTREAM_REF_INVALID")

    def test_shallow_repository_fails_closed(self) -> None:
        self.fixture.commit("common", {"README.md": "common\n"})
        shallow_directory = tempfile.TemporaryDirectory()
        self.addCleanup(shallow_directory.cleanup)
        subprocess.run(
            [
                "git",
                "clone",
                "--quiet",
                "--depth=1",
                f"file://{self.fixture.path}",
                shallow_directory.name,
            ],
            check=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
        )

        with self.assertRaises(AuditError) as raised:
            head = subprocess.run(
                ["git", "rev-parse", "HEAD"],
                cwd=shallow_directory.name,
                check=True,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
            ).stdout.strip()
            audit_repository(Path(shallow_directory.name).resolve(), head, head)

        self.assertEqual(raised.exception.code, "INSUFFICIENT_HISTORY")

    def test_partial_clone_configuration_fails_closed(self) -> None:
        self.fixture.commit("common", {"README.md": "common\n"})
        self.fixture._run("config", "extensions.partialClone", "origin")

        with self.assertRaises(AuditError) as raised:
            audit_repository(
                self.fixture.path,
                "refs/heads/main",
                "refs/heads/main",
            )

        self.assertEqual(raised.exception.code, "INSUFFICIENT_HISTORY")

    def test_explicit_incomplete_history_mode_never_claims_complete_evidence(
        self,
    ) -> None:
        self.fixture.commit("common", {"README.md": "common\n"})
        shallow_directory = tempfile.TemporaryDirectory()
        self.addCleanup(shallow_directory.cleanup)
        subprocess.run(
            [
                "git",
                "clone",
                "--quiet",
                "--depth=1",
                f"file://{self.fixture.path}",
                shallow_directory.name,
            ],
            check=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
        )
        head = subprocess.run(
            ["git", "rev-parse", "HEAD"],
            cwd=shallow_directory.name,
            check=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
        ).stdout.strip()

        report = audit_repository(
            Path(shallow_directory.name).resolve(),
            head,
            head,
            allow_incomplete_history=True,
        )

        self.assertEqual(
            report["history"],
            {
                "shallow": True,
                "completeness_claim": "not-made",
                "comparison_basis": "reachable-local-objects-only",
            },
        )
        self.assertTrue(report["review"]["required"])
        self.assertIn("INCOMPLETE_HISTORY", report["review"]["flags"])

    def test_incomplete_history_mode_reports_locally_reachable_divergence(
        self,
    ) -> None:
        self.fixture.commit("common", {"README.md": "common\n"})
        self.fixture._run("branch", "fork")
        self.fixture._run("switch", "--quiet", "-c", "upstream")
        self.fixture.commit("upstream", {"upstream.txt": "upstream\n"})
        self.fixture._run("switch", "--quiet", "fork")
        self.fixture.commit("fork", {"fork.txt": "fork\n"})
        shallow_directory = tempfile.TemporaryDirectory()
        self.addCleanup(shallow_directory.cleanup)
        subprocess.run(
            [
                "git",
                "clone",
                "--quiet",
                "--depth=2",
                "--branch",
                "fork",
                f"file://{self.fixture.path}",
                shallow_directory.name,
            ],
            check=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
        )
        subprocess.run(
            [
                "git",
                "-c",
                "protocol.file.allow=always",
                "fetch",
                "--quiet",
                "--depth=2",
                "origin",
                "upstream:refs/heads/upstream",
            ],
            cwd=shallow_directory.name,
            check=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
        )

        report = audit_repository(
            Path(shallow_directory.name).resolve(),
            "refs/heads/fork",
            "refs/heads/upstream",
            allow_incomplete_history=True,
        )

        self.assertEqual(
            report["divergence"],
            {"base_only_commits": 1, "upstream_only_commits": 1},
        )
        self.assertEqual([entry["path"] for entry in report["files"]], ["upstream.txt"])
        self.assertEqual(
            report["history"]["comparison_basis"],
            "reachable-local-objects-only",
        )

    def test_unrelated_histories_fail_closed(self) -> None:
        self.fixture.commit("local root", {"local.txt": "local\n"})
        unrelated = GitFixture()
        self.addCleanup(unrelated.close)
        unrelated.commit("other root", {"other.txt": "other\n"})
        self.fixture._run(
            "-c",
            "protocol.file.allow=always",
            "fetch",
            "--quiet",
            str(unrelated.path),
            "main:refs/heads/upstream",
        )

        with self.assertRaises(AuditError) as raised:
            audit_repository(
                self.fixture.path,
                "refs/heads/main",
                "refs/heads/upstream",
            )

        self.assertEqual(raised.exception.code, "NO_MERGE_BASE")

    def test_classifies_upstream_only_files_by_versioned_path_rules(self) -> None:
        self.fixture.commit("common", {"README.md": "common\n"})
        self.fixture._run("branch", "fork")
        self.fixture._run("switch", "--quiet", "-c", "upstream")
        network_commit = self.fixture.commit(
            "ordinary wording",
            {"Content.Shared/Network/Handshake.cs": "network\n"},
        )
        content_commit = self.fixture.commit(
            "another ordinary change",
            {"Resources/Prototypes/Entities/device.yml": "content\n"},
        )
        unknown_commit = self.fixture.commit(
            "no category hint",
            {"OddSurface/mystery.bin": "unknown\n"},
        )

        report = audit_repository(
            self.fixture.path,
            "refs/heads/fork",
            "refs/heads/upstream",
        )

        commits = {entry["commit"]: entry for entry in report["commits"]}
        self.assertEqual(
            commits[network_commit]["categories"],
            ["gameplay_source", "protocol_network"],
        )
        self.assertEqual(
            commits[content_commit]["categories"],
            ["content_maps_prototypes"],
        )
        self.assertEqual(
            commits[unknown_commit]["categories"],
            ["unclassified"],
        )
        files = {entry["path"]: entry for entry in report["files"]}
        self.assertEqual(
            files["Content.Shared/Network/Handshake.cs"]["matched_rule_ids"],
            ["content-runtime-source", "network-content-shared"],
        )
        self.assertEqual(
            report["classification"]["ruleset"],
            "solreign.upstream-drift-paths/v1",
        )
        self.assertEqual(report["classification"]["unclassified_file_count"], 1)

    def test_routes_generic_content_code_without_calling_it_safe(self) -> None:
        self.fixture.commit("common", {"README.md": "common\n"})
        self.fixture._run("branch", "fork")
        self.fixture._run("switch", "--quiet", "-c", "upstream")
        self.fixture.commit(
            "gameplay source",
            {"Content.Server/Speech/VoiceSystem.cs": "source\n"},
        )

        report = audit_repository(
            self.fixture.path,
            "refs/heads/fork",
            "refs/heads/upstream",
        )

        self.assertEqual(
            report["files"][0]["categories"],
            ["gameplay_source"],
        )
        self.assertEqual(
            report["files"][0]["matched_rule_ids"],
            ["content-runtime-source"],
        )

    def test_routes_resource_and_workspace_configuration(self) -> None:
        self.fixture.commit("common", {"README.md": "common\n"})
        self.fixture._run("branch", "fork")
        self.fixture._run("switch", "--quiet", "-c", "upstream")
        self.fixture.commit(
            "configuration",
            {
                "Resources/ConfigPresets/Build/development.toml": "resource\n",
                "flake.nix": "tooling\n",
            },
        )

        report = audit_repository(
            self.fixture.path,
            "refs/heads/fork",
            "refs/heads/upstream",
        )
        files = {entry["path"]: entry for entry in report["files"]}

        self.assertEqual(
            files["Resources/ConfigPresets/Build/development.toml"]["categories"],
            ["content_maps_prototypes"],
        )
        self.assertEqual(
            files["flake.nix"]["categories"],
            ["tooling_tests_docs"],
        )

    def test_routes_shared_database_project_to_both_runtime_and_schema_review(
        self,
    ) -> None:
        self.fixture.commit("common", {"README.md": "common\n"})
        self.fixture._run("branch", "fork")
        self.fixture._run("switch", "--quiet", "-c", "upstream")
        self.fixture.commit(
            "database contract",
            {
                "Content.Server.Database/Model.cs": "server\n",
                "Content.Shared.Database/LogType.cs": "shared\n",
            },
        )

        report = audit_repository(
            self.fixture.path,
            "refs/heads/fork",
            "refs/heads/upstream",
        )

        for entry in report["files"]:
            self.assertEqual(
                entry["categories"],
                ["database_schema", "gameplay_source"],
            )

    def test_routes_admin_and_access_paths_for_security_review(self) -> None:
        self.fixture.commit("common", {"README.md": "common\n"})
        self.fixture._run("branch", "fork")
        self.fixture._run("switch", "--quiet", "-c", "upstream")
        self.fixture.commit(
            "operator surfaces",
            {
                "Content.Server/Administration/AdminSystem.cs": "admin\n",
                "Content.Shared/Access/AccessLevel.cs": "access\n",
            },
        )

        report = audit_repository(
            self.fixture.path,
            "refs/heads/fork",
            "refs/heads/upstream",
        )

        for entry in report["files"]:
            self.assertEqual(
                entry["categories"],
                ["gameplay_source", "security_auth"],
            )

    def test_commit_limit_fails_instead_of_truncating(self) -> None:
        self.fixture.commit("common", {"README.md": "common\n"})
        self.fixture._run("branch", "fork")
        self.fixture._run("switch", "--quiet", "-c", "upstream")
        self.fixture.commit("one", {"one.txt": "one\n"})
        self.fixture.commit("two", {"two.txt": "two\n"})

        with self.assertRaises(AuditError) as raised:
            audit_repository(
                self.fixture.path,
                "refs/heads/fork",
                "refs/heads/upstream",
                limits=AuditLimits(max_commits=1, max_file_changes=100),
            )

        self.assertEqual(raised.exception.code, "LIMIT_EXCEEDED")

    def test_total_file_change_limit_fails_instead_of_truncating(self) -> None:
        self.fixture.commit("common", {"README.md": "common\n"})
        self.fixture._run("branch", "fork")
        self.fixture._run("switch", "--quiet", "-c", "upstream")
        self.fixture.commit(
            "two paths",
            {"one.txt": "one\n", "two.txt": "two\n"},
        )

        with self.assertRaises(AuditError) as raised:
            audit_repository(
                self.fixture.path,
                "refs/heads/fork",
                "refs/heads/upstream",
                limits=AuditLimits(max_file_changes=1),
            )

        self.assertEqual(raised.exception.code, "LIMIT_EXCEEDED")

    def test_audit_wide_deadline_fails_closed(self) -> None:
        self.fixture.commit("common", {"README.md": "common\n"})

        with self.assertRaises(AuditError) as raised:
            audit_repository(
                self.fixture.path,
                "refs/heads/main",
                "refs/heads/main",
                limits=AuditLimits(max_seconds=0.000001),
            )

        self.assertEqual(raised.exception.code, "AUDIT_TIMEOUT")

    def test_closed_git_pipes_still_report_the_audit_wide_deadline(self) -> None:
        fake_directory = tempfile.TemporaryDirectory()
        self.addCleanup(fake_directory.cleanup)
        fake_git = Path(fake_directory.name) / "git"
        fake_git.write_text(
            "#!/bin/sh\n"
            "exec 1>&-\n"
            "exec 2>&-\n"
            "/bin/sleep 2\n",
            encoding="utf-8",
        )
        fake_git.chmod(0o755)

        with mock.patch.object(upstream_module, "GIT_EXECUTABLE", fake_git):
            reader = upstream_module._GitReader(
                self.fixture.path,
                AuditLimits(max_seconds=0.5),
            )
            with self.assertRaises(AuditError) as raised:
                reader.run("rev-parse", "--show-toplevel")

        self.assertEqual(raised.exception.code, "AUDIT_TIMEOUT")

    def test_closed_git_pipes_report_the_per_child_deadline(self) -> None:
        fake_directory = tempfile.TemporaryDirectory()
        self.addCleanup(fake_directory.cleanup)
        fake_git = Path(fake_directory.name) / "git"
        fake_git.write_text(
            "#!/bin/sh\n"
            "exec 1>&-\n"
            "exec 2>&-\n"
            "/bin/sleep 2\n",
            encoding="utf-8",
        )
        fake_git.chmod(0o755)

        with mock.patch.object(upstream_module, "GIT_EXECUTABLE", fake_git):
            with mock.patch.object(
                upstream_module,
                "GIT_COMMAND_TIMEOUT_SECONDS",
                0.5,
            ):
                reader = upstream_module._GitReader(
                    self.fixture.path,
                    AuditLimits(max_seconds=2),
                )
                with self.assertRaises(AuditError) as raised:
                    reader.run("rev-parse", "--show-toplevel")

        self.assertEqual(raised.exception.code, "GIT_TIMEOUT")

    def test_non_finite_audit_deadlines_are_invalid(self) -> None:
        self.fixture.commit("common", {"README.md": "common\n"})

        for invalid in (float("nan"), float("inf"), float("-inf")):
            with self.subTest(value=invalid):
                with self.assertRaises(AuditError) as raised:
                    audit_repository(
                        self.fixture.path,
                        "refs/heads/main",
                        "refs/heads/main",
                        limits=AuditLimits(max_seconds=invalid),
                    )
                self.assertEqual(raised.exception.code, "INVALID_INPUT")

    def test_cumulative_git_output_limit_fails_closed(self) -> None:
        self.fixture.commit("common", {"README.md": "common\n"})

        with self.assertRaises(AuditError) as raised:
            audit_repository(
                self.fixture.path,
                "refs/heads/main",
                "refs/heads/main",
                limits=AuditLimits(max_git_output_bytes=1),
            )

        self.assertEqual(raised.exception.code, "LIMIT_EXCEEDED")

    def test_inflight_git_child_is_killed_at_audit_deadline(self) -> None:
        fake_directory = tempfile.TemporaryDirectory()
        self.addCleanup(fake_directory.cleanup)
        fake_git = Path(fake_directory.name) / "git"
        pid_file = Path(fake_directory.name) / "pid"
        completion_marker = Path(fake_directory.name) / "completed"
        fake_git.write_text(
            "#!/bin/sh\n"
            "/bin/sleep 2 &\n"
            "child=$!\n"
            f"printf '%s %s' \"$$\" \"$child\" > '{pid_file}'\n"
            "wait \"$child\"\n"
            f"/usr/bin/touch '{completion_marker}'\n",
            encoding="utf-8",
        )
        fake_git.chmod(0o755)

        with mock.patch.object(upstream_module, "GIT_EXECUTABLE", fake_git):
            reader = upstream_module._GitReader(
                self.fixture.path,
                AuditLimits(max_seconds=0.5),
            )
            with self.assertRaises(AuditError) as raised:
                reader.run("rev-parse", "--show-toplevel")

        self.assertEqual(raised.exception.code, "AUDIT_TIMEOUT")
        self.assertTrue(pid_file.is_file())
        for pid in map(
            int,
            pid_file.read_text(encoding="utf-8").split(),
        ):
            deadline = time.monotonic() + 1
            while time.monotonic() < deadline:
                try:
                    os.kill(pid, 0)
                except ProcessLookupError:
                    break
                time.sleep(0.01)
            else:
                self.fail(f"process group member {pid} survived termination")
        self.assertFalse(completion_marker.exists())

    def test_oversized_streaming_git_child_is_killed_before_completion(self) -> None:
        fake_directory = tempfile.TemporaryDirectory()
        self.addCleanup(fake_directory.cleanup)
        fake_git = Path(fake_directory.name) / "git"
        pid_file = Path(fake_directory.name) / "pid"
        completion_marker = Path(fake_directory.name) / "completed"
        fake_git.write_text(
            "#!/bin/sh\n"
            "/bin/sleep 2 &\n"
            "child=$!\n"
            f"printf '%s %s' \"$$\" \"$child\" > '{pid_file}'\n"
            "printf '0123456789abcdefghijklmnopqrstuvwxyz'\n"
            "wait \"$child\"\n"
            f"/usr/bin/touch '{completion_marker}'\n",
            encoding="utf-8",
        )
        fake_git.chmod(0o755)

        with mock.patch.object(upstream_module, "GIT_EXECUTABLE", fake_git):
            reader = upstream_module._GitReader(
                self.fixture.path,
                AuditLimits(max_git_output_bytes=8),
            )
            with self.assertRaises(AuditError) as raised:
                reader.run("rev-parse", "--show-toplevel")

        self.assertEqual(raised.exception.code, "LIMIT_EXCEEDED")
        self.assertTrue(pid_file.is_file())
        for pid in map(
            int,
            pid_file.read_text(encoding="utf-8").split(),
        ):
            deadline = time.monotonic() + 1
            while time.monotonic() < deadline:
                try:
                    os.kill(pid, 0)
                except ProcessLookupError:
                    break
                time.sleep(0.01)
            else:
                self.fail(f"process group member {pid} survived termination")
        self.assertFalse(completion_marker.exists())

    def test_process_termination_timeout_fails_closed_without_unbounded_wait(
        self,
    ) -> None:
        process = mock.Mock()
        process.pid = 123_456_789
        process.wait.side_effect = subprocess.TimeoutExpired("git", 0)

        with mock.patch.object(os, "killpg", side_effect=ProcessLookupError):
            with self.assertRaises(AuditError) as raised:
                upstream_module._GitReader._terminate(process)

        self.assertEqual(raised.exception.code, "PROCESS_TERMINATION_TIMEOUT")
        self.assertEqual(process.wait.call_count, 2)
        process.kill.assert_called_once_with()

    def test_failed_git_error_detail_and_json_are_bounded(self) -> None:
        fake_directory = tempfile.TemporaryDirectory()
        self.addCleanup(fake_directory.cleanup)
        fake_git = Path(fake_directory.name) / "git"
        fake_git.write_text(
            "#!/bin/sh\n"
            "/usr/bin/yes 'very long git failure detail' | "
            "/usr/bin/head -c 100000 >&2\n"
            "exit 1\n",
            encoding="utf-8",
        )
        fake_git.chmod(0o755)

        with mock.patch.object(upstream_module, "GIT_EXECUTABLE", fake_git):
            reader = upstream_module._GitReader(
                self.fixture.path,
                AuditLimits(),
            )
            with self.assertRaises(AuditError) as raised:
                reader.run("rev-parse", "--show-toplevel")

        self.assertEqual(raised.exception.code, "GIT_COMMAND_FAILED")
        self.assertIn("[truncated]", str(raised.exception))
        rendered = upstream_module._error_json(raised.exception)
        self.assertLessEqual(
            len(rendered.encode("utf-8")),
            upstream_module.MAX_ERROR_REPORT_BYTES,
        )

    def test_symbolic_or_short_refs_are_rejected(self) -> None:
        self.fixture.commit("common", {"README.md": "common\n"})

        with self.assertRaises(AuditError) as raised:
            audit_repository(self.fixture.path, "HEAD", "refs/heads/main")

        self.assertEqual(raised.exception.code, "REF_NOT_IMMUTABLE_OR_FULL")

    def test_revision_expression_disguised_as_full_ref_is_rejected(self) -> None:
        self.fixture.commit("parent", {"README.md": "parent\n"})
        self.fixture.commit("child", {"README.md": "child\n"})

        with self.assertRaises(AuditError) as raised:
            audit_repository(
                self.fixture.path,
                "refs/heads/main^",
                "refs/heads/main",
            )

        self.assertEqual(raised.exception.code, "REF_NOT_IMMUTABLE_OR_FULL")

    def test_sha256_sized_oid_reaches_object_resolution(self) -> None:
        self.fixture.commit("common", {"README.md": "common\n"})

        with self.assertRaises(AuditError) as raised:
            audit_repository(
                self.fixture.path,
                "0" * 64,
                "refs/heads/main",
            )

        self.assertEqual(raised.exception.code, "BASE_REF_INVALID")

    def test_nested_path_cannot_silently_select_parent_repository(self) -> None:
        self.fixture.commit("common", {"README.md": "common\n"})
        nested = self.fixture.path / "nested"
        nested.mkdir()

        with self.assertRaises(AuditError) as raised:
            audit_repository(
                nested,
                "refs/heads/main",
                "refs/heads/main",
            )

        self.assertEqual(raised.exception.code, "REPOSITORY_ROOT_MISMATCH")

    def test_symlinked_parent_path_is_rejected(self) -> None:
        self.fixture.commit("common", {"README.md": "common\n"})
        link_directory = tempfile.TemporaryDirectory()
        self.addCleanup(link_directory.cleanup)
        linked_parent = Path(link_directory.name).resolve() / "linked-parent"
        linked_parent.symlink_to(
            self.fixture.path.parent,
            target_is_directory=True,
        )
        linked_root = linked_parent / self.fixture.path.name

        with self.assertRaises(AuditError) as raised:
            audit_repository(
                linked_root,
                "refs/heads/main",
                "refs/heads/main",
            )

        self.assertEqual(raised.exception.code, "REPOSITORY_PATH_UNSAFE")

    def test_reports_conservative_changed_path_overlap(self) -> None:
        self.fixture.commit(
            "common",
            {
                "Content.Server/Shared.cs": "common\n",
                "Content.Server/LocalOnly.cs": "common\n",
            },
        )
        self.fixture._run("branch", "fork")
        self.fixture._run("switch", "--quiet", "-c", "upstream")
        self.fixture.commit(
            "upstream side",
            {
                "Content.Server/Shared.cs": "upstream\n",
                "Content.Server/UpstreamOnly.cs": "upstream\n",
            },
        )
        self.fixture._run("switch", "--quiet", "fork")
        self.fixture.commit(
            "fork side",
            {
                "Content.Server/Shared.cs": "fork\n",
                "Content.Server/LocalOnly.cs": "fork\n",
            },
        )

        report = audit_repository(
            self.fixture.path,
            "refs/heads/fork",
            "refs/heads/upstream",
        )

        self.assertEqual(
            report["collision_candidates"]["overlap_paths"],
            ["Content.Server/Shared.cs"],
        )
        self.assertEqual(
            report["collision_candidates"]["base_only_changed_file_count"],
            2,
        )
        self.assertEqual(
            report["collision_candidates"]["upstream_only_changed_file_count"],
            2,
        )

    def test_preserves_add_delete_and_type_change_statuses(self) -> None:
        self.fixture.commit(
            "common",
            {
                "delete.txt": "delete\n",
                "type-change": "regular\n",
            },
        )
        self.fixture._run("branch", "fork")
        self.fixture._run("switch", "--quiet", "-c", "upstream")
        (self.fixture.path / "delete.txt").unlink()
        (self.fixture.path / "type-change").unlink()
        os.symlink("target", self.fixture.path / "type-change")
        (self.fixture.path / "added.txt").write_text("added\n", encoding="utf-8")
        self.fixture._run("add", "--all")
        self.fixture._run("commit", "--quiet", "-m", "status changes")

        report = audit_repository(
            self.fixture.path,
            "refs/heads/fork",
            "refs/heads/upstream",
        )
        statuses = {
            entry["path"]: entry["statuses"]
            for entry in report["files"]
        }

        self.assertEqual(
            statuses,
            {
                "added.txt": ["A"],
                "delete.txt": ["D"],
                "type-change": ["T"],
            },
        )

    def test_merge_commit_is_compared_to_its_first_parent(self) -> None:
        self.fixture.commit("common", {"README.md": "common\n"})
        self.fixture._run("branch", "fork")
        self.fixture._run("switch", "--quiet", "-c", "upstream")
        self.fixture._run("switch", "--quiet", "-c", "topic")
        self.fixture.commit(
            "topic",
            {"Content.Server/Topic.cs": "topic\n"},
        )
        self.fixture._run("switch", "--quiet", "upstream")
        self.fixture.commit(
            "mainline",
            {"Content.Server/Mainline.cs": "mainline\n"},
        )
        self.fixture._run("merge", "--quiet", "--no-ff", "--no-edit", "topic")
        merge_commit = self.fixture._run("rev-parse", "HEAD").stdout.strip()

        report = audit_repository(
            self.fixture.path,
            "refs/heads/fork",
            "refs/heads/upstream",
        )
        merge_entry = next(
            entry
            for entry in report["commits"]
            if entry["commit"] == merge_commit
        )

        self.assertEqual(merge_entry["changed_file_count"], 1)
        self.assertEqual(merge_entry["categories"], ["gameplay_source"])

    def test_non_utf8_git_path_fails_closed(self) -> None:
        self.fixture.commit("common", {"README.md": "common\n"})
        fake_directory = tempfile.TemporaryDirectory()
        self.addCleanup(fake_directory.cleanup)
        fake_git = Path(fake_directory.name) / "git"
        fake_git.write_text(
            "#!/bin/sh\nprintf '\\377'\n",
            encoding="utf-8",
        )
        fake_git.chmod(0o755)

        with mock.patch.object(upstream_module, "GIT_EXECUTABLE", fake_git):
            reader = upstream_module._GitReader(
                self.fixture.path,
                AuditLimits(),
            )
            with self.assertRaises(AuditError) as raised:
                reader.run("rev-parse", "--show-toplevel")

        self.assertEqual(raised.exception.code, "NON_UTF8_OUTPUT")

    def test_reports_robusttoolbox_gitlink_pointer_delta_without_recursing(self) -> None:
        child = GitFixture()
        self.addCleanup(child.close)
        first_pointer = child.commit("engine one", {"engine.txt": "one\n"})
        self.fixture._run(
            "-c",
            "protocol.file.allow=always",
            "submodule",
            "add",
            "--quiet",
            str(child.path),
            "RobustToolbox",
        )
        self.fixture._run("commit", "--quiet", "-m", "common engine pointer")
        self.fixture._run("branch", "fork")
        self.fixture._run("switch", "--quiet", "-c", "upstream")
        second_pointer = child.commit("engine two", {"engine.txt": "two\n"})
        self.fixture._run(
            "update-index",
            "--cacheinfo",
            f"160000,{second_pointer},RobustToolbox",
        )
        self.fixture._run("commit", "--quiet", "-m", "advance engine pointer")
        self.fixture._run("config", "diff.ignoreSubmodules", "all")

        report = audit_repository(
            self.fixture.path,
            "refs/heads/fork",
            "refs/heads/upstream",
        )

        self.assertEqual(
            report["robusttoolbox"],
            {
                "path": "RobustToolbox",
                "base": {"mode": "160000", "object": first_pointer},
                "upstream": {"mode": "160000", "object": second_pointer},
                "changed": True,
                "analysis_scope": "parent-repository-pointer-only",
            },
        )
        self.assertEqual(
            report["files"][0]["categories"],
            ["robusttoolbox_pointer"],
        )

    def test_intermediate_gitlink_change_is_classified_from_commit_modes(self) -> None:
        child = GitFixture()
        self.addCleanup(child.close)
        pointer = child.commit("engine", {"engine.txt": "engine\n"})
        self.fixture.commit("common", {"README.md": "common\n"})
        self.fixture._run("branch", "fork")
        self.fixture._run("switch", "--quiet", "-c", "upstream")
        self.fixture._run(
            "update-index",
            "--add",
            "--cacheinfo",
            f"160000,{pointer},RobustToolbox",
        )
        self.fixture._run("commit", "--quiet", "-m", "add engine pointer")
        self.fixture._run("update-index", "--force-remove", "RobustToolbox")
        self.fixture._run("commit", "--quiet", "-m", "remove engine pointer")

        report = audit_repository(
            self.fixture.path,
            "refs/heads/fork",
            "refs/heads/upstream",
        )

        self.assertEqual(report["robusttoolbox"]["base"], None)
        self.assertEqual(report["robusttoolbox"]["upstream"], None)
        self.assertEqual(
            report["files"][0]["categories"],
            ["robusttoolbox_pointer"],
        )

    def test_json_rendering_is_deterministic_and_timestamp_free(self) -> None:
        self.fixture.commit("common", {"README.md": "common\n"})
        report = audit_repository(
            self.fixture.path,
            "refs/heads/main",
            "refs/heads/main",
        )

        first = render_json(report)
        second = render_json(report)

        self.assertEqual(first, second)
        self.assertTrue(first.endswith("\n"))
        self.assertNotIn(str(self.fixture.path), first)
        self.assertNotIn("generated_at", first)
        self.assertIn('"schema": "solreign.upstream-drift/v1"', first)

    def test_renderers_fail_before_emitting_an_oversized_report(self) -> None:
        self.fixture.commit("common", {"README.md": "common\n"})
        report = audit_repository(
            self.fixture.path,
            "refs/heads/main",
            "refs/heads/main",
        )
        report["limitations"].append("x" * 1_000)

        with self.assertRaises(AuditError) as json_error:
            render_json(report, max_bytes=100)
        with self.assertRaises(AuditError) as markdown_error:
            render_markdown(report, max_bytes=100)

        self.assertEqual(json_error.exception.code, "LIMIT_EXCEEDED")
        self.assertEqual(markdown_error.exception.code, "LIMIT_EXCEEDED")

    def test_cli_emits_the_canonical_json_report(self) -> None:
        commit = self.fixture.commit("common", {"README.md": "common\n"})
        script = Path(__file__).parents[1] / "upstream_drift.py"

        process = subprocess.run(
            [
                "/usr/bin/python3",
                "-B",
                str(script),
                "--repo",
                str(self.fixture.path),
                "--base-ref",
                "refs/heads/main",
                "--upstream-ref",
                "refs/heads/main",
                "--format",
                "json",
            ],
            check=False,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
        )

        self.assertEqual(process.returncode, 0)
        self.assertEqual(process.stderr, "")
        self.assertEqual(json.loads(process.stdout)["refs"]["base"]["commit"], commit)

    def test_cli_failure_is_machine_readable_and_writes_only_stderr(self) -> None:
        self.fixture.commit("common", {"README.md": "common\n"})
        script = Path(__file__).parents[1] / "upstream_drift.py"

        process = subprocess.run(
            [
                "/usr/bin/python3",
                "-B",
                str(script),
                "--repo",
                str(self.fixture.path),
                "--base-ref",
                "refs/heads/main",
                "--upstream-ref",
                "refs/heads/missing",
            ],
            check=False,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
        )

        self.assertEqual(process.returncode, 2)
        self.assertEqual(process.stdout, "")
        error = json.loads(process.stderr)
        self.assertEqual(error["schema"], "solreign.upstream-drift/error-v1")
        self.assertEqual(error["error"]["code"], "UPSTREAM_REF_INVALID")

    def test_audit_does_not_execute_a_path_substituted_git_binary(self) -> None:
        self.fixture.commit("common", {"README.md": "common\n"})
        fake_directory = tempfile.TemporaryDirectory()
        self.addCleanup(fake_directory.cleanup)
        marker = Path(fake_directory.name) / "executed"
        fake_git = Path(fake_directory.name) / "git"
        fake_git.write_text(
            f"#!/bin/sh\n/usr/bin/touch '{marker}'\nexit 99\n",
            encoding="utf-8",
        )
        fake_git.chmod(0o755)

        with mock.patch.dict(os.environ, {"PATH": fake_directory.name}):
            report = audit_repository(
                self.fixture.path,
                "refs/heads/main",
                "refs/heads/main",
            )

        self.assertEqual(report["divergence"]["upstream_only_commits"], 0)
        self.assertFalse(marker.exists())

    def test_dirty_worktree_is_byte_identical_after_successful_audit(self) -> None:
        self.fixture.commit(
            "common",
            {
                "staged.txt": "common\n",
                "unstaged.txt": "common\n",
            },
        )
        self.fixture._run("branch", "fork")
        self.fixture._run("switch", "--quiet", "-c", "upstream")
        self.fixture.commit("upstream", {"upstream.txt": "upstream\n"})
        self.fixture._run("switch", "--quiet", "fork")
        (self.fixture.path / "staged.txt").write_text("staged\n", encoding="utf-8")
        self.fixture._run("add", "staged.txt")
        (self.fixture.path / "unstaged.txt").write_text(
            "unstaged\n",
            encoding="utf-8",
        )
        (self.fixture.path / "untracked.txt").write_text(
            "untracked\n",
            encoding="utf-8",
        )

        before = self._repository_state()
        audit_repository(
            self.fixture.path,
            "refs/heads/fork",
            "refs/heads/upstream",
        )
        after = self._repository_state()

        self.assertEqual(after, before)

    def test_dirty_worktree_is_byte_identical_after_late_cli_failure(self) -> None:
        self.fixture.commit(
            "common",
            {
                "staged.txt": "common\n",
                "unstaged.txt": "common\n",
            },
        )
        self.fixture._run("branch", "fork")
        self.fixture._run("switch", "--quiet", "-c", "upstream")
        self.fixture.commit(
            "upstream",
            {
                "first-upstream.txt": "first\n",
                "second-upstream.txt": "second\n",
            },
        )
        self.fixture._run("switch", "--quiet", "fork")
        (self.fixture.path / "staged.txt").write_text("staged\n", encoding="utf-8")
        self.fixture._run("add", "staged.txt")
        (self.fixture.path / "unstaged.txt").write_text(
            "unstaged\n",
            encoding="utf-8",
        )
        (self.fixture.path / "untracked.txt").write_text(
            "untracked\n",
            encoding="utf-8",
        )
        script = Path(__file__).parents[1] / "upstream_drift.py"

        before = self._repository_state()
        process = subprocess.run(
            [
                "/usr/bin/python3",
                "-B",
                str(script),
                "--repo",
                str(self.fixture.path),
                "--base-ref",
                "refs/heads/fork",
                "--upstream-ref",
                "refs/heads/upstream",
                "--max-file-changes",
                "1",
            ],
            check=False,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
        )
        after = self._repository_state()

        self.assertEqual(process.returncode, 2)
        self.assertEqual(process.stdout, "")
        self.assertEqual(json.loads(process.stderr)["error"]["code"], "LIMIT_EXCEEDED")
        self.assertEqual(after, before)

    def test_markdown_escapes_controls_bidi_and_table_delimiters(self) -> None:
        self.fixture.commit("common", {"README.md": "common\n"})
        self.fixture._run("branch", "fork")
        self.fixture._run("switch", "--quiet", "-c", "upstream")
        hostile_path = "OddSurface/line\n\u202e|escape.md"
        self.fixture.commit("hostile path", {hostile_path: "content\n"})
        report = audit_repository(
            self.fixture.path,
            "refs/heads/fork",
            "refs/heads/upstream",
        )

        rendered = render_markdown(report)

        self.assertNotIn("\u202e", rendered)
        self.assertNotIn("line\n", rendered)
        self.assertIn(r"line\u000a\u202e\u007cescape.md", rendered)

    def test_markdown_exposes_incomplete_history_flags_and_escapes_markup(
        self,
    ) -> None:
        self.fixture.commit("common", {"README.md": "common\n"})
        shallow_directory = tempfile.TemporaryDirectory()
        self.addCleanup(shallow_directory.cleanup)
        subprocess.run(
            [
                "git",
                "clone",
                "--quiet",
                "--depth=1",
                f"file://{self.fixture.path}",
                shallow_directory.name,
            ],
            check=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
        )
        head = subprocess.run(
            ["git", "rev-parse", "HEAD"],
            cwd=shallow_directory.name,
            check=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
        ).stdout.strip()
        report = audit_repository(
            Path(shallow_directory.name).resolve(),
            head,
            head,
            allow_incomplete_history=True,
        )
        report["collision_candidates"]["overlap_paths"] = [
            "x` <https://attacker.invalid> [label]\u061c",
        ]

        rendered = render_markdown(report)

        self.assertIn("reachable-local-objects-only", rendered)
        self.assertIn("INCOMPLETE_HISTORY", rendered)
        self.assertNotIn("<https://attacker.invalid>", rendered)
        self.assertNotIn("\u061c", rendered)
        self.assertIn(r"\u0060", rendered)
        self.assertIn(r"\u003c", rendered)
        self.assertIn(r"\u061c", rendered)

    def test_review_flags_preserve_drift_overlap_unknown_and_sensitive_surfaces(
        self,
    ) -> None:
        self.fixture.commit(
            "common",
            {"Content.Shared/Shared.cs": "common\n"},
        )
        self.fixture._run("branch", "fork")
        self.fixture._run("switch", "--quiet", "-c", "upstream")
        self.fixture.commit(
            "upstream side",
            {
                "Content.Shared/Shared.cs": "upstream\n",
                "Content.Shared/Network/Handshake.cs": "network\n",
                "OddSurface/mystery.bin": "unknown\n",
            },
        )
        self.fixture._run("switch", "--quiet", "fork")
        self.fixture.commit(
            "fork side",
            {"Content.Shared/Shared.cs": "fork\n"},
        )

        report = audit_repository(
            self.fixture.path,
            "refs/heads/fork",
            "refs/heads/upstream",
        )

        self.assertEqual(
            report["review"]["flags"],
            [
                "COLLISION_CANDIDATES",
                "SENSITIVE_SURFACE_PATHS",
                "UNCLASSIFIED_PATHS",
                "UPSTREAM_DRIFT_PRESENT",
            ],
        )
        self.assertTrue(report["review"]["required"])

    def _repository_state(self) -> tuple[str, ...]:
        return (
            self.fixture._run("rev-parse", "HEAD").stdout,
            self.fixture._run(
                "for-each-ref",
                "--format=%(refname) %(objectname)",
            ).stdout,
            self.fixture._run(
                "status",
                "--porcelain=v1",
                "-z",
                "--untracked-files=all",
            ).stdout,
            self.fixture._run("diff", "--binary").stdout,
            self.fixture._run("diff", "--cached", "--binary").stdout,
            (self.fixture.path / "staged.txt").read_text(encoding="utf-8"),
            (self.fixture.path / "unstaged.txt").read_text(encoding="utf-8"),
            (self.fixture.path / "untracked.txt").read_text(encoding="utf-8"),
        )


if __name__ == "__main__":
    unittest.main()
