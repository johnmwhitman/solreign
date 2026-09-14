# Map Foundry v0

`Tools/_Solreign/MapFoundry/` is a bounded, offline, **read-only** companion
to `Tools/_Solreign/MapPatch/`. It performs two operations only:

1. **inventory** — walk the prototype tree, resolve the named map pool, and
   emit a deterministic JSON inventory of every gameMap the pool references
   (id, repository-relative path, population band).
2. **validate-manifest** — read a MapPatch JSON manifest, cross-check it
   against the resolved pool, and report exactly which pool maps are missing,
   which manifest paths are unexpected, and which paths duplicate, fail to
   resolve, or escape the repository root.

## Boundary

* The Foundry is **read-only**. It never opens a map file for writing, never
  re-emits YAML, never mutates the manifest, and never calls into the
  MapPatch mutation engine. Map files are opened only to confirm they exist
  on disk when validating a manifest.
* **MapPatch remains the sole mutation engine.** The Foundry will not
  apply, remove, or update a patch block. It is a verifier; MapPatch is the
  mutator.
* **No engine / network / hub / launcher / runtime C# changes.** The Foundry
  is pure Python (standard library only). It does not depend on
  RobustToolbox, does not touch the C# build, and does not open a socket.
* **No root-portfolio doc edits.** This directory is the only surface the
  Foundry writes to.

## CLI

```text
map_foundry.py [-h] [--root ROOT] [--pool POOL] [--pool-id POOL_ID]
               {inventory,validate-manifest} ...
```

| Flag                | Default                                            | Meaning |
|---------------------|----------------------------------------------------|---------|
| `--root`            | Derived from script location (SpaceStation14.slnx) | Game repository root. |
| `--pool`            | `Resources/Prototypes/_Solreign/map_pool.yml`      | Path to the map-pool prototype file. |
| `--pool-id`         | `SolreignMapPool`                                  | Map-pool prototype id to resolve. |
| `inventory`         | —                                                  | Emit deterministic pool inventory JSON. |
| `validate-manifest` | —                                                  | Validate a MapPatch JSON manifest against the resolved pool. |

### `inventory`

```sh
python3 Tools/_Solreign/MapFoundry/map_foundry.py inventory
```

Emits a JSON document to stdout shaped like:

```json
{
  "schema_version": 1,
  "pool_id": "SolreignMapPool",
  "maps": [
    {
      "id": "SolreignLeviathan",
      "path": "Resources/Maps/_Solreign/solreign_leviathan.yml",
      "population": {"min": 0, "max": 90}
    }
  ],
  "errors": []
}
```

Exit codes:

* `0` — every pool map resolved cleanly.
* `2` — at least one pool map failed to resolve (missing prototype, duplicate
  id, invalid map path, duplicate resolved path, invalid population band,
  etc.). The `errors[]` array describes each issue.

### `validate-manifest`

```sh
python3 Tools/_Solreign/MapFoundry/map_foundry.py validate-manifest \
    --manifest Tools/_Solreign/MapPatch/manifests/community-fixtures-v1.json
```

Emits a JSON document to stdout shaped like:

```json
{
  "schema_version": 1,
  "pool_id": "SolreignMapPool",
  "patch_id": "community-fixtures-v1",
  "manifest_coverage": "complete",
  "pool": { "id": "SolreignMapPool", "maps": [ ... ] },
  "manifest": {
    "declared_paths":   [ ... ],
    "missing_paths":    [ ... ],
    "unexpected_paths": [ ... ],
    "duplicate_paths":  [ ... ],
    "unresolved_paths": [ ... ],
    "out_of_root_paths":[ ... ]
  },
  "errors": []
}
```

Exit codes:

* `0` — every pool map is declared exactly once, every declared path is in
  the pool, no path duplicates, no path is unresolved, no path escapes the
  root.
* `1` — coverage is incomplete. The `manifest_coverage` field is
  `"incomplete"` and at least one of `missing_paths`, `unexpected_paths`,
  `duplicate_paths`, `unresolved_paths`, `out_of_root_paths`, or
  pool-resolution errors is non-empty.
* `2` — the tool itself could not run: manifest JSON could not be read or
  parsed, manifest `schema_version` is not `1`, manifest `maps` is missing
  or empty, etc.

## Fail-closed error codes

The tool surfaces issues via stable, lowercase, underscore-separated codes:

