#!/usr/bin/env python3
"""Fail-closed, manifest-driven additive patching for SS14 v7 map YAML.

This Game-local tool deliberately does not reuse OPS ``deploy/map_place.py``.
That utility searches for heuristically "enclosed" empty cells and assigns new
UIDs; it has no exact manifest, tile decoding, ownership sentinels, drift check,
or reversible operation.  This tool instead applies reviewable, byte-stable
patch blocks whose coordinates, UIDs, floor types, occupants, and anchors are
all pinned in source control.

Only the generated block is ever changed.  Existing YAML is parsed lexically
and preserved byte-for-byte so comments and engine-specific serialization are
not normalized by a general YAML library.
"""

from __future__ import annotations

import argparse
import base64
import difflib
import json
import math
import os
from pathlib import Path
import re
import struct
import sys
import tempfile
from typing import Any


MAP_ROOT = Path("Resources/Maps/_Solreign")
CHUNK_SIZE = 16
TILE_BYTES = 7
UNDERFLOOR_OCCUPANT_PREFIXES = ("Cable", "GasPipe")

PROTO_START_RE = re.compile(r"(?m)^- proto: (?P<proto>\S+)\n")
UID_START_RE = re.compile(r"(?m)^\s+- uid: (?P<uid>\d+)\n")
POS_RE = re.compile(r"(?m)^(?P<indent>\s+)pos: (?P<x>-?\d+(?:\.\d+)?),(?P<y>-?\d+(?:\.\d+)?)$")
PARENT_RE = re.compile(r"(?m)^\s+parent: (?P<parent>\d+)$")
TILEMAP_RE = re.compile(r"(?m)^  (?P<id>\d+): (?P<name>\S+)$")
CHUNK_RE = re.compile(
    r"(?m)^\s+(?:- )?ind: (?P<x>-?\d+),(?P<y>-?\d+)\n"
    r"\s+tiles: (?P<data>[A-Za-z0-9+/=]+)$"
)


class PatchError(Exception):
    """A validation error that must fail closed without writing maps."""


