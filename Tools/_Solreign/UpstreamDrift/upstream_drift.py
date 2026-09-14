from __future__ import annotations

import argparse
import json
import math
import os
import re
import selectors
import signal
import subprocess
import sys
import time
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Any


RULESET_ID = "solreign.upstream-drift-paths/v1"
GIT_EXECUTABLE = Path("/usr/bin/git")
GIT_COMMAND_TIMEOUT_SECONDS = 30
DEFAULT_MAX_REPORT_BYTES = 16 * 1024 * 1024
MAX_GIT_ERROR_DETAIL_BYTES = 8 * 1024
MAX_ERROR_REPORT_BYTES = 32 * 1024
CATEGORY_IDS = (
    "content_maps_prototypes",
    "database_schema",
    "engine_render_audio",
    "gameplay_source",
    "launcher_package",
    "protocol_network",
    "robusttoolbox_pointer",
    "security_auth",
    "tooling_tests_docs",
    "unclassified",
)


@dataclass(frozen=True)
class AuditLimits:
    max_commits: int = 5_000
    max_file_changes: int = 50_000
    max_seconds: float = 120.0
    max_git_output_bytes: int = 64 * 1024 * 1024
    max_report_bytes: int = DEFAULT_MAX_REPORT_BYTES

    def validate(self) -> None:
        if (
            self.max_commits < 1
            or self.max_file_changes < 1
            or not math.isfinite(self.max_seconds)
            or self.max_seconds <= 0
            or self.max_git_output_bytes < 1
            or self.max_report_bytes < 1
        ):
            raise AuditError("INVALID_INPUT", "audit limits must be positive")


class AuditError(RuntimeError):
    def __init__(self, code: str, message: str) -> None:
        super().__init__(message)
        self.code = code


