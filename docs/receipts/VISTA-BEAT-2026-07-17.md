# VISTA-BEAT receipt — 2026-07-17 (`feat/vista-beat`)

ACTS #5 cheap-add, council memo `docs/council/2026-07-16-design-magnetism.md` item 5: *"route the
first-shift path past one composed vista with a PROVIDENCE line."*

## What shipped

- **`SolreignVistaMarkerComponent`** (`Content.Shared/_Solreign/PlayerDelight/Vista/`) — invisible,
  anchored `MarkerBase` child carrying a per-map `lineId` + `range` (3 tiles). Prototype
  `SolreignVistaMarker` (`Resources/Prototypes/_Solreign/PlayerDelight/vista_marker.yml`);
  invisible in normal play because `MarkerBase`'s `Marker` component sprite is only shown when the
  client's `MarkerSystem` marker-visibility toggle is on (mapping/admin).
- **`SolreignVistaBeatSystem`** (`Content.Server/_Solreign/PlayerDelight/Vista/`) — 1 Hz throttled
  proximity scan. Fires only for players with an **active First Shift assignment** (new sanctioned
  accessor `FirstShiftSystem.HasActiveAssignment(NetUserId)`, reading the same round-local
  `FirstShiftRoundState` the beacon UI drives). Guard chain mirrors
  `ProvidenceWelcomeSystem.FireFirstShiftPersonal`: in-game session, live attached entity, same map,
  not ghost, not dead. Delivery = private popup + same line as a private chat message
  (`IChatManager.DispatchServerMessage` — the `FirstShiftSystem.OnComplete` idiom, chat persists
  after the popup fades). Mark-first so a mid-delivery throw can never double-fire.
- **Once per player per round**: pure `VistaBeatGate` (mirrors `ProvidenceWelcomeGate`), reset on
  round start/restart, unit-tested (`Content.Tests/_Solreign/VistaBeatGateTests.cs`, 5 tests).
- **CVar**: own tiny switch `solreign.first_shift_vista_beat` (default on, SERVERONLY;
  `Content.Shared/CCVar/CCVars.SolreignVista.cs`). NOT folded into `solreign.social_cheap_adds`,
  deliberately: that flag's documented contract is "every behavior behind it is inert without other
  players present" (its honesty-at-population-1 rationale). The vista beat is a strictly **solo**
  beat, so joining that flag would silently break its contract. Justification lives in the CVar doc.
- **Copy**: `Resources/Locale/en-US/_Solreign/vista-beat.ftl` — 7 map-specific PROVIDENCE lines,
  corporate-sinister-warm, PG-13. Every referenced visual grep-verified present at the spot (table
  below). Lines are asserted map-unique by the placement test.

## Per-map vista table (uid range 900905–900940, orchestrator-assigned; per-map grep-verified unused)

