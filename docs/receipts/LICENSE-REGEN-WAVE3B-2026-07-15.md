# Receipt — CC-BY-NC REGEN wave 3b: omnitool recovery + icon-only sweep (feat/orch-regen-wave3b)

**Date:** 2026-07-15 · **Branch:** `feat/orch-regen-wave3b`, off GAME master · worktree `~/AI/solreign-trees/orch-regen-wave3b` · NOT merged, NOT pushed — orchestrator review + MERGE LAW gate (only Claude merges). All commits local.

**NC census: 80 → 71.** 9 assets closed.

## 1. omnitool — RECOVERED (wave 1's only outright FAIL)

**Verdict: recovered.** Wave 1 failed because independent text-to-image calls for the 5 tool-head states (`omnitool-prying/screwing/pulsing/snipping/wrenching`) produced 5 unrelated objects (crowbar, ray gun, pistol, hammer — see `docs/receipts/probe-img2img/omnitool-raws.png`). Ground truth (`Resources/Textures/Objects/Tools/omnitool.rsi`) is a cream/tan barrel tool with an orange dashed seam stripe, grey rear grip, and a gold hex collar with a green center gem, with 5 distinct interchangeable front heads.

**Method (per `PROBE-IMG2IMG-COMPLEX-2026-07-15.md`, the sanctioned technique — never conditioned on the NC original):**
1. Generated 4 candidate CC0 anchor bases via **MiniMax image-01** (`omnitool/pulsing_anchor_v1..4_raw.png`), fanned in parallel. Picked v4 — cleanest silhouette: dark pistol-grip body, orange stripe accent, gold ribbed collar, green gem, rounded gold tip.
2. **img2img self-conditioned** on that one anchor (never on NC pixels) via `gemini_image.py edit --model gemini-2.5-flash-image --vertex`, 4 separate edit calls, each instructed to change only the front tip while holding the body/grip/stripe/collar/gem identical: screwdriver bit (screwing), forked pry hook (prying), cutting-plier jaws (snipping), open-end wrench jaw (wrenching).
3. **Result: identity held perfectly across all 5 states** — same body, grip, orange stripe, gold collar, green gem in every frame, only the tool head changing. This is exactly the result independent generation could not produce.
4. Post-processed 1024px raws → 32×32 via the pilot's `process_sprite.py` pipeline (chroma-key cyan, despeckle, crop, quantize). One color-fidelity bug found and fixed: BOX-resample averaging blended the small green gem with the adjacent dark body, shifting it toward teal; fixed with a targeted cyan→green pixel remap (the only non-key hue in the design, so safe to correct globally) rather than accepting the drift.

**Deliverable:** `Resources/Textures/Objects/Tools/omnitool.rsi` — 5 states, exact name parity (`omnitool-prying/-screwing/-pulsing/-snipping/-wrenching`), 32×32, `CC0-1.0`.

## 2. Icon-only bucket (`other` in nc-classes.json) — 8 done, 7 skipped

**Reference-first gate honored throughout:** every asset's ground-truth PNG + meta.json was viewed and described before any prompt was written (see triage contact sheets under the scratchpad; e.g. the ore materials, cash denominations, carp plush colors were all read off the actual pixels, not guessed from the state name — the exact failure mode that sank wave 1's `snap_pops`).

### Done (8 assets, 101 states)