class _GitReader:
    def __init__(self, repo: Path, limits: AuditLimits) -> None:
        self.repo = repo
        self.deadline = time.monotonic() + limits.max_seconds
        self.remaining_output_bytes = limits.max_git_output_bytes

    def run(self, *arguments: str) -> str:
        if not GIT_EXECUTABLE.is_file():
            raise AuditError("GIT_UNAVAILABLE", "/usr/bin/git is unavailable")
        audit_remaining = self.deadline - time.monotonic()
        if audit_remaining <= 0:
            raise AuditError("AUDIT_TIMEOUT", "audit-wide deadline exceeded")
        command_deadline = min(
            self.deadline,
            time.monotonic() + GIT_COMMAND_TIMEOUT_SECONDS,
        )
        environment = {
            "GIT_CONFIG_GLOBAL": os.devnull,
            "GIT_CONFIG_NOSYSTEM": "1",
            "GIT_OPTIONAL_LOCKS": "0",
            "GIT_NO_LAZY_FETCH": "1",
            "GIT_PAGER": "cat",
            "GIT_TERMINAL_PROMPT": "0",
            "HOME": "/var/empty",
            "LANG": "C",
            "LC_ALL": "C",
            "PATH": "/usr/bin:/bin",
        }
        command = [
            str(GIT_EXECUTABLE),
            "--no-pager",
            "--no-replace-objects",
            "-c",
            "color.ui=false",
            "-c",
            "log.showSignature=false",
            "-C",
            str(self.repo),
            *arguments,
        ]
        try:
            process = subprocess.Popen(
                command,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                env=environment,
                start_new_session=True,
            )
        except OSError as error:
            raise AuditError(
                "GIT_UNAVAILABLE",
                "cannot execute /usr/bin/git",
            ) from error
        assert process.stdout is not None
        assert process.stderr is not None
        output = bytearray()
        errors = bytearray()
        streams = {
            process.stdout: output,
            process.stderr: errors,
        }
        selector = selectors.DefaultSelector()
        for stream in streams:
            selector.register(stream, selectors.EVENT_READ)
        try:
            while selector.get_map():
                now = time.monotonic()
                remaining = command_deadline - now
                if remaining <= 0:
                    self._terminate(process)
                    code = (
                        "AUDIT_TIMEOUT"
                        if now >= self.deadline
                        else "GIT_TIMEOUT"
                    )
                    message = (
                        "audit-wide deadline exceeded"
                        if code == "AUDIT_TIMEOUT"
                        else "git command exceeded its time limit"
                    )
                    raise AuditError(code, message)
                for key, _ in selector.select(timeout=remaining):
                    chunk = os.read(key.fileobj.fileno(), 65_536)
                    if not chunk:
                        selector.unregister(key.fileobj)
                        key.fileobj.close()
                        continue
                    self.remaining_output_bytes -= len(chunk)
                    if self.remaining_output_bytes < 0:
                        self._terminate(process)
                        raise AuditError(
                            "LIMIT_EXCEEDED",
                            "cumulative git output exceeds the audit limit",
                        )
                    streams[key.fileobj].extend(chunk)
            process.wait(
                timeout=max(0.001, command_deadline - time.monotonic())
            )
        except subprocess.TimeoutExpired as error:
            self._terminate(process)
            code = (
                "AUDIT_TIMEOUT"
                if command_deadline == self.deadline
                else "GIT_TIMEOUT"
            )
            message = (
                "audit-wide deadline exceeded"
                if code == "AUDIT_TIMEOUT"
                else "git process did not terminate within its time limit"
            )
            raise AuditError(
                code,
                message,
            ) from error
        finally:
            selector.close()
            for stream in streams:
                if not stream.closed:
                    stream.close()
        try:
            stdout = bytes(output).decode("utf-8", errors="strict")
            stderr = bytes(errors).decode("utf-8", errors="strict")
        except UnicodeDecodeError as error:
            raise AuditError(
                "NON_UTF8_OUTPUT",
                "git returned non-UTF-8 data",
            ) from error
        if process.returncode != 0:
            detail = _truncate_utf8(
                stderr.strip() or "git command failed",
                MAX_GIT_ERROR_DETAIL_BYTES,
            )
            raise AuditError("GIT_COMMAND_FAILED", detail)
        return stdout

    @staticmethod
    def _terminate(process: subprocess.Popen[bytes]) -> None:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        except OSError:
            process.kill()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            try:
                process.kill()
            except ProcessLookupError:
                pass
            try:
                process.wait(timeout=1)
            except subprocess.TimeoutExpired as error:
                raise AuditError(
                    "PROCESS_TERMINATION_TIMEOUT",
                    "owned git process did not terminate after forced kill",
                ) from error


def _git(reader: _GitReader, *arguments: str) -> str:
    return reader.run(*arguments)


def _resolve_commit(
    reader: _GitReader,
    reference: str,
    error_code: str,
) -> str:
    try:
        return _git(
            reader,
            "rev-parse",
            "--verify",
            "--end-of-options",
            f"{reference}^{{commit}}",
        ).strip()
    except AuditError as error:
        if error.code != "GIT_COMMAND_FAILED":
            raise
        raise AuditError(error_code, f"cannot resolve commit: {reference}") from error


def _validate_reference(reference: str) -> None:
    full_oid = re.fullmatch(r"(?:[0-9A-Fa-f]{40}|[0-9A-Fa-f]{64})", reference)
    full_oid = full_oid is not None
    full_ref = reference.startswith(
        ("refs/heads/", "refs/remotes/", "refs/tags/")
    )
    unsafe_fragment = (
        ".." in reference
        or "@{" in reference
        or "//" in reference
        or reference.endswith(("/", "."))
        or any(fragment.endswith((".", ".lock")) for fragment in reference.split("/"))
        or any(character in " ~^:?*[\\" for character in reference)
        or any(ord(character) < 32 or ord(character) == 127 for character in reference)
    )
    if not (full_oid or full_ref) or unsafe_fragment:
        raise AuditError(
            "REF_NOT_IMMUTABLE_OR_FULL",
            "refs must be full object IDs or fully qualified heads/remotes/tags",
        )


