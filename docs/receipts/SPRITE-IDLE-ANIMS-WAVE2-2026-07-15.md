# Receipt — Sprite-animation factory WAVE 2 (feat/sprite-idle-anims)

**Date:** 2026-07-15 · **Branch:** `feat/sprite-idle-anims` (extends the pilot commit 40ef3d05cd) · **Scope:** the remaining 4 curated `_Solreign/` sprites — **all 6 curated sprites are now animated.** EasterEggs/ untouched (spoilers). Still NOT merged, NOT pushed, no deploy — post-Grand-Opening content, hand-off for orchestrator review.

## Orchestration provenance (this wave was fleet-designed)

Per the orchestrate doctrine, animation *design* was dispatched to the fleet: a self-contained brief (palette-indexed ASCII pixel maps + palettes + entity flavor + op vocabulary, `wave2/design-brief.txt`) went to **cdx (Codex/gpt-5.5)** and **grk (Grok)** in parallel. Both raw specs are preserved (`wave2/cdx-design.clean.json`, `wave2/grk-design.clean.json`). The two designs **converged independently** on horn shimmer / door-LED breathe / noodle sheen / ear flick — then every mask was **ground-truthed against real pixels** before execution (fleet-fabrication rule):

- **Grounded and used:** unicorn horn tip at (27,3) — both models correct (the horn is *green*, which my own brightness heuristic initially missed); pod door greens (cdx's 2 status px AND grk's 4-px inset — merged); grk's open-state cushion letters; cdx's traveling noodle glint.
- **Overridden:** both models proposed a Scarlet "ear flick" via ±10% brightness — **invisible on near-black fur**. Replaced with the classic SS14 mob idle: whole-tile 1-px breathing bob (bottom margin verified ≥2 rows on all 4 direction tiles).

Execution machinery: `wave2/execute_spec.py` (generic spec-executor) + `wave2/merged-spec.json` (the synthesized final spec) — fully reproducible.

## What changed (all in `Resources/Textures/_Solreign/`)

| RSI / state | Anim | Frames × delays | Mask (px changed/frame) |
|---|---|---|---|
| `unicorn.rsi` `icon` | Horn shimmer: tip flashes white, shimmer travels down shaft + faint mane glint | 3 × [0.55, 0.18, 0.18] s | 2 px / 18 px |
| `exec_recharge_pod.rsi` `closed` | Wellness-LED breathe: 2 status px lerp toward bright green, 4-px door inset hums | 3 × [0.45, 0.45, 0.45] s | 6 px / 6 px |
| `exec_recharge_pod.rsi` `open` | Cushion micro-breathe on interior greens (brightness ×1.08/×1.15) | 3 × [0.5, 0.5, 0.5] s | 81 px / 83 px |
| `pool_noodle.rsi` `icon` | Specular sheen slides up the foam highlight ridge | 3 × [0.4, 0.4, 0.4] s | 16 px / 21 px |
| `scarlet.rsi` `scarlet` (4-dir) | Idle breathing bob: whole tile dips 1 px on the second beat, per direction | 2 × [0.9, 0.7] s ×4 dirs | 343–408 px (the 1-px shift) |

`base`/`paper` (pod), `inhand-*` (noodle), and all `scarlet_*dead/old/pup` states: byte-identical to master (asserted). License `CC-BY-SA-3.0` + copyright `Solreign (AI-generated, human-reviewed)` preserved in every meta.json.

## Derived web assets (staged UNCOMMITTED in the website-v2 worktree)

`unicorn.gif`, `pool_noodle.gif`, `exec_recharge_pod.gif` (closed), `exec_recharge_pod_open.gif` (bonus), `scarlet.gif` (south tile) — all 256×256 8× NEAREST, true per-frame delays, disposal=2, corner-alpha-0 verified on every frame. `ANIM-MANIFEST.json`: all 6 curated sprites now `animated: true` with real frame data; re-parsed valid. No website pages edited.

## Before / after sheets

In `wave2/`: `<rsi>-<state>-before-sheet.png` / `-after-sheet.png` for all five states.

## Verified (programmatic, same rigor as the pilot)

- meta.json round-trip: PNG grid = frames × directions exactly; delays rows = direction count; per-direction delays identical.
- Frame 0 of **every direction** pixel-identical to `git show master:` originals.
- Per-frame diffs confined exactly to the designed masks (counts above).
- Untouched sibling states byte-identical to master; no stray `delays` keys.
- Prototypes (`unicorn.yml`, `pool_noodle.yml`, `scarlet.yml`/`pets.yml`, `vampire.yml` coffin layers) reference the same state names — zero YAML edits needed. Note the coffin's door layer swaps `closed`↔`open`, both now animated; `base` deliberately static beneath them.
- YAML linter: exit 0, "No errors found in 50473 ms."

## Needs John's eyeball (same as pilot)

In-game feel at the dev client: unicorn sparkle rhythm (0.55 s pause → 2-frame twinkle), pod LED breathe subtlety, Scarlet's bob cadence (0.9/0.7 s — the one most worth a look; a 1-px bob can read "floaty" if the cadence feels off; trivially tunable delays). GIF aesthetics on the site's dark background.
