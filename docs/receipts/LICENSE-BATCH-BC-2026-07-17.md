# LICENSE BATCH B+C receipt — 2026-07-17

Branch `feat/license-batch-bc` off GAME master `481bcc212e`.
Spec: LICENSE-CENSUS-W7-2026-07-16 (orch-ops), Batches B (9 root NC tiles,
absorbing the census's D rock-tiles) + C (7 NC parallax layers).
Closes the LAST 16 NC texture declarations: **census 16 → 0**.

## Method note (RAIL compliance, both batches)

All replacements are PROCEDURAL — deterministic Python generators, no AI
image generation, no img2img/inpaint/ControlNet, no sampling or tracing of
NC pixels. The NC originals were VIEWED only to describe their SUBJECTS and
unprotectable technical facts (canvas size, variant count from
`Prototypes/Tiles/floors.yml`, alpha convention, tiled-vs-untiled from the
parallax prototype configs, palette mood). Fresh palettes and compositions
were authored in-script; every output pixel comes from the generators:

- `docs/receipts/license-batch-bc/draw_tiles_batch_b.py`
- `docs/receipts/license-batch-bc/draw_parallax_batch_c.py`

Gates (batch-A convention): size/format match, opaque-pixel ratio >= 0.55
vs original, EDGE-WRAP gate (wrapped seam luminance step <= 1.05x strongest
interior step + 1.0) for every tileable 32x32 variant and every tiled
parallax layer. The tile generator retries deterministically (seed+13) when
a voronoi crack legitimately lands on the wrap column — only passing
variants ship (one retry needed: chromite variant 4, seed try 1).

## Batch B — 9 root NC tiles (`Resources/Textures/Tiles/`)

| File | Canvas | Variants | Orig license (removed) | Subject regenerated |
|---|---|---|---|---|
| metaldiamond.png | 32x32 | 1 | goonstation NC-SA3 | gray diamond-plate steel |
| arcadeblue2.png | 32x32 | 1 | goonstation NC-SA3 | navy arcade carpet, fresh neon motifs |
| carpetclown.png | 32x32 | 1 | goonstation NC-SA3 | rainbow torus-voronoi patchwork |
| carpetoffice.png | 32x32 | 1 | goonstation NC-SA3 | teal twill + color flecks |
| boxing.png | 128x32 | 4 | goonstation NC-SA3 | pale-blue worn canvas mat |
| gym.png | 128x32 | 4 | goonstation NC-SA3 | crimson worn mat |
| cave.png | 224x32 | 7 | Mojave-Sun NC-SA3 | gray-brown cracked cobble |
| cavedrought.png | 256x32 | 8 | Mojave-Sun NC-SA3 | dry dirt, pebbles + cracks |
| chromite.png | 224x32 | 7 | Mojave-Sun NC-SA3 | blue-violet cracked cobble |

All 34 32x32 variants passed the edge-wrap gate; ratio 1.00 (fully opaque,
matching originals). Attributions: the goonstation 6-file entry and both
Mojave-Sun entries replaced by one `CC0-1.0` Solreign in-house entry
pointing at the generator + this receipt.

Live-map exposure de-NC'd: metaldiamond (leviathan/oasis/perihelion/
verdant/terminus), arcadeblue2 (leviathan/terminus), carpetclown
(leviathan), carpetoffice (verdant/perihelion), boxing (terminus), plus all
placeable FloorTileItems and dungeon/salvage/biome content (cave,
cavedrought, chromite).

## Batch C — 7 NC parallax layers (`Resources/Textures/Parallaxes/`)

**Usage ground truth (supersedes any "maybe unused" assumption):**
`Content.Client/MainMenu/UI/MainMenuControl.xaml.cs` random-picks the main
menu background from an allowlist containing `PlasmaStation`,
`AmberStation`, `OriginStation`, `BagelStation`, `ExoStation`,
`TrainStation` — so those parallaxes are player-visible on every game boot,
and their NC layers had to be REGENERATED, not deleted. `plasma.yml`,
`bagel.yml`, `exo.yml` also ship as loadable maps referencing them.
Tiling convention: `Content.Client/Parallax/Data/ParallaxLayerConfig.cs:29`
— `Tiled` defaults **true**; `planet`/`gas_giant` layers set
`tiled: false` in their prototypes (single-composition discs, no wrap
needed); the other four are tiled and were drawn on the torus (mod-size),
then edge-wrap-gated.