def _classify_path(
    path: str,
    *,
    robusttoolbox_is_gitlink: bool,
) -> tuple[list[str], list[str]]:
    matches: list[tuple[str, str]] = []
    lower_path = path.lower()

    if path.startswith(("Content.Shared/Network/", "Content.Server/Network/")):
        matches.append(("protocol_network", "network-content-shared"))
    if path.startswith(
        (
            "Content.Client/",
            "Content.Server/",
            "Content.Server.Database/",
            "Content.Shared/",
            "Content.Shared.Database/",
        )
    ):
        matches.append(("gameplay_source", "content-runtime-source"))
    if path.startswith(
        (
            "Content.Client/Administration/",
            "Content.Server/Access/",
            "Content.Server/Administration/",
            "Content.Server/Authentication/",
            "Content.Shared/Access/",
            "Content.Shared/Administration/",
            "Content.Shared/Authentication/",
        )
    ):
        matches.append(("security_auth", "security-auth-content"))
    if path.startswith(
        (
            "Content.Server/Database/",
            "Content.Server.Database/",
            "Content.Shared.Database/",
        )
    ) or "/Migrations/" in path:
        matches.append(("database_schema", "database-server-or-migration"))
    if path == "RobustToolbox" and robusttoolbox_is_gitlink:
        matches.append(("robusttoolbox_pointer", "robusttoolbox-gitlink"))
    elif path.startswith("RobustToolbox/"):
        matches.append(("engine_render_audio", "robusttoolbox-tree"))
    if path.startswith(
        (
            "Content.Client/Graphics/",
            "Content.Client/Audio/",
            "Content.Shared/Audio/",
            "Resources/Shaders/",
        )
    ):
        matches.append(("engine_render_audio", "render-audio-surface"))
    if path.startswith("Resources/"):
        matches.append(("content_maps_prototypes", "resources-content"))
    if path.startswith(
        (
            "Tools/",
            ".github/",
            ".vscode/",
            "docs/",
            "Content.Tests/",
            "Content.IntegrationTests/",
        )
    ) or lower_path.startswith(("readme", "license")) or path in {
        ".gitignore",
        "flake.lock",
        "flake.nix",
        "shell.nix",
    } or path.endswith((".sln", ".slnx", ".DotSettings")):
        matches.append(("tooling_tests_docs", "tooling-tests-docs"))
    if path.startswith(
        (
            "RobustToolbox/Robust.Launcher/",
            "Tools/Packaging/",
            "Tools/package",
        )
    ):
        matches.append(("launcher_package", "launcher-packaging"))

    if not matches:
        return ["unclassified"], ["unclassified"]
    return (
        sorted({category for category, _ in matches}),
        sorted({rule for _, rule in matches}),
    )


def _first_parent(reader: _GitReader, commit: str) -> str | None:
    parent_fields = _git(
        reader,
        "rev-list",
        "--parents",
        "-n",
        "1",
        commit,
    ).split()
    if not parent_fields or parent_fields[0] != commit:
        raise AuditError("GIT_OUTPUT_INVALID", "git returned invalid parent data")
    return parent_fields[1] if len(parent_fields) > 1 else None


