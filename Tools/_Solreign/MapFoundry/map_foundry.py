#!/usr/bin/env python3
"""Read-only map-pool inventory and MapPatch manifest validator.

Tools/_Solreign/MapFoundry is a bounded, offline companion to
Tools/_Solreign/MapPatch. It performs two operations only:

* inventory: walk the prototype tree, resolve the named map pool, and emit a
  deterministic JSON inventory (id, repository-relative path, min/max players).
* validate-manifest: read a MapPatch JSON manifest, cross-check it against
  the resolved pool, and report exactly which pool maps are missing, which
  manifest paths are unexpected, and which paths duplicate / fail to resolve
  / escape the repository root.

This tool never mutates map files, never touches the MapPatch mutation
engine, and never opens the engine, network, hub, or launcher. The CLI is
fail-closed: every operation that cannot prove the pool or manifest is
consistent produces a non-zero exit code and an `errors` array.

Path-privacy: every JSON payload and every stderr diagnostic uses only
repository-relative paths or bounded placeholders. The tool never leaks the
caller's --root, system temp directories, $HOME, or any external absolute
path the caller passed in. The single exception is the ``source`` field on
the internal pool/result dataclass, which is stripped before any JSON
serialization.

Symlink safety: every file the tool opens (the repository root, the
prototype root, the supplied pool file, every prototype file, every gameMap
map file, and every manifest map path) is symlink-resolved and must remain
inside the resolved repository root. Symlinks that point outside the
repository — whether the escape is via a file link or via a directory link
— are rejected. Prototype files that live outside Resources/Prototypes are
also rejected.
"""

import argparse
import json
import os
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any


SCHEMA_VERSION = 1
DEFAULT_POOL_FILE = "Resources/Prototypes/_Solreign/map_pool.yml"
DEFAULT_POOL_ID = "SolreignMapPool"
PROTOTYPE_ROOT = "Resources/Prototypes"
SOLREIGN_POOL_FILE = DEFAULT_POOL_FILE

# Top-level list items in a prototype YAML file. We only care about items at
# the root of the document; nested items (under `stations:`, `components:`,
# `availableJobs:`, etc.) are not gameMap or gameMapPool prototypes.
TOP_LEVEL_ITEM_RE = re.compile(r"^- type: (\S+)\s*$")
# Fields directly under a top-level list item, at two-space indent.
BLOCK_FIELD_RE = re.compile(r"^  ([A-Za-z_]\w*):\s*(.*?)\s*$")
# List items directly under a field, at the same two-space indent as the
# field. The engine YAML convention (see map_pool.yml) writes
#     maps:
#     - Foo
#     - Bar
# where `-` sits in column 2 — i.e. at the same indent as the parent key.
BLOCK_LIST_RE = re.compile(r"^  -\s*(.*?)\s*$")
# Strict integer literal: optional sign, all digits, nothing else.
INTEGER_RE = re.compile(r"^-?\d+$")


# --------------------------------------------------------------------------- #
# Path-privacy & symlink safety                                                #
# --------------------------------------------------------------------------- #


# Path prefixes that strongly suggest an absolute path. These are used to
# detect and redact caller-supplied strings that may otherwise leak into
# error messages. Matching is intentionally conservative: any of these
# substrings will trigger redaction to a bounded placeholder.
_ABSOLUTE_PATH_HINTS = ("/Users/", "/home/", "/tmp/", "/var/", "/etc/",
                        "/private/", "C:\\", "%TEMP%", "%HOME%")


def _redact_absolute(value: str) -> str:
    """Return a bounded placeholder if `value` looks like an absolute path.

    The Foundry never echoes back a caller-supplied path that contains a
    known absolute-path prefix. Relative paths are passed through.
    """
    if not value:
        return value
    for hint in _ABSOLUTE_PATH_HINTS:
        if hint in value:
            return "<external-path>"
    # A value that starts with `/` is treated as external even if no hint
    # matched (e.g., /opt/secret, /mnt/private).
    if value.startswith("/") or value.startswith("\\"):
        return "<external-path>"
    # tilde expansion: the engine never expands `~` and neither do we.
    if value.startswith("~"):
        return "<external-path>"
    return value