def require_dict(value: Any, context: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise PatchError(f"{context} must be an object")
    return value


def require_list(value: Any, context: str) -> list[Any]:
    if not isinstance(value, list):
        raise PatchError(f"{context} must be an array")
    return value


def require_str(value: Any, context: str) -> str:
    if not isinstance(value, str) or not value:
        raise PatchError(f"{context} must be a non-empty string")
    return value


def require_int(value: Any, context: str) -> int:
    if not isinstance(value, int) or isinstance(value, bool):
        raise PatchError(f"{context} must be an integer")
    return value


def require_position(value: Any, context: str) -> tuple[float, float]:
    items = require_list(value, context)
    if len(items) != 2 or any(not isinstance(item, (int, float)) or isinstance(item, bool) for item in items):
        raise PatchError(f"{context} must contain two numbers")
    position = (float(items[0]), float(items[1]))
    for coordinate in position:
        if not math.isfinite(coordinate) or coordinate - math.floor(coordinate) != 0.5:
            raise PatchError(f"{context} coordinates must be .5-centered")
    return position


def format_coordinate(value: float) -> str:
    return f"{value:.1f}"


def is_underfloor_occupant(prototype: str) -> bool:
    """Only cable and sealed pipe segments may share a fixture's floor cell."""
    return prototype.startswith(UNDERFLOOR_OCCUPANT_PREFIXES)


def split_entity_records(text: str) -> list[dict[str, Any]]:
    """Return prototype/UID/transform records without interpreting all YAML."""
    records: list[dict[str, Any]] = []
    prototype_matches = list(PROTO_START_RE.finditer(text))
    for proto_index, prototype_match in enumerate(prototype_matches):
        block_end = (
            prototype_matches[proto_index + 1].start()
            if proto_index + 1 < len(prototype_matches)
            else len(text)
        )
        block = text[prototype_match.start():block_end]
        uid_matches = list(UID_START_RE.finditer(block))
        for uid_index, uid_match in enumerate(uid_matches):
            record_end = uid_matches[uid_index + 1].start() if uid_index + 1 < len(uid_matches) else len(block)
            record_text = block[uid_match.start():record_end]
            position_match = POS_RE.search(record_text)
            parent_match = PARENT_RE.search(record_text)
            records.append(
                {
                    "proto": prototype_match.group("proto"),
                    "uid": int(uid_match.group("uid")),
                    "pos": (
                        (float(position_match.group("x")), float(position_match.group("y")))
                        if position_match
                        else None
                    ),
                    "parent": int(parent_match.group("parent")) if parent_match else None,
                    "text": record_text,
                }
            )
    return records


def sentinel_strings(patch_id: str) -> tuple[str, str]:
    return (
        f"# BEGIN SOLREIGN MAP PATCH {patch_id}\n",
        f"# END SOLREIGN MAP PATCH {patch_id}\n",
    )


def render_block(patch_id: str, map_entry: dict[str, Any]) -> str:
    begin, end = sentinel_strings(patch_id)
    lines = [
        begin.rstrip("\n"),
        "# Generated by Tools/_Solreign/MapPatch/map_patch.py; edit the manifest, not this block.",
    ]
    placements = require_list(map_entry.get("placements"), "map placements")
    for index, raw_placement in enumerate(placements):
        placement = require_dict(raw_placement, f"placement {index}")
        proto = require_str(placement.get("proto"), f"placement {index} proto")
        uid = require_int(placement.get("uid"), f"placement {index} uid")
        x, y = require_position(placement.get("pos"), f"placement {index} pos")
        parent = require_int(placement.get("parent"), f"placement {index} parent")
        component_raw = placement.get("component")
        if component_raw is None:
            # Map-level component override is optional. A placement whose
            # prototype already carries every component it needs (e.g.
            # SolreignGolfBall — physics, stroke counter, and sound are
            # baked in) declares no override here and renders as a bare
            # Transform-only entity.
            component = None
        else:
            component = require_dict(component_raw, f"placement {index} component")
            component_type = require_str(component.get("type"), f"placement {index} component type")
            fields = require_dict(component.get("fields"), f"placement {index} component fields")
            if not fields:
                raise PatchError(f"placement {index} component fields cannot be empty")

        lines.extend(
            [
                f"- proto: {proto}",
                "  entities:",
                f"  - uid: {uid}",
                "    components:",
                "    - type: Transform",
                f"      pos: {format_coordinate(x)},{format_coordinate(y)}",
                f"      parent: {parent}",
            ]
        )
        if component is not None:
            component_type = component["type"]
            fields = component["fields"]
            lines.append(f"    - type: {component_type}")
            for field_name, field_value in fields.items():
                if not isinstance(field_name, str) or not isinstance(field_value, (str, int, float, bool)):
                    raise PatchError(f"placement {index} component fields must contain scalar values")
                rendered_value = str(field_value).lower() if isinstance(field_value, bool) else str(field_value)
                lines.append(f"      {field_name}: {rendered_value}")
    lines.append(end.rstrip("\n"))
    return "\n".join(lines) + "\n"


def extract_owned_block(
    text: str,
    patch_id: str,
    expected_block: str,
    accepted_existing_block: str | None = None,
) -> tuple[str, str]:
    """Return (state, baseline), rejecting partial, duplicate, or edited blocks."""
    begin, end = sentinel_strings(patch_id)
    begin_count = text.count(begin)
    end_count = text.count(end)
    if begin_count == 0 and end_count == 0:
        return "absent", text
    if begin_count != 1 or end_count != 1:
        raise PatchError(f"owned sentinel state for {patch_id} is partial or duplicated")
    start = text.index(begin)
    finish = text.index(end, start) + len(end)
    actual_block = text[start:finish]
    if actual_block != expected_block:
        if accepted_existing_block is None or actual_block != accepted_existing_block:
            raise PatchError(
                f"owned block for {patch_id} was modified; refusing to overwrite or remove it"
            )
        state = "outdated"
    else:
        state = "applied"
    return state, text[:start] + text[finish:]


def find_tile_name(
    baseline: str,
    records: list[dict[str, Any]],
    parent: int,
    position: tuple[float, float],
) -> str:
    grids = [record for record in records if record["uid"] == parent and "    - type: MapGrid\n" in record["text"]]
    if len(grids) != 1:
        raise PatchError(f"parent uid {parent} does not identify exactly one MapGrid")

    tilemap = {int(match.group("id")): match.group("name") for match in TILEMAP_RE.finditer(baseline)}
    if not tilemap:
        raise PatchError("map has no readable tilemap")

    tile_x, tile_y = math.floor(position[0]), math.floor(position[1])
    chunk_x, chunk_y = tile_x // CHUNK_SIZE, tile_y // CHUNK_SIZE
    chunks: dict[tuple[int, int], str] = {}
    for match in CHUNK_RE.finditer(grids[0]["text"]):
        key = (int(match.group("x")), int(match.group("y")))
        if key in chunks:
            raise PatchError(f"MapGrid parent {parent} has duplicate chunk {key}")
        chunks[key] = match.group("data")
    encoded = chunks.get((chunk_x, chunk_y))
    if encoded is None:
        raise PatchError(f"no tile chunk at {chunk_x},{chunk_y} for position {position}")
    try:
        raw = base64.b64decode(encoded, validate=True)
    except (ValueError, base64.binascii.Error) as error:
        raise PatchError(f"invalid base64 tile chunk at {chunk_x},{chunk_y}") from error
    expected_length = CHUNK_SIZE * CHUNK_SIZE * TILE_BYTES
    if len(raw) != expected_length:
        raise PatchError(
            f"tile chunk {chunk_x},{chunk_y} has {len(raw)} bytes; expected {expected_length}"
        )
    local_x = tile_x - chunk_x * CHUNK_SIZE
    local_y = tile_y - chunk_y * CHUNK_SIZE
    offset = (local_y * CHUNK_SIZE + local_x) * TILE_BYTES
    tile_id = struct.unpack_from("<I", raw, offset)[0]
    if tile_id not in tilemap:
        raise PatchError(f"tile id {tile_id} is absent from tilemap")
    return tilemap[tile_id]


def is_wall_prototype(prototype: str) -> bool:
    return prototype.startswith("Wall")


def is_latejoin_prototype(prototype: str) -> bool:
    # Both spellings ship on the rotation: SpawnPointLatejoin and
    # CryogenicSleepUnitSpawnerLateJoin (terminus cryo room).
    return "latejoin" in prototype.lower()


def suggest_candidates(
    text: str,
    parent: int,
    center: tuple[float, float],
    radius: int,
) -> list[dict[str, Any]]:
    """Enumerate placement-candidate cells around ``center`` on grid ``parent``.

    A cell is a candidate only if its decoded tile is not Space (the trap that
    put two beacons in space on 2026-08-02), it holds no wall (a wallmount
    target is the floor tile BESIDE a wall, never the wall tile), and it holds
    no latejoin spawn point. Occupants are reported exactly so the caller can
    judge overlap; wall adjacency is flagged for the wallmount float ratchet.
    """
    if radius < 0:
        raise PatchError("suggest radius cannot be negative")
    records = split_entity_records(text)

    occupancy: dict[tuple[int, int], set[tuple[str, int]]] = {}
    for record in records:
        if record["parent"] == parent and record["pos"] is not None:
            cell = (math.floor(record["pos"][0]), math.floor(record["pos"][1]))
            occupancy.setdefault(cell, set()).add((record["proto"], record["uid"]))

    def tile_at(cell: tuple[int, int]) -> str | None:
        try:
            return find_tile_name(text, records, parent, (cell[0] + 0.5, cell[1] + 0.5))
        except PatchError as error:
            if str(error).startswith("no tile chunk"):
                return None
            raise

    center_cell = (math.floor(center[0]), math.floor(center[1]))
    candidates: list[dict[str, Any]] = []
    for cell_y in range(center_cell[1] - radius, center_cell[1] + radius + 1):
        for cell_x in range(center_cell[0] - radius, center_cell[0] + radius + 1):
            cell = (cell_x, cell_y)
            tile_name = tile_at(cell)
            if tile_name is None or tile_name == "Space":
                continue
            occupants = occupancy.get(cell, set())
            if any(is_wall_prototype(proto) for proto, _ in occupants):
                continue
            if any(is_latejoin_prototype(proto) for proto, _ in occupants):
                continue
            wall_adjacent = any(
                any(
                    is_wall_prototype(proto)
                    for proto, _ in occupancy.get((cell_x + dx, cell_y + dy), set())
                )
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))
            )
            candidates.append(
                {
                    "cell": [cell_x, cell_y],
                    "pos": [cell_x + 0.5, cell_y + 0.5],
                    "tile": tile_name,
                    "occupants": sorted([proto, uid] for proto, uid in occupants),
                    "wall_adjacent": wall_adjacent,
                    "distance": max(abs(cell_x - center_cell[0]), abs(cell_y - center_cell[1])),
                }
            )
    candidates.sort(key=lambda candidate: (candidate["distance"], candidate["cell"]))
    return candidates


