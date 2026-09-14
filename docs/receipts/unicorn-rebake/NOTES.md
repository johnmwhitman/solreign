# Unicorn animation re-bake — staging notes (R4 / v14.5 work-item 3)

Staging-only deliverable. Nothing in the game worktree or any repo was modified.
Everything here was produced from
`game-v141-absorb/Resources/Textures/_Solreign/unicorn.rsi/unicorn.png` per
`ops-v140-docs/docs/specs/unicorn-anim-wiring/UNICORN-ANIM-WIRING-SPEC.md` (Option A).

## Deliverables

- `unicorn.rsi/` — complete staged RSI, drop-in replacement candidate:
  - `sheep_bare.png` — 128x128, 4 dirs x 4 frames of 32x32 cells, full-body animated unicorn
  - `sheep_wool.png` — 128x128, same grid, mane/horn-only overlay (RgbLightController target, spec A3a)
  - `meta.json` — proposed meta (see below)
  - `sheep_dead.png`, `sheep_wool_dead.png`, `icon.png` — copied verbatim, unchanged
  - NOTE: raw `unicorn.png` intentionally NOT carried over (spec A4: delete once baked)
- `check_consistency.py` — verification (all states PASS; run it yourself: exit 0)
- `preview_bare_dir0.gif` — sheep_bare South loop, 8x, 200ms/frame
- `preview_composite_alldirs.gif` — all 4 dirs, bare + wool hue-cycled (RgbLightController sim)
- `preview_sheep_bare_sheet.png`, `preview_sheep_wool_sheet.png` — upscaled contact views
- `analyze_layout.py`, `bake.py` — reproducible pipeline
- `verify_grid_16x32.png`, `band2_y24-40.png`, `band5_y92-128.png` — layout-verification evidence

## Layout re-verification (independent, PIL)

Confirmed from pixels: 128x128 sheet, 16px horizontal pitch (columns 15,31,47,... fully
empty), 8 frame columns x 4 direction rows, direction order S,N,E,W as the spec says.

**Spec deviation found (important):** the vertical layout is NOT four clean 16x32
figures. The sheet contains SIX content bands separated by fully-empty row gaps:

| global y band | content |
|---|---|
| 2–20  | South/front walk figure (kept, dir 0) |
| 27–31 | **stowaway: lying/sleeping unicorn pose, 8 frames** (NOT part of South) |
| 37–58 | North/back figure (kept, dir 1) |
| 70–88 | East/right profile (kept, dir 2) |
| 96–105| **stowaway: small galloping left-facing figure, 8 frames** (NOT part of West) |
| 110–124| West/left profile walk (kept, dir 3) |

The spec's raw `figure(r,c) = 16x32 cell` slice (A2) would have baked the lying
unicorn into every South cell and the mini-gallop into every West cell (bboxes
r0=(4,2,15,32), r3=(1,0,15,29) and r3's ~190 opaque px vs ~120 for other rows prove
the double occupancy). **I sliced per-direction content bands instead of raw 16x32
cells.** Band gaps were verified empty across all 8 frame columns.

Bonus finding for the lane: the two stowaway rows are usable art — the lying pose is
a natural `sheep_dead` upgrade and the gallop row a future "run" state. Not used here
(task says dead states unchanged).

## Cell assembly

- Horizontal: each 16px-wide band crop pasted at x+8 (centered in the 32px cell),
  preserving per-frame jitter.
- Vertical: bottom-aligned per direction to the live sheep's measured ground line
  (sheep_bare per-cell content bottoms = 29,29,28,28 → baseline y=28), one fixed dy
  per direction so the inter-frame walk bob is preserved. This deviates from spec A2
  ("keep y") because after removing stowaways the figures are 15–22px tall, not 32.

## Frame choices (4 of 8 per direction, temporal order kept)

Selection criterion: brute-force all C(8,4) subsets, maximize the MINIMUM
cyclic-consecutive mean-abs frame difference (avoids two near-identical adjacent
frames in the loop), tie-break on total pairwise motion. Chosen:

| dir | frames | min cyclic diff | total motion | note |
|---|---|---|---|---|
| 0 South | 2,3,5,7 | 4.93 | 35.5 | subtle idle bob |
| 1 North | 1,2,5,7 | 5.48 | 35.0 | subtle idle bob |
| 2 East  | 0,2,4,5 | 38.80 | 225.8 | the real walk row — biggest motion |
| 3 West  | 1,5,6,7 | 10.22 | 66.6 | moderate walk |

(Spec-default (0,2,4,6) scores lower on every row; e.g. East 126.6 vs 225.8 total.)

## Wool/mane mask rule (spec A3a, v1)

The task allowed a geometric head-crest box; the pixels support something better:
the mane, horn, tail-tip and hooves are the only GOLDEN pixels on an otherwise
near-white body. Mask rule actually used, per figure band:

    keep pixel iff  alpha>0  AND  R>150  AND  B < R-40  AND  B < G-30   (golden test)
                    AND  band-local y < 0.65 * band height              (excludes golden hooves)

Result: 6–16 mane/horn px per cell (South 6–7, North 7, East 9–16, West 13–14) —
sparse but correctly placed (horn + mane crest; see composite GIF: rainbow lands on
mane/horn only, not the body). If the lane wants a chunkier glow, dilate the mask by
1px or drop the 0.65 cutoff (adds hooves).

## Proposed meta.json

- `license: CC0-1.0`, `size: 32x32`, copyright string carried over VERBATIM from the
  live meta (Imagen 4 provenance note).
- `sheep_bare` and `sheep_wool`: `directions: 4`, `delays: [[0.2,0.2,0.2,0.2]] x 4`
  (matches upstream `space_sheep_wool_glowmask` precedent cited in the spec).
- `sheep_dead`, `sheep_wool_dead`, `icon`: unchanged 1-dir 1-frame `[[1]]`.
- Note: the copyright string still claims "mane split to wool layer" — after this
  bake that claim is finally TRUE (it wasn't in the raw sheet, spec risk R1).

## Consistency check results (check_consistency.py)

    sheep_bare       128x128  16 cells  4 dirs x 4 frames  need 16  OK
    sheep_wool       128x128  16 cells  4 dirs x 4 frames  need 16  OK
    sheep_dead       32x32     1 cell   1x1                need 1   OK
    sheep_wool_dead  32x32     1 cell   1x1                need 1   OK
    icon             32x32     1 cell   1x1                need 1   OK
    png<->meta file agreement: clean both ways
    RESULT: ALL CHECKS PASS

## Open risks / for the reviewer

- Spec R5 stands: figures are 11–15px wide in a 32px cell — visibly slimmer than the
  sheep silhouette in-game. Cosmetic; a re-art task if it bothers anyone.
- Wool layer is sparse on S/N (6–7 px). Functional, but the "rainbow mane" reads
  best on E/W profiles. See mask-rule note for how to fatten it.
- In-game verification (spec step 5 — spawn SolreignMobUnicorn, rotate, shear, kill)
  still required after landing; cannot be done from this staging run.
- Landing this = replace the five PNGs + meta.json in
  `Resources/Textures/_Solreign/unicorn.rsi/` with `unicorn-rebake/unicorn.rsi/*`
  and DELETE the raw `unicorn.png`. `unicorn.yml` needs no change (spec A5).
