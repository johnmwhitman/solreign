#!/usr/bin/env python3
"""Fail-closed, manifest-driven component attachment for SS14 v7 map YAML.

WHY THIS EXISTS (2026-07-29)
----------------------------
``MapPatch`` only APPENDS new entities inside a sentinel-delimited owned block. It
cannot attach a component to an entity that is ALREADY on the map.

That gap had a real cost. The Afterlife Activities menu was empty on six of seven
stations: all nine ``SolreignGhostActivity`` components lived on ``solreign_oasis.yml``,
and on Oasis they are attached to furniture that was already there (BlockGameArcade,
ChurchOrganInstrument, KitchenMicrowave...). Fixing the other six the MapPatch way would
have meant appending eighteen duplicate arcade cabinets and microwaves purely to carry a
marker component - worse content than the bug. The fix shipped as a throwaway script
(commit 013f4c0c26a). This tool is that script, made reviewable and reusable.

DESIGN
------
Same spirit as MapPatch: manifest-driven, stdlib only, no YAML library, lexical
line-based parsing so existing bytes and mapper comments survive untouched.

Ownership WITHOUT sentinels. MapPatch can delete its block because the block is
contiguous and delimited. An attachment is three lines spliced into somebody else's
entity, so a sentinel comment would pollute the map and be lost by any real map editor.
Instead ownership is re-derived from the manifest: an attachment is "present" only if
the component type is on the expected entity AND every field matches exactly. That makes
--check and --remove deterministic without writing marker syntax into the map.

FAIL CLOSED. Refuses when: the uid does not exist; the uid's enclosing prototype is not
the expected one; the entity has no Transform; the same uid appears twice; the component
is present with DIFFERENT field values (that is drift, not idempotence, and it needs a
human).

IDEMPOTENT. Re-applying an already-correct attachment is a no-op, never a duplicate.

Usage:
  python3 map_attach.py --root <repo> --manifest <m.json> [--apply|--check|--remove]
"""
from __future__ import annotations

import argparse
import difflib
import json
import re
import sys
from pathlib import Path
from typing import Any

PROTO_RE = re.compile(r"^- proto: (\S+)\s*$")
UID_RE = re.compile(r"^  - uid: (\d+)\s*$")
COMPONENT_RE = re.compile(r"^    - type: (\S+)\s*$")
FIELD_RE = re.compile(r"^      (\w+): ?(.*)$")


class AttachError(Exception):
    """Raised for any manifest or map condition we refuse to guess about."""