def plan_moves(text: str, moves: list[Any]) -> tuple[list[dict[str, Any]], str]:
    """Plan (and textually apply) block-scoped moves of UNOWNED entities.

    The beacon_move_v2 pattern: every move pins proto, uid, parent, both
    endpoints, the decoded target tile, and the exact target-cell occupant
    set. An entity at neither endpoint is drift and fails closed. Only the
    matched record's pos line is rewritten; every other byte is preserved.
    """
    report: list[dict[str, Any]] = []
    current = text
    for index, raw_move in enumerate(moves):
        move = require_dict(raw_move, f"move {index}")
        proto = require_str(move.get("proto"), f"move {index} proto")
        uid = require_int(move.get("uid"), f"move {index} uid")
        parent = require_int(move.get("parent"), f"move {index} parent")
        from_pos = require_position(move.get("from"), f"move {index} from")
        to_pos = require_position(move.get("to"), f"move {index} to")
        expected_tile = require_str(move.get("expected_tile"), f"move {index} expected_tile")
        if expected_tile == "Space":
            raise PatchError(
                f"move {index} {proto}: refusing a Space target tile — "
                "the 2026-08-02 trap put two beacons in space"
            )
        expected_occupants_raw = require_list(
            move.get("expected_occupants"), f"move {index} expected_occupants"
        )
        expected_occupants = {
            (
                require_str(require_dict(occupant, f"move {index} occupant").get("proto"), "occupant proto"),
                require_int(require_dict(occupant, f"move {index} occupant").get("uid"), "occupant uid"),
            )
            for occupant in expected_occupants_raw
        }

        records = split_entity_records(current)
        matches = [
            record
            for record in records
            if record["proto"] == proto and record["uid"] == uid and record["parent"] == parent
        ]
        if len(matches) != 1:
            raise PatchError(
                f"move {index}: {proto} uid {uid} parent {parent} matches {len(matches)} records; need exactly 1"
            )
        record = matches[0]
        if record["pos"] == to_pos:
            report.append({"proto": proto, "uid": uid, "state": "applied"})
            continue
        if record["pos"] != from_pos:
            raise PatchError(
                f"move {index}: {proto} uid {uid} drifted — at {record['pos']}, "
                f"neither from {from_pos} nor to {to_pos}"
            )

        tile_name = find_tile_name(current, records, parent, to_pos)
        if tile_name == "Space":
            raise PatchError(f"move {index} {proto}: target tile decodes to Space; refusing")
        if tile_name != expected_tile:
            raise PatchError(
                f"move {index} {proto}: expected tile {expected_tile}, found {tile_name}"
            )
        target_cell = (math.floor(to_pos[0]), math.floor(to_pos[1]))
        actual_occupants = {
            (other["proto"], other["uid"])
            for other in records
            if other["parent"] == parent
            and other["uid"] != uid
            and other["pos"] is not None
            and (math.floor(other["pos"][0]), math.floor(other["pos"][1])) == target_cell
        }
        if actual_occupants != expected_occupants:
            raise PatchError(
                f"move {index} {proto}: target occupant set drifted; "
                f"expected {sorted(expected_occupants)}, found {sorted(actual_occupants)}"
            )

        pos_match = POS_RE.search(record["text"])
        if pos_match is None:
            raise PatchError(f"move {index} {proto}: record has no parseable pos line")
        old_pos_line = pos_match.group(0)
        new_pos_line = (
            f"{pos_match.group('indent')}pos: {format_coordinate(to_pos[0])},{format_coordinate(to_pos[1])}"
        )
        if record["text"].count(old_pos_line) != 1:
            raise PatchError(f"move {index} {proto}: pos line is not unique inside the record")
        if current.count(record["text"]) != 1:
            raise PatchError(f"move {index} {proto}: record text is not unique in the map")
        current = current.replace(record["text"], record["text"].replace(old_pos_line, new_pos_line, 1), 1)
        report.append({"proto": proto, "uid": uid, "state": "pending"})
    return report, current