def _safe_root(repo_root: Path) -> Path:
    """Return the resolved repository root.

    The root is resolved (following symlinks) once per call. All subsequent
    symlink checks in the tool compare a candidate's resolved path to this
    value.
    """
    if not isinstance(repo_root, Path):
        repo_root = Path(repo_root)
    try:
        return repo_root.resolve(strict=False)
    except OSError:
        # `strict=False` should not raise on POSIX, but fall back gracefully.
        return Path(os.path.abspath(str(repo_root)))


def _safe_under(root: Path, candidate: Path) -> Path:
    """Resolve `candidate` and ensure the result remains inside `root`.

    The candidate is treated as relative to `root` unless it is already
    absolute, in which case it is resolved in place. The returned path is
    the symlink-resolved absolute path. A symlink that points outside `root`
    is rejected with FoundryError. A symlink that points inside `root` is
    accepted.
    """
    root_resolved = root.resolve(strict=False) if not root.is_absolute() else root
    if candidate.is_absolute():
        target = candidate
    else:
        target = root_resolved / candidate
    target_resolved = target.resolve(strict=False)
    try:
        target_resolved.relative_to(root_resolved)
    except ValueError as error:
        raise FoundryError(
            "path escapes repository root"
        ) from error
    return target_resolved


def _relativize(root: Path, abs_path: Path) -> str:
    """Return `abs_path` as a stable repository-relative posix string.

    If the path is not under `root` (which should never happen after a
    successful `_safe_under` call), fall back to a bounded placeholder
    rather than echoing the absolute path.
    """
    try:
        return abs_path.relative_to(root).as_posix()
    except ValueError:
        return "<external-path>"


# --------------------------------------------------------------------------- #
# Errors                                                                      #
# --------------------------------------------------------------------------- #


class FoundryError(Exception):
    """A non-recoverable error raised when the tool cannot proceed at all.

    This is distinct from the per-error entries collected inside a PoolResult
    or validation result. FoundryError is reserved for malformed CLI
    arguments, unreadable manifest files, prototype trees that cannot even be
    walked, and similar catastrophic conditions.
    """


class _InvalidInt(ValueError):
    """Internal: a scalar could not be strictly converted to an integer."""


# --------------------------------------------------------------------------- #
# Dataclasses                                                                  #
# --------------------------------------------------------------------------- #


@dataclass
class PoolResult:
    """Outcome of resolving a named map pool against the prototype tree.

    The ``source`` field is a path-like label of the pool file. It is
    stripped from any JSON payload to keep path-privacy guarantees.
    """

    pool_id: str
    maps: list[dict[str, Any]] = field(default_factory=list)
    errors: list[dict[str, str]] = field(default_factory=list)
    source: str | None = None


# --------------------------------------------------------------------------- #
# Prototype YAML parser (pure stdlib, tolerant of comments and order)         #
# --------------------------------------------------------------------------- #


def _convert_scalar(value: str) -> Any:
    """Convert a raw field value to int / float / bool / str, leaving strings intact."""
    if value == "":
        return ""
    if value == "true":
        return True
    if value == "false":
        return False
    if value.lstrip("-").isdigit():
        return int(value)
    try:
        return float(value)
    except ValueError:
        pass
    if len(value) >= 2 and value[0] == value[-1] and value[0] in ("'", '"'):
        return value[1:-1]
    return value


def parse_prototype_text(text: str, source: str) -> list[dict[str, Any]]:
    """Return every top-level list item as {type, source, ...fields}.

    When a field like ``maps:`` opens a list, blank lines and comment-only
    lines are tolerated: they neither terminate the list nor replace its
    accumulated entries. A real sibling field (another two-space-indented
    key) or a new top-level item boundary is the only thing that closes the
    list.
    """
    lines = text.split("\n")
    boundaries: list[tuple[int, str]] = []
    for idx, line in enumerate(lines):
        match = TOP_LEVEL_ITEM_RE.match(line)
        if match:
            boundaries.append((idx, match.group(1)))

    items: list[dict[str, Any]] = []
    for index, (start, type_name) in enumerate(boundaries):
        end = boundaries[index + 1][0] if index + 1 < len(boundaries) else len(lines)
        item: dict[str, Any] = {"type": type_name, "source": source}
        pending_list_key: str | None = None
        for line in lines[start + 1:end]:
            stripped = line.strip()
            if pending_list_key is not None:
                # Blank and comment-only lines are inert inside a list and
                # must not close it. Real list items append and continue;
                # any other two-space-indented key closes the list.
                if stripped == "" or stripped.startswith("#"):
                    continue
                list_match = BLOCK_LIST_RE.match(line)
                if list_match:
                    item.setdefault(pending_list_key, []).append(list_match.group(1))
                    continue
                pending_list_key = None
            field_match = BLOCK_FIELD_RE.match(line)
            if not field_match:
                continue
            key = field_match.group(1)
            value = field_match.group(2)
            if value == "" and key in ("maps",):
                pending_list_key = key
                continue
            item[key] = _convert_scalar(value)
        items.append(item)
    return items