| File | Canvas | Disposition | Evidence |
|---|---|---|---|
| planet.png | 480x480 | REGEN (untiled molten world + debris ring + icy moon) | plasma.yml, bagel.yml; main-menu visible |
| gas_giant.png | 480x480 | REGEN (untiled purple banded giant + dust ring + moonlet) | origin.yml; main-menu visible |
| Asteroids.png | 480x480 | REGEN (tiled shaded rock scatter; ratio 0.95) | plasma.yml, amber.yml, train.yml |
| debris_small.png | 480x480 | REGEN (tiled tiny scrap glyphs; opaque 664 vs 631 orig) | amber.yml; main-menu visible |
| space_map3.png | 1024x1024 | REGEN (tiled fully-opaque deep-space field + stars) | exo.yml; main-menu visible |
| XenoParallaxNeb.png | 1024x1024 | REGEN (tiled lavender nebula puffs; ratio 1.15) | exo.yml |
| core_planet.png | 480x480 | **DELETED** with `Prototypes/Parallaxes/core.yml` | zero-reference proof below |

**core_planet zero-reference proof:** its only user was the `CoreStation`
parallax prototype (`core.yml`). Repo-wide
`grep -rIn 'CoreStation' Resources Content.Client Content.Server
Content.Shared Content.IntegrationTests Content.Tests` → **zero hits**
after deletion (and before deletion, the only hit outside `core.yml` was
none — it is absent from the MainMenuControl allowlist, every shipped map,
and all code). Deleting the prototype removes the file's last reference;
both were removed together with the NC declaration.

All four tiled outputs passed the edge-wrap gate (exact-by-construction
torus drawing; measured wrap step far below interior max). `space_map3`
verified fully opaque like its original.

Attributions: the five NC entries (planet / gas_giant / Asteroids /
debris_small / space_map3+XenoParallaxNeb) replaced by one `CC0-1.0`
Solreign in-house entry; the core_planet entry deleted with its file.

## Contact sheets (mandatory orchestrator eyeball)

- `docs/receipts/license-batch-bc/CONTACT-tiles.png` — per tile, ORIG (NC)
  above NEW (CC0), 4x nearest.
- `docs/receipts/license-batch-bc/CONTACT-parallax.png` — the 6 regenerated
  layers, ORIG above NEW, 256px thumbs (deleted core_planet appears only in
  the proof list above).

## Census re-run (W7 method: PyYAML parse of every attributions.yml, NC regex on the license field only, on-disk existence check)

| Metric | Before (post-batch-A) | After batch B+C |
|---|---|---|
| NC texture declarations via attributions.yml | 16 | **0** |
| NC audio declarations | 92 | 92 (separate gated lane, untouched) |
| NC in non-texture/non-audio attributions | 0 | 0 |
| meta.json `"license": "CC-BY-NC*"` under Resources/Textures | 0 | **0** |

(The loose substring `CC-BY-NC` still matches 64 meta.json files — all are
provenance prose in *copyright* fields of clean regenerated CC0 art, e.g.
"replaces CC-BY-NC art"; the license-field metric is the census metric and
is 0.)

## Verification battery (Release, this branch, all foreground)

- `dotnet build -c Release` — 50 projects, **0 errors** (1245 pre-existing warnings)
- `dotnet test Content.Tests` — **2298 passed, 0 failed, 3 skipped**
- `dotnet test Content.IntegrationTests --filter GameMapsLoadableTest` — **246/246 passed** (all shipped maps load with the regenerated tiles + parallaxes, and without core.yml)
- `dotnet test Content.IntegrationTests --filter Solreign` — **307 passed, 0 failed, 1 skipped** (pre-existing skip)
- `Content.YAMLLinter` — **"No errors found"**

## Commits

1. `71e8348327` — batch B: 9 root NC floor tiles regenerated procedurally CC0
2. `f2fa1270ae` — batch C: 6 NC parallax layers regenerated CC0 + CoreStation deletion
3. (this receipt)