def require_dict(value: Any, context: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise AttachError(f"{context} must be an object")
    return value


def require_list(value: Any, context: str) -> list[Any]:
    if not isinstance(value, list):
        raise AttachError(f"{context} must be a list")
    return value


def require_str(value: Any, context: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise AttachError(f"{context} must be a non-empty string")
    return value


def require_int(value: Any, context: str) -> int:
    if not isinstance(value, int) or isinstance(value, bool):
        raise AttachError(f"{context} must be an integer")
    return value


def render_field(value: Any) -> str:
    if isinstance(value, bool):
        return str(value).lower()
    if not isinstance(value, (str, int, float)):
        raise AttachError("component fields must contain scalar values")
    return str(value)


def render_component(component: dict[str, Any]) -> list[str]:
    ctype = require_str(component.get("type"), "component type")
    fields = require_dict(component.get("fields"), "component fields")
    if not fields:
        raise AttachError("component fields cannot be empty")
    out = [f"    - type: {ctype}"]
    for name, value in fields.items():
        if not isinstance(name, str):
            raise AttachError("component field names must be strings")
        out.append(f"      {name}: {render_field(value)}")
    return out


def entity_span(lines: list[str], uid_index: int) -> int:
    """Return the exclusive end index of the entity record beginning at uid_index."""
    j = uid_index + 1
    while j < len(lines) and not (UID_RE.match(lines[j]) or PROTO_RE.match(lines[j])):
        j += 1
    return j


def locate(lines: list[str], uid: int, expected_proto: str) -> tuple[int, int]:
    """Find the entity record for uid; refuse on absent, duplicated, or wrong proto."""
    hits: list[tuple[int, int, str]] = []
    current = None
    for i, line in enumerate(lines):
        m = PROTO_RE.match(line)
        if m:
            current = m.group(1)
            continue
        um = UID_RE.match(line)
        if um and int(um.group(1)) == uid:
            hits.append((i, entity_span(lines, i), current or "<none>"))
    if not hits:
        raise AttachError(f"uid {uid} does not exist in this map")
    if len(hits) > 1:
        raise AttachError(f"uid {uid} appears {len(hits)} times; refusing to guess")
    start, end, proto = hits[0]
    if proto != expected_proto:
        raise AttachError(
            f"uid {uid} sits under proto {proto!r}, manifest expects {expected_proto!r}"
        )
    return start, end


def component_bounds(block: list[str], ctype: str) -> tuple[int, int] | None:
    """Span of an existing component of this type within an entity block, if present."""
    for k, line in enumerate(block):
        m = COMPONENT_RE.match(line)
        if m and m.group(1) == ctype:
            j = k + 1
            while j < len(block) and FIELD_RE.match(block[j]):
                j += 1
            return k, j
    return None


def insertion_point(block: list[str], uid: int) -> int:
    """Index just past the Transform component - where Oasis puts these."""
    seen = False
    for k, line in enumerate(block):
        m = COMPONENT_RE.match(line)
        if m and m.group(1) == "Transform":
            seen = True
        elif seen and COMPONENT_RE.match(line):
            return k
    if not seen:
        raise AttachError(f"uid {uid} has no Transform component")
    return len(block)


def apply_attachments(text: str, attachments: list[dict[str, Any]]) -> tuple[str, int, int]:
    """Return (new_text, inserted, already_present). Refuses on drift."""
    lines = text.splitlines()
    pending = 0
    present = 0
    # Apply bottom-up so earlier indices stay valid.
    resolved = []
    for index, raw in enumerate(attachments):
        att = require_dict(raw, f"attachment {index}")
        uid = require_int(att.get("uid"), f"attachment {index} uid")
        proto = require_str(att.get("proto"), f"attachment {index} proto")
        component = require_dict(att.get("component"), f"attachment {index} component")
        rendered = render_component(component)
        start, end = locate(lines, uid, proto)
        resolved.append((start, end, uid, rendered, component))
    for start, end, uid, rendered, component in sorted(resolved, reverse=True):
        block = lines[start + 1 : end]
        ctype = component["type"]
        bounds = component_bounds(block, ctype)
        if bounds is not None:
            lo, hi = bounds
            if block[lo:hi] == rendered:
                present += 1
                continue
            raise AttachError(
                f"uid {uid} already carries {ctype} with different values - "
                f"drift, not idempotence; resolve by hand"
            )
        at = insertion_point(block, uid)
        block[at:at] = rendered
        lines[start + 1 : end] = block
        pending += 1
    return "\n".join(lines) + "\n", pending, present


def remove_attachments(text: str, attachments: list[dict[str, Any]]) -> tuple[str, int]:
    lines = text.splitlines()
    removed = 0
    resolved = []
    for index, raw in enumerate(attachments):
        att = require_dict(raw, f"attachment {index}")
        uid = require_int(att.get("uid"), f"attachment {index} uid")
        proto = require_str(att.get("proto"), f"attachment {index} proto")
        component = require_dict(att.get("component"), f"attachment {index} component")
        rendered = render_component(component)
        start, end = locate(lines, uid, proto)
        resolved.append((start, end, rendered, component))
    for start, end, rendered, component in sorted(resolved, reverse=True):
        block = lines[start + 1 : end]
        bounds = component_bounds(block, component["type"])
        if bounds is None:
            continue
        lo, hi = bounds
        if block[lo:hi] != rendered:
            raise AttachError(
                f"refusing to remove {component['type']}: on-disk values differ from the manifest"
            )
        del block[lo:hi]
        lines[start + 1 : end] = block
        removed += 1
    return "\n".join(lines) + "\n", removed


def unified_diff(path: Path, before: str, after: str, root: Path) -> str:
    rel = path.relative_to(root)
    return "".join(
        difflib.unified_diff(
            before.splitlines(keepends=True),
            after.splitlines(keepends=True),
            fromfile=f"a/{rel}",
            tofile=f"b/{rel}",
        )
    )


def load_manifest(manifest_path: Path) -> tuple[str, list[dict[str, Any]]]:
    try:
        manifest = require_dict(
            json.loads(manifest_path.read_text(encoding="utf-8")), "manifest"
        )
    except (OSError, json.JSONDecodeError) as error:
        raise AttachError(f"cannot read manifest {manifest_path}: {error}") from error
    if manifest.get("schema_version") != 1:
        raise AttachError("manifest schema_version must be 1")
    patch_id = require_str(manifest.get("patch_id"), "manifest patch_id")
    if not re.fullmatch(r"[a-z0-9][a-z0-9-]*", patch_id):
        raise AttachError("manifest patch_id must contain lowercase letters, digits, and hyphens")
    maps = require_list(manifest.get("maps"), "manifest maps")
    if not maps:
        raise AttachError("manifest maps cannot be empty")
    return patch_id, maps


def run(args: argparse.Namespace) -> int:
    root = args.root.resolve()
    _, maps = load_manifest(args.manifest.resolve())

    changes: list[tuple[Path, str]] = []
    pending_total = present_total = 0
    seen: set[Path] = set()

    for index, raw_entry in enumerate(maps):
        entry = require_dict(raw_entry, f"manifest map {index}")
        rel = require_str(entry.get("path"), f"manifest map {index} path")
        path = (root / rel).resolve()
        if not str(path).startswith(str(root)):
            raise AttachError(f"map path escapes the repository root: {rel}")
        if path in seen:
            raise AttachError(f"manifest repeats map path {rel}")
        seen.add(path)
        if not path.is_file():
            raise AttachError(f"map does not exist: {rel}")
        attachments = require_list(entry.get("attachments"), f"manifest map {index} attachments")
        if not attachments:
            raise AttachError(f"manifest map {index} attachments cannot be empty")

        text = path.read_text(encoding="utf-8")
        if args.remove:
            desired, removed = remove_attachments(text, attachments)
            pending_total += removed
        else:
            desired, pending, present = apply_attachments(text, attachments)
            pending_total += pending
            present_total += present
        if text != desired:
            changes.append((path, desired))
            if not args.check:
                print(unified_diff(path, text, desired, root), end="")

    if args.check:
        for path, _ in changes:
            print(f"map_attach: PENDING: {path.relative_to(root)}", file=sys.stderr)
        return 1 if changes else 0

    if args.apply and changes:
        for path, desired in changes:
            path.write_text(desired, encoding="utf-8")
        verb = "removed" if args.remove else "attached"
        print(f"map_attach: {verb} {pending_total} component(s) across {len(changes)} map(s)")
    elif not args.apply:
        print(
            f"map_attach: dry-run; {pending_total} pending, {present_total} already present, "
            "nothing written"
        )
    else:
        print("map_attach: already in requested state")
    return 0


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    parser.add_argument("--root", type=Path, default=Path.cwd(), help="Game repository root")
    parser.add_argument("--manifest", type=Path, required=True, help="JSON attachment manifest")
    parser.add_argument("--apply", action="store_true", help="write the validated changes")
    parser.add_argument("--check", action="store_true", help="require every attachment to match exactly")
    parser.add_argument("--remove", action="store_true", help="remove exact owned attachments")
    args = parser.parse_args(argv)
    if args.check and (args.apply or args.remove):
        parser.error("--check cannot be combined with --apply or --remove")
    return args


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    try:
        return run(args)
    except AttachError as error:
        print(f"map_attach: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