# --------------------------------------------------------------------------- #
# Strict integer coercion                                                      #
# --------------------------------------------------------------------------- #


def _strict_int(value: Any, field: str, map_id: str) -> int:
    """Convert `value` to a Python int, rejecting bool/float/non-integer.

    A bare bool is rejected even though ``isinstance(True, int)`` is True
    in Python. Every float is rejected — including whole-valued floats
    such as ``1.0`` and scientific notation parsed as float (e.g.
    ``1e2`` → ``100.0``, ``3.5e1`` → ``35.0``). The canonical
    SolreignTerminus shape (``maxPlayers`` omitted entirely) is handled by
    the caller and never reaches this function. A string is rejected
    unless it matches the strict integer literal grammar
    (``^-?\\d+$``); ``"1.5"``, ``"1e2"``, ``"+5"``, and ``"true"`` are
    not acceptable.
    """
    if isinstance(value, bool):
        raise _InvalidInt(
            f"gameMap {map_id!r} has {field}={value!r} (must be integer, not bool)"
        )
    if isinstance(value, int):
        return value
    if isinstance(value, float):
        # Reject every float — including whole-valued floats (1.0, 100.0)
        # and scientific notation parsed as float (1e2 → 100.0). The
        # engine never emits fractional player counts, and the YAML
        # grammar permits only integer literals for population bands.
        raise _InvalidInt(
            f"gameMap {map_id!r} has {field}={value!r} (must be integer, not float)"
        )
    if isinstance(value, str):
        s = value.strip()
        if not INTEGER_RE.match(s):
            raise _InvalidInt(
                f"gameMap {map_id!r} has {field}={value!r} (must be integer)"
            )
        return int(s)
    raise _InvalidInt(
        f"gameMap {map_id!r} has {field}={value!r} (must be integer)"
    )


# --------------------------------------------------------------------------- #
# Prototype-tree walk                                                          #
# --------------------------------------------------------------------------- #


def _read_prototype_text(path: Path) -> str:
    """Read a prototype or pool file as UTF-8 with bounded failure.

    Both ``OSError`` (file disappeared, permissions denied, etc.) and
    ``UnicodeDecodeError`` (file is not valid UTF-8) are caught and
    re-raised as ``FoundryError`` with a stable, path-free message. The
    absolute path of ``path`` is intentionally NOT echoed in the
    exception to keep the path-privacy guarantee: the caller surfaces the
    error through the bounded error-response path, and any leak would
    otherwise expose the caller's filesystem layout.
    """
    try:
        return path.read_text(encoding="utf-8")
    except OSError:
        raise FoundryError("cannot read prototype file") from None
    except UnicodeDecodeError:
        raise FoundryError("prototype file is not valid UTF-8") from None


def find_prototype_files(root: Path) -> list[Path]:
    """Return every .yml/.yaml file under Resources/Prototypes, sorted.

    The prototype root is symlink-resolved and verified to remain inside
    the repository root. Files whose resolved paths escape the root are
    silently dropped — the prototype root is the authoritative boundary.
    """
    safe_root = _safe_root(root)
    proto_root = safe_root / PROTOTYPE_ROOT
    if not proto_root.is_dir():
        raise FoundryError("prototype root does not exist in the repository")
    try:
        proto_root_resolved = _safe_under(safe_root, proto_root)
    except FoundryError as error:
        raise FoundryError("prototype root escapes repository root") from error
    files: list[Path] = []
    for path in sorted(proto_root_resolved.rglob("*")):
        if not path.is_file():
            continue
        if path.suffix not in (".yml", ".yaml"):
            continue
        try:
            _safe_under(safe_root, path)
        except FoundryError:
            # A symlink that escapes the repository root is rejected.
            continue
        files.append(path)
    return files