| Map | uid | Marker pos (grid) | Chosen spot & verified visuals | Line id |
|---|---|---|---|---|
| Leviathan | 900905 | `1.5,-21.5` (13329) | Cargo/QM corridor row the First Shift beacon (2.5,-21.5) is mounted on: QM window run (Grille+RWindow −0.5..2.5,−22.5); behind the glass a **bioluminescent potted plant** (−1.47,−23.83) + **gold lamp** (0.51,−24.10); **liability board** (−1.5,−22.5); **AsteroidRock breaching the corridor** (2.5,−17.5/−18.5). Tile: CableApcExtension+GasPipeStraight only. | `solreign-vista-leviathan` |
| Meridian | 900906 | `-45.5,2.5` (30) | Corridor 4 tiles south of the arrivals First Shift beacon (−46.5,6.5): **3-window run onto space** west (−46.5, y=1.5..3.5); **executive lounge** behind interior glass east (green carpet, ComfyChairs, **LampGold** −42.55,3.66, **Cohiba Robusto Ad** −43.5,2.5). Tile: DisposalPipe+GasPipeStraight only. | `solreign-vista-meridian` |
| Nocturne | 900907 | `7.5,11.5` (2) | Dim **blue-carpet promenade** (CarpetSBlue x=7.5, y=7.5..16.5) between arrivals (7.5,16.5) and the cargo reception First Shift beacon (0.5,7.5); waiting nook: 2 Chairs (5.5,10.5/11.5) under **"Just a Week Away..." poster** (4.5,11.5, "long delayed project" per its own description); SolreignNocturneDimBaseline lighting. Tile: CableMV+CarpetSBlue only. | `solreign-vista-nocturne` |
| Oasis | 900908 | `23.5,9.5` (31) | Loading dock 1 tile east of the First Shift beacon (22.5,9.5), facing the QM's glass office front (glass door 25.5,9.5 + window 25.5,10.5): **Wall-mounted Carp** (27.5,11.5), **Saltern Map** (27.5,7.5), orange carpet, desk Lamp (28.31,10.80). Tile: CableApcExtension+GasPipeBend only. | `solreign-vista-oasis` |
| Perihelion | 900909 | `7.5,22.5` (2) | Cargo bay tile beside the First Shift beacon (6.5,22.5), looking through the QM office interior glass (6.5/7.5,20.5) past the cargo-orders console: **lit Fireplace** (5.5,19.5), black carpet, **CigarGold + DrinkMugBlack** on the desk (6.5,19.5). Tile: GasPipeStraight only. | `solreign-vista-perihelion` |
| Terminus | 900910 | `-44.5,-1.5` (2) | Contracts hall 1 tile north of the First Shift beacon (−44.5,−2.5): **SolreignContractsBoard** (−46.5,−0.5) + how-to sign, **personal support cube** (−43.5,−0.5) under the "reward pending" sign, **employee reward cake display** (−44.5,−0.5). Tile: GasVentScrubber only. | `solreign-vista-terminus` |
| Verdant | 900911 | `11.5,23.5` (2) | Cargo bay corner east of the First Shift beacon (6.5,22.5): **corporate kudzu** (10.5,23.5/24.5) climbing the crate row (9.5..12.5,24.5), lit by the acid-green **photosynthesis compliance unit** (SolreignGlowPlantAcid 11.5,24.5). Tile: clear floor. | `solreign-vista-verdant` |

No dressing entities were added: all seven spots were already composed (less is more; zero geometry
changes, zero new furniture). Uids 900912–900940 of the assigned range remain unused.

Spot-selection honesty note: five markers sit 1–4 tiles from the map's First Shift beacon (where an
assignment becomes active, so the pass-by is guaranteed); Nocturne's sits on the promenade linking
that beacon to arrivals; Meridian's on the beacon's exit corridor. One candidate was **rejected** by
the same reading that found it: Perihelion's ironsand statue alcove (22.5,6.5) turned out to be a
maintenance catwalk behind `AirlockMaintLocked` — a vista no first-shift player would pass.

## Tests

- `Content.Tests/_Solreign/VistaBeatGateTests.cs` — once-per-round-per-player gate (5 tests).
- `Content.IntegrationTests/Tests/_Solreign/SolreignVistaMarkerMapPlacementIntegrationTest.cs` —
  mirrors `SolreignWingmateBeaconMapPlacementIntegrationTest` per map (×7): exactly one marker on a
  station-owned grid, anchored, non-space tile, atmos pressure/temperature-safe, ≥16 kPa O₂ — plus
  vista-specific asserts: map-set `lineId` non-empty, resolves via `ILocalizationManager`
  (proves the prototype + component load), positive range, and **no two maps share a line**.
- `GameMapsLoadableTest` (246) covers the placements loading as part of every map load.

## Verification (Release, this branch)

- Build: clean, 0 errors.
- `Content.Tests` full: **2334 passed / 3 skipped** (baseline 2329/3 + 5 new).
- `Content.IntegrationTests` `~Solreign`: **320 passed / 1 skipped** (baseline 313/1 + 7 new).
- `GameMapsLoadableTest`: **246 passed**.
- YAML linter: clean.
