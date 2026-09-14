# Mood Rollout — SolreignStationMood on every rotation map — 2026-07-16

Date: 2026-07-16

Repository: GAME (worktree `~/AI/solreign-trees/mood-rollout`)

Branch: `feat/mood-rollout`, based on GAME `master` @ `055b89858a`

Mission: the engine-feasibility lane ranked "roll the already-built ambient-mood system out to every
rotation map" as the single best wow-per-effort visual play. Before this pass, only 5 of the 7
`SolreignMapPool` maps carried `SolreignStationMood` at all — and one of those 5 (Oasis) was
shipping the component's raw C# defaults, i.e. an undesigned identity. After this pass all 7
rotation maps carry a deliberately designed mood profile, each visually/tonally distinct at a
glance.

Authority: local commits only; no push; no merge. Claude remains the only merge operator per repo
convention (V13 MERGE LAW).

## Ground truth (system audit)

`SolreignStationMoodSystem` (Content.Server/_Solreign/StationIdentity/) — server-authoritative,
YAML-declared per-station ambient identity. Three mechanics:

1. **Day/night ambient lerp** — triangle-wave interpolation of
   `MapLightComponent.AmbientLightColor` between `colorA` and `colorB` (sRGB hex in YAML,
   converted via `Color.FromSrgb`) over `cyclePeriodSeconds` (full A→B→A loop), throttled to one
   write per 2s. `dayNightCycle: false` = flat light, TickDayNight no-ops entirely.
2. **Weather-event preference** — `weatherEventPrototypes` (typed `EntProtoId` list; only two
   valid ids exist: `SolreignSolarFlare`, `SolreignSporeDrift`). Read by both weather GameRules
   via `IsWeatherEventPreferred` to lean harder into their effect on stations that call them out.
3. **Mood popups** — `moodPopupLines` (LocId list), a random line popped at a random station tile
   every `moodPopupMin/MaxIntervalSeconds` (defaults 900–2400s).

Opt-in point: the component is attached in each map's `gameMap` prototype under
`stations: <id>: components:` (Resources/Prototypes/Maps/_Solreign/solreign_*.yml) — same slot as
`StationNameSetup`/`StationJobs`. No CVars; all knobs are the component's DataFields.

