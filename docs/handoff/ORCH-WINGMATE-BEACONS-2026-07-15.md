# Wingmates rollout — SolreignWingmateBeacon map placement — 2026-07-15

Date: 2026-07-15

Repository: GAME (worktree `orch-wingmate-beacons`)

Branch: `feat/orch-wingmate-beacons`, off GAME `master`

Task: execute the sanctioned Wingmates rollout step from the v12 handoff — "map the beacon into all 7
maps" — by placing exactly one `SolreignWingmateBeacon` entity on each of the seven reviewed Solreign
rotation maps (`SolreignMapTestCatalog.MapIds`).

Authority: local commits only; no push; no merge. Claude remains the only merge operator per repo
convention.

## Why this doesn't turn Wingmates on

`Resources/Prototypes/_Solreign/PlayerDelight/wingmate_beacon.yml` carries its own comment: "Admin-
spawned canary only. Deliberately absent from maps and inventories while
`solreign.wingmates_enabled` remains default-off." Placing the entity into the map files is the next
sanctioned step, not a flip of that switch: `Content.Server/_Solreign/PlayerDelight/Wingmates/
WingmateSystem.cs` gates the entire feature server-side on `CCVars.SolreignWingmatesEnabled`
(`solreign.wingmates_enabled`, default `false`, `SERVERONLY`) — when disabled, `OnEnabledChanged`
clears all live state and every UI/request/offer handler is inert. The beacon sitting on a map with
the CVar off is exactly as inert as the previous admin-spawn-only canary; this branch does not touch
the CVar or the prototype.

## Placement method