def validate_map(
    path: Path,
    text: str,
    patch_id: str,
    map_entry: dict[str, Any],
    accepted_existing_block: str | None = None,
) -> tuple[str, str, str]:
    if not re.search(r"(?m)^meta:\n  format: 7$", text):
        raise PatchError(f"{path}: only exact SS14 map format 7 is supported")

    expected_block = render_block(patch_id, map_entry)
    state, baseline = extract_owned_block(
        text,
        patch_id,
        expected_block,
        accepted_existing_block,
    )
    placements = require_list(map_entry.get("placements"), f"{path} placements")
    if not placements:
        raise PatchError(f"{path}: placements cannot be empty")
    records = split_entity_records(baseline)

    anchor = require_dict(map_entry.get("anchor"), f"{path} anchor")
    anchor_proto = require_str(anchor.get("proto"), f"{path} anchor proto")
    anchor_uid = require_int(anchor.get("uid"), f"{path} anchor uid")
    anchor_pos = require_position(anchor.get("pos"), f"{path} anchor pos")
    anchor_parent = require_int(anchor.get("parent"), f"{path} anchor parent")
    anchor_matches = [
        record
        for record in records
        if record["proto"] == anchor_proto
        and record["uid"] == anchor_uid
        and record["pos"] == anchor_pos
        and record["parent"] == anchor_parent
    ]
    if len(anchor_matches) != 1:
        raise PatchError(
            f"{path}: anchor {anchor_proto} uid {anchor_uid} at {anchor_pos} parent {anchor_parent} drifted"
        )

    max_distance = require_int(map_entry.get("max_manhattan_tiles"), f"{path} max_manhattan_tiles")
    if max_distance < 0:
        raise PatchError(f"{path}: max_manhattan_tiles cannot be negative")

    existing_uids = {record["uid"] for record in records}
    placement_uids: set[int] = set()
    for index, raw_placement in enumerate(placements):
        placement = require_dict(raw_placement, f"{path} placement {index}")
        proto = require_str(placement.get("proto"), f"{path} placement {index} proto")
        uid = require_int(placement.get("uid"), f"{path} placement {index} uid")
        position = require_position(placement.get("pos"), f"{path} placement {index} pos")
        parent = require_int(placement.get("parent"), f"{path} placement {index} parent")
        expected_tile = require_str(
            placement.get("expected_tile"),
            f"{path} placement {index} expected_tile",
        )

        if uid in placement_uids or uid in existing_uids:
            raise PatchError(f"{path}: placement uid {uid} collides with an existing or planned uid")
        placement_uids.add(uid)
        if any(record["proto"] == proto for record in records):
            raise PatchError(f"{path}: target prototype {proto} already exists outside the owned block")
        if parent != anchor_parent:
            raise PatchError(f"{path}: placement {proto} parent {parent} differs from anchor parent {anchor_parent}")
        distance = abs(math.floor(position[0]) - math.floor(anchor_pos[0])) + abs(
            math.floor(position[1]) - math.floor(anchor_pos[1])
        )
        if distance > max_distance:
            raise PatchError(
                f"{path}: placement {proto} is {distance} tiles from anchor; maximum is {max_distance}"
            )

        tile_name = find_tile_name(baseline, records, parent, position)
        if tile_name != expected_tile:
            raise PatchError(
                f"{path}: placement {proto} expected tile {expected_tile}, found {tile_name}"
            )

        expected_occupants_raw = require_list(
            placement.get("expected_occupants"),
            f"{path} placement {index} expected_occupants",
        )
        expected_occupants: set[tuple[str, int]] = set()
        for occupant_index, raw_occupant in enumerate(expected_occupants_raw):
            occupant = require_dict(
                raw_occupant,
                f"{path} placement {index} expected occupant {occupant_index}",
            )
            expected_occupants.add(
                (
                    require_str(occupant.get("proto"), "expected occupant proto"),
                    require_int(occupant.get("uid"), "expected occupant uid"),
                )
            )
        prohibited_occupants = sorted(
            prototype
            for prototype, _ in expected_occupants
            if not is_underfloor_occupant(prototype)
        )
        if prohibited_occupants:
            raise PatchError(
                f"{path}: placement {proto} cannot overlap physical occupant prototype(s): "
                f"{prohibited_occupants}"
            )
        target_cell = (math.floor(position[0]), math.floor(position[1]))
        actual_occupants = {
            (record["proto"], record["uid"])
            for record in records
            if record["parent"] == parent
            and record["pos"] is not None
            and (math.floor(record["pos"][0]), math.floor(record["pos"][1])) == target_cell
        }
        if actual_occupants != expected_occupants:
            raise PatchError(
                f"{path}: placement {proto} occupant set drifted; "
                f"expected {sorted(expected_occupants)}, found {sorted(actual_occupants)}"
            )

    return state, baseline, expected_block