| Code                                | Meaning |
|-------------------------------------|---------|
| `pool_file_not_found`               | The pool prototype file does not exist, or it is outside the repository, or it is a symlink that escapes the repository root. |
| `pool_not_found`                    | The named `pool_id` is not defined in the supplied pool file. A same-id pool defined in any other prototype file is intentionally NOT used. |
| `pool_duplicate_definition`         | The supplied pool file defines the named `pool_id` more than once. |
| `pool_invalid_map_id`               | A `maps:` list entry in the pool is not a non-empty string (e.g., a bare `-` or `- ''` line). |
| `pool_duplicate_map_id`             | The pool `maps:` list contains the same id twice. Reserved for actual duplicates, not for non-string entries. |
| `unresolved_map_id`                 | The pool references a `gameMap` id that is not defined in any prototype. |
| `game_map_invalid_map_path`         | A `gameMap` is missing, malformed, or escapes the repository (including via a symlink), or the file it points at does not exist. |
| `game_map_duplicate_resolved_path`  | Two `gameMap` ids resolve to the same repository-relative path. |
| `game_map_invalid_population_band`  | `minPlayers` is missing / non-integer / negative, or explicit `maxPlayers` is non-integer / bool / float / non-integer-string / `max < min`. Omitted `maxPlayers` is treated as unbounded (rendered as JSON `null`). |
| `manifest_missing_pool_map`         | The manifest does not declare a path that the pool requires. |
| `manifest_unexpected_map`           | The manifest declares a path that the pool does not list. |
| `manifest_duplicate_path`           | The manifest repeats the same path more than once. |
| `manifest_unresolved_path`          | The manifest path does not exist in the repository. |
| `manifest_path_outside_root`        | The manifest path is absolute, escapes the repository root, or resolves to a symlink that escapes the repository root. |

## Path-privacy and symlink safety

* Every JSON payload and every stderr diagnostic uses only repository-relative
  paths or bounded placeholders (`<external-path>`). The tool never echoes the
  caller's `--root`, system temp directories, `$HOME`, or any external absolute
  path that the caller passed in. Pool/source files are stored internally
  as resolved absolute paths for the duplicate-id check but are stripped from
  the JSON before any payload is emitted.
* Every file the tool opens (the repository root, the prototype root, the
  supplied pool file, every prototype file, every gameMap map file, and
  every manifest map path) is symlink-resolved and must remain inside the
  resolved repository root. Symlinks that point outside the repository —
  whether the escape is via a file link or via a directory link — are
  rejected with a fail-closed error. Symlinks that stay inside the
  repository are accepted.

## Source-of-truth for the named pool

* The `--pool` file is the **sole** source of the named `gameMapPool`. A
  same-id pool defined in any other prototype file is intentionally
  ignored; the tool surfaces `pool_not_found` instead of silently taking
  it. The full prototype tree is still walked for `gameMap` resolution.
* The `--pool` file is parsed once. If the named `pool_id` is defined more
  than once in that file, the tool surfaces `pool_duplicate_definition`
  and refuses to resolve.

## Prototype parser

* The Foundry's prototype parser is a small, pure-stdlib YAML reader that
  recognizes only the engine's top-level `- type: <id>` items, their
  two-space-indented scalar fields, and their two-space-indented list
  items. It is not a general YAML parser and is not intended to be one.
* Inside an open list (e.g. a `maps:` field), **blank lines and
  comment-only lines are inert**: they neither truncate the list nor
  consume the next list item. The list is closed only by a real sibling
  field at the same indent or by the next top-level item boundary.
* Population-band values are read strictly: `minPlayers` is required and
  must be a non-negative Python `int`. `maxPlayers` is optional; when
  present it must be a non-negative Python `int` and must be `>= minPlayers`.
  Booleans, floats, and non-integer strings are rejected without
  silent coercion or truncation.

## Determinism

* `maps[]` is sorted by `id` (alphabetical).
* `errors[]` is sorted by `(code, message)` (stable). Errors that share
  a code are ordered by their message text, so the JSON output is
  byte-stable across runs.
* All paths in the output are repository-relative and never absolute.
* No timestamps, no entity UIDs, no coordinates, no secrets, no player data
  are emitted.
* The tool does not read or depend on filesystem mtimes, inode numbers, or
  environment variables.

## Non-goals (v0)

* Does not produce coordinates, patch UIDs, placement layouts, or any
  artifact the MapPatch tool requires.
* Does not validate anchor drift, expected tile, expected occupants, or
  any field MapPatch itself already validates. MapPatch is the source of
  truth for those.
* Does not search for "available" maps, propose additions to a pool, or
  reconcile map_pool.yml against the on-disk chassis set; it only verifies
  that what the pool already references is well-formed and consistent.
* Does not edit any YAML or C# file.
