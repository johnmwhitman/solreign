# ORCH license hygiene — CC-BY-NC DELETE + SWAP-UPSTREAM lane (2026-07-15)

**Branch:** `feat/orch-license-hygiene` (worktree `~/AI/solreign-trees/orch-license-hygiene`, off GAME master `993c323375`)
**Source of truth:** `~/AI/SUCCESSION/staged/ccbync-swap/SUMMARY.md` + `manifest.json` (2026-07-12 audit, 115 NC assets; buckets: 2 DELETE, 4 SWAP-UPSTREAM, 109 REGEN)
**Scope:** DELETE + SWAP-UPSTREAM buckets only. No REGEN asset touched. No AI-generated art integrated.

---

## Headline finding: 5 of the 6 manifest items were ALREADY DONE on master

The staged manifest (generated 2026-07-12 against `~/AI/Kolton-SS14/server`) predates two
license-hygiene commits that subsequently landed on GAME master and are ancestors of this
branch:

| Commit | Date | What it did | NC census |
|---|---|---|---:|
| (audit baseline) | 2026-07-12 | — | 115 |
| `b062deaba1` | 2026-07-12 | Both DELETE items + 3 of the 4 SWAP items (noir, snakeskin, Jumpsuit/rainbow). Explicitly **skipped** glass_coupe_shape (state mismatch). | 110 |
| `48f45c59b7` | 2026-07-12 | 3 additional swaps of **REGEN-classified manifest entries** (outside the DELETE/SWAP-UPSTREAM scope: bizarresoft→greysoft, Boots/performer→highheelboots, buffrat→regalrat). | 107 |
| `f4cba49085` (this lane, see below) | 2026-07-15 | The remaining manifest item: glass_coupe_shape swap + delete. | **106** |

So the arithmetic in the task brief ("115 − 2 − 4 = 109") holds against the audit baseline —
the DELETE+SWAP buckets account for exactly 6 removals, 115 → 109 — but master had already
absorbed 5 of those 6 plus 3 extra out-of-manifest swaps, so the observed census went
**107 → 106** in this worktree. Verified census method: parse every
`Resources/Textures/**/meta.json` with `utf-8-sig` (two files carry a UTF-8 BOM) and count
`license` containing `NC`.

**No separate "deletes" commit exists in this lane** because there was nothing left to
delete: both DELETE items (`Objects/Fun/Plushies/meta.json` stray, `Objects/Misc/flies.rsi`)
were independently re-verified as gone and unreferenced (see grep proofs below).

---

## What this lane actually changed: the glass_coupe_shape swap

The one item the 2026-07-12 pass skipped, done here with explicit state mapping.

