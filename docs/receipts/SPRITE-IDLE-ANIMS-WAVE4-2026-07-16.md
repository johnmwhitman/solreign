# Receipt — Sprite-animation factory WAVE 4 (feat/sprite-idle-anims)

**Date:** 2026-07-16 · **Branch:** `feat/sprite-idle-anims` (extends 40ef3d05cd pilot + 5c43b785a3 wave 2 + 3f5e80282d wave 3) · **Scope:** idle animations for 9 more static sprites (11 physical RSI states), designed by **grk** (Grok — the fleet's first worker on this factory), executed deterministically via a wave-4 copy of the wave-3 executor. NOT merged, NOT pushed.

## 0. Candidate survey — a scope note

`Resources/Textures/_Solreign/` is now fully exhausted: all 7 top-level custom RSIs (companion_cube, hot_potato, pool_noodle, scarlet, exec_recharge_pod, unicorn, holographic_cake) are animated (waves 1–3), and every EasterEgg has been triaged (13 animated across waves 1–3 incl. the pre-existing egg11_winged_berry; the remaining 19 were already judged poor candidates in wave 3). Per the mission brief, wave 4 was also pointed at "the recently REGEN-integrated assets from waves 3a/3b" — but this worktree's branch point (b74fe79b) is **34 commits behind current `master`** (055b89858a), and those REGEN waves (`orch-regen-wave3a`/`3b`) landed only on master afterward. Reaching them would require a merge/fast-forward, which is out of this task's hard scope ("must NOT merge, must NOT touch any other worktree"). Read the wave3b receipt read-only via `git show master:docs/receipts/LICENSE-REGEN-WAVE3B-2026-07-15.md` for awareness only — nothing from it was pulled in.

Candidates were instead sourced from the broader **vanilla SS14 asset tree already checked out on this branch** (3,078 RSIs), scanning for static single-frame sprites with a legible glow/energy/crystal/display feature at 32×32, the same criteria the mission specified. Two safety findings shaped selection:
- **Runtime tinting.** Several vanilla assets (crystal/shard/glowstick textures) are stored as flat *grayscale* masks that the game re-colors per-entity at runtime (`Sprite: color: "#..."` + a matching `PointLight`, e.g. `CrystalGreen` tints `crystal_grey` green, `GlowstickRed` tints `glowstick_lit`/`glowstick_glow` red). Verified via the entity prototypes (`Resources/Prototypes/Entities/Structures/Decoration/crystals.yml`, `Objects/Materials/crystal_shard.yml`, `Objects/Tools/glowstick.yml`). For these, briefs restricted Grok to **only** `brightness`/`alpha`/`lerp-toward-white-or-black` ops — tint-agnostic under multiply-tinting — and forbade `hue_shift` or colored `lerp`. Two sprites (telecrystal, oracle_screen) were verified tint-free (single fixed-use entity, no `color:` override) and left free to use color ops.
- **Palette-letter cap.** The wave-3 palette-by-count scheme (62 letters) works for flat cel-shaded pixel art but overflows on anti-aliased vanilla sprites (`lantern-on` alone had 136 distinct colors). Rather than extend the shared palette scheme, candidates were filtered to ones with ≤62 opaque colors, matching the tool as-is (`lantern-on` was dropped for this reason; see below).

## 1. Candidates — chosen and dropped

**Chosen (9 sprites / 11 states):**

| Sprite | RSI path | Feature | Note |
|---|---|---|---|
| crystal_grey | `Structures/Decoration/crystal.rsi` | decorative wall crystal | grayscale, tint-safe ops only |
| telecrystal | `Objects/Specific/Syndicate/telecrystal.rsi` | glowing red currency crystal | fixed color |
| crystal_shard1/2/3 | `Objects/Materials/Shards/crystal.rsi` | raw crystal shards ×3 | grayscale, tint-safe ops only |
| oracle_screen | `Structures/Wallmounts/screen.rsi` | wall monitor, reused by the Solreign `SolreignDirectiveTerminal` ("a dull acid-green monitor, always half-lit... something is deciding whether to answer") | fixed color — verified no other entity tints `screen`, so this one got a deliberate green phosphor lerp |
| glowstick_lit (+glowstick_glow, Claude-synced) | `Objects/Misc/glowstick.rsi` | lit chemical glowstick body + its unshaded glow-mask layer | grayscale body, tint-safe ops only |
| flashlight_overlay | `Objects/Tools/flashlight.rsi` | unshaded lens-glow overlay on a lit flashlight (composited over the untouched base body layer) | fixed color, has an independent 2px status-LED accent |
| carp_statue_eyes (+teeth_unshaded, Claude-synced) | `Structures/Specific/carp_statue.rsi` | unshaded glowing-eyes/teeth overlay on an alien guardian statue (single-use entity, `xeno.yml`) | fixed color |

**Dropped:**
- `lantern-on` (Objects/Tools/lantern.rsi) — 136 distinct colors, overflows the 62-letter palette scheme; would need a tool extension (quantized/bucketed palette) that wasn't warranted for one asset this wave.
- `cash`/low denominations, `artifact_fragments.rsi` — no legible glow/energy feature at 32×32 (plain paper bills; static depletion-level fragments with no established glow palette).
- Vanilla anomaly `*_core.rsi` (Structures/Specific/Anomalies/Cores) — each already has a dedicated 4-frame `pulse` state distinct from the static `core` state; animating `core` would blur an existing deliberate active/inert game-state distinction. Treated as "already animated" per the mission's skip rule.
- Vending machines, arcade screens, jukebox, statues (32×64) — wrong tile size (not single 32×32), or already have code-driven multi-state visuals (denied/select/screen_* states swapped by game logic, not idle loops).
- `signalscreen.rsi` — PNG is 64×32 but meta declares no directions/frames (likely a pre-existing unused second tile / latent asset quirk unrelated to this work); skipped rather than guess at undocumented behavior.
- `flashlight-on` — not actually wired into the main flashlight prototype's Sprite layers (which use `flashlight` + `flashlight-overlay`); animating it would be cosmetic-only with no in-game effect, so `flashlight-overlay` (the real composited glow layer) was used instead.

## 2. Fleet quality note

**9/9 grk designs landed clean, zero redesigns needed** (better than wave 3's 11/12). Dispatched in 2 batches (6 + 3) to `~/AI/Tools/grk`, foreground, each brief fully self-contained with inline palette-indexed pixel maps + the tint-safety rule. Programmatic QC (`qc_masks.py`) checked every resolved mask size before execution: no fabricated features, no near-zero masks except intentional 1-2px accent pixels (glowstick tip glint, flashlight status LED, carp eye/teeth points) — all using the spec's explicit `"pixels"` form, same idiom as wave 3's `spark` accents. Grok correctly respected the tint-safety constraint: all 5 grayscale-tinted sprites used only `brightness`/`alpha`/`lerp→white` ops; the 4 fixed-color sprites used `hue_shift`-free colored `lerp` where it served the concept (telecrystal's ember cool-down, oracle_screen's acid-green phosphor).

`glowstick_glow` and `teeth_unshaded` were **not** independently fleet-designed — both are single-flat-color companion mask layers (1 opaque color each) that composite alongside a fleet-designed sibling (`glowstick_lit`, `eyes_unshaded`); Claude reused the sibling's brightness/delay rhythm against each one's own single-pixel-value mask rather than spending a fleet design call on a trivial companion layer. This mirrors wave 3's treatment of inhand states as a "statically treated" companion, but here the companion layers *are* animated in sync, not left static.

**Self-caught bug in the companion pass:** the first `glowstick_glow` attempt mirrored `glowstick_lit`'s literal `brightness`/`lerp` ops and produced three dead frames — `verify.py` caught it (`frame 0->1: 0 px differ`). Root cause: `glowstick_glow`'s stored pixel is already pure white (`255,255,255`) at low alpha (a translucent bloom mask); `brightness` factors >1 clamp at 255 (no-op on already-max channels) and `lerp` toward white is likewise a no-op from white. Separately, the executor's `alpha` op is `min(a, value)` — a ceiling, never a boost — so pushing `value` *above* the base alpha (38) is also a silent no-op. Fixed by re-expressing the breathe as an alpha pulse that dips *below* 38 and back up toward it (25 → 12 → 32), verified non-zero-diff on every frame pair before committing.

## 3. Verification (all programmatic — `docs/receipts/wave4/verify.py`)

- All 11 targets (9 fleet-designed + 2 Claude-synced companions): PNG grid = declared frame count (32×N); frame 0 byte-identical to `git show HEAD:` original; every consecutive frame pair differs (no dead frames); license/copyright preserved verbatim; meta `delays` match the executed spec exactly.
- Sibling states in shared RSIs (e.g. `crystal_shard1/2/3` all live in one `crystal.rsi`; `glowstick_lit`/`glowstick_glow` in one `glowstick.rsi`; `carp_statue_eyes`/`teeth` in one `carp_statue.rsi`; `telecrystal`'s inhand states; `flashlight`'s 7 other states) verified byte-for-byte/meta-for-meta unchanged.
- `git status` confirms exactly the 9 target RSI directories (11 PNGs) + 7 meta.json files touched, nothing else in the tree.
- No prototype YAML was touched this wave (pure RSI asset work) — the YAML linter gate does not apply; skipped per the mission's own conditional.
- Review montage: `docs/receipts/wave4/wave4-review-montage.png` (one row per sprite, first column = frame 0 = the original) — visually confirmed the oracle_screen scanline roll and carp-statue eye flicker read correctly at native scale.

## 4. Reproducibility

`docs/receipts/wave4/`: `dump_pixelmap.py` (palette-indexed pixel-map generator, reused execute_spec's exact palette-letter scheme), `brief1-grk.txt`/`brief2-grk.txt` (the exact dispatched briefs), `grk_batch1_raw.txt`/`grk_batch2_raw.txt` (raw fleet output), `batch1.clean.json`/`batch2.clean.json` (the 9 fleet-designed specs), `companions.clean.json` (the 2 Claude-authored companion-layer specs), `execute_spec.py` (wave-4 executor — same op algorithm as wave 3, generalized RSI path resolution via `RSI_PATHS` since targets live outside `_Solreign`, **website/GIF output deliberately disabled** — wave 3's executor wrote GIFs into the sibling `website-v2` worktree, which is out of scope for this branch), `qc_masks.py` (pre-execution mask-size quality gate — note: only meaningful run *before* `execute_spec.py` has overwritten a target; re-running it after execution against an already-multi-frame PNG recomputes a different palette and reports false "0 px" mask sizes — this is a stale-invocation artifact, not a regression, and `verify.py` is the authoritative post-execution check), `verify.py` (post-execution ground-truth suite), `build_montage.py`.

**Side note, unrelated to the asset work:** a `git checkout -- <one file>` invocation (used mid-wave to revert a bad `glowstick_glow.png` write) triggered this repo's submodule sync as a side effect and materialized a previously-uninitialized nested submodule, `BuildChecker/RobustToolbox/` (~30MB, untracked). It was left in place rather than force-deleted (outside this task's scope to destroy pre-existing submodule structure); it is untracked and was never staged, so it has no effect on this wave's commit, but John may want to `git submodule deinit` or otherwise clean it up if unwanted.

## Needs John's eyeball

The Directive Terminal's green scanline in the live dev client (the acid-green lerp was chosen from the entity's flavor text, not a design reference image — check it doesn't read too faint against the wall). The glowstick/eyes-statue "synced companion layer" treatment (Claude-authored, not independently fleet-designed) — flag if that convention shouldn't extend to future waves. Whether `lantern-on` (dropped for palette-cap reasons) is worth a dedicated tool extension in wave 5.
