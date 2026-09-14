#!/usr/bin/env python3
"""Detect cross-file same-kind prototype ID collisions in SS14 YAML.

SS14 keys prototypes by (kind, id), not bare id. The same string may legally
appear as an accessLevel and an accessGroup (e.g. Engineering). Real boot
failures are same-kind collisions — typically a stale monolith surviving an
upstream split (see 2026-07-28 uplink_catalog.yml incident).

Stdlib only. Line-oriented parser — no PyYAML (deploy box has no pip; trees
are huge).

Usage:
  python3 deploy/check_prototype_collisions.py [PrototypesDir]
  # default: Resources/Prototypes relative to cwd, or GAME_REPO/Resources/Prototypes

Exit 0 if clean, non-zero if any (kind, id) is defined more than once.
"""

from __future__ import annotations

import argparse
import os
import re
import sys
from collections import defaultdict
from pathlib import Path
from typing import DefaultDict, Iterable, List, Optional, Tuple

# Top-level prototype entry: dash at column 0, then type: <kind>
TYPE_RE = re.compile(r"^- type:\s*(\S+)\s*(?:#.*)?$")
# Direct field of that entry (same indent as the type key → two spaces)
ID_RE = re.compile(r"^  id:\s*(.+?)\s*(?:#.*)?$")
DOC_SEP_RE = re.compile(r"^---\s*(?:#.*)?$")


def strip_yaml_scalar(raw: str) -> str:
    """Unquote a simple YAML scalar used as an id."""
    s = raw.strip()
    if len(s) >= 2 and s[0] == s[-1] and s[0] in ("'", '"'):
        return s[1:-1]
    return s


def iter_yml_files(root: Path) -> Iterable[Path]:
    for dirpath, _dirnames, filenames in os.walk(root):
        for name in filenames:
            if name.endswith(".yml") or name.endswith(".yaml"):
                yield Path(dirpath) / name


def parse_prototypes(path: Path, root: Path) -> List[Tuple[str, str, str, int]]:
    """Return list of (kind, id, relpath, line_no) for one file.

    line_no is 1-based and points at the `- type:` line of the entry.
    """
    try:
        rel = str(path.relative_to(root))
    except ValueError:
        rel = str(path)
    try:
        text = path.read_text(encoding="utf-8-sig")  # strip BOM if present
    except OSError as exc:
        print(f"error: cannot read {path}: {exc}", file=sys.stderr)
        return []

    results: List[Tuple[str, str, str, int]] = []
    current_kind: Optional[str] = None
    current_line: Optional[int] = None
    current_id: Optional[str] = None

    def flush() -> None:
        nonlocal current_kind, current_line, current_id
        if current_kind is not None and current_id is not None and current_line is not None:
            results.append((current_kind, current_id, rel, current_line))
        current_kind = None
        current_line = None
        current_id = None

    for lineno, raw_line in enumerate(text.splitlines(), start=1):
        line = raw_line.rstrip("\r")

        if DOC_SEP_RE.match(line):
            flush()
            continue

        type_m = TYPE_RE.match(line)
        if type_m:
            flush()
            current_kind = type_m.group(1)
            current_line = lineno
            current_id = None
            continue

        if current_kind is None:
            continue

        # Next top-level non-indented content ends the entry (comments/blank ok).
        if line and not line[0].isspace() and not line.lstrip().startswith("#"):
            flush()
            continue

        id_m = ID_RE.match(line)
        if id_m and current_id is None:
            current_id = strip_yaml_scalar(id_m.group(1))

    flush()
    return results


def check_tree(prototypes_root: Path) -> int:
    if not prototypes_root.is_dir():
        print(f"error: not a directory: {prototypes_root}", file=sys.stderr)
        return 2

    # key: (kind, id) → list of "path:line"
    locations: DefaultDict[Tuple[str, str], List[str]] = defaultdict(list)
    file_count = 0
    proto_count = 0

    for yml in sorted(iter_yml_files(prototypes_root)):
        file_count += 1
        for kind, pid, rel, lineno in parse_prototypes(yml, prototypes_root):
            proto_count += 1
            locations[(kind, pid)].append(f"{rel}:{lineno}")

    collisions = {k: v for k, v in locations.items() if len(v) > 1}

    if not collisions:
        print(
            f"OK: {proto_count} prototypes in {file_count} files, "
            f"0 (kind, id) collisions under {prototypes_root}"
        )
        return 0

    print(
        f"FAIL: {len(collisions)} (kind, id) collision(s) under {prototypes_root}",
        file=sys.stderr,
    )
    for (kind, pid) in sorted(collisions.keys(), key=lambda t: (t[0], t[1])):
        locs = collisions[(kind, pid)]
        print(f"  [{kind}] id={pid!r} defined {len(locs)} times:", file=sys.stderr)
        for loc in locs:
            print(f"    {loc}", file=sys.stderr)

    print(
        f"summary: {proto_count} prototypes, {file_count} files, "
        f"{len(collisions)} collisions",
        file=sys.stderr,
    )
    return 1


def default_prototypes_root() -> Path:
    env = os.environ.get("GAME_REPO")
    if env:
        candidate = Path(env) / "Resources" / "Prototypes"
        if candidate.is_dir():
            return candidate
    cwd = Path.cwd() / "Resources" / "Prototypes"
    if cwd.is_dir():
        return cwd
    return Path("Resources/Prototypes")


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description="Report SS14 prototype (kind, id) collisions across a Prototypes tree."
    )
    parser.add_argument(
        "prototypes_root",
        nargs="?",
        type=Path,
        default=None,
        help="Path to Resources/Prototypes (default: $GAME_REPO/Resources/Prototypes or ./Resources/Prototypes)",
    )
    args = parser.parse_args(argv)
    root = args.prototypes_root if args.prototypes_root is not None else default_prototypes_root()
    return check_tree(root.resolve())


if __name__ == "__main__":
    sys.exit(main())