**PDA-dim guardrail** (live bug, 2026-07-14, fixed in Nocturne's map file): lighting pushed too
dark made handheld lights read as broken; the fix chose LightCycle bounds `minLightLevel: 0.12` /
`maxLightLevel: 1.1`. Applied to THIS system as a design floor: no `colorA`/`colorB` endpoint
darker than `#1E1E24` (the darkest value already shipped, Leviathan's marble-night — kept as the
floor, never exceeded). Every color below clears it.

## Per-map profile table (as landed)

| Map | dayNightCycle | ColorA (dark) | ColorB (bright) | Cycle (s) | Weather pref | Popups | Change type |
|---|---|---|---|---|---|---|---|
| **SolreignOasis** | true | `#243038` blue-teal night pool | `#D4F0E8` turquoise pool | 2100 | SolarFlare + SporeDrift | generic ×3 (existing) | **Redesigned** — was raw engine defaults (`#2B2B44`/`#D8D8F2`, 1800s); "waystation oasis at dusk" |
| **SolreignNocturne** | **false** | — (map-file LightCycle owns ambient: 3600s, 0.12/1.1 bounds) | — | — | SporeDrift | **4 new noir lines** | **New component** — weather + popups only; deliberately no colors so two systems never fight over `AmbientLightColor` |
| **SolreignPerihelion** | true | `#3A2A14` dark amber | `#FFE9C7` sun cream | 2400 | SolarFlare | perihelion ×3 (existing) | **Reviewed, kept as-is** — already the pool's only warm amber pair; grk sanity-check confirmed |
| **SolreignVerdant** | true | `#2A3420` forest gold-green | `#E8F0B8` dappled chartreuse | 3600 | SporeDrift | **4 new grove lines** | **New profile** — had no mood component at all; "sun through leaves," slow-overgrowth pacing |
| **SolreignMeridian** | true | `#C8E8DC` cool acid-mint | `#F0FAF6` sterile white | 2400 | SporeDrift | generic ×3 (existing) | **Retuned** — was `#F2FBF4`/`#B9F2C6`, colliding with Leviathan's pale green; keeps the "acid-green science city" brief in a cyan-leaning register |
| **SolreignLeviathan** | true | `#1E1E24` marble night (THE floor) | `#F0E8D4` champagne ivory | 5400 | SolarFlare + SporeDrift | leviathan ×4 (existing) | **Retuned** — ColorB was `#E8F5D0` pale green, colliding with Meridian; now "cold executive luxury," longest cycle kept |
| **SolreignTerminus** | **false** (kept) | — eternal shift, by ground rule | — | — | SolarFlare + SporeDrift | terminus ×3 (existing) | **Reviewed, kept as-is** — flat industrial light is the identity; grk concurred, no override |

Glance test after retune: Perihelion = warm amber · Oasis = blue-teal aquatic · Verdant = warm
gold-green organic · Meridian = cool mint/clinical, never dark · Leviathan = near-black ↔
champagne, slowest swing · Nocturne = neon-noir dim (own mechanic) · Terminus = flat eternal
shift. No two stations share a color family on both endpoints.

## Design rationale

- **Design pass ran through grk** (fleet second-opinion, one self-contained prompt with the full
  parameter surface + per-map character sketches + the PDA-dim floor). Its numbers were
  sanity-checked against the component's actual fields (all valid; hex parse via `Color.FromHex`;
  no clamping needed — every proposed endpoint cleared the `#1E1E24` luminance floor).
- **Two collisions fixed**: Meridian↔Leviathan shared pale-green bright endpoints (both moved);
  grk's *review* pass then caught that the first fix had created a second, quieter collision
  (Oasis `#2E3834` vs Verdant `#2A3420` — same desaturated green-black family at night) AND had
  over-corrected Meridian to pure blue-white, killing the "acid-green" half of that station's own
  written brief. Both re-fixed: Oasis dark went blue-dominant (`#243038`), Meridian landed on a
  controlled acid-mint (`#C8E8DC`/`#F0FAF6`).