def collect_game_maps(root: Path, files: list[Path]) -> dict[str, dict[str, Any]]:
    """Walk the files and return {id: gameMap spec}; raise on duplicate ids.

    Every file in `files` is symlink-resolved and must remain inside `root`.
    The path stored in the ``source`` field is the resolved absolute path
    (used internally for the duplicate-id error) but is not serialized to
    JSON — see render_inventory.
    """
    safe_root = _safe_root(root)
    specs: dict[str, dict[str, Any]] = {}
    for path in files:
        try:
            safe_path = _safe_under(safe_root, path)
        except FoundryError:
            # A symlink that escaped between find_prototype_files and now
            # is rejected.
            continue
        text = _read_prototype_text(safe_path)
        for item in parse_prototype_text(text, str(safe_path)):
            if item["type"] != "gameMap":
                # Ignore parallax, entity, etc. even when they reuse the same id.
                continue
            map_id = item.get("id")
            if not isinstance(map_id, str) or not map_id:
                raise FoundryError(
                    "gameMap block missing id in the prototype tree"
                )
            if map_id in specs:
                raise FoundryError(
                    f"duplicate gameMap prototype id {map_id!r} in the prototype tree"
                )
            specs[map_id] = {
                "id": map_id,
                "mapPath": item.get("mapPath"),
                "minPlayers": item.get("minPlayers"),
                "maxPlayers": item.get("maxPlayers"),
                "source": str(safe_path),
            }
    return specs


def collect_pool_from_file(
    root: Path, pool_file: Path
) -> tuple[dict[str, dict[str, Any]], str | None]:
    """Parse `pool_file` as the SOLE source of gameMapPool prototypes.

    Returns ({id: pool spec}, duplicate_id_or_None). The duplicate_id is
    set when the same pool id is defined more than once in `pool_file` —
    the caller surfaces this as a fail-closed error.
    """
    safe_root = _safe_root(root)
    try:
        safe_pool_file = _safe_under(safe_root, pool_file)
    except FoundryError:
        raise
    if not safe_pool_file.is_file():
        raise FoundryError(
            "pool prototype file does not exist in the repository"
        )
    text = _read_prototype_text(safe_pool_file)
    pools: dict[str, dict[str, Any]] = {}
    duplicate_id: str | None = None
    for item in parse_prototype_text(text, str(safe_pool_file)):
        if item["type"] != "gameMapPool":
            continue
        pool_id = item.get("id")
        if not isinstance(pool_id, str) or not pool_id:
            continue
        if pool_id in pools:
            duplicate_id = pool_id
            continue
        pools[pool_id] = {
            "id": pool_id,
            "maps": list(item.get("maps", [])),
            "source": str(safe_pool_file),
        }
    return pools, duplicate_id


# --------------------------------------------------------------------------- #
# Path normalization                                                          #
# --------------------------------------------------------------------------- #


def normalize_map_path(raw: Any) -> str:
    """Convert an engine-style /Maps/... mapPath to canonical Resources/Maps/...

    Also accepts the already-canonical Resources/... form, which is what
    MapPatch manifests use. Raw values that look like absolute paths are
    rejected without echoing them back.
    """
    if not isinstance(raw, str) or not raw:
        raise FoundryError("mapPath must be a non-empty string")
    if raw.startswith("Resources/"):
        candidate = raw
    elif raw.startswith("/Maps/"):
        candidate = "Resources" + raw
    else:
        raise FoundryError(
            "mapPath must start with /Maps/ or Resources/"
        )
    if ".." in Path(candidate).parts:
        raise FoundryError("mapPath escapes Resources root")
    if not (candidate.endswith(".yml") or candidate.endswith(".yaml")):
        raise FoundryError("mapPath must be a YAML file")
    return candidate


def normalize_manifest_path(raw: Any) -> str:
    """Validate a MapPatch manifest path; return its normalized form.

    Absolute paths, parent traversals, and non-YAML suffixes are rejected
    without echoing the offending raw value.
    """
    if not isinstance(raw, str) or not raw:
        raise FoundryError("manifest path must be a non-empty string")
    if raw.startswith("/") or raw.startswith("\\"):
        raise FoundryError("manifest path must be repository-relative")
    if ".." in Path(raw).parts:
        raise FoundryError("manifest path escapes repository root")
    if not (raw.endswith(".yml") or raw.endswith(".yaml")):
        raise FoundryError("manifest path must be a YAML file")
    return raw