def _commit_paths(
    reader: _GitReader,
    commit: str,
) -> list[tuple[str, str]]:
    first_parent = _first_parent(reader, commit)
    if first_parent is not None:
        output = _git(
            reader,
            "diff",
            "--name-status",
            "-z",
            "--no-renames",
            "--no-ext-diff",
            "--ignore-submodules=none",
            first_parent,
            commit,
            "--",
        )
    else:
        output = _git(
            reader,
            "diff-tree",
            "--root",
            "--no-commit-id",
            "--name-status",
            "-r",
            "-z",
            "--no-renames",
            "--no-ext-diff",
            "--ignore-submodules=none",
            commit,
        )
    fields = output.split("\0")
    if fields and fields[-1] == "":
        fields.pop()
    if len(fields) % 2 != 0:
        raise AuditError("GIT_OUTPUT_INVALID", "git returned malformed path data")
    changes: list[tuple[str, str]] = []
    for index in range(0, len(fields), 2):
        status = fields[index]
        path = fields[index + 1]
        if status not in {"A", "D", "M", "T", "U", "X", "B"} or not path:
            raise AuditError("GIT_OUTPUT_INVALID", "git returned invalid path status")
        changes.append((path, status))
    return sorted(changes)


def _tree_entry(
    reader: _GitReader,
    commit: str,
    path: str,
) -> dict[str, str] | None:
    output = _git(reader, "ls-tree", "-z", commit, "--", path)
    if not output:
        return None
    entries = [entry for entry in output.split("\0") if entry]
    if len(entries) != 1 or "\t" not in entries[0]:
        raise AuditError("GIT_OUTPUT_INVALID", "git returned malformed tree data")
    metadata, returned_path = entries[0].split("\t", 1)
    fields = metadata.split()
    if len(fields) != 3 or returned_path != path:
        raise AuditError("GIT_OUTPUT_INVALID", "git returned invalid tree entry")
    mode, object_type, object_id = fields
    if mode == "160000" and object_type != "commit":
        raise AuditError("GIT_OUTPUT_INVALID", "gitlink does not reference a commit")
    return {"mode": mode, "object": object_id}


def _has_partial_clone_configuration(reader: _GitReader) -> bool:
    output = _git(reader, "config", "--local", "--null", "--list")
    for record in output.split("\0"):
        if not record:
            continue
        if "\n" not in record:
            raise AuditError(
                "GIT_OUTPUT_INVALID",
                "git returned malformed local configuration",
            )
        key, value = record.split("\n", 1)
        normalized_key = key.lower()
        if normalized_key == "extensions.partialclone":
            return True
        if (
            normalized_key.startswith("remote.")
            and normalized_key.endswith(".promisor")
            and value.lower() == "true"
        ):
            return True
    return False


def _robusttoolbox_change_is_gitlink(
    reader: _GitReader,
    commit: str,
) -> bool:
    current = _tree_entry(reader, commit, "RobustToolbox")
    first_parent = _first_parent(reader, commit)
    previous = (
        _tree_entry(reader, first_parent, "RobustToolbox")
        if first_parent is not None
        else None
    )
    return any(
        entry is not None and entry["mode"] == "160000"
        for entry in (current, previous)
    )


def _truncate_utf8(value: str, max_bytes: int) -> str:
    encoded = value.encode("utf-8")
    if len(encoded) <= max_bytes:
        return value
    marker = " [truncated]"
    prefix_budget = max_bytes - len(marker.encode("utf-8"))
    prefix = encoded[:prefix_budget].decode("utf-8", errors="ignore")
    return prefix + marker


def render_json(
    report: dict[str, Any],
    *,
    max_bytes: int = DEFAULT_MAX_REPORT_BYTES,
) -> str:
    encoder = json.JSONEncoder(
        ensure_ascii=True,
        indent=2,
        sort_keys=True,
    )
    chunks: list[str] = []
    observed_bytes = 0
    for chunk in encoder.iterencode(report):
        observed_bytes += len(chunk.encode("utf-8"))
        if observed_bytes + 1 > max_bytes:
            raise AuditError(
                "LIMIT_EXCEEDED",
                "rendered report exceeds the output byte limit",
            )
        chunks.append(chunk)
    return "".join(chunks) + "\n"