- **Nocturne composition**: its map file already wires upstream's client-side `LightCycle`
  (predicted per-frame writes to `MapLightComponent.AmbientLightColor`). Enabling this
  component's server-side lerp too would be two writers on one networked field. `dayNightCycle:
  false` + weather + popups is the correct composition (same pattern as Terminus). grk verified
  this against both systems' code and confirmed. Weather pick moved SolarFlare→SporeDrift on its
  flag (sun-exposure event on a windowless night-bar was thematically sideways).
- **New flavor text** (Nocturne ×4, Verdant ×4) written in the established house voice (dry
  corporate horror, PG-13 per John's ruling #5); one Verdant line softened on review
  ("something large exhales" → "the air moves like something large just left") to stay
  facilities-horror rather than creature-feature.
- **Zero C# changes**: the system expressed every needed profile. YAML + FTL only.

## Files touched (7)

- `Resources/Prototypes/Maps/_Solreign/solreign_oasis.yml` — redesigned profile
- `Resources/Prototypes/Maps/_Solreign/solreign_nocturne.yml` — new component (weather+popups)
- `Resources/Prototypes/Maps/_Solreign/solreign_verdant.yml` — new full profile
- `Resources/Prototypes/Maps/_Solreign/solreign_meridian.yml` — colorA/colorB retune
- `Resources/Prototypes/Maps/_Solreign/solreign_leviathan.yml` — colorB retune
- `Resources/Locale/en-US/_solreign/nocturne.ftl` — 4 new mood-popup lines
- `Resources/Locale/en-US/_solreign/verdant-lore.ftl` — 4 new mood-popup lines

Perihelion and Terminus: reviewed, deliberately unchanged (their existing profiles ARE the design).

## Verification

All commands run from the worktree root; same invocations as the SR-W-012 Phase A handoff
(docs/handoff/ORCH-MAP-HEALTH-PHASE-A-2026-07-15.md).

1. **YAML linter** — `dotnet run --project Content.YAMLLinter -c DebugOpt` — exit 0,
   **"No errors found"** — run twice (37,804 ms after the first edit wave; 40,735 ms after the
   post-review fixes). Only pre-existing CS0618/RA0033/NU1510 warnings, none from this change.
2. **Map-health scorecard (SR-W-012)** —
   `dotnet test Content.IntegrationTests ... --filter "FullyQualifiedName~SolreignMapHealthScorecardIntegrationTest"`
   — **Passed: 11, Failed: 0, Skipped: 0** — run twice (42 s first wave; 1 m 2 s after fixes).
   All 7 rotation maps emit clean scorecard rows. (Note: the Phase A handoff's 2 red
   SpawnAtmosphere findings on Oasis/Terminus are green on current master — resolved upstream
   before this branch; not a mood interaction.)
3. **Full Solreign integration battery** —
   `dotnet test Content.IntegrationTests ... --filter "FullyQualifiedName~Content.IntegrationTests.Tests._Solreign"`
   — **Passed: 169, Failed: 0, Skipped: 1, Total: 170** — 4 m 41 s, run FOREGROUND. The 1 skip is
   the known pre-existing `PersistentBlockSurvivesRoundRestartAndPreventsOffer` (Windows-only).
4. **grk review pass on the final diff** — verdict "ship with fixes"; both blockers
   (Oasis/Verdant dark collision, Meridian over-correction) and both recommendations (Verdant
   popup line, Nocturne weather) applied, then linter + scorecard re-run green. Best catch: the
   Oasis↔Verdant night-phase collision — the first-pass fix for the Meridian↔Leviathan collision
   had quietly recreated the same class of bug one shelf over.

## First live shift observation list

Per-map "is the mood readable, is the game readable" checks for whoever watches the first live
rotation through these maps:

- **All maps**: handheld lights (PDA, flashlight) stay clearly useful at each map's darkest cycle
  point — the exact regression class the 0.6-cap bug shipped. No corridor should read as
  unlit-black at any cycle phase.
- **Oasis**: night phase reads *blue*-teal (pool at night), not green — if it reads green at a
  glance, it's colliding with Verdant again.
- **Verdant**: gold-green "dappled" bright phase visibly warmer than Meridian's mint; glow-flora
  rooms still read as plant-lit at the cycle's dark end.
- **Meridian**: never appears to go dark at all (both endpoints bright, smallest swing in the
  pool — deliberate); confirm the swing is perceptible enough to not read as static, and that the
  mint tint is visible against white lab walls.
- **Leviathan**: the 90-min swing from marble-black to champagne is the pool's slowest — confirm
  a full round actually shows visible drift, and the `#1E1E24` night phase doesn't make the Vault
  wing feel unplayably dim.
- **Perihelion**: unchanged, but it's the warmest-dark of the set — spot-check PDA readability at
  its `#3A2A14` trough specifically (grk flagged as watch-only).
- **Nocturne**: LightCycle behavior byte-identical to pre-rollout (this pass added no lighting);
  mood popups fire in the bar register; spore-drift preference feels right for the room.
- **Terminus**: flat light byte-identical to pre-rollout; popups still fire.
- **All maps with popups**: lines appear rarely (15–40 min cadence), localized, PG-13, and never
  during/over a real emergency announcement in a confusing way.

## Receipts

- grk design prompt + full response: scratchpad (session-local); design tables reproduced above.
- grk review response: verdict + all 4 applied fixes summarized above.
- Test/linter tails: recorded verbatim in "Verification" above.
