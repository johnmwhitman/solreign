# Receipt — Sprite-animation factory WAVE 3 (feat/sprite-idle-anims)

**Date:** 2026-07-15 · **Branch:** `feat/sprite-idle-anims` (extends 40ef3d05cd pilot + 5c43b785a3 wave 2) · **Scope:** the holographic cake TODO (new derived RSI) + idle animations for 12 EasterEggs. Still NOT merged, NOT pushed — post-Grand-Opening content, orchestrator review gate.

## 1. Holographic cake — NEW RSI, resolves two TODO(sprite factory) entries

`Resources/Textures/_Solreign/holographic_cake.rsi` (Claude-built, not fleet):
- **Derivation:** upstream `Objects/Consumable/Food/Baked/cake.rsi` birthday states → luminance→acid-green hologram ramp (rgb(16,54,22)→rgb(201,253,143)), scanlines (alternate rows ×0.78), translucency (alpha ×0.67). A plain multiply of the old `#c9fd8faa` tint was tried first and rejected — it still read as red cake, not projected light.
- **`birthday`** (world state): 4-frame budget-frame-rate flicker — stable hold 0.6 s / scanline roll + flare 0.1 s / hold 0.35 s / dim glitch 0.08 s (rows 12–13 jump 1 px, alpha drops). **`birthday-inhand-left/right`**: 4-dir, statically treated.
- **License:** CC-BY-SA-3.0; meta.json copyright carries the full tgstation attribution chain + Solreign modification note.
- **Prototype wired** (`portal_props.yml`): `SolreignHolographicCake` now uses the dedicated RSI; runtime `color:` tint removed; `unshaded` + PointLight kept; `heldPrefix: birthday` still resolves (inhand states present). Both TODO comments (header + entity) resolved.
- Reproducible: `wave3/build_holo_cake.py`.

## 2. EasterEggs — 12 in-game idle animations (fleet-designed)

Design dispatch: 6 sprites each to **cdx** and **grk** (briefs with palette-indexed pixel maps + flavor lines: `wave3/egg-brief-*.txt`; raw specs: `wave3/*-eggs.clean.json`). New executor op `hue_shift` added for the rainbow blade. All masks ground-truthed via dry-run diff counts + visual montage (`wave3/egg-review-montage.png` — first column of each row is frame 0 = the original).

| Egg | Concept | Frames × delays |
|---|---|---|
| egg01_aperture | portal-core breathe → white-green surge → charged glint | 4 × [0.55, 0.38, 0.14, 0.2] |
| egg02_trifecta | golden gleam sweeps top→mid→base | 4 × [0.55, 0.16, 0.16, 0.22] |
| egg05_blocky_sword | enchant glint races tip→edge→guard | 4 × [0.5, 0.12, 0.14, 0.2] |
| egg06_rainbow_sword | blade hue-cycles through spectral alignments (hue_shift) | 3 × [0.5, 0.18, 0.18] |
| egg08_grace_flask | liquid glow breathes, bubble highlight rises | 4 × [0.45, 0.28, 0.32, 0.35] |
| egg10_red_pendant | heartbeat: hold → swell → flash → settle | 4 × [0.55, 0.12, 0.16, 0.38] |
| egg15_wrist_organizer | CRT scanline rolls down the green display | 4 × [0.55, 0.12, 0.12, 0.2] |
| egg17_plasmid_injector | cyan charge races up the reservoir | 4 × [0.55, 0.14, 0.14, 0.32] |
| egg19_pocket_radio | status LEDs alternate, grille breathes | 4 × [0.4, 0.22, 0.22, 0.35] |
| egg20_omni_gauntlet | orb breathe/surge, gauntlet greens catch the light | 4 × [0.6, 0.14, 0.14, 0.18] |
| nanoware_trenchcoat | circuitry boots hem-upward | 4 × [0.58, 0.16, 0.16, 0.4] |
| moon_sugar_beaker | brew sparkles, hue-warps, deep pulse | 4 × [0.4, 0.2, 0.28, 0.22] |

**Fleet quality note:** 11/12 fleet designs landed after ground-truthing. cdx's omni_gauntlet mask resolved to 1 px (fabricated "three stones") — redesigned by Claude from the real pixel map (the sprite is an orb over a bracer). egg11_winged_berry was already animated (pre-existing house reference) and untouched; the remaining 19 EasterEggs were judged poor animation candidates (no glow/energy feature that reads at 32×32) and left static.

## 3. Website assets (staged UNCOMMITTED in website-v2, no pages edited)

- `anim/holographic_cake.gif` (256×256, true delays, disposal=2, transparent) + `ANIM-MANIFEST.json` entry (7th public sprite) — **EasterEggs deliberately excluded from all web assets (spoilers); verified zero egg files in the anim dir.**
- `sprite-holographic-cake-128/256.png` statics + `MANIFEST.json` entry, matching the existing per-sprite pattern (kebab-case, label/blurb/source/license).

## Verified (programmatic)

- All 12 eggs: PNG grid = declared frames; frame 0 byte-identical to `git show master:` original; sibling states byte-identical, no stray `delays`; no dead frames (per-frame diff > 0); license/copyright preserved verbatim.
- Cake: 128×32 4-frame strip + 64×64 4-dir inhands; meta delays `[[0.6, 0.1, 0.35, 0.08]]`; tgstation attribution asserted present.
- Both web manifests re-parsed as valid JSON after update.
- YAML linter (prototype changed this wave): exit 0, "No errors found in 50239 ms."

## Needs John's eyeball

Holographic cake in-game (unshaded + PointLight will make it pop more than the sheet suggests — if it reads too pastel, raise the ramp's HI green or alpha); rainbow sword hue-cycle taste check; egg flickers at the dev client when found naturally.