def _markdown_text(value: object) -> str:
    escaped: list[str] = []
    safe_characters = frozenset(
        "abcdefghijklmnopqrstuvwxyz"
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ"
        "0123456789"
        " ._/:,-"
    )
    for character in str(value):
        codepoint = ord(character)
        if character not in safe_characters:
            width = 4 if codepoint <= 0xFFFF else 8
            prefix = "u" if width == 4 else "U"
            escaped.append(f"\\{prefix}{codepoint:0{width}x}")
        elif codepoint < 32 or codepoint == 127:
            escaped.append(f"\\u{codepoint:04x}")
        else:
            escaped.append(character)
    return "".join(escaped)


def render_markdown(
    report: dict[str, Any],
    *,
    max_bytes: int = DEFAULT_MAX_REPORT_BYTES,
) -> str:
    base = report["refs"]["base"]
    upstream = report["refs"]["upstream"]
    divergence = report["divergence"]
    history = report["history"]
    review = report["review"]
    lines: list[str] = []
    observed_bytes = 0

    def add(*new_lines: str) -> None:
        nonlocal observed_bytes
        for line in new_lines:
            observed_bytes += len(line.encode("utf-8")) + 1
            if observed_bytes > max_bytes:
                raise AuditError(
                    "LIMIT_EXCEEDED",
                    "rendered report exceeds the output byte limit",
                )
            lines.append(line)

    add(
        "# SOLREIGN upstream drift audit",
        "",
        f"- Schema: `{_markdown_text(report['schema'])}`",
        f"- Base: `{_markdown_text(base['input'])}` → `{base['commit']}`",
        f"- Upstream: `{_markdown_text(upstream['input'])}` → `{upstream['commit']}`",
        f"- Base-only commits: {divergence['base_only_commits']}",
        f"- Upstream-only commits: {divergence['upstream_only_commits']}",
        f"- Merge base(s): {', '.join(report['merge_bases'])}",
        f"- Shallow repository: `{str(history['shallow']).lower()}`",
        f"- Completeness claim: `{history['completeness_claim']}`",
        f"- Comparison basis: `{history['comparison_basis']}`",
        f"- Review required: `{str(review['required']).lower()}`",
        f"- Review flags: `{_markdown_text(', '.join(review['flags']) or 'none')}`",
        "",
        "## Classification counts",
        "",
        "| Category | Files |",
        "|---|---:|",
    )
    for category, count in report["classification"][
        "category_file_counts"
    ].items():
        add(f"| `{category}` | {count} |")
    add(
        "",
        "## Upstream-only commits",
        "",
        "| Commit | Changed files | Categories |",
        "|---|---:|---|",
    )
    for commit in report["commits"]:
        categories = ", ".join(commit["categories"]) or "none"
        add(
            f"| `{commit['commit']}` | {commit['changed_file_count']} | "
            f"{_markdown_text(categories)} |"
        )
    add(
        "",
        "## Upstream-only paths",
        "",
        "| Path | Statuses | Categories | Rule IDs |",
        "|---|---|---|---|",
    )
    for file_entry in report["files"]:
        add(
            "| "
            + " | ".join(
                (
                    _markdown_text(file_entry["path"]),
                    _markdown_text(", ".join(file_entry["statuses"])),
                    _markdown_text(", ".join(file_entry["categories"])),
                    _markdown_text(", ".join(file_entry["matched_rule_ids"])),
                )
            )
            + " |"
        )
    collision = report["collision_candidates"]
    robusttoolbox = report["robusttoolbox"]
    add(
        "",
        "## Conservative collision candidates",
        "",
        f"- Base-only changed paths: {collision['base_only_changed_file_count']}",
        f"- Upstream-only changed paths: {collision['upstream_only_changed_file_count']}",
        f"- Overlap paths: {len(collision['overlap_paths'])}",
    )
    for path in collision["overlap_paths"]:
        add(f"  - `{_markdown_text(path)}`")
    add(
        "",
        "## RobustToolbox pointer",
        "",
        f"- Base: `{_markdown_text(robusttoolbox['base'])}`",
        f"- Upstream: `{_markdown_text(robusttoolbox['upstream'])}`",
        f"- Changed: `{str(robusttoolbox['changed']).lower()}`",
        f"- Scope: `{robusttoolbox['analysis_scope']}`",
        "",
        "## Limitations",
        "",
    )
    for limitation in report["limitations"]:
        add(f"- {_markdown_text(limitation)}")
    return "\n".join(lines) + "\n"