# --------------------------------------------------------------------------- #
# Pool resolution                                                             #
# --------------------------------------------------------------------------- #


def resolve_pool(
    root: Path, pool_id: str, pool_file: Path | None = None
) -> PoolResult:
    """Resolve the named map pool to a list of validated gameMap specs.

    The supplied ``pool_file`` is the SOLE source of the named gameMapPool:
    the tool does not silently fall back to a same-id pool defined in a
    different prototype file. The full prototype tree is still walked for
    ``gameMap`` resolution.

    Never raises on per-map issues: those become entries in
    ``PoolResult.errors`` so the caller (typically the CLI) can render them
    and exit non-zero. Raises FoundryError only on catastrophic conditions
    (e.g., the prototype tree cannot be walked at all).
    """
    safe_root = _safe_root(root)
    target_pool_file = pool_file if pool_file else safe_root / SOLREIGN_POOL_FILE

    # 1. The pool file must be a real file inside the repository.
    try:
        safe_pool_file = _safe_under(safe_root, target_pool_file)
    except FoundryError:
        return PoolResult(
            pool_id=pool_id,
            errors=[
                {
                    "code": "pool_file_not_found",
                    "message": "pool prototype file is outside the repository",
                }
            ],
        )
    if not safe_pool_file.is_file():
        return PoolResult(
            pool_id=pool_id,
            errors=[
                {
                    "code": "pool_file_not_found",
                    "message": "pool prototype file does not exist in the repository",
                }
            ],
        )

    # 2. Parse the pool file alone. Duplicate definitions in the same file
    #    fail closed.
    try:
        pools, duplicate_id = collect_pool_from_file(safe_root, safe_pool_file)
    except FoundryError:
        return PoolResult(
            pool_id=pool_id,
            errors=[
                {
                    "code": "pool_file_not_found",
                    "message": "pool prototype file does not exist in the repository",
                }
            ],
        )
    if duplicate_id is not None and duplicate_id == pool_id:
        return PoolResult(
            pool_id=pool_id,
            source=_relativize(safe_root, safe_pool_file),
            errors=[
                {
                    "code": "pool_duplicate_definition",
                    "message": (
                        f"pool id {pool_id!r} is defined more than once in "
                        "the pool file"
                    ),
                }
            ],
        )

    if pool_id not in pools:
        return PoolResult(
            pool_id=pool_id,
            source=_relativize(safe_root, safe_pool_file),
            errors=[
                {
                    "code": "pool_not_found",
                    "message": (
                        f"pool id {pool_id!r} is not defined in the pool file"
                    ),
                }
            ],
        )

    pool = pools[pool_id]

    # 3. The prototype tree is walked in full for gameMap resolution.
    try:
        files = find_prototype_files(safe_root)
        game_maps = collect_game_maps(safe_root, files)
    except FoundryError as error:
        return PoolResult(
            pool_id=pool_id,
            source=_relativize(safe_root, safe_pool_file),
            errors=[{"code": "foundry_error", "message": str(error)}],
        )

    resolved_maps: list[dict[str, Any]] = []
    seen_ids: set[str] = set()
    seen_paths: dict[str, str] = {}
    errors: list[dict[str, str]] = []

    for raw_id in pool["maps"]:
        if not isinstance(raw_id, str) or not raw_id:
            errors.append(
                {
                    "code": "pool_invalid_map_id",
                    "message": (
                        f"pool {pool_id!r} contains a non-string/empty map id"
                    ),
                }
            )
            continue
        if raw_id in seen_ids:
            errors.append(
                {
                    "code": "pool_duplicate_map_id",
                    "message": f"pool {pool_id!r} repeats map id {raw_id!r}",
                }
            )
            continue
        if raw_id not in game_maps:
            errors.append(
                {
                    "code": "unresolved_map_id",
                    "message": (
                        f"pool {pool_id!r} references gameMap id {raw_id!r} "
                        "which is not defined in any prototype"
                    ),
                }
            )
            continue

        spec = game_maps[raw_id]
        try:
            normalized_path = normalize_map_path(spec.get("mapPath"))
        except FoundryError as error:
            errors.append(
                {
                    "code": "game_map_invalid_map_path",
                    "message": f"gameMap {raw_id!r}: {error}",
                }
            )
            continue

        # Resolve the map file path. It must remain inside the repo root.
        try:
            full_path = _safe_under(safe_root, Path(normalized_path))
        except FoundryError:
            errors.append(
                {
                    "code": "game_map_invalid_map_path",
                    "message": (
                        f"gameMap {raw_id!r} mapPath {normalized_path} "
                        "escapes the repository"
                    ),
                }
            )
            continue
        if not full_path.is_file():
            errors.append(
                {
                    "code": "game_map_invalid_map_path",
                    "message": (
                        f"gameMap {raw_id!r} mapPath {normalized_path} "
                        "does not exist in the repository"
                    ),
                }
            )
            continue

        if normalized_path in seen_paths:
            errors.append(
                {
                    "code": "game_map_duplicate_resolved_path",
                    "message": (
                        f"gameMaps {seen_paths[normalized_path]!r} and "
                        f"{raw_id!r} both resolve to {normalized_path}"
                    ),
                }
            )
            continue

        # 4. Validate population band: min required, integer, >= 0; max
        #    optional, integer; max >= min.
        min_raw = spec.get("minPlayers")
        max_raw = spec.get("maxPlayers")
        try:
            min_players = _strict_int(min_raw, "minPlayers", raw_id)
        except _InvalidInt as error:
            errors.append(
                {
                    "code": "game_map_invalid_population_band",
                    "message": str(error),
                }
            )
            continue
        if min_players < 0:
            errors.append(
                {
                    "code": "game_map_invalid_population_band",
                    "message": (
                        f"gameMap {raw_id!r} has minPlayers={min_players} "
                        "(must be >= 0)"
                    ),
                }
            )
            continue

        if max_raw is None:
            max_players: int | None = None
        else:
            try:
                max_players = _strict_int(max_raw, "maxPlayers", raw_id)
            except _InvalidInt as error:
                errors.append(
                    {
                        "code": "game_map_invalid_population_band",
                        "message": str(error),
                    }
                )
                continue
            if max_players < min_players:
                errors.append(
                    {
                        "code": "game_map_invalid_population_band",
                        "message": (
                            f"gameMap {raw_id!r} has maxPlayers={max_players} "
                            f"below minPlayers={min_players}"
                        ),
                    }
                )
                continue

        seen_ids.add(raw_id)
        seen_paths[normalized_path] = raw_id
        resolved_maps.append(
            {
                "id": raw_id,
                "path": normalized_path,
                "population": {"min": min_players, "max": max_players},
            }
        )

    return PoolResult(
        pool_id=pool_id,
        maps=resolved_maps,
        errors=_sort_errors(errors),
        source=_relativize(safe_root, safe_pool_file),
    )


