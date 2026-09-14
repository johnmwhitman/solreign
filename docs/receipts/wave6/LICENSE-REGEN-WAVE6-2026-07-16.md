# LICENSE REGEN — Wave 6 (SR-W-084) — 2026-07-16

Branch `feat/regen-wave6` off master `da83722e43`. Worktree `~/AI/solreign-trees/regen-wave6`.

## Re-census (hard law, run BEFORE work, master da83722e43)

Method: parse EVERY `Resources/**/meta.json` (incl. 45 UTF-8-BOM files a naive
grep misses) and check actual `license` FIELDS (top-level + per-state), not
prose mentions. Audio untouched (separate gated lane).

**TRUE meta.json NC count: 4**

| asset | status |
|---|---|
| `Objects/Power/powersink.rsi` | **DONE this wave** (procedural; text-to-image capability boundary, rejected 3x prior) |
| `Interface/Default` (goonstation HUD set) | **DONE this wave** (56 png + 4 svg, procedural) — was never on the ledger; inherited from upstream |
| `Objects/Fun/Plushies/lamp.rsi` | in-flight on unmerged `feat/regen-final` @ fed6001425 — NOT redone here |
| `Objects/Fun/capgun.rsi` | in-flight on unmerged `feat/regen-final` @ fed6001425 — NOT redone here |

The task-menu items were all STALE (already on master): glass_clear/lube-tube/
glue-tube fills (CC-BY-SA), greyscale decals (bricktile/minitile/slats CC0),
equipped/worn states (worn-integrate merged @ c73b57037e; zero NC equipped
states remain → the equipped-probe is moot for license debt).

**NEW DEBT SURFACED (outside the meta.json census method): attributions.yml
declares CC-BY-NC-SA over 32 texture files** — Tiles 9 (arcadeblue2, boxing,
carpetclown, carpetoffice, gym, metaldiamond, cave, cavedrought, chromite),
Tiles/Planet/Concrete 6, LobbyScreens 10 (webp artworks — cheapest fix is
delete-from-pool), Parallaxes 7 (planet/gas_giant/Asteroids/core_planet/
debris_small/space_map3/XenoParallaxNeb — procedural starfield/nebula
candidates). Not executed this wave (not on the menu; orchestrator call).

## Executed

1. **powersink** (`draw_powersink.py`): front-on symmetric console drawn from
   scratch in code — screen w/ red glow scanline, 3 segmented coil towers, gold
   contacts, legs. Inhands (64x64 2x2 4-dir grids) rigged via the measured
   hand-anchor convention (`probe-img2img/inhand_rig.py`). Ratio 0.87 asserted
   at integration. Sheet: `CONTACT-powersink.png`.
2. **Interface/Default** (`draw_interface_default.py`): full HUD set — 23 slot
   icons (original ASCII-art glyph designs: backpack, belt, ear, sunglasses,
   gloves, beanie, ID, gas mask, scarf, pouch, boots, suit, armor+strap,
   jumpsuit, X-harness, mittens w/ 3x5 letter font, marker frames), 12 root
   chrome (slot bg, blocked, highlight, item-status panels, list arrows + fresh
   hand-authored SVG sources), 21 storage-grid tiles. Drop-in identical
   filenames (`camo.png`/`contra.png` are hardcoded in
   `StrippableBoundUserInterface.cs` — no code changes needed).
   Ratio gate asserted across all 56 files at integration (fails=[]); the gate
   CAUGHT undersized list arrows (0.24-0.30) on the first run — enlarged.
   Semantic eyeball iterations: glasses (unreadable → redrawn larger), neck
   (too thin → thickened), ears (read as a mirror → refilled silhouette).
   Sheet: `CONTACT-interface-default.png` (before/after all 56, ratios inline).

## Legal rail

No NC pixels read, sampled, traced, img2img-conditioned, or composited.
Originals viewed only to describe subjects and technical parameters (canvas
sizes, alpha conventions, HUD color family — unprotectable facts). All glyph
layouts are original designs authored in the scripts.

## Honest exclusions

- lamp + capgun: intentionally skipped (live on `feat/regen-final`, unmerged —
  merging that branch closes them; redoing = the stale-master collision again).
- attributions.yml debt (32 files) + undeclared-license goonstation-style HUD
  sibling themes (Plasmafire/Slimecore/etc carry NO license declaration at
  all): surfaced, not executed.
- equipped-/worn-state img2img probe: NOT run — census shows zero NC equipped
  states remain, so there is no license-debt case for it.
- Audio NC: untouched (separate gated lane).