def resolve_map_path(root: Path, raw_path: Any) -> Path:
    relative = Path(require_str(raw_path, "map path"))
    if relative.is_absolute():
        raise PatchError(f"map path must be relative and contained by {MAP_ROOT}")
    map_root = (root / MAP_ROOT).resolve()
    candidate = (root / relative).resolve()
    try:
        candidate.relative_to(map_root)
    except ValueError as error:
        raise PatchError(f"map path {relative} is outside {MAP_ROOT}") from error
    if candidate.suffix not in {".yml", ".yaml"}:
        raise PatchError(f"map path {relative} is not YAML")
    if not candidate.is_file():
        raise PatchError(f"map path {relative} does not exist")
    return candidate


def unified_diff(path: Path, before: str, after: str, root: Path) -> str:
    relative = path.relative_to(root.resolve())
    return "".join(
        difflib.unified_diff(
            before.splitlines(keepends=True),
            after.splitlines(keepends=True),
            fromfile=f"a/{relative}",
            tofile=f"b/{relative}",
        )
    )


def add_owned_block(baseline: str, block: str) -> str:
    """Place additions inside the existing YAML document, if it has an end marker."""
    if baseline.endswith("...\n"):
        return baseline[:-4] + block + "...\n"
    return baseline + block


def build_applied_text(path: Path, baseline: str, expected_block: str) -> str:
    return add_owned_block(baseline, expected_block)