# --------------------------------------------------------------------------- #
# Manifest validation                                                         #
# --------------------------------------------------------------------------- #


def _sort_errors(errors: list[dict[str, str]]) -> list[dict[str, str]]:
    """Stable sort by (code, message)."""
    return sorted(errors, key=lambda entry: (entry["code"], entry["message"]))


def validate_manifest(
    root: Path,
    pool_id: str,
    pool_file: Path | None,
    manifest_path: Path,
) -> dict[str, Any]:
    """Validate a MapPatch JSON manifest against the resolved pool."""
    safe_root = _safe_root(root)
    pool_result = resolve_pool(safe_root, pool_id, pool_file)

    # Read the manifest file. The path itself is not echoed on failure.
    try:
        safe_manifest = _safe_under(safe_root, manifest_path)
    except FoundryError:
        raise FoundryError("manifest file is outside the repository") from None
    try:
        manifest_text = safe_manifest.read_text(encoding="utf-8")
    except OSError as error:
        raise FoundryError("cannot read the manifest file") from error
    try:
        manifest_obj = json.loads(manifest_text)
    except json.JSONDecodeError as error:
        raise FoundryError("cannot parse manifest JSON") from error
    if not isinstance(manifest_obj, dict):
        raise FoundryError("manifest must be a JSON object")
    if manifest_obj.get("schema_version") != SCHEMA_VERSION:
        raise FoundryError(
            f"manifest schema_version must be {SCHEMA_VERSION}, "
            f"got {manifest_obj.get('schema_version')!r}"
        )
    patch_id = manifest_obj.get("patch_id")
    if not isinstance(patch_id, str) or not patch_id:
        raise FoundryError("manifest patch_id must be a non-empty string")
    raw_maps = manifest_obj.get("maps")
    if not isinstance(raw_maps, list):
        raise FoundryError("manifest maps must be a JSON array")
    if not raw_maps:
        raise FoundryError("manifest maps cannot be empty")

    pool_paths = {entry["path"]: entry["id"] for entry in pool_result.maps}
    expected_paths = sorted(pool_paths)

    declared_norm: list[str] = []
    declared_seen: set[str] = set()
    missing_paths: list[str] = []
    unexpected_paths: list[str] = []
    duplicate_paths: list[str] = []
    unresolved_paths: list[str] = []
    out_of_root_paths: list[str] = []

    for raw_entry in raw_maps:
        if not isinstance(raw_entry, dict):
            raise FoundryError("manifest maps entry must be a JSON object")
        raw_path = raw_entry.get("path")
        try:
            norm = normalize_manifest_path(raw_path)
        except FoundryError:
            # Classify absolute or parent-traversal paths as
            # out-of-root without echoing the raw value.
            if isinstance(raw_path, str) and (
                raw_path.startswith("/")
                or raw_path.startswith("\\")
                or ".." in Path(raw_path).parts
            ):
                redacted = _redact_absolute(raw_path)
                if redacted != raw_path:
                    out_of_root_paths.append(redacted)
                else:
                    out_of_root_paths.append(raw_path)
                continue
            raise
        # Resolve under the repository root. Any symlink escape is an
        # out-of-root path.
        try:
            _safe_under(safe_root, Path(norm))
        except FoundryError:
            out_of_root_paths.append(norm)
            continue
        if norm in declared_seen:
            duplicate_paths.append(norm)
            continue
        if not (safe_root / norm).is_file():
            unresolved_paths.append(norm)
            declared_seen.add(norm)
            declared_norm.append(norm)
            continue
        declared_seen.add(norm)
        declared_norm.append(norm)

    declared_set = set(declared_norm)
    missing_paths = [path for path in expected_paths if path not in declared_set]
    unexpected_paths = sorted(
        path for path in declared_norm if path not in pool_paths
    )

    coverage = "complete"
    if (
        missing_paths
        or unexpected_paths
        or duplicate_paths
        or unresolved_paths
        or out_of_root_paths
        or pool_result.errors
    ):
        coverage = "incomplete"

    errors: list[dict[str, str]] = list(pool_result.errors)
    for path in sorted(missing_paths):
        map_id = pool_paths[path]
        errors.append(
            {
                "code": "manifest_missing_pool_map",
                "message": (
                    f"pool map {map_id!r} ({path}) is not declared in the manifest"
                ),
            }
        )
    for path in unexpected_paths:
        errors.append(
            {
                "code": "manifest_unexpected_map",
                "message": (
                    f"manifest declares {path} which is not in pool {pool_id!r}"
                ),
            }
        )
    for path in sorted(set(duplicate_paths)):
        errors.append(
            {
                "code": "manifest_duplicate_path",
                "message": f"manifest repeats path {path}",
            }
        )
    for path in sorted(set(unresolved_paths)):
        errors.append(
            {
                "code": "manifest_unresolved_path",
                "message": f"manifest path {path} does not exist in the repository",
            }
        )
    for path in sorted(set(out_of_root_paths)):
        errors.append(
            {
                "code": "manifest_path_outside_root",
                "message": f"manifest path escapes the repository root",
            }
        )

    return {
        "schema_version": SCHEMA_VERSION,
        "pool_id": pool_id,
        "patch_id": patch_id,
        "manifest_coverage": coverage,
        "pool": {
            "id": pool_id,
            "maps": sorted(pool_result.maps, key=lambda entry: entry["id"]),
        },
        "manifest": {
            "declared_paths": sorted(declared_seen),
            "missing_paths": sorted(missing_paths),
            "unexpected_paths": unexpected_paths,
            "duplicate_paths": sorted(set(duplicate_paths)),
            "unresolved_paths": sorted(set(unresolved_paths)),
            "out_of_root_paths": sorted(set(out_of_root_paths)),
        },
        "errors": _sort_errors(errors),
    }