| Asset | States | Approach |
|---|---|---|
| `directionalfan` | 1 (4×4 anim) | **Procedural** — reused wave 2's `tinyfan` spinning-blade technique (8× supersample, BOX downsample), housing rotated 90°/dir |
| `xenoturret` | 1 (4×10 anim) | **Procedural** — pulsing bio-pod (tentacle legs, glowing green tip) drawn from concept; the 4 declared "directions" are a stationary alien pod replicated identically (defensible: no facing-dependent geometry in the original either) |
| `toy_singularity` | 3 | **Procedural** — swirling spiral vortex (concentric rotating rings), matching the animated black-hole toy concept; `singu-inhand-*` derived via the probe's inhand rig at a large `grow` factor so the glow reads prominently in-hand |
| `cash` | 11 | **Procedural** — bill shape + 3×5 bitmap font (reused killsign's font technique) for the `$`/denomination label; high denominations get a procedural diagonal glint-sweep animation matching their declared frame counts exactly |
| `artifact_fragments` | 30 | **Procedural** — 5 families (precursor/wizard/martian/eldritch/ancient) × 6 depletion levels; ground truth showed each family shrinking from a large intact shape (level 1) to a tiny fragment (level 6), replicated as a size-scaling parameter over one hand-drawn base shape per family |
| `ore` | 30 | **MiniMax** (10 materials, fanned 3 variants each, curated) + **procedural rig** for the 20 `-inhand-left/right` states |
| `carp` | 12 | **MiniMax** (4 plush color variants, fanned, curated) + procedural rig for 8 inhand states; `holoplush`/`holocarpplush-inhand` get a brightness-cycle shimmer animation (reused guardian_info's wave-2 technique) matching declared frame counts |
| `monitors` | 8 | **MiniMax** for `party`/`rad`/`shipalert` device families (rad1's blink and shipalert's 3 alert levels are procedural brightness variants of one generation) + `mobilevision`/`television` (stationary tripod camera drones, one generated view replicated across the 4 declared directions — defensible for a direction-invariant stationary device) |

### Skipped (7 assets, 163 states) — recorded reasons, not capability failures

| Asset | States | Reason |
|---|---|---|
| `guardians.rsi` | 12 | 4-directional layered/composited alien-guardian mobs (base+flare compositing per family, `holoclown` a fully distinct character) — same complexity class as wave 3a/3c directional work, not a simple icon; needs a dedicated directional-mob pass |
| `xeno_artifacts.rsi` | 73 | 36 genuinely distinct alien artifact sculptures (2× `item_artifacts`' count) + activation icon, no inhand; volume of distinct-design curation exceeds this wave's budget |
| `item_artifacts.rsi` | 44 | 11 distinct alien artifact designs × base/animated-glow/inhand×2; tractable in principle (11 icons + procedural glow overlay + rig) but deprioritized behind the 8 delivered assets given time budget |
| `Interface/Default` | 25 | UI-critical hotbar slot glyphs (backpack/shirt/ID-card/etc. silhouettes) seen by every player constantly; needs a dedicated icon-design pass with per-glyph human legibility review, not bulk generation |
| `greyscale.rsi` | 85 | Systematic autotile decal atlas (full/half/quarter/three-quarter tile rotations, brick corner/end/line, minitile family, checker/diagonal/herringbone/offset/pavement/slats); precise geometric tiling alignment is safety-critical (misalignment looks broken on every floor using it) — doable procedurally but sizable enough to deserve its own dedicated geometry pass |
| `golden_toilet.rsi` | 12 | All states are 4-directional structures (disposal unit rotates on the grid), 2 further animated 5-frame states; scoped out to protect time/quality budget |
| `toilet.rsi` | 10 | Same reason as `golden_toilet` (near-identical asset, shares the same directional-structure complexity) |

## Method notes / findings this wave

- **MiniMax rate limits bite at 4-way parallelism.** The first fan-out (4 parallel `mmx_worker.py` shards) hit `rate limit exceeded(RPM)` on ~64% of calls. Reducing to 2 parallel, then 1, cleared the backlog steadily; final tally 61/61 raw generations eventually succeeded (0 permanent failures), just not all on the first parallel pass. Vertex/Gemini img2img hit the same wall at 3-way parallelism (`HTTP 429 RESOURCE_EXHAUSTED`); sequential retries succeeded immediately.
- **A destructive self-inflicted bug, caught before it shipped:** an attempt to clean up a cosmetic magenta drop-shadow remnant (via an aggressive `key_sweep` pass borrowed from wave 2's pipeline) instead ate through dark, desaturated item colors (bananium's dark-brown rock, quantifiably: its Euclidean-distance-to-magenta heuristic misfires on near-black hues) and corrupted several ore icons into unrecognizable fragments. Caught by re-inspecting the "already fixed" curation grids at native pixel scale before integration, root-caused by isolating each pipeline stage, and fixed by reverting to the plain `process_sprite.py` pipeline (no sweep) — the minor shadow cosmetic was accepted rather than risk more damage.
- **Two pre-existing, unrelated build breaks blocked every gate** (`Content.Client/_Solreign/MovementBob/MovementBobSystem.cs` — non-partial class with non-readonly-required `[Dependency]` fields; `Content.IntegrationTests/Tests/_Solreign/StationDirectiveIntegrationTest.cs` — analyzer-forbidden literal in `StartGameRule`; `SolreignMapHealthTypesTest.cs` — nullable-annotation-context error). Confirmed via `git diff`/`git log` that all three predate this branch and are untouched by the asset work. Fixed minimally (matching each error's own established in-repo pattern) **so the mandated gates could actually run** — committed as a **separate commit**, clearly distinguished from the asset-regen commit, so the license work stays additive-only as instructed. Flagging for John/other lanes: this worktree's inherited base could not build at all before this fix.

## Gates (all run FOREGROUND after fixing the build)

- **State-name parity** — exact match vs every original's `git`-committed `meta.json`, verified programmatically (`integrate.py`), all 9 assets.
- **Geometry parity** — every replacement PNG matches its original's exact pixel dimensions; declared `frames × directions` fits the addressable tile grid, all 9 assets.
- **License gate** — NC in → CC0-1.0 out, asserted for all 9 (`CC-BY-NC-SA-3.0`×8, `CC-BY-NC-4.0`×1 → `CC0-1.0`).
- **C# hardcoded-path sweep** — grepped `Content.Server`/`Content.Client`/`Content.Shared` for the 9 RSI paths and every specific state-name string literal (the `killsign` lesson: RSI paths/states can be referenced from C# invisibly to YAML grep). **Zero hits** — no hardcoded dependency risk for any of the 9 assets.
- **YAMLLinter** — `dotnet run --project Content.YAMLLinter -c Release`: **"No errors found in 30677 ms."**
- **GameMapsLoadableTest** — `dotnet test Content.IntegrationTests --filter "FullyQualifiedName~GameMapsLoadableTest"`: **Passed! Failed: 0, Passed: 246, Skipped: 0**, 2m01s.
- **NC census** (utf-8-sig decode, counts every `meta.json` under `Resources/Textures` whose `license` contains "NC"): **80 → 71.**

## Deliverables

- Montage: `docs/receipts/wave3b/wave3b-integrated-montage.png`
- Pipeline scripts (reusable for future waves): `docs/receipts/wave3b/{wave3b_common.py, build_procedural.py, assemble_icons.py, integrate.py, mmx_worker.py}`
- Omnitool working files: `docs/receipts/wave3b/omnitool/` (anchor + 4 self-conditioned heads, raw + processed)
- Raw generations + curation grids: `docs/receipts/wave3b/icons-gen/`
- Staged RSIs pre-integration: `docs/receipts/wave3b/icons-proc/`

## Remaining license debt

**71 NC assets remain**: the 7 skipped icon-only assets above (163 states) plus wave 3a's `inhand_only` list and wave 3c's unsolved `equipped` list (owned by parallel agents in sibling worktrees, not touched here). Recommended next targets in priority order: `item_artifacts.rsi` (44 states, tractable with this wave's exact ore/carp pipeline — 11 icons + procedural glow overlay + rig), `golden_toilet.rsi`/`toilet.rsi` (22 states, needs a directional-structure base + procedural detail-overlay compositing pass), then the harder atlas/UI-critical assets (`greyscale.rsi`, `Interface/Default`, `xeno_artifacts.rsi`, `guardians.rsi`).

## Needs John's eyeball

Spawn the dev client and check: an omnitool held in each of its 5 modes (does the head-swap read clearly against the held body?), the ore/carp inhand poses at native scale (rig-derived, not hand-tuned per item), a shipalert panel cycling its 3 states, and the toy_singularity/xenoturret animations in motion (procedural, never test-rendered in the live engine).