def write_all_with_rollback(changes: list[tuple[Path, str]]) -> None:
    """Prepare every replacement and byte-exact backup, then roll back a partial commit."""
    prepared: list[tuple[Path, Path, Path]] = []
    replaced: list[tuple[Path, Path]] = []
    preserved_backups: set[Path] = set()
    try:
        for path, content in changes:
            descriptor, temporary_name = tempfile.mkstemp(
                prefix=f".{path.name}.",
                suffix=".tmp",
                dir=path.parent,
            )
            temporary = Path(temporary_name)
            with os.fdopen(descriptor, "w", encoding="utf-8", newline="") as stream:
                stream.write(content)
                stream.flush()
                os.fsync(stream.fileno())

            backup_descriptor, backup_name = tempfile.mkstemp(
                prefix=f".{path.name}.",
                suffix=".bak",
                dir=path.parent,
            )
            backup = Path(backup_name)
            with path.open("rb") as source, os.fdopen(backup_descriptor, "wb") as destination:
                while chunk := source.read(1024 * 1024):
                    destination.write(chunk)
                destination.flush()
                os.fsync(destination.fileno())

            prepared.append((path, temporary, backup))

        try:
            for path, temporary, backup in prepared:
                os.replace(temporary, path)
                replaced.append((path, backup))
        except OSError as commit_error:
            rollback_errors: list[tuple[Path, Path]] = []
            for path, backup in reversed(replaced):
                try:
                    os.replace(backup, path)
                except OSError:
                    preserved_backups.add(backup)
                    rollback_errors.append((path, backup))

            if rollback_errors:
                raise PatchError(
                    "map write failed and rollback could not restore destination(s); "
                    "byte-exact backup(s) preserved: "
                    + ", ".join(
                        f"{path} <- {backup}"
                        for path, backup in rollback_errors
                    )
                ) from commit_error
            raise PatchError("map write failed; every replaced map was restored") from commit_error
    finally:
        for _, temporary, backup in prepared:
            for residue in (temporary, backup):
                if residue in preserved_backups:
                    continue
                try:
                    residue.unlink()
                except FileNotFoundError:
                    pass