def _error_json(error: AuditError) -> str:
    rendered = json.dumps(
        {
            "schema": "solreign.upstream-drift/error-v1",
            "error": {"code": error.code, "message": str(error)},
        },
        ensure_ascii=True,
        sort_keys=True,
    ) + "\n"
    if len(rendered.encode("utf-8")) <= MAX_ERROR_REPORT_BYTES:
        return rendered
    return json.dumps(
        {
            "schema": "solreign.upstream-drift/error-v1",
            "error": {
                "code": error.code,
                "message": "error detail exceeded the report limit",
            },
        },
        ensure_ascii=True,
        sort_keys=True,
    ) + "\n"


def audit_repository(
    repo: Path | str,
    base_ref: str,
    upstream_ref: str,
    *,
    limits: AuditLimits = AuditLimits(),
    allow_incomplete_history: bool = False,
) -> dict[str, Any]:
    limits.validate()
    _validate_reference(base_ref)
    _validate_reference(upstream_ref)
    supplied_repository = Path(os.path.abspath(os.path.expanduser(str(repo))))
    repository = supplied_repository.resolve()
    if supplied_repository != repository:
        raise AuditError(
            "REPOSITORY_PATH_UNSAFE",
            "the repository path must not contain symlink components",
        )
    reader = _GitReader(repository, limits)
    discovered_root = Path(
        _git(reader, "rev-parse", "--show-toplevel").strip()
    ).resolve()
    if discovered_root != repository:
        raise AuditError(
            "REPOSITORY_ROOT_MISMATCH",
            "the supplied repository path is not the Git worktree root",
        )
    if _has_partial_clone_configuration(reader):
        raise AuditError(
            "INSUFFICIENT_HISTORY",
            "partial-clone repositories are not auditable offline",
        )
    shallow_output = _git(
        reader,
        "rev-parse",
        "--is-shallow-repository",
    ).strip()
    if shallow_output not in {"true", "false"}:
        raise AuditError(
            "INSUFFICIENT_HISTORY",
            "shallow or indeterminate repository history is not auditable",
        )
    shallow = shallow_output == "true"
    if shallow and not allow_incomplete_history:
        raise AuditError(
            "INSUFFICIENT_HISTORY",
            "shallow repository history requires explicit incomplete-history mode",
        )
    base_commit = _resolve_commit(reader, base_ref, "BASE_REF_INVALID")
    upstream_commit = _resolve_commit(
        reader,
        upstream_ref,
        "UPSTREAM_REF_INVALID",
    )
    counts = _git(
        reader,
        "rev-list",
        "--left-right",
        "--count",
        f"{base_commit}...{upstream_commit}",
    ).split()
    if len(counts) != 2:
        raise AuditError(
            "GIT_OUTPUT_INVALID",
            "git returned an invalid divergence count",
        )
    try:
        base_only_count, upstream_only_count = (int(count) for count in counts)
    except ValueError as error:
        raise AuditError(
            "GIT_OUTPUT_INVALID",
            "git returned non-integer divergence counts",
        ) from error
    if base_only_count + upstream_only_count > limits.max_commits:
        raise AuditError(
            "LIMIT_EXCEEDED",
            "commit count exceeds the configured limit",
        )
    try:
        merge_base_output = _git(
            reader,
            "merge-base",
            "--all",
            base_commit,
            upstream_commit,
        )
    except AuditError as error:
        if error.code != "GIT_COMMAND_FAILED":
            raise
        raise AuditError(
            "NO_MERGE_BASE",
            "the compared commits do not have a merge base",
        ) from error
    merge_bases = sorted(line for line in merge_base_output.splitlines() if line)
    if not merge_bases:
        raise AuditError(
            "NO_MERGE_BASE",
            "the compared commits do not have a merge base",
        )
    robusttoolbox_base = _tree_entry(reader, base_commit, "RobustToolbox")
    robusttoolbox_upstream = _tree_entry(
        reader,
        upstream_commit,
        "RobustToolbox",
    )
    base_only_commits = sorted(
        line
        for line in _git(
            reader,
            "rev-list",
            f"{upstream_commit}..{base_commit}",
        ).splitlines()
        if line
    )
    upstream_commits = sorted(
        line
        for line in _git(
            reader,
            "rev-list",
            f"{base_commit}..{upstream_commit}",
        ).splitlines()
        if line
    )
    if (
        len(base_only_commits) != base_only_count
        or len(upstream_commits) != upstream_only_count
    ):
        raise AuditError(
            "GIT_OUTPUT_INVALID",
            "commit enumeration does not match divergence counts",
        )
    observed_file_changes = 0
    base_only_paths: set[str] = set()
    for commit in base_only_commits:
        changes = _commit_paths(reader, commit)
        observed_file_changes += len(changes)
        if observed_file_changes > limits.max_file_changes:
            raise AuditError(
                "LIMIT_EXCEEDED",
                "file-change count exceeds the configured limit",
            )
        base_only_paths.update(path for path, _ in changes)
    commit_entries: list[dict[str, Any]] = []
    file_accumulator: dict[str, dict[str, set[str]]] = defaultdict(
        lambda: {
            "statuses": set(),
            "commits": set(),
            "categories": set(),
            "matched_rule_ids": set(),
        }
    )
    for commit in upstream_commits:
        changes = _commit_paths(reader, commit)
        observed_file_changes += len(changes)
        if observed_file_changes > limits.max_file_changes:
            raise AuditError(
                "LIMIT_EXCEEDED",
                "file-change count exceeds the configured limit",
            )
        commit_categories: set[str] = set()
        for path, status in changes:
            robusttoolbox_is_gitlink = (
                _robusttoolbox_change_is_gitlink(reader, commit)
                if path == "RobustToolbox"
                else False
            )
            categories, rules = _classify_path(
                path,
                robusttoolbox_is_gitlink=robusttoolbox_is_gitlink,
            )
            commit_categories.update(categories)
            file_accumulator[path]["statuses"].add(status)
            file_accumulator[path]["commits"].add(commit)
            file_accumulator[path]["categories"].update(categories)
            file_accumulator[path]["matched_rule_ids"].update(rules)
        commit_entries.append(
            {
                "commit": commit,
                "categories": sorted(commit_categories),
                "changed_file_count": len(changes),
            }
        )
    file_entries = [
        {
            "path": path,
            "statuses": sorted(values["statuses"]),
            "commits": sorted(values["commits"]),
            "categories": sorted(values["categories"]),
            "matched_rule_ids": sorted(values["matched_rule_ids"]),
        }
        for path, values in sorted(file_accumulator.items())
    ]
    category_file_counts = {
        category: sum(category in entry["categories"] for entry in file_entries)
        for category in CATEGORY_IDS
    }

    limitations = [
        "Classification is path-based routing evidence for human review.",
        "The report does not establish upstream freshness.",
        "The report does not establish merge safety or compatibility.",
        "The report does not establish security correctness or vulnerability absence.",
        "The report does not establish build, launcher, hub, license, or deploy approval.",
        "RobustToolbox analysis is limited to the parent-repository gitlink pointer.",
    ]
    review_flags: list[str] = []
    if shallow:
        limitations.append(
            "History is shallow; counts cover only locally reachable Git objects."
        )
        review_flags.append("INCOMPLETE_HISTORY")
    if upstream_only_count:
        review_flags.append("UPSTREAM_DRIFT_PRESENT")
    if category_file_counts["unclassified"]:
        review_flags.append("UNCLASSIFIED_PATHS")
    if base_only_paths & set(file_accumulator):
        review_flags.append("COLLISION_CANDIDATES")
    if robusttoolbox_base != robusttoolbox_upstream:
        review_flags.append("ROBUSTTOOLBOX_POINTER_CHANGED")
    sensitive_categories = {
        "database_schema",
        "engine_render_audio",
        "launcher_package",
        "protocol_network",
        "security_auth",
    }
    if any(category_file_counts[category] for category in sensitive_categories):
        review_flags.append("SENSITIVE_SURFACE_PATHS")

    return {
        "schema": "solreign.upstream-drift/v1",
        "schema_version": 1,
        "history": {
            "shallow": shallow,
            "completeness_claim": "not-made",
            "comparison_basis": (
                "reachable-local-objects-only"
                if shallow
                else "complete-local-history"
            ),
        },
        "refs": {
            "base": {"input": base_ref, "commit": base_commit},
            "upstream": {"input": upstream_ref, "commit": upstream_commit},
        },
        "merge_bases": merge_bases,
        "divergence": {
            "base_only_commits": base_only_count,
            "upstream_only_commits": upstream_only_count,
        },
        "commits": commit_entries,
        "files": file_entries,
        "classification": {
            "ruleset": RULESET_ID,
            "category_file_counts": category_file_counts,
            "unclassified_file_count": category_file_counts["unclassified"],
        },
        "collision_candidates": {
            "base_only_changed_file_count": len(base_only_paths),
            "upstream_only_changed_file_count": len(file_entries),
            "overlap_paths": sorted(base_only_paths & set(file_accumulator)),
        },
        "robusttoolbox": {
            "path": "RobustToolbox",
            "base": robusttoolbox_base,
            "upstream": robusttoolbox_upstream,
            "changed": robusttoolbox_base != robusttoolbox_upstream,
            "analysis_scope": "parent-repository-pointer-only",
        },
        "review": {
            "required": bool(review_flags),
            "flags": sorted(review_flags),
        },
        "limitations": limitations,
    }


