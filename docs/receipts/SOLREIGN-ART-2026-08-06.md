# SOLREIGN art landing — APC + station-identity floor set (2026-08-06)

Owner-approved 2026-08-06. Replaces upstream CC-BY-SA-3.0 art in place with in-house CC0-1.0
art, following the convention already established by `LICENSE-BATCH-A-2026-07-16.md` and
`LICENSE-BATCH-BC-2026-07-17.md`.

**No upstream pixels are read, sampled, traced, img2img'd or conditioned on.** Every output
pixel comes from a generator script in the `sprite-factory` repo. What was taken from the
vanilla art is *measurements only* — canvas sizes, per-frame bounding boxes, state lists —
which is what makes the replacements drop-in.

## What landed

| asset | source generator | contract |
|---|---|---|
| `Resources/Textures/Structures/Wallmounts/apc.rsi` (13 PNG + meta; moved from `Structures/Power` by upstream-v286) | `scripts/machine_swath_builders.py::build_apc` | 114 of 115 frames occupy the vanilla bounding box exactly; opaque counts identical for all 13 states |
| `Resources/Textures/Tiles/{plating,steel,lattice,dark,tech_maint,white}.png` | `scripts/solreign_tile_builders.py` | canvas sizes match their tile definitions; upstream-v286's three-variant lattice sheet repeats the approved 32x32 Solreign tile without introducing new pixels |

Generated at `sprite-factory` commits `38ba1df` (APC) and `b30fb78` (tiles).

## Why these six tiles

Decoding every SOLREIGN map's grid chunks (format 7: int32 id + flags/variant/rotation per
tile) across all seven stations gives **84,591 non-space tiles**. These six prototypes account
for **78.2%** of every walkable surface:

    Plating 38.56% · FloorSteel 13.93% · Lattice 11.59% · FloorDark 7.55%
    FloorTechMaint 3.53% · FloorWhite 3.01%

Maps store tile prototype **IDs** in a `tilemap:` block and never texture paths (`git grep
"Textures/Tiles" -- Resources/Maps` returns nothing), so replacing these PNGs re-skins all
seven stations with **zero map edits**.

## Verification performed before landing

| check | result |
|---|---|
| `RobustToolbox/Schemas/validate_rsis.py Resources/Textures/Structures/Wallmounts` | **exit 0** |
| ...mutation control: added a state with no PNG | **exit 1**, named `apc.rsi: a-state-with-no-png` |
| `attributions.yml` vs `RobustToolbox/Schemas/rga.yml` + `rga_validators.py` (the schema CI enforces) | **exit 0** |
| ...mutation control: `license: "NOT-A-LICENSE"` | **exit 1**, `'NOT-A-LICENSE' is not a license` |
| `Tools/solreign_gate.sh` | see the landing commit message for the exit code |
| sprite-factory suite (generators + contract + seam tests) | exit 0, 7691 passed |

Both validators were watched failing on a deliberate defect and passing on the real tree. A
gate nobody has watched fail is decoration.

## Licence bookkeeping

`Resources/Textures/Tiles/attributions.yml`: the six filenames were removed from their upstream
CC-BY-SA-3.0 blocks and added to one CC0-1.0 block. Two blocks that listed *only* a replaced
file (`plating.png`, `tech_maint.png`) were dropped whole.

A first attempt at this rewrite did regex surgery on filenames and silently left those two
single-entry blocks intact — the tiles would have shipped claiming both our CC0 and the
upstream CC-BY-SA at once. It was caught by a post-condition asserting each filename appears in
exactly one block, not by reading the diff. The rewrite now parses blocks, and asserts
afterwards that no *other* file lost its attribution (120 names retained).

`apc.rsi/meta.json` carries its own licence: changed from CC-BY-SA-3.0 (tgstation) to CC0-1.0.

## Known divergence, deliberate

`sparks-unlit` frame 0 is fully transparent in vanilla. The sprite doctor fails any 0-opaque
frame before the sparse exemption is consulted, so ours opens with frame 1's two pixels. It is
the single bounding-box difference of 115 frames.

## Measured consequence worth knowing

Usage-weighted mean floor luminance drops **68.1 -> 55.3 (-19%)**. `FloorSteel`, the primary
deck, goes 123.2 -> 75.6. An earlier draft had it at 54.7, which put it within noise of
`FloorDark` at 31.0; vanilla separates those two hard and players navigate by the difference,
so the separation was restored deliberately. `Lattice` goes the other way, 44.3 -> 103.7.

## Not covered

Deploying to the live server is a separate, gated action and is not part of this change.