def run(args: argparse.Namespace) -> int:
    root = args.root.resolve()
    manifest_path = args.manifest.resolve()
    try:
        manifest = require_dict(json.loads(manifest_path.read_text(encoding="utf-8")), "manifest")
    except (OSError, json.JSONDecodeError) as error:
        raise PatchError(f"cannot read manifest {manifest_path}: {error}") from error
    if manifest.get("schema_version") != 1:
        raise PatchError("manifest schema_version must be 1")
    patch_id = require_str(manifest.get("patch_id"), "manifest patch_id")
    if not re.fullmatch(r"[a-z0-9][a-z0-9-]*", patch_id):
        raise PatchError("manifest patch_id must contain lowercase letters, digits, and hyphens")
    maps = require_list(manifest.get("maps"), "manifest maps")
    if not maps:
        raise PatchError("manifest maps cannot be empty")

    prior_entries_by_path: dict[str, dict[str, Any]] = {}
    if args.update_from is not None:
        prior_path = args.update_from.resolve()
        try:
            prior_manifest = require_dict(
                json.loads(prior_path.read_text(encoding="utf-8")),
                "prior manifest",
            )
        except (OSError, json.JSONDecodeError) as error:
            raise PatchError(f"cannot read prior manifest {prior_path}: {error}") from error
        if prior_manifest.get("schema_version") != 1:
            raise PatchError("prior manifest schema_version must be 1")
        if prior_manifest.get("patch_id") != patch_id:
            raise PatchError("prior manifest patch_id must match the new manifest")
        for index, raw_entry in enumerate(
            require_list(prior_manifest.get("maps"), "prior manifest maps")
        ):
            entry = require_dict(raw_entry, f"prior manifest map {index}")
            entry_path = require_str(entry.get("path"), f"prior manifest map {index} path")
            if entry_path in prior_entries_by_path:
                raise PatchError(f"prior manifest repeats map path {entry_path}")
            prior_entries_by_path[entry_path] = entry

    analyses: list[tuple[Path, str, str, str, str]] = []
    seen_paths: set[Path] = set()
    for index, raw_map_entry in enumerate(maps):
        map_entry = require_dict(raw_map_entry, f"manifest map {index}")
        path = resolve_map_path(root, map_entry.get("path"))
        if path in seen_paths:
            raise PatchError(f"manifest repeats map path {path}")
        seen_paths.add(path)
        text = path.read_text(encoding="utf-8")
        raw_relative_path = require_str(map_entry.get("path"), f"manifest map {index} path")
        accepted_existing_block = None
        if args.update_from is not None:
            prior_entry = prior_entries_by_path.get(raw_relative_path)
            if prior_entry is None:
                raise PatchError(
                    f"prior manifest has no exact entry for map path {raw_relative_path}"
                )
            accepted_existing_block = render_block(patch_id, prior_entry)
        state, baseline, expected_block = validate_map(
            path,
            text,
            patch_id,
            map_entry,
            accepted_existing_block,
        )
        analyses.append((path, text, state, baseline, expected_block))

    if args.check:
        pending = False
        for path, _, state, _, _ in analyses:
            if state == "applied":
                continue
            pending = True
            print(f"map_patch: PENDING: {path.relative_to(root)}", file=sys.stderr)
        return 1 if pending else 0

    changes: list[tuple[Path, str]] = []
    for path, text, state, baseline, expected_block in analyses:
        if args.remove:
            desired = baseline
        elif state == "applied":
            desired = text
        else:
            desired = build_applied_text(path, baseline, expected_block)
        if text != desired:
            changes.append((path, desired))
            print(unified_diff(path, text, desired, root), end="")

    if args.apply and changes:
        write_all_with_rollback(changes)
        print(f"map_patch: applied {len(changes)} map change(s)")
    elif not args.apply:
        print(f"map_patch: dry-run; {len(changes)} map change(s), nothing written")
    else:
        print("map_patch: already in requested state")
    return 0


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--root", type=Path, default=Path.cwd(), help="Game repository root")
    parser.add_argument("--manifest", type=Path, required=True, help="JSON patch manifest")
    parser.add_argument("--apply", action="store_true", help="write the validated changes")
    parser.add_argument("--check", action="store_true", help="require every owned block to match exactly")
    parser.add_argument("--remove", action="store_true", help="remove exact owned blocks")
    parser.add_argument(
        "--update-from",
        type=Path,
        help="prior manifest whose exact generated blocks are authorized for replacement",
    )
    args = parser.parse_args(argv)
    if args.check and (args.apply or args.remove):
        parser.error("--check cannot be combined with --apply or --remove")
    if args.update_from is not None and (not args.apply or args.check or args.remove):
        parser.error("--update-from requires --apply and cannot combine with --check or --remove")
    return args