def main(arguments: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Audit local SS14 fork/upstream Git divergence without mutation.",
    )
    parser.add_argument("--repo", type=Path, required=True)
    parser.add_argument("--base-ref", required=True)
    parser.add_argument("--upstream-ref", required=True)
    parser.add_argument("--format", choices=("json", "markdown"), default="json")
    parser.add_argument("--max-commits", type=int, default=5_000)
    parser.add_argument("--max-file-changes", type=int, default=50_000)
    parser.add_argument("--max-seconds", type=float, default=120.0)
    parser.add_argument(
        "--max-git-output-bytes",
        type=int,
        default=64 * 1024 * 1024,
    )
    parser.add_argument(
        "--max-report-bytes",
        type=int,
        default=DEFAULT_MAX_REPORT_BYTES,
    )
    parser.add_argument(
        "--allow-incomplete-history",
        action="store_true",
        help="Report only locally reachable objects and mark the result indeterminate.",
    )
    parsed = parser.parse_args(arguments)

    limits = AuditLimits(
        max_commits=parsed.max_commits,
        max_file_changes=parsed.max_file_changes,
        max_seconds=parsed.max_seconds,
        max_git_output_bytes=parsed.max_git_output_bytes,
        max_report_bytes=parsed.max_report_bytes,
    )
    try:
        report = audit_repository(
            parsed.repo,
            parsed.base_ref,
            parsed.upstream_ref,
            limits=limits,
            allow_incomplete_history=parsed.allow_incomplete_history,
        )
        rendered = (
            render_markdown(report, max_bytes=limits.max_report_bytes)
            if parsed.format == "markdown"
            else render_json(report, max_bytes=limits.max_report_bytes)
        )
    except AuditError as error:
        sys.stderr.write(_error_json(error))
        return 2
    sys.stdout.write(rendered)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
