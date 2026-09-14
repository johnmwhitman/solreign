# HUD-THEMES-GEN — 2026-07-16

Procedural regeneration of all six undeclared-license, goonstation-family
player-selectable HUD themes (census LICENSE-CENSUS-W7 Batch E, the "real
unlicensed risk" set). Branch `feat/hud-themes-gen` off GAME master
`e29185f6a8`. Additive-only: no player-facing feature removed — every theme
keeps its exact file set, filenames, dimensions, and slot semantics.

## Method note (license rail compliance)

Generator: `docs/receipts/hud-themes/draw_hud_themes.py` — a parameterized
extension of the merged wave-6 generator
(`docs/receipts/wave6/draw_interface_default.py`). One generator, six theme
parameter packs.

- The Solreign-original CC0 glyph set from wave 6 is **imported** and
  re-rendered through per-theme palettes; one new original glyph
  (`ears_headset`, an audio headset) is authored in this file. All chrome
  (panels, borders, stripes, brackets, storage pieces, sidebars, tiles,
  arrows) is fresh per-theme drawing code.
- The NC-suspect originals were **viewed only** to describe mood vocabulary
  (Ashen = desaturated grey; Plasmafire = orange fire on dark indigo; Retro =
  chunky green-on-blue flat; Clockwork = brass/bronze diagonal machining;
  Slimecore = green on dark green-grey; Minimalist = thin ghost-grey on
  near-black with a folded corner) and to read **unprotectable technical
  facts**: canvas sizes, per-file alpha conventions (full-bleed vs 28x28/30x30
  inset panels, Retro's bare-glyph files, Retro's fully transparent
  template_small + sidebars), and opaque-pixel counts for the ratio gate.
- **No NC pixels were read into the art path, sampled, traced, img2img'd, or
  conditioned on. No AI image generation anywhere — pure PIL drawing code,
  license-clean by construction.** Fully deterministic (zero randomness).
- Wave-6 dual gates kept: size identity assert per file + opaque-ratio gate
  (fail < 0.55) + before/after contact-sheet eyeball loop.

## Per-theme results

| Theme | Files regenerated | Ratio-gate fails | Commit |
|---|---|---|---|
| Ashen | 44 | 0 | `3df3c923c5` |
| Plasmafire | 41 | 0 | `9d1f52d46b` |
| Retro | 41 | 0 | `9a0514adbd` |
| Clockwork | 39 | 0 | `01a79e8afd` |
| Slimecore | 38 | 0 | `5dc7e08fd3` |
| Minimalist | 29 | 0 | `481d30f326` |
| **Total** | **232** | **0** | |

License declaration follows the wave-6 Interface/Default precedent: one
`meta.json` per theme directory, `"license": "CC0-1.0"`, copyright noting the
generator and that no prior art pixels were used or referenced. The census
coverage sweep keys on ancestor `meta.json`, so each declaration covers its
whole theme subtree (Slots/, Storage/).

Alpha conventions honored so in-game layout rhythm is unchanged:
- Plasmafire/Clockwork/Slimecore: 28x28 inset panels on back/belt/id/pocket/
  web/template_small; full-bleed elsewhere.
- Retro: bare glyphs (no panel, one chunky dilation pass) on
  back/belt/id/pocket/web/suit_storage; fully transparent template_small and
  sidebar_top/mid/bottom, mirroring the originals' empty canvases.
- Ashen: 30x30 rounded twin-border panels; Minimalist: 30x30 folded-corner
  (dog-ear) panels; Minimalist toggle keeps its green expand-button identity.
- Theme texture lookup (`RobustToolbox UiTheme.TryResolveTexture`) falls back
  to Interface/Default for files a theme doesn't ship, so per-theme file sets
  were preserved exactly (e.g. hand_m/ears_headset only in Ashen+Minimalist,
  Storage/back+exit+sidebar_fat only in Ashen, no Storage dir in Minimalist).

## Contact sheets (orchestrator eyeball gate)

- `docs/receipts/hud-themes/CONTACT-Ashen.png`
- `docs/receipts/hud-themes/CONTACT-Plasmafire.png`
- `docs/receipts/hud-themes/CONTACT-Retro.png`
- `docs/receipts/hud-themes/CONTACT-Clockwork.png`
- `docs/receipts/hud-themes/CONTACT-Slimecore.png`
- `docs/receipts/hud-themes/CONTACT-Minimalist.png`
- `docs/receipts/hud-themes/CONTACT-in-context.png` (mock HUD arrangement:
  inventory row, hands trio with active-hand highlight, item-status chips,
  storage-grid mock per theme)

Each per-theme sheet is before/after pairs (original left, regenerated right)
with the opaque-ratio per file.

## License census delta

Sweep = the census method (attributions.yml parse + ancestor-meta.json
coverage walk over every image under `Resources/Textures`):

| Metric | Before | After |
|---|---|---|
| Undeclared-license image files (repo-wide) | 739 | **507** |
| … of which suspect HUD sibling themes | 232 | **0** |
| `grep -rl '"license": *"CC-BY-NC' Resources/Textures --include=meta.json` | 0 | 0 |

The remaining 507 are the census's README-default bucket (KEEP, low risk).

## Test evidence (all on this branch, Release)

- `dotnet build SpaceStation14.slnx -c Release` — **0 errors** (50 projects;
  1244 pre-existing warnings).
- `dotnet test Content.Tests -c Release --no-build` — **Passed 2216,
  Failed 0, Skipped 3** (total 2219).
- `dotnet test Content.IntegrationTests -c Release --no-build --filter
  "FullyQualifiedName~Solreign"` — **Passed 302, Failed 0, Skipped 1**
  (total 303; run twice, identical result).
- `dotnet run --project Content.YAMLLinter -c Release` — **"No errors
  found"** (33.2 s).
- No dedicated client theme-loading test exists beyond the prototype checks
  exercised by the integration battery (`hud.yml` hudTheme prototypes load;
  texture resolution is runtime-fallback by design, so a theme missing a file
  falls back to the wave-6 CC0 Default set — never to NC art).

## No merges, no pushes

Per lane orders: branch `feat/hud-themes-gen` only. Merge is the
orchestrator's call after eyeballing all six contact sheets.