For each map: located a public, high-traffic, non-secure landmark (cargo reception desk/ordering
console, arrivals beacon marker, or the station's dedicated cargo-reception hall), decoded the grid's
actual tile data (`MapChunkSerializer` v7 format: 7 bytes/tile — `int32 id, byte flags, byte variant,
byte rotationMirroring`, base64-packed per 16×16 chunk) to confirm a real walkable floor tile (not
Space/Lattice/Plating), cross-referenced every other anchored entity's `Transform.pos` on that grid to
confirm the chosen tile carries no furniture/structure conflict (infrastructure-only entities — cable,
pipe — are ubiquitous under floor tiles in this codebase and are not treated as occupying it), and
appended the new entity block at the end of the map's `entities:` list, matching this fork's own
established convention (every map file's own additive "wave" comments — e.g. Leviathan's and
Terminus's "Wave 21 (S8)" — are appended out of alphabetical order at the tail, not merged back into
the alphabetically-sorted base catalog). Each insertion follows the same wave-comment style: rationale,
fresh uid with prior-max provenance, and a tile-verification note.

One judgment call worth flagging: Meridian's `ComputerCargoOrders` terminal turned out to sit inside
the bridge/command cluster on that chassis (`ComputerPowerMonitoring`, `ComputerSolarControl`,
`HolopadCommandBridgeLongRange` neighbors) rather than a public cargo desk — not the landmark the
design intent wants. That map used `DefaultStationBeaconArrivals` instead (a genuine public
lounge/waiting area). Terminus's `ComputerCargoOrders` similarly sat near records/security consoles
(`ComputerCriminalRecords`, `ComputerId`, `ComputerSurveillanceCameraMonitor`) — that map used the
dedicated `DefaultStationBeaconCargoReception` hall instead, which also happens to be where that map's
own Contracts Nook companion-cube/cake-display cluster already lives.

## Per-map placements

| Map | uid | pos | parent grid | Landmark / rationale |
| --- | --- | --- | --- | --- |
| `SolreignLeviathan` | 900612 | `2.5,-21.5` | 13329 | Cargo/QM corridor directly outside the QM office window (window at `2.5,-22.5`); public corridor traffic, not the secured office interior. Tile: FloorSteel, infra-only occupants (cable, pipe). |
| `SolreignMeridian` | 900563 | `-46.5,6.5` | 30 | Arrivals lounge, one tile from `DefaultStationBeaconArrivals` (`-46.5,5.5`) — every crew member passes through on roundstart. Swapped off `ComputerCargoOrders` (that terminal sits inside the bridge cluster on this chassis, not a public desk). Tile: FloorSteel, no occupants. |
| `SolreignNocturne` | 3533 | `0.5,7.5` | 2 | Cargo bay floor, one tile west of `ComputerCargoOrders` (`1.5,7.5`), beside the bay's glass airlock entrance. Tile: FloorSteel, infra-only occupants (cable, pipe). |
| `SolreignOasis` | 90772 | `22.5,9.5` | 31 | Open cargo loading dock, just outside the QM office door (`AirlockQuartermasterGlassLocked` at `25.5,9.5`) — public receiving-bay floor, not the QM office interior. Tile: FloorSteel, no occupants. |
| `SolreignPerihelion` | 900553 | `6.5,22.5` | 2 | Cargo bay floor beside the receiving conveyor belts, one tile from `ComputerCargoOrders`. Tile: FloorSteel, no occupants. |
| `SolreignTerminus` | 90543 | `-44.5,-2.5` | 2 | Cargo/contracts reception hall — the same open hall as `DefaultStationBeaconCargoReception` (`-46.5,-0.5`) and the map's `SolreignContractsBoard`/Contracts Nook cluster. Swapped off `ComputerCargoOrders` (that terminal sits near a records/security console cluster on this chassis). Tile: FloorSteel, no occupants. |
| `SolreignVerdant` | 900613 | `6.5,22.5` | 2 | Cargo bay floor beside the receiving conveyor belts (same chassis layout as Perihelion), one tile from `ComputerCargoOrders`. Tile: FloorSteel, no occupants. |

Every insertion: one `- proto: SolreignWingmateBeacon` block (`Transform` pos/parent + empty
`Fixtures`), no `rot:` override (defaults to south, matching several existing wallmount examples in
these files that also omit it), placed on open floor rather than flush against a wall tile — several
other wallmount decorations in this fork's maps (signs, air alarms) conventionally share a `Plating`
wall tile with the `Wall*` entity they're mounted on, but the task's own placement instructions
explicitly required "walkable floor (not space/wall/lattice)", so this branch placed the beacon on
adjacent open floor instead. `WallMountComponent` has no runtime wall-adjacency requirement (it only
widens interaction-exemption arcs), so this is cosmetic, not functional.

## New verification coverage

`Content.IntegrationTests/Tests/_Solreign/SolreignWingmateBeaconMapPlacementIntegrationTest.cs` — one
NUnit case per catalog map (`SolreignMapTestCatalog.MapIds`, the same seven-map source
`SolreignMapPoolIntegrationTest` and the SR-W-012 map-health scorecard already share). Per map:

1. Loads the map, resolves every station-owned grid (same "largest station-member grid" /
   `StationDataComponent.Grids` idiom the scorecard uses, generalized to every station grid since a
   map can have more than one, e.g. Terminus).
2. Asserts exactly one `WingmateBeaconComponent` entity sits on a station-owned grid.
3. Asserts the beacon is anchored and stays on a station-owned grid.
4. Adapts the map-health scorecard's check-6 spawn-tile-safety idiom to this single tile: non-space
   (`TurfSystem.IsSpace`), `AtmosphereSystem.IsTileMixtureProbablySafe` (pressure/temperature), and the
   same 16 kPa oxygen partial-pressure floor the scorecard uses (composition isn't covered by
   `IsMixtureProbablySafe` — see that method's own remarks). Polls (bounded, 8 s) for the tile's gas
   mixture to resolve before evaluating, same readiness idiom as the scorecard's check 6.

## Verification gates run

### 1. Baseline (before any change) — map-health scorecard

```
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
  --filter "FullyQualifiedName~SolreignMapHealthScorecardIntegrationTest" \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```

Result: `Passed: 11, Failed: 0, Skipped: 0, Total: 11` — clean baseline confirmed before any map edits.

### 2. New fixture — beacon placement test

```
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
  --filter "FullyQualifiedName~SolreignWingmateBeaconMapPlacementIntegrationTest" \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```

`Passed: 7, Failed: 0, Skipped: 0, Total: 7` — Duration **29 s**. 7/7 catalog maps.

First run of this fixture was `Failed: 1, Passed: 6` — a **real defect this test caught**, not a test
bug. See "Key finding" below.

### 3. Map-health scorecard (post-change regression)

```
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
  --filter "FullyQualifiedName~SolreignMapHealthScorecardIntegrationTest" \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```

`Passed: 11, Failed: 0, Skipped: 0, Total: 11` — Duration **34 s**. Identical to the pre-change
baseline (11/11): all seven production maps still pass every Phase A hard check (station grid,
latejoin spawn, job spawns, emergency shuttle, APC load, spawn atmosphere) — the beacon insertions
broke no map integrity.

### 4. Generic map-load regression

```
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
  --filter "FullyQualifiedName~PostMapInitTest.GameMapsLoadableTest" \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```

`Passed: 26, Failed: 0, Skipped: 0, Total: 26` — Duration **1 m 21 s**. Every map in the repo (not
just the Solreign seven) still loads.

### 5. YAML lint

```
dotnet run --project Content.YAMLLinter/Content.YAMLLinter.csproj
```

`No errors found in 25760 ms.`

### 6. All-Solreign integration filter

```
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
  --filter "FullyQualifiedName~Content.IntegrationTests.Tests._Solreign" \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```

`Passed: 169, Failed: 0, Skipped: 1, Total: 170` — Duration **2 m 52 s**.

The 1 skip is the known pre-existing `PersistentBlockSurvivesRoundRestartAndPreventsOffer` (confirmed
by name in a verbose re-run, matching the SR-W-012 handoff's recorded skip). Prior handoff recorded
`163 passed / 1 skipped / 164 total`-class numbers on the pre-branch tree; +7 new cases = 170, all
green. No test regressed.

### 7. Release server build

```
dotnet build Content.Server/Content.Server.csproj --configuration Release --no-incremental \
  --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```

`16 projects, 0 errors, 837 warnings`. This branch touches zero files under `Content.Server`; the 837
warnings match the count the SR-W-011 handoff recorded (the SR-W-012 handoff's 840 reflected a
different intermediate commit) — no new warnings introduced.

## Key finding: the new test caught a real placement defect on its first run

The first run of the new fixture was `Failed: 1, Passed: 6` — `SolreignLeviathan` failed on
`IsTileMixtureProbablySafe`. This was a **genuine bad placement**, not a test bug, and is exactly why
the mandatory verification step earns its keep.

Root cause: an off-by-one in translating "the tile adjacent to the landmark" into a `pos` string on
**negative** Y coordinates. SS14 entity positions are tile-center floats, so the tile a `pos` occupies
is `(floor(x), floor(y))`. For Leviathan I had targeted the corridor tile at cell `(2,-22)` but wrote
`pos: 2.5,-22.5`, which floors to `(2,-23)` — the `Plating` wall tile carrying the QM office's
`Grille`/`ReinforcedWindow`, i.e. **inside the wall**, hard vacuum. The correct `pos` for cell
`(2,-22)` is `2.5,-21.5`. Meridian carried the same class of error (`-47.5,6.5` → cell `(-48,6)`
instead of the intended `(-47,6)`); it passed the atmosphere check by luck — `(-48,6)` happens to also
be breathable open floor — but was still not the tile the rationale claimed, so it was corrected to
`-46.5,6.5` as well.

Both were fixed and all seven now pass. Two process notes worth carrying forward:

1. **The atmosphere assertion, not the "is it floor?" reasoning, is what caught this.** My own
   pre-insertion tile decoding said "FloorSteel, clear" — because I was decoding the *intended* cell,
   not the cell the *written string* resolves to. The check that bit was the one grounded in what the
   engine actually loaded. An additional post-fix pass re-decoded all seven from the final `pos`
   strings as written (independent of intent) and confirmed all seven land on `FloorSteel` with no
   furniture conflict.
2. **Meridian is the cautionary half.** A wrong placement that still passes the safety gate is the
   failure mode to watch: had Leviathan not failed loudly, Meridian's silent mis-tiling would likely
   have shipped. Anything reviewing this diff should check `pos` against the claimed cell arithmetic,
   not just the green test run.

## Diff scope

`git diff --stat` from the branch point: seven `Resources/Maps/_Solreign/solreign_*.yml` files (14-15
insertions each, additive only) plus one new test file under
`Content.IntegrationTests/Tests/_Solreign/`. No prototype edits, no CVar value changes, no existing
entity moved or removed.

## Constraints honored

- Additive only — no existing entity or tile moved/removed (verified: every diff is pure insertion,
  `git diff` shows zero deletions).
- No CVar changes — `solreign.wingmates_enabled` untouched; feature stays inert.
- No prototype edits — `wingmate_beacon.yml` untouched.
- Local commits only, no push, no merge.
