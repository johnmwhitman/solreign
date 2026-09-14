# LICENSE BATCH A receipt — 2026-07-16

Branch `feat/license-batch-a` off GAME master `e29185f6a8`.
Spec: LICENSE-CENSUS-W7-2026-07-16 (orch-ops), Batch A + hygiene.

## 1. Lobby screen purge (DELETE)

`Resources/Prototypes/lobbyscreens.yml` registers **only**
`/Textures/LobbyScreens/solreign.png` — no other lobby art can display.

Zero-reference claim **re-verified on this branch** per exact filename:
`grep -rIl "<name>.webp" .` (whole tree, .git excluded) → only hit for every
one of the 10 names was `Resources/Textures/LobbyScreens/attributions.yml`
itself. The only code touching the directory is
`Content.Client/Clickable/ClickMapManager.cs`, which lists
`/Textures/LobbyScreens` in an **ignore** array (no dynamic enumeration).

Deleted (8 webp + 8 `.webp.yml` texture-param sidecars):
`warden`, `pharmacy`, `ssxiv`, `susstation`, `doomed`, `blueprint`,
`behonker`, `reclaimer-nuke`.

Pruned **10** NC declarations from `LobbyScreens/attributions.yml`: the 8
above plus the 2 stale entries whose files were already gone
(`supermatter.webp`, `robotics.webp` — confirmed absent on disk before edit).
The 5 clean entries (CC-BY-SA/CC0: skellyvstherev, terminalstation,
justaweekaway, invisiblewall, janishootout) kept verbatim, original order.

## 2. Attributions hygiene — Voice/Talk malformed entry

`Resources/Audio/Voice/Talk/attributions.yml` had one comma-joined string
masquerading as a file list:
`- files: ["vulp.ogg, vulp_ask.ogg, vulp_exclaim.ogg"]`
→ split into three proper list items. License/copyright/source unchanged
(formatting fix, not a relicense). NC audio census count unaffected (the W7
census already counted it as 3).

## 3. Concrete 6-pack procedural regen (Tiles/Planet/Concrete)

Files: `concrete.png`, `concrete_mono.png`, `concrete_smooth.png`,
`grayconcrete.png`, `grayconcrete_mono.png`, `grayconcrete_smooth.png`
(FloorConcrete* / FloorGrayConcrete* — live on Terminus floors).

**Method note (RAIL compliance):** the NC originals were VIEWED only to
describe unprotectable technical facts — canvas 128x32 RGBA = 4 variants of
32x32 (`floors.yml: variants: 4`), three geometry roles (tile = 2x2 grid of
16px sub-tiles with grout; mono/slab = one bordered slab per tile; smooth =
featureless speckle), fully-opaque alpha, and the palette *mood* (warm
greenish-beige family / dark cool gray family). Replacements were then
designed fresh: palettes authored in-script (not sampled), deterministic
integer-hash speckle + TILE-periodic lattice mottle, worn-industrial pits,
chips and hairline cracks (cracks kept ≥3px from all edges so random variant
adjacency never cuts one). No NC pixels were read, sampled, traced, img2img'd
or otherwise conditioned on — every output pixel comes from
`docs/receipts/license-batch-a/draw_concrete.py`.

**Gates (all green, output in the script run log):**
- size/format: each strip 128x32 RGBA, fully opaque (matches originals)
- opaque-pixel ratio (wave-6 convention): 1.00 for all 6
- EDGE-WRAP gate, per variant (24 tiles): wrapped seam strength
  (col31→col0, row31→row0 mean-abs luminance diff) ≤ 1.05× the strongest
  transition already inside the texture → tiling introduces no new seam.

**Attributions:** the goonstation CC-BY-NC-SA-3.0 entry for these 6 replaced
with `CC0-1.0`, Solreign in-house, pointing at the generator script and this
receipt. (These tiles are attributions-declared; no per-file meta.json.)

**Contact sheet (mandatory orchestrator eyeball):**
`docs/receipts/license-batch-a/CONTACT-concrete.png` — per file, ORIG (NC)
strip on top, NEW (CC0) strip below, 4x nearest-neighbour.

## 4. Census re-run (same method as W7: PyYAML parse, NC regex on the license field only)

| Metric | Before (W7) | After batch A |
|---|---|---|
| NC texture declarations via attributions.yml | 32 declared / 30 on disk | **16 declared / 16 on disk** |
| Stale NC declarations (file missing) | 2 | **0** |
| NC audio declarations | 92 | 92 (unchanged; separate lane) |
| NC meta.json under Resources/Textures | 0 | 0 |

Remaining 16: 7 Parallaxes (batch C) + 9 root Tiles (batches B/D).

## 5. Verification battery (Release, this branch)

- `dotnet build -c Release` — 50 projects, **0 errors** (1244 pre-existing warnings)
- `dotnet test Content.IntegrationTests --filter GameMapsLoadableTest` — **246/246 passed** (Terminus loads with the regenerated tiles)
- `dotnet test Content.Tests` — **2216 passed, 0 failed, 3 skipped** (license/attribution tests included)
- `Content.YAMLLinter` — **"No errors found"**

## Commits

1. `42d0dfece2` — lobby purge + attributions hygiene (18 files, −16 assets, −10 NC declarations, vulp fix)
2. `1ea4854dc1` — concrete 6-pack procedural regen + CC0 attributions + generator + contact sheet
3. (this receipt)