# --------------------------------------------------------------------------- #
# JSON rendering                                                              #
# --------------------------------------------------------------------------- #


def render_inventory(result: PoolResult) -> dict[str, Any]:
    # The internal ``source`` field is intentionally not emitted.
    return {
        "schema_version": SCHEMA_VERSION,
        "pool_id": result.pool_id,
        "maps": sorted(result.maps, key=lambda entry: entry["id"]),
        "errors": _sort_errors(result.errors),
    }


# --------------------------------------------------------------------------- #
# CLI                                                                        #
# --------------------------------------------------------------------------- #


def _default_root() -> Path:
    """Derive the repository root by walking up from this script.

    We look for the SpaceStation14.slnx sentinel first, then fall back to
    four parents up (Tools/_Solreign/MapFoundry/map_foundry.py -> repo root).
    """
    script = Path(__file__).resolve()
    for parent in [script.parent, *script.parents]:
        if (parent / "SpaceStation14.slnx").exists():
            return parent
    # Fallback: script -> MapFoundry -> _Solreign -> Tools -> repo.
    return script.parents[3]


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument(
        "--root",
        type=Path,
        default=None,
        help=(
            "Game repository root (default: derived from script location; "
            "must contain Resources/Prototypes/_Solreign/map_pool.yml)"
        ),
    )
    parser.add_argument(
        "--pool",
        type=Path,
        default=None,
        help=(
            "Path to the map-pool prototype file "
            f"(default: {DEFAULT_POOL_FILE} relative to --root)"
        ),
    )
    parser.add_argument(
        "--pool-id",
        default=DEFAULT_POOL_ID,
        help=f"Map-pool prototype id to resolve (default: {DEFAULT_POOL_ID})",
    )

    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("inventory", help="Emit deterministic pool inventory JSON")
    validate_parser = sub.add_parser(
        "validate-manifest",
        help="Validate a MapPatch JSON manifest against the resolved pool",
    )
    validate_parser.add_argument(
        "--manifest",
        type=Path,
        required=True,
        help="Path to a MapPatch JSON manifest",
    )

    args = parser.parse_args(argv)
    if args.root is None:
        args.root = _default_root()
    if args.pool is None:
        args.pool = args.root / DEFAULT_POOL_FILE
    return args