def parse_suggest_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        prog="map_patch.py suggest",
        description="Enumerate decoded-tile placement candidates around a point.",
    )
    parser.add_argument("--root", type=Path, default=Path.cwd(), help="Game repository root")
    parser.add_argument("--map", dest="map_path", required=True, help="map YAML path relative to the root")
    parser.add_argument("--parent", type=int, required=True, help="MapGrid entity uid")
    parser.add_argument("--near", required=True, help="center position as X,Y (entity coordinates)")
    parser.add_argument("--radius", type=int, default=3, help="Chebyshev tile radius to scan")
    return parser.parse_args(argv)


def run_suggest(args: argparse.Namespace) -> int:
    path = resolve_map_path(args.root, args.map_path)
    text = path.read_text(encoding="utf-8")
    near_parts = str(args.near).split(",")
    try:
        center = require_position([float(part) for part in near_parts], "--near")
    except ValueError as error:
        raise PatchError("--near must be X,Y with two numbers") from error
    candidates = suggest_candidates(text, parent=args.parent, center=center, radius=args.radius)
    print(json.dumps({"map": str(args.map_path), "candidates": candidates}, indent=2))
    if not candidates:
        print("map_patch: suggest: no eligible cell in range", file=sys.stderr)
        return 1
    return 0


def parse_move_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        prog="map_patch.py move",
        description="Manifest-driven block-scoped mover for unowned entities.",
    )
    parser.add_argument("--root", type=Path, default=Path.cwd(), help="Game repository root")
    parser.add_argument("--manifest", type=Path, required=True, help="JSON move manifest")
    parser.add_argument("--apply", action="store_true", help="write the validated moves")
    parser.add_argument("--check", action="store_true", help="require every move to already be applied")
    args = parser.parse_args(argv)
    if args.apply and args.check:
        parser.error("--check cannot be combined with --apply")
    return args


def run_move(args: argparse.Namespace) -> int:
    try:
        manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise PatchError(f"cannot read move manifest {args.manifest}: {error}") from error
    maps = require_list(require_dict(manifest, "move manifest").get("maps"), "manifest maps")
    if not maps:
        raise PatchError("move manifest maps cannot be empty")

    all_reports: list[dict[str, Any]] = []
    changes: list[tuple[Path, str]] = []
    for map_index, raw_entry in enumerate(maps):
        entry = require_dict(raw_entry, f"manifest map {map_index}")
        path = resolve_map_path(args.root, entry.get("path"))
        moves = require_list(entry.get("moves"), f"manifest map {map_index} moves")
        if not moves:
            raise PatchError(f"manifest map {map_index} moves cannot be empty")
        text = path.read_text(encoding="utf-8")
        report, new_text = plan_moves(text, moves)
        for item in report:
            all_reports.append({"map": str(entry.get("path")), **item})
        if new_text != text:
            if not args.check:
                print(unified_diff(path, text, new_text, args.root), end="")
            changes.append((path, new_text))

    print(json.dumps({"moves": all_reports}, indent=2))
    pending = [item for item in all_reports if item["state"] == "pending"]
    if args.check:
        if pending:
            print(f"map_patch: move --check: {len(pending)} move(s) still pending", file=sys.stderr)
            return 1
        return 0
    if args.apply and changes:
        write_all_with_rollback(changes)
        print(f"map_patch: move: wrote {len(changes)} map file(s)")
    elif not args.apply:
        print("map_patch: move: dry-run; nothing written")
    return 0


def main(argv: list[str] | None = None) -> int:
    arguments = list(sys.argv[1:] if argv is None else argv)
    try:
        if arguments and arguments[0] == "suggest":
            return run_suggest(parse_suggest_args(arguments[1:]))
        if arguments and arguments[0] == "move":
            return run_move(parse_move_args(arguments[1:]))
        return run(parse_args(arguments))
    except PatchError as error:
        print(f"map_patch: ERROR: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