### Reference inventory (exhaustive)
`Objects/Consumable/Drinks/glass_coupe_shape.rsi` was referenced by exactly **one** place in
the whole repo (prototypes, maps, and all C# checked): the `DrinkGlassCoupeShaped` entity in
`Resources/Prototypes/Entities/Objects/Consumable/Drinks/drinks_cups.yml`. Matches the
manifest. No map or C# references.

### State-name mapping decisions
Old NC rsi (11 states) vs swap target `champagneglass.rsi` (CC-BY-SA-3.0, 6 states:
`icon`, `icon_empty`, `fill-1..4` — no overlay, no inhand art):

| Old state (glass_coupe_shape) | Mapping in champagneglass | How |
|---|---|---|
| `icon` | `icon` | direct |
| `icon-front` (overlay layer) | **none** | Overrode the inherited `DrinkVisualsFillOverlay` sprite layers down to base+fill only (the same two-layer shape as plain `DrinkVisualsFill`). No Overlay-mapped layer remains, so `SolutionContainerVisualsSystem` never touches a missing state. |
| `fill-1..fill-5` | `fill-1..fill-4` | `maxFillLevels: 4` set on the prototype (was inherited 5). `RoundToLevels` rescales the fraction, so all fill fractions still render — one fewer visual step. |
| `inhand-left`, `inhand-right` | `icon` (reused) | champagneglass has no inhand art. Rather than let the held sprite silently vanish (the reason the 07-12 pass skipped this swap), the prototype now sets explicit `Item.inhandVisuals` layers using the `icon` state for both hands. Note: champagneglass's `icon` is a 4-frame (1s/frame) animated state, so the held glass animates in-hand. Slightly oversized for a held item but recognizable, and strictly better than nothing. |
| `inhand-left-fill-1`, `inhand-right-fill-1` | **none** | `inHandsMaxFillLevels: 0` (was 1). `OnGetHeldVisuals` returns early when the computed level is 0 and independently validates states via `TryGetState` before adding layers — fail-safe either way. |

All mapping claims checked against the actual client code paths:
`Content.Client/Items/Systems/ItemSystem.cs` (explicit `inhandVisuals` bypasses
`TryGetDefaultVisuals`; `HandsSystem` still assigns `Sprite.BaseRSI` and
`SpriteComponent` resolves/validates the `icon` state) and
`Content.Client/Chemistry/Visualizers/SolutionContainerVisualsSystem.cs`
(`OnGetHeldVisuals`/`GetVisualsLayer` validate states with `TryGetState` before use).

After repointing, `glass_coupe_shape.rsi` (all 11 PNGs + meta.json) was deleted.

### Re-verification of the already-landed items (did not trust the audit or the prior commits blindly)
- **`Objects/Fun/Plushies/meta.json` (DELETE):** absent from tree; only `*.rsi` subfolders
  remain under `Fun/Plushies/`. Grep for `Fun/Plushies/meta.json`: zero hits. The
  `Fun/Plushies/` hits that do exist (9 YAML files + `AdminVerbSystem.Smites.cs`) all point
  at per-plushie `*.rsi` subfolders (e.g. the smite hardcodes
  `/Textures/Objects/Fun/Plushies/lizard.rsi` — untouched, separately licensed).
- **`Objects/Misc/flies.rsi` (DELETE):** absent from tree. Per the killsign lesson, C# was
  swept too: `grep -rn '"flies|flies\.rsi'` across Content.Server/Client/Shared/
  IntegrationTests and Resources → zero hits.
- **Swap targets have the states their prototypes use** (from each target's meta.json):
  - `Jumpskirt/rainbow.rsi`: `icon`, `equipped-INNERCLOTHING`, `inhand-left/right` — covers
    `ClothingUniformColorRainbow` + the `UplinkChameleon` icon. The old
    `equipped-INNERCLOTHING-monkey` state is lost; degrades gracefully (species-state lookup
    falls back, no error), as recorded in `b062deaba1`.
  - `Glasses/sunglasses.rsi`: `icon`, `equipped-EYES` (+ species variants, inhands) — full
    coverage for `ClothingEyesGlassesNoir`.
  - `Shoes/Misc/leather.rsi`: `icon`, `equipped-FEET`, `inhand-left/right` — exact state
    match for `ClothingShoesSnakeskinBoots`.

---

## Verification gates (all run in this worktree, post-change)

| Gate | Result |
|---|---|
| `dotnet run --project Content.YAMLLinter -c DebugOpt` | **"No errors found in 52575 ms."** |
| `dotnet test … --filter FullyQualifiedName~Content.IntegrationTests.Tests._Solreign -m:1 -nodeReuse:false -p:UseSharedCompilation=false` | **Passed! Failed: 0, Passed: 148, Skipped: 1** (known skip `PersistentBlockSurvivesRoundRestartAndPreventsOffer`), 2 m 57 s |
| `dotnet test … --filter FullyQualifiedName~GameMapsLoadableTest` (maps may reference swapped clothing) | **Passed! Failed: 0, Passed: 246, Skipped: 0**, 2 m 4 s |
| Grep proof, zero remaining references to each deleted/swapped path (`glass_coupe_shape`, `Uniforms/Jumpsuit/rainbow`, `Glasses/noir`, `Shoes/Misc/snakeskin`, `Misc/flies`, `Fun/Plushies/meta.json`) across Prototypes, Maps, and all Content.* C# | **zero hits for all six** |
| NC census (utf-8-sig decode of every Textures meta.json) | **107 → 106** in this worktree; **115 → 106 cumulative** vs the audit baseline (109 expected from DELETE+SWAP alone; the extra −3 = the out-of-manifest `48f45c59b7` swaps) |

## Commits in this lane
1. Swap: `DrinkGlassCoupeShaped` repoint + `glass_coupe_shape.rsi` deletion.
2. This receipt doc.

## Not done / out of scope (deliberate)
- 106 CC-BY-NC assets remain — all REGEN bucket (plus `Jumpskirt/performer.rsi`, skipped by
  `48f45c59b7` for lack of a viable substitute). They need original replacement art; not this
  lane's job.
- The NC texture files of REGEN assets are untouched on disk, as mandated.
- Local commits only; nothing pushed.