def _emit(payload: dict[str, Any], errors_to_stderr: str | None = None) -> None:
    print(json.dumps(payload, indent=2, sort_keys=False))
    if errors_to_stderr:
        print(errors_to_stderr, file=sys.stderr)


def cmd_inventory(args: argparse.Namespace) -> int:
    try:
        result = resolve_pool(args.root, args.pool_id, args.pool)
    except FoundryError as error:
        payload = render_inventory(
            PoolResult(
                pool_id=args.pool_id,
                errors=[{"code": "foundry_error", "message": str(error)}],
            )
        )
        _emit(payload, "map_foundry: ERROR: see errors[] in inventory JSON")
        return 2
    payload = render_inventory(result)
    diagnostic = None
    if result.errors:
        diagnostic = (
            f"map_foundry: {len(result.errors)} pool issue(s) found; "
            "see errors[] in inventory JSON"
        )
    _emit(payload, diagnostic)
    return 0 if not result.errors else 2


def cmd_validate_manifest(args: argparse.Namespace) -> int:
    try:
        result = validate_manifest(
            args.root, args.pool_id, args.pool, args.manifest
        )
    except FoundryError as error:
        payload = {
            "schema_version": SCHEMA_VERSION,
            "pool_id": args.pool_id,
            "errors": [{"code": "foundry_error", "message": str(error)}],
            "manifest_coverage": "incomplete",
        }
        _emit(payload, "map_foundry: ERROR: see errors[] in validate-manifest JSON")
        return 2
    _emit(result)
    if result.get("errors"):
        print(
            f"map_foundry: {len(result['errors'])} issue(s) found; "
            f"coverage={result['manifest_coverage']}",
            file=sys.stderr,
        )
    return 0 if result["manifest_coverage"] == "complete" else 1


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    if args.command == "inventory":
        return cmd_inventory(args)
    if args.command == "validate-manifest":
        return cmd_validate_manifest(args)
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
